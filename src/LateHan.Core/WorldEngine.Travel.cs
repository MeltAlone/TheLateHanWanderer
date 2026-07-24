using System.Globalization;

namespace LateHan.Core;

public sealed partial class WorldEngine
{
    public ActionInstanceState BeginTravel(
        string actorId,
        string destinationPlaceId,
        TravelMode mode,
        IReadOnlyList<string>? causeIds = null)
    {
        var actor = GetActor(actorId);
        _ = GetPlace(destinationPlaceId);
        if (actor.IsInTransit)
        {
            throw new DomainCommandException("actor_in_transit", $"Actor '{actorId}' is already in transit.");
        }

        if (actor.LocationId == destinationPlaceId)
        {
            throw new DomainCommandException("already_at_destination", $"Actor '{actorId}' is already at '{destinationPlaceId}'.");
        }

        if (FindActiveAction(actorId) is not null || HasPendingAccessRequest(actorId))
        {
            throw new DomainCommandException("actor_busy", $"Actor '{actorId}' already has an active action.");
        }

        var accessDecision = EvaluateAccess(actor, destinationPlaceId);
        if (!accessDecision.Allowed)
        {
            throw new DomainCommandException(
                "access_denied",
                $"Access to '{destinationPlaceId}' is denied: {accessDecision.Reason}.");
        }

        var legs = FindTravelPath(actor.LocationId, destinationPlaceId, mode);
        var firstLeg = legs[0];
        var firstDuration = firstLeg.GetMinutes(mode);
        _ = checked(State.CurrentMinute + firstDuration);
        var sequence = State.NextActionSequence;
        var actionId = $"action.{sequence:D8}";
        var totalMinutes = legs.Sum(leg => leg.GetMinutes(mode));
        var started = AppendEvent(
            "travel_started",
            State.CurrentMinute,
            actor.LocationId,
            [actorId],
            causeIds ?? [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = actionId,
                ["destination"] = destinationPlaceId,
                ["mode"] = ToModeId(mode),
                ["route_ids"] = string.Join(',', legs.Select(leg => leg.RouteId)),
                ["expected_minutes"] = totalMinutes.ToString(CultureInfo.InvariantCulture),
            });
        var travel = new TravelActionState(
            actor.LocationId,
            destinationPlaceId,
            mode,
            legs,
            segmentStartedMinute: State.CurrentMinute,
            segmentRemainingMinutes: firstDuration);
        var action = new ActionInstanceState(
            sequence,
            actionId,
            actorId,
            WorldActionKind.Travel,
            ActionStatus.Running,
            State.CurrentMinute,
            State.CurrentMinute,
            "traveling",
            started.Id,
            travel);
        State.AddAction(action);
        State.NextActionSequence++;
        State.BeginActorTransit(actor.Id, new TransitPositionState(
            action.Id,
            firstLeg.RouteId,
            firstLeg.FromPlaceId,
            firstLeg.ToPlaceId,
            0));
        MarkActorPositionDetailDirty(actor.Id, travel.OriginPlaceId, null);
        ScheduleTravelCompletion(action, [started.Id]);
        return action;
    }

