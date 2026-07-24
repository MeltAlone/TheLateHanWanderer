using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class PlanLoopTests
{
    [Fact]
    public void PlanWaitsUntilFormalReportArrives()
    {
        var engine = RepositoryFixture.CreateEngine();

        var result = engine.Wait(210);

        var plan = engine.State.Plans["plan.wang_yun.review_gate_security"];
        Assert.Equal(210, engine.State.CurrentMinute);
        Assert.Equal("place.luoyang.henan_office", engine.State.Actors[plan.OwnerId].LocationId);
        Assert.Null(plan.ActiveActionId);
        Assert.Equal(PlanStatus.Active, plan.Status);
        Assert.Equal("awaiting_written_report", plan.Stage);
        Assert.Equal(270, plan.NextEvaluationMinute);
        Assert.Contains(result.Events, item =>
            item.Type == "plan_evaluated" && item.Details["decision"] == "wait");
    }

    [Fact]
    public void FormalReportUpdatesBeliefAndStartsNpcInspection()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Move("person.chen_zhi", "place.luoyang.henan_office", TravelMode.Walk);

        var result = engine.Deliver(
            "person.chen_zhi",
            "item.north_gate_watch_report",
            "person.wang_yun");

        var belief = engine.State.Beliefs["belief.wang_yun.traffic"];
        var plan = engine.State.Plans["plan.wang_yun.review_gate_security"];
        Assert.Equal(114, engine.State.CurrentMinute);
        Assert.Equal("person.wang_yun", engine.State.Items["item.north_gate_watch_report"].HolderId);
        Assert.Equal(8500, belief.ConfidenceBp);
        Assert.Equal("official_document", belief.Source);
        Assert.Equal(114, belief.AcquiredAtMinute);
        Assert.Equal("item_transferred", engine.State.Events.Single(item => item.Id == belief.SourceEventId).Type);
        Assert.Equal(PlanStatus.Running, plan.Status);
        Assert.Equal("traveling_to_inspect", plan.Stage);
        Assert.NotNull(plan.ActiveActionId);
        Assert.True(engine.State.Actors[plan.OwnerId].IsInTransit);
        Assert.Contains(result.Events, item => item.Type == "belief_updated");
        Assert.Contains(result.Events, item => item.Type == "plan_evaluated");
        Assert.Contains(result.Events, item => item.Type == "travel_started");
        Assert.DoesNotContain(engine.State.ScheduledEvents, item => item.DueMinute == 210);
    }

    [Fact]
    public void InspectionArrivalCompletesPlanWithCause()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Move("person.chen_zhi", "place.luoyang.henan_office", TravelMode.Walk);
        _ = engine.Deliver("person.chen_zhi", "item.north_gate_watch_report", "person.wang_yun");
        var plan = engine.State.Plans["plan.wang_yun.review_gate_security"];

        var result = engine.AdvanceAction(plan.ActiveActionId!);

        Assert.Equal(ActionStatus.Completed, result.Status);
        Assert.Equal("place.luoyang.north_gate", engine.State.Actors[plan.OwnerId].LocationId);
        Assert.Equal(PlanStatus.Completed, plan.Status);
        Assert.Equal("inspection_completed", plan.Stage);
        Assert.Null(plan.ActiveActionId);
        var completed = engine.State.Events.Last(item => item.Type == "plan_completed");
        Assert.Contains(completed.CauseIds, cause =>
            engine.State.Events.Any(item => item.Id == cause && item.Type == "travel_completed"));
    }

    [Fact]
    public void SnapshotContinuationPreservesPlanLoop()
    {
        var store = new WorldSnapshotStore();
        var original = RepositoryFixture.CreateEngine();
        _ = original.Move("person.chen_zhi", "place.luoyang.henan_office", TravelMode.Walk);
        _ = original.Deliver("person.chen_zhi", "item.north_gate_watch_report", "person.wang_yun");
        var actionId = original.State.Plans["plan.wang_yun.review_gate_security"].ActiveActionId!;
        var path = Path.Combine(Path.GetTempPath(), $"latehan-plan-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(original.State, path);
            var restored = new WorldEngine(store.Load(path));
            _ = original.AdvanceAction(actionId);
            _ = restored.AdvanceAction(actionId);

            Assert.Equal(original.State.CurrentMinute, restored.State.CurrentMinute);
            Assert.Equal(original.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(original.State.Plans["plan.wang_yun.review_gate_security"].Status,
                restored.State.Plans["plan.wang_yun.review_gate_security"].Status);
            Assert.Equal(original.State.Plans["plan.wang_yun.review_gate_security"].Stage,
                restored.State.Plans["plan.wang_yun.review_gate_security"].Stage);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }
}
