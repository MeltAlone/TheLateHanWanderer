using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class AccessQueueTests
{
    [Fact]
    public void QueuedRequestsUseRequestOrderAndOneAdmissionPerMinute()
    {
        var engine = CreateEngine();
        ScheduleAccessChange(engine, 10, open: true);

        var first = engine.Enter("person.a", "place.destination");
        var second = engine.Enter("person.b", "place.destination");

        Assert.Equal(ActionStatus.Scheduled, first.Status);
        Assert.Equal(ActionStatus.Scheduled, second.Status);
        Assert.Equal("place.destination", engine.State.Actors["person.a"].PlaceId);
        Assert.Equal("place.origin", engine.State.Actors["person.b"].PlaceId);
        Assert.Equal("person.a", engine.State.PlaceAccessStates["place.destination"].LastAdmittedActorId);

        _ = engine.Wait(5, "person.b");

        var state = engine.State.PlaceAccessStates["place.destination"];
        Assert.Equal("place.destination", engine.State.Actors["person.b"].PlaceId);
        Assert.Equal("person.b", state.LastAdmittedActorId);
        Assert.Equal(15, state.LastAdmissionMinute);
        Assert.Equal(0, state.QueueCount);
        Assert.Equal(
            ["person.a", "person.b"],
            engine.State.Events
                .Where(item => item.Type == "place_entered")
                .Select(item => item.SubjectIds[0]));
    }

    [Fact]
    public void ClosedQueueTimesOutWithExplicitRefusal()
    {
        var engine = CreateEngine();

        var queued = engine.Enter("person.a", "place.destination");
        var busy = Assert.Throws<DomainCommandException>(() =>
            engine.BeginTravel("person.a", "place.destination", TravelMode.Walk));
        _ = engine.Wait(60, "person.a");

        var refusal = Assert.Single(engine.State.Events, item =>
            item.Type == "access_refused" && item.SubjectIds.Contains("person.a"));
        Assert.Equal(ActionStatus.Scheduled, queued.Status);
        Assert.Equal("actor_busy", busy.Code);
        Assert.Equal("place_closed", refusal.Details["reason"]);
        Assert.Equal("60", refusal.Details["waited_minutes"]);
        Assert.Equal("place.origin", engine.State.Actors["person.a"].PlaceId);
        Assert.Equal(0, engine.State.PlaceAccessStates["place.destination"].QueueCount);
        Assert.DoesNotContain(engine.State.ScheduledEvents, item => item.Kind == "access_queue_review");
    }

    [Fact]
    public void IneligibleActorIsRefusedWithoutEnteringClosedQueue()
    {
        var engine = CreateEngine(["controller_membership"]);

        var refused = engine.Enter("person.a", "place.destination");

        Assert.Equal(ActionStatus.Refused, refused.Status);
        Assert.Contains(refused.Events, item =>
            item.Type == "access_refused" &&
            item.Details["reason"] == "requirements_not_met:access.queued");
        Assert.DoesNotContain(refused.Events, item => item.Type == "access_queued");
        Assert.DoesNotContain(engine.State.ScheduledEvents, item => item.Kind == "access_queue_review");
        Assert.Equal(0, engine.State.PlaceAccessStates["place.destination"].QueueCount);
    }

    [Fact]
    public void SnapshotPreservesQueuedRequestAndSameMinuteControlOrdering()
    {
        var continuous = CreateEngine();
        ScheduleAccessChange(continuous, 10, open: true, controllerId: "organization.new_controller");
        _ = continuous.Enter("person.a", "place.destination");
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-access-queue-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(continuous.State, path);
            var restored = new WorldEngine(store.Load(path));

            _ = continuous.Wait(5, "person.a");
            _ = restored.Wait(5, "person.a");

            Assert.Equal("place.destination", restored.State.Actors["person.a"].PlaceId);
            Assert.Equal("organization.new_controller", restored.State.PlaceAccessStates["place.destination"].ControllerId);
            Assert.Equal(continuous.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(continuous.State.ComputeMaterialStateFingerprint(), restored.State.ComputeMaterialStateFingerprint());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }

    private static WorldEngine CreateEngine(IReadOnlyList<string>? queuedRequirements = null)
    {
        ActorState[] actors =
        [
            new("person.a", "A", "place.origin"),
            new("person.b", "B", "place.origin"),
        ];
        PlaceDefinition[] places =
        [
            new("place.origin", "Origin", "access.public", null),
            new("place.destination", "Destination", "access.queued", "organization.old_controller"),
        ];
        RouteDefinition[] routes =
        [
            new(
                "route.access",
                "place.origin",
                "place.destination",
                10,
                true,
                new Dictionary<TravelMode, int> { [TravelMode.Walk] = 5 }),
        ];
        var world = new WorldState(
            "scenario.access-queue-test",
            "1.0.0",
            "access-queue-test.v1",
            RandomMetadata.Xoshiro256StarStarV1,
            EngineMetadata.Version,
            "sha256:access-queue-test",
            "person.a",
            0,
            actors,
            places,
            routes,
            items: [],
            commitments: [],
            accessRules:
            [
                new AccessRuleDefinition("access.public", "Public", [], false),
                new AccessRuleDefinition("access.queued", "Queued", queuedRequirements ?? [], true),
            ],
            placeAccessStates:
            [
                new PlaceAccessState("place.destination", false, 0, "closed"),
            ]);
        return new WorldEngine(world);
    }

    private static void ScheduleAccessChange(
        WorldEngine engine,
        long minute,
        bool open,
        string? controllerId = null)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["open"] = open.ToString().ToLowerInvariant(),
            ["place_id"] = "place.destination",
            ["security_posture"] = open ? "normal" : "closed",
        };
        if (controllerId is not null)
        {
            details["controller_id"] = controllerId;
        }

        engine.Schedule(
            minute,
            ScheduledEventPhase.AccessAndControlChange,
            "place.destination",
            "place_access_changed",
            "place.destination",
            details: details);
    }
}