    public ActionResult AdvanceAction(string actionId)
    {
        var action = GetAction(actionId);
        var startMinute = State.CurrentMinute;
        var firstEventSequence = State.EventSequenceCursor;
        while (action.Status == ActionStatus.Running)
        {
            var pendingId = action.Travel.PendingScheduledEventId
                ?? throw new InvalidOperationException($"Running action '{action.Id}' has no pending event.");
            var pending = State.ScheduledEvents.FirstOrDefault(
                item => string.Equals(item.Id, pendingId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Pending event '{pendingId}' is missing.");
            _ = AdvanceTimeTo(pending.DueMinute, action.Id);
        }

        var events = State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray();
        return new ActionResult(startMinute, State.CurrentMinute, events, action.Status);
    }

    public ScheduledWorldEvent ScheduleTravelInterruption(
        string actionId,
        long dueMinute,
        string reason)
    {
        var action = ValidateTravelInterruption(actionId, dueMinute);
        return Schedule(
            dueMinute,
            ScheduledEventPhase.DeathOrRemoval,
            action.ActorId,
            "travel_disrupted",
            interruptsPlayer: string.Equals(action.ActorId, State.PlayerActorId, StringComparison.Ordinal),
            causeIds: [action.StartedByEventId],
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["reason"] = reason,
            });
    }

    public ScheduledWorldEvent ScheduleTravelRiskCheck(
        string actionId,
        long dueMinute,
        string disruptionReason,
        ulong disruptionThreshold)
    {
        var action = ValidateTravelInterruption(actionId, dueMinute);
        var draw = State.RandomStreams.NextUInt64("travel-risk", action.Id);
        var disrupted = draw <= disruptionThreshold;
        return Schedule(
            dueMinute,
            disrupted ? ScheduledEventPhase.DeathOrRemoval : ScheduledEventPhase.PlanEvaluation,
            action.ActorId,
            disrupted ? "travel_disrupted" : "travel_risk_cleared",
            interruptsPlayer: disrupted && string.Equals(action.ActorId, State.PlayerActorId, StringComparison.Ordinal),
            causeIds: [action.StartedByEventId],
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["reason"] = disruptionReason,
                ["rng_draw"] = draw.ToString(CultureInfo.InvariantCulture),
                ["rng_stream_key"] = $"travel-risk:{action.Id}",
                ["threshold"] = disruptionThreshold.ToString(CultureInfo.InvariantCulture),
            });
    }

