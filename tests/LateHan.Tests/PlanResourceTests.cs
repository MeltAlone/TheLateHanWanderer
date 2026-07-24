using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class PlanResourceTests
{
    [Fact]
    public void EqualPriorityConflictWaitsThenAcquiresAfterRelease()
    {
        var first = CreatePlan("plan.a", "person.a", priority: 1);
        var second = CreatePlan("plan.b", "person.b", priority: 1);
        var engine = CreateEngine(first, second);

        _ = engine.Wait(1);

        Assert.Equal(PlanStatus.Running, first.Status);
        Assert.Equal(PlanStatus.Active, second.Status);
        Assert.Equal("waiting_for_resources", second.Stage);
        Assert.Equal("plan.a", engine.State.PlanResourceLocks["resource.inspection_team"].PlanId);
        Assert.Contains(engine.State.Events, item =>
            item.Type == "plan_resource_conflict" && item.SubjectIds.Contains("plan.b"));

        _ = engine.AdvanceAction(first.ActiveActionId!);
        _ = engine.Wait(second.NextEvaluationMinute - engine.State.CurrentMinute);

        Assert.Equal(PlanStatus.Completed, first.Status);
        Assert.Equal(PlanStatus.Running, second.Status);
        Assert.Equal("plan.b", engine.State.PlanResourceLocks["resource.inspection_team"].PlanId);
    }

    [Fact]
    public void HigherPriorityPlanReplacesLowerPriorityHolder()
    {
        var lower = CreatePlan("plan.a", "person.a", priority: 1);
        var higher = CreatePlan("plan.b", "person.b", priority: 10, mayReplaceLowerPriority: true);
        var engine = CreateEngine(lower, higher);

        _ = engine.Wait(1);

        Assert.Equal(PlanStatus.Cancelled, lower.Status);
        Assert.Equal(PlanStatus.Running, higher.Status);
        Assert.Equal(ActionStatus.Cancelled, engine.State.Actions[lower.ActiveActionId ?? "action.00000001"].Status);
        Assert.Equal("plan.b", engine.State.PlanResourceLocks["resource.inspection_team"].PlanId);
        Assert.Contains(engine.State.Events, item =>
            item.Type == "plan_cancelled" && item.Details["reason"] == "replaced_by:plan.b");
        Assert.Contains(engine.State.Events, item => item.Type == "plan_resources_released");
    }

    [Fact]
    public void CancellingLinkedActionCancelsPlanAndReleasesResources()
    {
        var plan = CreatePlan("plan.a", "person.a", priority: 1);
        var engine = CreateEngine(plan);
        _ = engine.Wait(1);
        var actionId = plan.ActiveActionId!;

        var result = engine.CancelAction(actionId, "inspection_recalled");

        Assert.Equal(ActionStatus.Cancelled, result.Status);
        Assert.Equal(PlanStatus.Cancelled, plan.Status);
        Assert.Empty(engine.State.PlanResourceLocks);
        Assert.Contains(result.Events, item =>
            item.Type == "plan_cancelled" && item.Details["reason"] == "inspection_recalled");
        Assert.Contains(result.Events, item => item.Type == "plan_resources_released");
    }

    [Fact]
    public void FailedPlanStartReleasesResourcesAndSchedulesRetry()
    {
        var plan = CreatePlan("plan.a", "person.a", priority: 1);
        var engine = CreateEngine([plan], destinationOpen: false);

        _ = engine.Wait(1);

        Assert.Equal(PlanStatus.Active, plan.Status);
        Assert.Equal("waiting_to_start", plan.Stage);
        Assert.Empty(engine.State.PlanResourceLocks);
        Assert.Empty(engine.State.Actions);
        Assert.Contains(engine.State.Events, item =>
            item.Type == "plan_start_failed" && item.Details["code"] == "access_denied");
        Assert.Contains(engine.State.Events, item =>
            item.Type == "plan_resources_released" && item.Details["reason"] == "plan_start_failed");
        Assert.Contains(engine.State.ScheduledEvents, item =>
            item.Kind == "plan_evaluation_due" && item.DueMinute == 60);
    }

    [Fact]
    public void SnapshotPreservesResourceLockAndDeterministicContinuation()
    {
        var continuousPlan = CreatePlan("plan.a", "person.a", priority: 1);
        var continuous = CreateEngine(continuousPlan);
        _ = continuous.Wait(1);
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-plan-resource-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(continuous.State, path);
            var restored = new WorldEngine(store.Load(path));
            var actionId = continuousPlan.ActiveActionId!;

            _ = continuous.AdvanceAction(actionId);
            _ = restored.AdvanceAction(actionId);

            Assert.Empty(continuous.State.PlanResourceLocks);
            Assert.Empty(restored.State.PlanResourceLocks);
            Assert.Equal(continuous.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(continuous.State.ComputeMaterialStateFingerprint(), restored.State.ComputeMaterialStateFingerprint());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }

    private static PlanState CreatePlan(
        string id,
        string ownerId,
        int priority,
        bool mayReplaceLowerPriority = false) => new(
        id,
        ownerId,
        "inspect_destination",
        "ready",
        beliefRequirementIds: [],
        nextEvaluationMinute: 0,
        PlanEvaluationRule.WrittenReportInspection,
        triggerItemId: $"item.{ownerId}",
        triggerPropositionId: "proposition.report",
        destinationPlaceId: "place.destination",
        confidenceThresholdBp: 0,
        reevaluationIntervalMinutes: 60,
        requiredResourceIds: ["resource.inspection_team"],
        priority: priority,
        mayReplaceLowerPriority: mayReplaceLowerPriority);

    private static WorldEngine CreateEngine(params PlanState[] plans) => CreateEngine(plans, destinationOpen: true);

    private static WorldEngine CreateEngine(PlanState[] plans, bool destinationOpen)
    {
        var actors = plans.Select(plan => new ActorState(plan.OwnerId, plan.OwnerId, "place.origin")).ToArray();
        var items = plans.Select(plan => new ItemState(
            $"item.{plan.OwnerId}",
            "Report",
            "written_report",
            plan.OwnerId,
            propositionIds: ["proposition.report"])).ToArray();
        var world = new WorldState(
            "scenario.plan-resource-test",
            "1.0.0",
            "plan-resource-test.v1",
            RandomMetadata.Xoshiro256StarStarV1,
            EngineMetadata.Version,
            "sha256:plan-resource-test",
            actors[0].Id,
            0,
            actors,
            places:
            [
                new PlaceDefinition("place.origin", "Origin", "access.public", null),
                new PlaceDefinition("place.destination", "Destination", "access.public", null),
            ],
            routes:
            [
                new RouteDefinition(
                    "route.plan-resource",
                    "place.origin",
                    "place.destination",
                    10,
                    true,
                    new Dictionary<TravelMode, int> { [TravelMode.Walk] = 20 }),
            ],
            items,
            commitments: [],
            plans: plans,
            accessRules: [new AccessRuleDefinition("access.public", "Public", [], false)],
            placeAccessStates:
            [
                new PlaceAccessState(
                    "place.destination",
                    destinationOpen,
                    queueCount: 0,
                    destinationOpen ? "normal" : "closed"),
            ]);
        var engine = new WorldEngine(world);
        engine.InitializePlans();
        return engine;
    }
}
