using LateHan.Core;

namespace LateHan.Tests;

public sealed class WorldEngineTests
{
    [Fact]
    public void InitialQueriesDoNotAdvanceTimeOrRevealRemoteActors()
    {
        var engine = RepositoryFixture.CreateEngine();

        var look = engine.Look();
        var status = engine.Status();

        Assert.Equal(0, engine.State.CurrentMinute);
        Assert.Empty(engine.State.Events);
        Assert.Contains(look.VisibleActors, actor => actor.Id == "person.he_jin");
        Assert.DoesNotContain(look.VisibleActors, actor => actor.Id == "person.zhang_rang");
        Assert.Contains(status.HeldItems, item => item.Id == "item.sealed_note_to_yuan_shao");
        Assert.Equal(2, status.OpenCommitments.Count);
    }

    [Fact]
    public void PlayerCanWalkDeliverTheDocumentAndReportBack()
    {
        var engine = RepositoryFixture.CreateEngine();

        var outbound = engine.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        var delivery = engine.Deliver(
            "person.player_clerk",
            "item.sealed_note_to_yuan_shao",
            "person.yuan_shao");
        var inbound = engine.Move("person.player_clerk", "place.luoyang.general_in_chief_office", TravelMode.Walk);
        var report = engine.Tell(
            "person.player_clerk",
            "person.li_wen",
            "proposition.general_office_requests_status");

        Assert.Equal(22, outbound.EndMinute - outbound.StartMinute);
        Assert.Equal(5, delivery.EndMinute - delivery.StartMinute);
        Assert.Equal(22, inbound.EndMinute - inbound.StartMinute);
        Assert.Equal(5, report.EndMinute - report.StartMinute);
        Assert.Equal(54, engine.State.CurrentMinute);
        Assert.Equal("person.yuan_shao", engine.State.Items["item.sealed_note_to_yuan_shao"].HolderId);
        Assert.Equal("completed", engine.State.Commitments["commitment.player.deliver_note"].Status);
        Assert.Equal("completed", engine.State.Commitments["commitment.player.report_back"].Status);
        Assert.Equal(
            [
                "travel_started",
                "travel_completed",
                "delivery_started",
                "item_transferred",
                "commitment_completed",
                "travel_started",
                "travel_completed",
                "proposition_told",
                "commitment_completed",
            ],
            engine.State.Events.Select(worldEvent => worldEvent.Type));
    }

    [Fact]
    public void InvalidMoveDoesNotAdvanceTimeOrCreateEvents()
    {
        var engine = RepositoryFixture.CreateEngine();

        var exception = Assert.Throws<DomainCommandException>(
            () => engine.Move("person.player_clerk", "place.missing", TravelMode.Walk));

        Assert.Equal("unknown_place", exception.Code);
        Assert.Equal(0, engine.State.CurrentMinute);
        Assert.Empty(engine.State.Events);
    }

    [Fact]
    public void RepeatedRunsProduceTheSameEventFingerprint()
    {
        var first = RunDeliveryPath();
        var second = RunDeliveryPath();

        Assert.Equal(first.State.CurrentMinute, second.State.CurrentMinute);
        Assert.Equal(first.State.ComputeEventFingerprint(), second.State.ComputeEventFingerprint());
    }

    [Fact]
    public void WaitStopsExactlyAtInterruptAfterSettlingSameMinuteInStableOrder()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Schedule(
            95,
            ScheduledEventPhase.SummaryAndNotification,
            "person.zhang_rang",
            "remote_summary");
        engine.Schedule(
            95,
            ScheduledEventPhase.SummaryAndNotification,
            "person.player_clerk",
            "urgent_recall",
            "place.luoyang.general_in_chief_office",
            interruptsPlayer: true);
        engine.Schedule(
            95,
            ScheduledEventPhase.DeathOrRemoval,
            "person.he_jin",
            "actor_removed");
        engine.Schedule(
            120,
            ScheduledEventPhase.PlanEvaluation,
            "person.dong_zhuo",
            "plan_evaluated");

        var result = engine.Wait(240);

        Assert.Equal(ActionStatus.Interrupted, result.Status);
        Assert.Equal(95, result.EndMinute);
        Assert.Equal(95, engine.State.CurrentMinute);
        Assert.Equal(
            ["wait_started", "actor_removed", "urgent_recall", "remote_summary", "wait_interrupted"],
            result.Events.Select(item => item.Type));
        Assert.Equal("145", result.Events[^1].Details["remaining_minutes"]);
        Assert.Single(engine.State.ScheduledEvents);
        Assert.Equal(120, engine.State.ScheduledEvents[0].DueMinute);
    }

    [Fact]
    public void SamePhaseUsesOrdinalSubjectThenInsertionSequence()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Schedule(10, ScheduledEventPhase.PlanEvaluation, "subject.z", "z_first");
        engine.Schedule(10, ScheduledEventPhase.PlanEvaluation, "subject.a", "a_first");
        engine.Schedule(10, ScheduledEventPhase.PlanEvaluation, "subject.a", "a_second");

        var result = engine.Wait(10);

        Assert.Equal(
            ["wait_started", "a_first", "a_second", "z_first", "wait_completed"],
            result.Events.Select(item => item.Type));
    }

    [Fact]
    public void InvalidSchedulingDoesNotChangeEventsOrSequenceCursors()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Wait(10);
        var eventCursor = engine.State.EventSequenceCursor;
        var scheduledCursor = engine.State.ScheduledEventSequenceCursor;

        var exception = Assert.Throws<DomainCommandException>(() => engine.ScheduleExternalIntervention(
            9,
            ScheduledEventPhase.PlanEvaluation,
            "person.dong_zhuo",
            "plan_evaluated"));

        Assert.Equal("scheduled_event_in_past", exception.Code);
        Assert.Equal(eventCursor, engine.State.EventSequenceCursor);
        Assert.Equal(scheduledCursor, engine.State.ScheduledEventSequenceCursor);
        Assert.False(engine.State.ReplayModified);

        var invalidPhase = Assert.Throws<DomainCommandException>(() => engine.ScheduleExternalIntervention(
            10,
            (ScheduledEventPhase)99,
            "person.dong_zhuo",
            "plan_evaluated"));

        Assert.Equal("invalid_scheduled_event", invalidPhase.Code);
        Assert.Equal(eventCursor, engine.State.EventSequenceCursor);
        Assert.Equal(scheduledCursor, engine.State.ScheduledEventSequenceCursor);
        Assert.False(engine.State.ReplayModified);
    }

    [Fact]
    public void OverflowingWaitDoesNotStartAnAction()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Wait(10);
        var eventCursor = engine.State.EventSequenceCursor;

        _ = Assert.Throws<OverflowException>(() => engine.Wait(long.MaxValue));

        Assert.Equal(10, engine.State.CurrentMinute);
        Assert.Equal(eventCursor, engine.State.EventSequenceCursor);
    }

    private static WorldEngine RunDeliveryPath()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        engine.Deliver("person.player_clerk", "item.sealed_note_to_yuan_shao", "person.yuan_shao");
        engine.Move("person.player_clerk", "place.luoyang.general_in_chief_office", TravelMode.Walk);
        engine.Tell("person.player_clerk", "person.li_wen", "proposition.general_office_requests_status");
        return engine;
    }
}