    public ScheduledWorldEvent ScheduleExternalTravelInterruption(
        string actionId,
        long dueMinute,
        string reason)
    {
        var action = ValidateTravelInterruption(actionId, dueMinute);
        return ScheduleExternalIntervention(
            dueMinute,
            ScheduledEventPhase.DeathOrRemoval,
            action.ActorId,
            "travel_disrupted",
            interruptsPlayer: string.Equals(action.ActorId, State.PlayerActorId, StringComparison.Ordinal),
            causeIds: [action.StartedByEventId],
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["reason"] = reason,
            });
    }

    private ActionInstanceState ValidateTravelInterruption(string actionId, long dueMinute)
    {
        var action = GetAction(actionId);
        if (action.Kind != WorldActionKind.Travel || action.Status != ActionStatus.Running)
        {
            throw new DomainCommandException("action_not_running", $"Action '{actionId}' is not running travel.");
        }

        var travel = action.Travel;
        var remainingMinutes = travel.SegmentRemainingMinutes + travel.Legs
            .Skip(travel.CurrentLegIndex + 1)
            .Sum(leg => leg.GetMinutes(travel.Mode));
        var projectedCompletionMinute = checked(State.CurrentMinute + remainingMinutes);
        if (dueMinute < State.CurrentMinute || dueMinute >= projectedCompletionMinute)
        {
            throw new DomainCommandException(
                "invalid_interruption_time",
                $"Travel interruption must be scheduled before minute {projectedCompletionMinute}.");
        }

        return action;
    }

    public ActionResult ResumeTravel(string actionId, TravelMode mode)
    {
        var action = GetAction(actionId);
        if (action.Kind != WorldActionKind.Travel || action.Status != ActionStatus.Interrupted)
        {
            throw new DomainCommandException("action_not_resumable", $"Action '{actionId}' is not an interrupted travel action.");
        }

        var actor = GetActor(action.ActorId);
        var transit = actor.Transit
            ?? throw new InvalidOperationException($"Interrupted travel action '{actionId}' has no transit position.");
        var startMinute = State.CurrentMinute;
        var firstEventSequence = State.EventSequenceCursor;
        var travel = action.Travel;
        var resumed = AppendEvent(
            "travel_resumed",
            State.CurrentMinute,
            null,
            [action.ActorId],
            travel.InterruptionEventId is null ? [action.StartedByEventId] : [travel.InterruptionEventId],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["mode"] = ToModeId(mode),
                ["route_id"] = travel.CurrentLeg.RouteId,
                ["progress_q1000"] = travel.CurrentLegProgressQ1000.ToString(CultureInfo.InvariantCulture),
            });
        travel.Mode = mode;
        travel.SegmentStartedMinute = State.CurrentMinute;
        travel.SegmentRemainingMinutes = CalculateRemainingMinutes(travel.CurrentLeg, mode, travel.CurrentLegProgressQ1000);
        travel.InterruptionEventId = null;
        action.Status = ActionStatus.Running;
        action.Phase = "traveling";
        action.LastUpdatedMinute = State.CurrentMinute;
        transit.ProgressQ1000 = travel.CurrentLegProgressQ1000;
        InvalidateActorDetailLevels([action.ActorId]);
        ScheduleTravelCompletion(action, [resumed.Id]);
        _ = AdvanceAction(action.Id);
        var events = State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray();
        return new ActionResult(startMinute, State.CurrentMinute, events, action.Status);
    }

    public ActionResult CancelAction(
        string actionId,
        string reason = "cancelled_by_command",
        IReadOnlyList<string>? linkedPlanCauseIds = null)
    {
        var action = GetAction(actionId);
        if (action.Status is not (ActionStatus.Running or ActionStatus.Interrupted))
        {
            throw new DomainCommandException("action_not_cancellable", $"Action '{actionId}' cannot be cancelled.");
        }

        var startMinute = State.CurrentMinute;
        if (action.Status == ActionStatus.Running)
        {
            UpdateTravelProgress(action, State.CurrentMinute);
            if (action.Travel.PendingScheduledEventId is { } pendingId)
            {
                _ = State.RemoveScheduledEvent(pendingId);
            }
        }

        var cancelled = AppendEvent(
            "travel_cancelled",
            State.CurrentMinute,
            null,
            [action.ActorId],
            action.Travel.InterruptionEventId is null ? [action.StartedByEventId] : [action.Travel.InterruptionEventId],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["elapsed_minutes"] = action.Travel.ElapsedMinutes.ToString(CultureInfo.InvariantCulture),
                ["progress_q1000"] = action.Travel.CurrentLegProgressQ1000.ToString(CultureInfo.InvariantCulture),
            });
        action.Status = ActionStatus.Cancelled;
        action.Phase = "cancelled_in_transit";
        action.LastUpdatedMinute = State.CurrentMinute;
        action.Travel.PendingScheduledEventId = null;
        InvalidateActorDetailLevels([action.ActorId]);
        var events = new List<WorldEvent> { cancelled };
        foreach (var planEvent in CancelLinkedPlanForAction(action, cancelled, reason, linkedPlanCauseIds ?? []))
        {
            events.Add(planEvent);
        }

        return new ActionResult(startMinute, State.CurrentMinute, events, ActionStatus.Cancelled);
    }

    private IReadOnlyList<WorldEvent> ProcessScheduledEvent(ScheduledWorldEvent scheduled)
    {
        return scheduled.Kind switch
        {
            "travel_segment_completed" or "travel_completed" => ProcessTravelCompletion(scheduled),
            "plan_evaluation_due" => ProcessPlanEvaluation(scheduled),
            "place_access_changed" => [ProcessPlaceAccessChange(scheduled)],
            "access_queue_review" => [ProcessAccessQueueReview(scheduled)],
            "actor_relocated" => [ProcessActorRelocation(scheduled)],
            "detail_message_retention_expired" => [ProcessDetailMessageRetentionExpired(scheduled)],
            "remote_world_tick" => ProcessRemoteWorldTick(scheduled),
            _ => [AppendScheduledEvent(scheduled)],
        };
    }

    private IReadOnlyList<WorldEvent> ProcessTravelCompletion(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("action_id", out var actionId))
        {
            throw new InvalidOperationException($"Scheduled travel event '{scheduled.Id}' has no action ID.");
        }

        var action = GetAction(actionId);
        var travel = action.Travel;
        if (action.Status != ActionStatus.Running ||
            !string.Equals(travel.PendingScheduledEventId, scheduled.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scheduled travel event '{scheduled.Id}' is not active.");
        }

        var actor = GetActor(action.ActorId);
        var leg = travel.CurrentLeg;
        travel.ElapsedMinutes = checked(travel.ElapsedMinutes + (scheduled.DueMinute - travel.SegmentStartedMinute));
        travel.CurrentLegProgressQ1000 = 1000;
        travel.SegmentRemainingMinutes = 0;
        travel.PendingScheduledEventId = null;
        action.LastUpdatedMinute = scheduled.DueMinute;
        var isFinalLeg = travel.CurrentLegIndex == travel.Legs.Count - 1;
        var accessDecision = EvaluateAccess(actor, leg.ToPlaceId);
        if (isFinalLeg && !accessDecision.Allowed)
        {
            var previousRouteId = actor.Transit?.RouteId;
            State.MoveActorToPlace(actor.Id, leg.FromPlaceId);
            action.Status = ActionStatus.Blocked;
            action.Phase = "blocked_by_access";
            MarkActorPositionDetailDirty(actor.Id, null, previousRouteId);
            var blockedDetails = scheduled.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            blockedDetails["scheduled_event_id"] = scheduled.Id;
            blockedDetails["destination"] = leg.ToPlaceId;
            blockedDetails["reason"] = accessDecision.Reason;
            return
            [
                AppendEvent(
                    "travel_access_blocked",
                    scheduled.DueMinute,
                    leg.FromPlaceId,
                    [action.ActorId],
                    scheduled.CauseIds,
                    blockedDetails),
            ];
        }

        var priorRouteId = actor.Transit?.RouteId;
        State.MoveActorToPlace(actor.Id, leg.ToPlaceId);
        var details = scheduled.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        details["scheduled_event_id"] = scheduled.Id;
        details["scheduled_phase"] = ToPhaseId(scheduled.Phase);
        details["route_id"] = leg.RouteId;
        details["elapsed_minutes"] = travel.ElapsedMinutes.ToString(CultureInfo.InvariantCulture);
        if (scheduled.Kind == "travel_completed")
        {
            details["origin"] = travel.OriginPlaceId;
            details["mode"] = ToModeId(travel.Mode);
        }

        var completed = AppendEvent(
            scheduled.Kind,
            scheduled.DueMinute,
            leg.ToPlaceId,
            [action.ActorId],
            scheduled.CauseIds,
            details);
        if (travel.CurrentLegIndex == travel.Legs.Count - 1)
        {
            action.Status = ActionStatus.Completed;
            action.Phase = "completed";
            MarkActorPositionDetailDirty(actor.Id, null, priorRouteId);
            var events = new List<WorldEvent> { completed };
            CompleteLinkedPlan(action, completed, events);
            return events;
        }

        travel.CurrentLegIndex++;
        travel.CurrentLegProgressQ1000 = 0;
        travel.SegmentStartedMinute = scheduled.DueMinute;
        travel.SegmentRemainingMinutes = travel.CurrentLeg.GetMinutes(travel.Mode);
        var nextLeg = travel.CurrentLeg;
        State.BeginActorTransit(actor.Id, new TransitPositionState(
            action.Id,
            nextLeg.RouteId,
            nextLeg.FromPlaceId,
            nextLeg.ToPlaceId,
            0));
        MarkActorPositionDetailDirty(actor.Id, null, priorRouteId);
        var nextStarted = AppendEvent(
            "travel_segment_started",
            scheduled.DueMinute,
            nextLeg.FromPlaceId,
            [action.ActorId],
            [completed.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["route_id"] = nextLeg.RouteId,
                ["leg_index"] = travel.CurrentLegIndex.ToString(CultureInfo.InvariantCulture),
                ["expected_minutes"] = travel.SegmentRemainingMinutes.ToString(CultureInfo.InvariantCulture),
            });
        ScheduleTravelCompletion(action, [nextStarted.Id]);
        return [completed, nextStarted];
    }

    private WorldEvent ProcessActorRelocation(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("destination_place_id", out var destinationPlaceId))
        {
            throw new InvalidOperationException($"Relocation event '{scheduled.Id}' has no destination.");
        }

        var actor = GetActor(scheduled.StableSubjectId);
        _ = GetPlace(destinationPlaceId);
        if (actor.IsInTransit || FindActiveAction(actor.Id) is not null)
        {
            return AppendEvent(
                "actor_relocation_blocked",
                scheduled.DueMinute,
                actor.PlaceId,
                [actor.Id],
                scheduled.CauseIds,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["scheduled_event_id"] = scheduled.Id,
                    ["reason"] = "actor_busy",
                });
        }

        var origin = actor.LocationId;
        State.MoveActorToPlace(actor.Id, destinationPlaceId);
        MarkActorPositionDetailDirty(actor.Id, origin, null);
        return AppendEvent(
            scheduled.Kind,
            scheduled.DueMinute,
            destinationPlaceId,
            [actor.Id],
            scheduled.CauseIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scheduled_event_id"] = scheduled.Id,
                ["origin"] = origin,
                ["destination"] = destinationPlaceId,
            });
    }

    private WorldEvent AppendScheduledEvent(ScheduledWorldEvent scheduled)
    {
        var details = scheduled.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        details["scheduled_event_id"] = scheduled.Id;
        details["scheduled_phase"] = ToPhaseId(scheduled.Phase);
        return AppendEvent(
            scheduled.Kind,
            scheduled.DueMinute,
            scheduled.LocationId,
            [scheduled.StableSubjectId],
            scheduled.CauseIds,
            details);
    }

    private WorldEvent? InterruptRunningTravel(string actorId, IReadOnlyList<string> causeIds)
    {
        var action = FindActiveAction(actorId);
        if (action is null || action.Status != ActionStatus.Running || action.Kind != WorldActionKind.Travel)
        {
            return null;
        }

        UpdateTravelProgress(action, State.CurrentMinute);
        if (action.Travel.PendingScheduledEventId is { } pendingId)
        {
            _ = State.RemoveScheduledEvent(pendingId);
        }

        var actor = GetActor(actorId);
        var interrupted = AppendEvent(
            "travel_interrupted",
            State.CurrentMinute,
            null,
            [actorId],
            causeIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["route_id"] = action.Travel.CurrentLeg.RouteId,
                ["progress_q1000"] = action.Travel.CurrentLegProgressQ1000.ToString(CultureInfo.InvariantCulture),
                ["elapsed_minutes"] = action.Travel.ElapsedMinutes.ToString(CultureInfo.InvariantCulture),
            });
        action.Status = ActionStatus.Interrupted;
        action.Phase = "interrupted";
        action.LastUpdatedMinute = State.CurrentMinute;
        action.Travel.PendingScheduledEventId = null;
        action.Travel.InterruptionEventId = interrupted.Id;
        if (actor.Transit is { } transit)
        {
            transit.ProgressQ1000 = action.Travel.CurrentLegProgressQ1000;
        }

        InvalidateActorDetailLevels([actorId]);

        return interrupted;
    }

    private void UpdateTravelProgress(ActionInstanceState action, long minute)
    {
        var travel = action.Travel;
        var elapsed = checked(minute - travel.SegmentStartedMinute);
        if (elapsed < 0 || elapsed > travel.SegmentRemainingMinutes)
        {
            throw new InvalidOperationException($"Travel progress for '{action.Id}' is outside its segment bounds.");
        }

        if (elapsed == 0)
        {
            return;
        }

        var remainingProgress = 1000 - travel.CurrentLegProgressQ1000;
        var gainedProgress = (int)((remainingProgress * elapsed) / travel.SegmentRemainingMinutes);
        travel.CurrentLegProgressQ1000 = Math.Min(999, travel.CurrentLegProgressQ1000 + gainedProgress);
        travel.ElapsedMinutes = checked(travel.ElapsedMinutes + elapsed);
        travel.SegmentRemainingMinutes -= (int)elapsed;
        travel.SegmentStartedMinute = minute;
    }

    private void ScheduleTravelCompletion(ActionInstanceState action, IReadOnlyList<string> causeIds)
    {
        var travel = action.Travel;
        var dueMinute = checked(State.CurrentMinute + travel.SegmentRemainingMinutes);
        var kind = travel.CurrentLegIndex == travel.Legs.Count - 1
            ? "travel_completed"
            : "travel_segment_completed";
        var scheduled = Schedule(
            dueMinute,
            ScheduledEventPhase.ArrivalAndDeparture,
            action.ActorId,
            kind,
            travel.CurrentLeg.ToPlaceId,
            causeIds: causeIds,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action_id"] = action.Id,
                ["leg_index"] = travel.CurrentLegIndex.ToString(CultureInfo.InvariantCulture),
            });
        travel.PendingScheduledEventId = scheduled.Id;
    }

    private IReadOnlyList<TravelLegState> FindTravelPath(
        string startPlaceId,
        string destinationPlaceId,
        TravelMode mode)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [startPlaceId] = 0 };
        var previous = new Dictionary<string, (string PlaceId, RouteDefinition Route)>(StringComparer.Ordinal);
        var frontier = new SortedSet<TravelPathCandidate>(TravelPathCandidateComparer.Instance)
        {
            new(startPlaceId, 0),
        };

        while (frontier.Count > 0)
        {
            var current = frontier.Min!;
            frontier.Remove(current);
            if (current.Cost != distances[current.PlaceId])
            {
                continue;
            }

            if (current.PlaceId == destinationPlaceId)
            {
                break;
            }

            foreach (var edge in State.GetRouteTraversals(current.PlaceId))
            {
                if (!edge.Route.MinutesByMode.ContainsKey(mode))
                {
                    continue;
                }

                var nextCost = checked(current.Cost + edge.Route.GetMinutes(mode));
                if (distances.TryGetValue(edge.NextPlaceId, out var knownCost) && knownCost <= nextCost)
                {
                    continue;
                }

                distances[edge.NextPlaceId] = nextCost;
                previous[edge.NextPlaceId] = (current.PlaceId, edge.Route);
                frontier.Add(new TravelPathCandidate(edge.NextPlaceId, nextCost));
            }
        }

        if (!distances.ContainsKey(destinationPlaceId))
        {
            throw new DomainCommandException("destination_unreachable", $"No {mode} route reaches '{destinationPlaceId}'.");
        }

        var legs = new List<TravelLegState>();
        var cursor = destinationPlaceId;
        while (cursor != startPlaceId)
        {
            var step = previous[cursor];
            legs.Add(MapTravelLeg(step.Route, step.PlaceId, cursor));
            cursor = step.PlaceId;
        }

        legs.Reverse();
        return legs;
    }

    private static TravelLegState MapTravelLeg(RouteDefinition route, string fromPlaceId, string toPlaceId)
    {
        return new TravelLegState(
            route.Id,
            fromPlaceId,
            toPlaceId,
            route.MinutesByMode.GetValueOrDefault(TravelMode.Walk),
            route.MinutesByMode.GetValueOrDefault(TravelMode.Horse),
            route.MinutesByMode.GetValueOrDefault(TravelMode.WithGroup));
    }

    private static int CalculateRemainingMinutes(TravelLegState leg, TravelMode mode, int progressQ1000)
    {
        var totalMinutes = leg.GetMinutes(mode);
        var remaining = (totalMinutes * (1000 - progressQ1000) + 999) / 1000;
        return Math.Max(1, remaining);
    }

    private ActionInstanceState GetAction(string actionId)
    {
        return State.Actions.TryGetValue(actionId, out var action)
            ? action
            : throw new DomainCommandException("unknown_action", $"Unknown action '{actionId}'.");
    }

    private ActionInstanceState? FindActiveAction(string actorId)
    {
        return State.GetActionsByActor(actorId)
            .Where(item => item.Status is ActionStatus.Running or ActionStatus.Interrupted)
            .FirstOrDefault();
    }

    private sealed record TravelPathCandidate(string PlaceId, int Cost);

    private sealed class TravelPathCandidateComparer : IComparer<TravelPathCandidate>
    {
        public static TravelPathCandidateComparer Instance { get; } = new();

        public int Compare(TravelPathCandidate? x, TravelPathCandidate? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var cost = x.Cost.CompareTo(y.Cost);
            return cost != 0 ? cost : StringComparer.Ordinal.Compare(x.PlaceId, y.PlaceId);
        }
    }
}
