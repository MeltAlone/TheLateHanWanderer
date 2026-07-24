using System.Collections.ObjectModel;
using System.Globalization;

namespace LateHan.Core;

public sealed record LocationView(
    long Minute,
    string PlaceId,
    string PlaceName,
    IReadOnlyList<(string Id, string Name)> VisibleActors);

public sealed record StatusView(
    long Minute,
    string ActorId,
    string ActorName,
    string PlaceId,
    string PlaceName,
    IReadOnlyList<(string Id, string Name)> HeldItems,
    IReadOnlyList<CommitmentState> OpenCommitments);

public enum ActionStatus
{
    Scheduled,
    Running,
    Completed,
    PartiallyCompleted,
    Refused,
    Blocked,
    Interrupted,
    Cancelled,
    Invalid,
}

public sealed record ActionResult(
    long StartMinute,
    long EndMinute,
    IReadOnlyList<WorldEvent> Events,
    ActionStatus Status = ActionStatus.Completed);

public sealed partial class WorldEngine
{
    public WorldEngine(WorldState state)
    {
        State = state;
    }

    public WorldState State { get; }

    public LocationView Look(string? actorId = null)
    {
        var actor = GetActor(actorId ?? State.PlayerActorId);
        if (actor.Transit is { } transit)
        {
            var visibleTravelers = State.Actors.Values
                .Where(candidate => candidate.Id != actor.Id)
                .Where(candidate => candidate.Transit?.RouteId == transit.RouteId)
                .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
                .Select(candidate => (candidate.Id, candidate.Name))
                .ToArray();
            return new LocationView(
                State.CurrentMinute,
                transit.RouteId,
                $"途中：{transit.FromPlaceId} -> {transit.ToPlaceId} ({transit.ProgressQ1000}/1000)",
                visibleTravelers);
        }

        var place = GetPlace(actor.LocationId);
        var visibleActors = State.Actors.Values
            .Where(candidate => candidate.PlaceId == actor.PlaceId && candidate.Id != actor.Id)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(candidate => (candidate.Id, candidate.Name))
            .ToArray();

        return new LocationView(State.CurrentMinute, place.Id, place.Name, visibleActors);
    }

    public StatusView Status(string? actorId = null)
    {
        var actor = GetActor(actorId ?? State.PlayerActorId);
        var positionId = actor.Transit?.RouteId ?? actor.LocationId;
        var positionName = actor.Transit is { } transit
            ? $"途中：{transit.FromPlaceId} -> {transit.ToPlaceId} ({transit.ProgressQ1000}/1000)"
            : GetPlace(actor.LocationId).Name;
        var heldItems = State.Items.Values
            .Where(item => item.HolderId == actor.Id)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => (item.Id, item.Name))
            .ToArray();
        var commitments = State.Commitments.Values
            .Where(commitment => commitment.DebtorId == actor.Id && commitment.Status == "open")
            .OrderBy(commitment => commitment.DueMinute)
            .ThenBy(commitment => commitment.Id, StringComparer.Ordinal)
            .ToArray();

