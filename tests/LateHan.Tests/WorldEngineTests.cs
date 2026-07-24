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
        Assert.Empty(engine.State.Actions);
        Assert.Empty(engine.State.ScheduledEvents);
        Assert.Equal(1, engine.State.ActionSequenceCursor);
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

    [Fact]
    public void TravelInterruptionPreservesElapsedTimeAndRoutePosition()
    {
        var engine = RepositoryFixture.CreateEngine();
        var action = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.eastern_road",
            TravelMode.Horse);
        engine.ScheduleTravelRiskCheck(action.Id, 40, "horse_injured", ulong.MaxValue);

        var result = engine.AdvanceAction(action.Id);

        Assert.Equal(ActionStatus.Interrupted, result.Status);
        Assert.Equal(40, engine.State.CurrentMinute);
        Assert.Equal(40, action.Travel.ElapsedMinutes);
        Assert.Equal("route.east_market_east_gate", action.Travel.CurrentLeg.RouteId);
        Assert.Equal(272, action.Travel.CurrentLegProgressQ1000);
        Assert.Null(engine.State.Actors["person.player_clerk"].PlaceId);
        Assert.Equal(272, engine.State.Actors["person.player_clerk"].Transit?.ProgressQ1000);
        Assert.Contains(result.Events, item => item.Type == "travel_disrupted");
        Assert.Contains(result.Events, item => item.Type == "travel_interrupted");
        Assert.Equal(1UL, engine.State.RandomStreams.Streams[$"travel-risk:{action.Id}"].DrawCount);
        Assert.Contains("rng_draw", result.Events.First(item => item.Type == "travel_disrupted").Details.Keys);
        Assert.DoesNotContain(engine.State.ScheduledEvents, item => item.Kind == "travel_segment_completed");
    }

    [Fact]
    public void InterruptedTravelCanResumeOnFootWithoutLosingProgress()
    {
        var engine = RepositoryFixture.CreateEngine();
        var action = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.eastern_road",
            TravelMode.Horse);
        engine.ScheduleTravelInterruption(action.Id, 40, "horse_injured");
        _ = engine.AdvanceAction(action.Id);

        var result = engine.ResumeTravel(action.Id, TravelMode.Walk);

        Assert.Equal(ActionStatus.Completed, result.Status);
        Assert.Equal(172, engine.State.CurrentMinute);
        Assert.Equal(172, action.Travel.ElapsedMinutes);
        Assert.Equal("place.luoyang.eastern_road", engine.State.Actors["person.player_clerk"].LocationId);
        Assert.Null(engine.State.Actors["person.player_clerk"].Transit);
        Assert.Contains(result.Events, item => item.Type == "travel_resumed");
        Assert.Equal("travel_completed", result.Events[^1].Type);
    }

    [Fact]
    public void TargetCanLeaveWhilePlayerIsTraveling()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Schedule(
            10,
            ScheduledEventPhase.ArrivalAndDeparture,
            "person.yuan_shao",
            "actor_relocated",
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination_place_id"] = "place.luoyang.henan_office",
            });

        var travel = engine.Move(
            "person.player_clerk",
            "place.luoyang.sili_office",
            TravelMode.Walk);
        var exception = Assert.Throws<DomainCommandException>(() => engine.Deliver(
            "person.player_clerk",
            "item.sealed_note_to_yuan_shao",
            "person.yuan_shao"));

        Assert.Equal(ActionStatus.Completed, travel.Status);
        Assert.Equal(22, engine.State.CurrentMinute);
        Assert.Equal("place.luoyang.henan_office", engine.State.Actors["person.yuan_shao"].LocationId);
        Assert.Equal("recipient_not_present", exception.Code);
        Assert.Equal("person.player_clerk", engine.State.Items["item.sealed_note_to_yuan_shao"].HolderId);
        Assert.Equal("open", engine.State.Commitments["commitment.player.deliver_note"].Status);
    }

    [Fact]
    public void InterruptedTravelCanBeCancelledWithoutRewindingPosition()
    {
        var engine = RepositoryFixture.CreateEngine();
        var action = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.eastern_road",
            TravelMode.Horse);
        engine.ScheduleTravelInterruption(action.Id, 10, "horse_injured");
        _ = engine.AdvanceAction(action.Id);
        var progress = action.Travel.CurrentLegProgressQ1000;

        var result = engine.CancelAction(action.Id);

        Assert.Equal(ActionStatus.Cancelled, result.Status);
        Assert.Equal(10, engine.State.CurrentMinute);
        Assert.Equal(progress, engine.State.Actors["person.player_clerk"].Transit?.ProgressQ1000);
        Assert.Empty(engine.State.ScheduledEvents);
        Assert.Equal("travel_cancelled", result.Events.Single().Type);
        Assert.Throws<DomainCommandException>(() => engine.ResumeTravel(action.Id, TravelMode.Walk));
    }

    [Fact]
    public void HigherPrioritySameMinuteInterruptionPreventsArrival()
    {
        var engine = RepositoryFixture.CreateEngine();
        var action = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.sili_office",
            TravelMode.Walk);
        engine.Schedule(
            22,
            ScheduledEventPhase.DeathOrRemoval,
            "person.player_clerk",
            "actor_incapacitated",
            interruptsPlayer: true);

        var result = engine.AdvanceAction(action.Id);

        Assert.Equal(ActionStatus.Interrupted, result.Status);
        Assert.Equal(22, engine.State.CurrentMinute);
        Assert.Null(engine.State.Actors["person.player_clerk"].PlaceId);
        Assert.Equal("route.office_sili", engine.State.Actors["person.player_clerk"].Transit?.RouteId);
        Assert.Equal(999, engine.State.Actors["person.player_clerk"].Transit?.ProgressQ1000);
        Assert.DoesNotContain(result.Events, item => item.Type == "travel_completed");
        Assert.DoesNotContain(engine.State.ScheduledEvents, item => item.Kind == "travel_completed");
    }

    [Fact]
    public void InterruptedTravelerCanWaitInPlaceAndRemoteTransitDoesNotBreakLook()
    {
        var engine = RepositoryFixture.CreateEngine();
        var action = engine.BeginTravel(
            "person.yuan_shao",
            "place.luoyang.eastern_road",
            TravelMode.Horse);
        engine.Schedule(
            10,
            ScheduledEventPhase.DeathOrRemoval,
            "person.yuan_shao",
            "actor_incapacitated",
            interruptsPlayer: false);
        _ = engine.AdvanceAction(action.Id);

        var look = engine.Look("person.player_clerk");
        var wait = engine.Wait(5, "person.yuan_shao");

        Assert.Equal("place.luoyang.general_in_chief_office", look.PlaceId);
        Assert.Equal(ActionStatus.Completed, wait.Status);
        Assert.Equal(15, engine.State.CurrentMinute);
        Assert.True(engine.State.Actors["person.yuan_shao"].IsInTransit);
        Assert.Null(wait.Events[0].LocationId);
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