        return new StatusView(State.CurrentMinute, actor.Id, actor.Name, positionId, positionName, heldItems, commitments);
    }

    public ActionResult Move(string actorId, string destinationPlaceId, TravelMode mode)
    {
        var startMinute = State.CurrentMinute;
        var firstEventSequence = State.EventSequenceCursor;
        var action = BeginTravel(actorId, destinationPlaceId, mode);
        _ = AdvanceAction(action.Id);
        var events = State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray();
        return new ActionResult(startMinute, State.CurrentMinute, events, action.Status);
    }

    public ActionResult Deliver(string actorId, string itemId, string recipientId)
    {
        var firstEventSequence = State.EventSequenceCursor;
        var actor = GetActor(actorId);
        var recipient = GetActor(recipientId);
        if (actor.PlaceId is null)
        {
            throw new DomainCommandException("actor_in_transit", $"Actor '{actorId}' cannot deliver while in transit.");
        }

        if (recipient.PlaceId is null || actor.PlaceId != recipient.PlaceId)
        {
            throw new DomainCommandException("recipient_not_present", $"Recipient '{recipientId}' is not at '{actor.PlaceId}'.");
        }

        if (!State.Items.TryGetValue(itemId, out var item))
        {
            throw new DomainCommandException("unknown_item", $"Unknown item '{itemId}'.");
        }

        if (item.HolderId != actorId)
        {
            throw new DomainCommandException("item_not_held", $"Actor '{actorId}' does not hold '{itemId}'.");
        }

        var startMinute = State.CurrentMinute;
        var started = AppendEvent(
            "delivery_started",
            startMinute,
            actor.LocationId,
            [actorId, recipientId, itemId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));

        var advancement = AdvanceTimeTo(checked(startMinute + 5));
        if (advancement.InterruptEventIds.Count > 0)
        {
            var causes = new List<string> { started.Id };
            causes.AddRange(advancement.InterruptEventIds);
            _ = AppendEvent(
                "delivery_interrupted",
                State.CurrentMinute,
                actor.PlaceId,
                [actorId, recipientId, itemId],
                causes,
                new Dictionary<string, string>(StringComparer.Ordinal));
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Interrupted);
        }

        if (actor.PlaceId is null || recipient.PlaceId is null || actor.PlaceId != recipient.PlaceId)
        {
            _ = AppendEvent(
                "delivery_failed",
                State.CurrentMinute,
                actor.PlaceId,
                [actorId, recipientId, itemId],
                [started.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "recipient_left",
                });
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Blocked);
        }

        item.HolderId = recipientId;
        var transferred = AppendEvent(
            "item_transferred",
            State.CurrentMinute,
            actor.LocationId,
            [actorId, recipientId, itemId],
            [started.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = actorId,
                ["to"] = recipientId,
            });

        var events = new List<WorldEvent> { started, transferred };
        var matchingCommitments = State.Commitments.Values
            .Where(commitment => commitment.Status == "open")
            .Where(commitment => commitment.DebtorId == actorId)
            .Where(commitment => commitment.Action == "deliver")
            .Where(commitment => commitment.TargetId == itemId && commitment.RecipientId == recipientId)
            .OrderBy(commitment => commitment.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var commitment in matchingCommitments)
        {
            commitment.Status = "completed";
            InvalidateActorDetailLevels([commitment.DebtorId, commitment.CreditorId, commitment.RecipientId]);
            events.Add(AppendEvent(
                "commitment_completed",
                State.CurrentMinute,
                actor.LocationId,
                [commitment.Id, actorId, recipientId],
                [transferred.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["completion_kind"] = "delivered",
                }));
        }

        if (string.Equals(item.IntendedRecipientId, recipientId, StringComparison.Ordinal))
        {
            foreach (var belief in State.Beliefs.Values
                         .Where(candidate => string.Equals(candidate.HolderId, recipientId, StringComparison.Ordinal))
                         .Where(candidate => item.PropositionIds.Contains(candidate.PropositionId, StringComparer.Ordinal))
                         .OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
            {
                belief.ConfidenceBp = 8500;
                belief.Source = "official_document";
                belief.AcquiredAtMinute = State.CurrentMinute;
                belief.SourceEventId = transferred.Id;
                var updated = AppendEvent(
                    "belief_updated",
                    State.CurrentMinute,
                    actor.LocationId,
                    [belief.Id, recipientId],
                    [transferred.Id],
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["confidence_bp"] = belief.ConfidenceBp.ToString(CultureInfo.InvariantCulture),
                        ["proposition_id"] = belief.PropositionId,
                        ["source"] = belief.Source,
                    });
                events.Add(updated);

                foreach (var plan in State.Plans.Values
                             .Where(candidate => candidate.Status == PlanStatus.Active)
                             .Where(candidate => candidate.EvaluationRule == PlanEvaluationRule.WrittenReportInspection)
                             .Where(candidate => string.Equals(candidate.OwnerId, recipientId, StringComparison.Ordinal))
                             .Where(candidate => string.Equals(candidate.TriggerItemId, item.Id, StringComparison.Ordinal))
                             .Where(candidate => candidate.BeliefRequirementIds.Contains(belief.Id, StringComparer.Ordinal))
                             .OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
                {
                    if (plan.PendingScheduledEventId is { } pendingId)
                    {
                        _ = State.RemoveScheduledEvent(pendingId);
                        plan.PendingScheduledEventId = null;
                    }

                    SchedulePlanEvaluation(plan, State.CurrentMinute, [updated.Id]);
                }
            }

            _ = AdvanceTimeTo(State.CurrentMinute);
        }

        return new ActionResult(
            startMinute,
            State.CurrentMinute,
            State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray());
    }

    public ActionResult Tell(string actorId, string recipientId, string propositionId)
    {
        var actor = GetActor(actorId);
        var recipient = GetActor(recipientId);
        if (actor.PlaceId is null)
        {
            throw new DomainCommandException("actor_in_transit", $"Actor '{actorId}' cannot speak while in transit.");
        }

        if (recipient.PlaceId is null || actor.PlaceId != recipient.PlaceId)
        {
            throw new DomainCommandException("recipient_not_present", $"Recipient '{recipientId}' is not at '{actor.PlaceId}'.");
        }

        var startMinute = State.CurrentMinute;
        var firstEventSequence = State.EventSequenceCursor;
        var created = AppendEvent(
            "message_created",
            startMinute,
            actor.LocationId,
            [actorId, recipientId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["proposition_id"] = propositionId,
            });

        var advancement = AdvanceTimeTo(checked(startMinute + 5));
        if (advancement.InterruptEventIds.Count > 0)
        {
            var causes = new List<string> { created.Id };
            causes.AddRange(advancement.InterruptEventIds);
            _ = AppendEvent(
                "message_interrupted",
                State.CurrentMinute,
                actor.PlaceId,
                [actorId, recipientId],
                causes,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["proposition_id"] = propositionId,
                });
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Interrupted);
        }

        if (actor.PlaceId is null || recipient.PlaceId is null || actor.PlaceId != recipient.PlaceId)
        {
            _ = AppendEvent(
                "message_delivery_failed",
                State.CurrentMinute,
                actor.PlaceId,
                [actorId, recipientId],
                [created.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["proposition_id"] = propositionId,
                    ["reason"] = "recipient_left",
                });
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Blocked);
        }
        var senderBelief = State.Beliefs.Values
            .Where(item => string.Equals(item.HolderId, actorId, StringComparison.Ordinal))
            .Where(item => string.Equals(item.PropositionId, propositionId, StringComparison.Ordinal))
            .OrderByDescending(item => item.ConfidenceBp)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        var confidenceBp = senderBelief?.ConfidenceBp ?? 6000;
        var parentMessageId = senderBelief?.SourceEventId is { } sourceEventId
            ? State.Messages.Values.FirstOrDefault(message => message.DeliveredEventId == sourceEventId)?.Id
            : null;
        var messageId = $"message.{created.Sequence:D8}";
        var delivered = AppendEvent(
            "message_delivered",
            State.CurrentMinute,
            actor.LocationId,
            [messageId, actorId, recipientId],
            [created.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["confidence_bp"] = confidenceBp.ToString(CultureInfo.InvariantCulture),
                ["parent_message_id"] = parentMessageId ?? string.Empty,
                ["proposition_id"] = propositionId,
            });
        State.AddMessage(new MessageState(
            messageId,
            propositionId,
            actorId,
            recipientId,
            confidenceBp,
            startMinute,
            created.Id,
            delivered.Id,
            parentMessageId));
        InvalidateActorDetailLevels([actorId, recipientId]);
        _ = Schedule(
            checked(startMinute + SimulationDetailPolicy.RecentMessageRetentionMinutes + 1),
            ScheduledEventPhase.SummaryAndNotification,
            recipientId,
            "detail_message_retention_expired",
            actor.LocationId,
            causeIds: [delivered.Id],
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message_id"] = messageId,
                ["recipient_id"] = recipientId,
                ["sender_id"] = actorId,
            });

        var belief = State.Beliefs.Values
            .Where(item => string.Equals(item.HolderId, recipientId, StringComparison.Ordinal))
            .FirstOrDefault(item => string.Equals(item.PropositionId, propositionId, StringComparison.Ordinal));
        if (belief is null)
        {
            belief = new BeliefState(
                $"belief.message.{delivered.Sequence:D8}",
                recipientId,
                propositionId,
                confidenceBp,
                "direct_message",
                State.CurrentMinute,
                delivered.Id);
            State.AddBelief(belief);
        }
        else
        {
            belief.ConfidenceBp = confidenceBp;
            belief.Source = "direct_message";
            belief.AcquiredAtMinute = State.CurrentMinute;
            belief.SourceEventId = delivered.Id;
        }

        _ = AppendEvent(
            "belief_updated",
            State.CurrentMinute,
            actor.LocationId,
            [belief.Id, recipientId],
            [delivered.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["confidence_bp"] = confidenceBp.ToString(CultureInfo.InvariantCulture),
                ["proposition_id"] = propositionId,
                ["source"] = "direct_message",
            });

        var completedTargets = State.Commitments.Values
            .Where(commitment => commitment.Status == "completed")
            .Select(commitment => commitment.Id)
            .ToHashSet(StringComparer.Ordinal);
        var reportCommitments = State.Commitments.Values
            .Where(commitment => commitment.Status == "open")
            .Where(commitment => commitment.DebtorId == actorId && commitment.RecipientId == recipientId)
            .Where(commitment => commitment.Action == "report_delivery_result")
            .Where(commitment => completedTargets.Contains(commitment.TargetId))
            .OrderBy(commitment => commitment.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var commitment in reportCommitments)
        {
            commitment.Status = "completed";
            InvalidateActorDetailLevels([commitment.DebtorId, commitment.CreditorId, commitment.RecipientId]);
            _ = AppendEvent(
                "commitment_completed",
                State.CurrentMinute,
                actor.LocationId,
                [commitment.Id, actorId, recipientId],
                [delivered.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["completion_kind"] = "reported",
                });
        }

        return new ActionResult(
            startMinute,
            State.CurrentMinute,
            State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray());
    }

    public ActionResult Wait(long minutes, string? actorId = null)
    {
        if (minutes <= 0)
        {
            throw new DomainCommandException("invalid_duration", "Wait duration must be positive.");
        }

        var actor = GetActor(actorId ?? State.PlayerActorId);
        var startMinute = State.CurrentMinute;
        var targetMinute = checked(startMinute + minutes);
        var started = AppendEvent(
            "wait_started",
            startMinute,
            actor.PlaceId,
            [actor.Id],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["duration_minutes"] = minutes.ToString(CultureInfo.InvariantCulture),
            });
        var advancement = AdvanceTimeTo(targetMinute);
        var events = new List<WorldEvent> { started };
        events.AddRange(advancement.Events);
        if (advancement.InterruptEventIds.Count > 0)
        {
            var causes = new List<string> { started.Id };
            causes.AddRange(advancement.InterruptEventIds);
            events.Add(AppendEvent(
                "wait_interrupted",
                State.CurrentMinute,
                actor.PlaceId,
                [actor.Id],
                causes,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["remaining_minutes"] = (targetMinute - State.CurrentMinute).ToString(CultureInfo.InvariantCulture),
                }));

            return new ActionResult(startMinute, State.CurrentMinute, events, ActionStatus.Interrupted);
        }

        var completed = AppendEvent(
            "wait_completed",
            State.CurrentMinute,
            actor.PlaceId,
            [actor.Id],
            [started.Id],
            new Dictionary<string, string>(StringComparer.Ordinal));
        events.Add(completed);
        return new ActionResult(startMinute, State.CurrentMinute, events);
    }

    public ScheduledWorldEvent Schedule(
        long dueMinute,
        ScheduledEventPhase phase,
        string stableSubjectId,
        string kind,
        string? locationId = null,
        bool interruptsPlayer = false,
        IReadOnlyList<string>? causeIds = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ValidateScheduleRequest(dueMinute, phase, stableSubjectId, kind);

        var sequence = State.NextScheduledEventSequence;
        var scheduledEvent = ScheduledWorldEvent.Create(
            sequence,
            dueMinute,
            phase,
            stableSubjectId,
            kind,
            locationId,
            interruptsPlayer,
            causeIds,
            details);
        State.AddScheduledEvent(scheduledEvent);
        State.NextScheduledEventSequence++;
        return scheduledEvent;
    }

    public ScheduledWorldEvent ScheduleExternalIntervention(
        long dueMinute,
        ScheduledEventPhase phase,
        string stableSubjectId,
        string kind,
        string? locationId = null,
        bool interruptsPlayer = false,
        IReadOnlyList<string>? causeIds = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ValidateScheduleRequest(dueMinute, phase, stableSubjectId, kind);

        var intervention = AppendEvent(
            "debug_intervention",
            State.CurrentMinute,
            locationId,
            [stableSubjectId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["scheduled_due_minute"] = dueMinute.ToString(CultureInfo.InvariantCulture),
                ["scheduled_kind"] = kind,
            });
        State.ReplayModified = true;
        var scheduledCauses = new List<string> { intervention.Id };
        scheduledCauses.AddRange(causeIds ?? []);
        return Schedule(
            dueMinute,
            phase,
            stableSubjectId,
            kind,
            locationId,
            interruptsPlayer,
            scheduledCauses,
            details);
    }

    private void ValidateScheduleRequest(
        long dueMinute,
        ScheduledEventPhase phase,
        string stableSubjectId,
        string kind)
    {
        if (dueMinute < State.CurrentMinute)
        {
            throw new DomainCommandException(
                "scheduled_event_in_past",
                $"Cannot schedule an event at minute {dueMinute} when the world is at {State.CurrentMinute}.");
        }

        if (!Enum.IsDefined(phase) || string.IsNullOrWhiteSpace(stableSubjectId) || string.IsNullOrWhiteSpace(kind))
        {
            throw new DomainCommandException(
                "invalid_scheduled_event",
                "A scheduled event requires a known phase, stable subject ID, and kind.");
        }
    }

    private TimeAdvancementResult AdvanceTimeTo(long targetMinute, string? stopWhenActionId = null)
    {
        var events = new List<WorldEvent>();
        while (State.PeekScheduledEvent() is { } next && next.DueMinute <= targetMinute)
        {
            var batchMinute = next.DueMinute;
            State.CurrentMinute = batchMinute;
            var interruptIds = new List<string>();
            while (State.PeekScheduledEvent() is { DueMinute: var dueMinute } scheduled && dueMinute == batchMinute)
            {
                if (!State.RemoveScheduledEvent(scheduled))
                {
                    throw new InvalidOperationException($"Could not remove scheduled event '{scheduled.Id}'.");
                }

                var occurred = ProcessScheduledEvent(scheduled);
                events.AddRange(occurred);
                if (scheduled.Kind is "travel_disrupted" or "actor_incapacitated" or "actor_removed")
                {
                    if (InterruptRunningTravel(scheduled.StableSubjectId, [occurred[0].Id]) is { } subjectInterrupted)
                    {
                        events.Add(subjectInterrupted);
                    }
                }

                if (scheduled.InterruptsPlayer)
                {
                    interruptIds.Add(occurred[0].Id);
                    if (!string.Equals(scheduled.StableSubjectId, State.PlayerActorId, StringComparison.Ordinal) &&
                        InterruptRunningTravel(State.PlayerActorId, [occurred[0].Id]) is { } interrupted)
                    {
                        events.Add(interrupted);
                    }
                }
            }

            if (interruptIds.Count > 0)
            {
                return new TimeAdvancementResult(events, interruptIds);
            }

            if (stopWhenActionId is not null &&
                State.Actions.TryGetValue(stopWhenActionId, out var monitoredAction) &&
                monitoredAction.Status != ActionStatus.Running)
            {
                return new TimeAdvancementResult(events, []);
            }
        }

        State.CurrentMinute = targetMinute;
        return new TimeAdvancementResult(events, []);
    }

    private ActorState GetActor(string actorId)
    {
        return State.Actors.TryGetValue(actorId, out var actor)
            ? actor
            : throw new DomainCommandException("unknown_actor", $"Unknown actor '{actorId}'.");
    }

    private PlaceDefinition GetPlace(string placeId)
    {
        return State.Places.TryGetValue(placeId, out var place)
            ? place
            : throw new DomainCommandException("unknown_place", $"Unknown place '{placeId}'.");
    }

    private IEnumerable<(string NextPlaceId, RouteDefinition Route)> GetEdges(string placeId, TravelMode mode)
    {
        foreach (var route in State.Routes.Values.OrderBy(route => route.Id, StringComparer.Ordinal))
        {
            if (!route.MinutesByMode.ContainsKey(mode))
            {
                continue;
            }

            if (route.FromPlaceId == placeId)
            {
                yield return (route.ToPlaceId, route);
            }
            else if (route.Bidirectional && route.ToPlaceId == placeId)
            {
                yield return (route.FromPlaceId, route);
            }
        }
    }

    private WorldEvent AppendEvent(
        string type,
        long minute,
        string? locationId,
        IReadOnlyList<string> subjectIds,
        IReadOnlyList<string> causeIds,
        IReadOnlyDictionary<string, string> details)
    {
        var sequence = State.NextEventSequence++;
        var worldEvent = new WorldEvent(
            sequence,
            $"event.{sequence:D8}",
            type,
            minute,
            locationId,
            subjectIds.ToArray(),
            causeIds.ToArray(),
            new ReadOnlyDictionary<string, string>(
                new SortedDictionary<string, string>(
                    details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal)));
        State.AddEvent(worldEvent);
        return worldEvent;
    }

    private static string ToModeId(TravelMode mode) => mode switch
    {
        TravelMode.Walk => "walk",
        TravelMode.Horse => "horse",
        TravelMode.WithGroup => "with-group",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static string ToPhaseId(ScheduledEventPhase phase) => phase switch
    {
        ScheduledEventPhase.DeathOrRemoval => "death_or_removal",
        ScheduledEventPhase.AccessAndControlChange => "access_and_control_change",
        ScheduledEventPhase.ArrivalAndDeparture => "arrival_and_departure",
        ScheduledEventPhase.DeliveryAndTransfer => "delivery_and_transfer",
        ScheduledEventPhase.PerceptionAndBelief => "perception_and_belief",
        ScheduledEventPhase.PlanEvaluation => "plan_evaluation",
        ScheduledEventPhase.SummaryAndNotification => "summary_and_notification",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    private sealed record TimeAdvancementResult(
        IReadOnlyList<WorldEvent> Events,
        IReadOnlyList<string> InterruptEventIds);

}
