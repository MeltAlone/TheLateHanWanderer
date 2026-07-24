using System.Globalization;

namespace LateHan.Core;

public sealed partial class WorldEngine
{
    public ActionResult Enter(string actorId, string destinationPlaceId)
    {
        var actor = GetActor(actorId);
        _ = GetPlace(destinationPlaceId);
        if (actor.IsInTransit)
        {
            throw new DomainCommandException("actor_in_transit", $"Actor '{actorId}' cannot enter a place while in transit.");
        }

        if (!AreAdjacent(actor.LocationId, destinationPlaceId))
        {
            throw new DomainCommandException(
                "not_adjacent",
                $"Place '{destinationPlaceId}' is not adjacent to '{actor.LocationId}'.");
        }

        if (FindActiveAction(actorId) is not null || HasPendingAccessRequest(actorId))
        {
            throw new DomainCommandException("actor_busy", $"Actor '{actorId}' already has an active action.");
        }

        var startMinute = State.CurrentMinute;
        var firstEventSequence = State.EventSequenceCursor;
        var requested = AppendEvent(
            "access_requested",
            startMinute,
            actor.LocationId,
            [actorId, destinationPlaceId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination"] = destinationPlaceId,
            });
        var advancement = AdvanceTimeTo(checked(startMinute + 5));
        if (advancement.InterruptEventIds.Count > 0)
        {
            var causes = new List<string> { requested.Id };
            causes.AddRange(advancement.InterruptEventIds);
            _ = AppendEvent(
                "access_interrupted",
                State.CurrentMinute,
                actor.PlaceId,
                [actorId, destinationPlaceId],
                causes,
                new Dictionary<string, string>(StringComparer.Ordinal));
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Interrupted);
        }

        var eligibility = EvaluateAccessEligibility(actor, destinationPlaceId);
        if (!eligibility.Allowed)
        {
            _ = AppendEvent(
                "access_refused",
                State.CurrentMinute,
                actor.LocationId,
                [actorId, destinationPlaceId],
                [requested.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = eligibility.Reason,
                    ["waited_minutes"] = (State.CurrentMinute - startMinute).ToString(CultureInfo.InvariantCulture),
                });
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Refused);
        }

        var decision = EvaluateAccess(actor, destinationPlaceId);
        if (!decision.Allowed)
        {
            if (decision.Reason == "place_closed" && GetAccessRule(destinationPlaceId).MayQueue)
            {
                return QueueAccess(actor, destinationPlaceId, startMinute, firstEventSequence, requested);
            }

            _ = AppendEvent(
                "access_refused",
                State.CurrentMinute,
                actor.LocationId,
                [actorId, destinationPlaceId],
                [requested.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = decision.Reason,
                    ["waited_minutes"] = (State.CurrentMinute - startMinute).ToString(CultureInfo.InvariantCulture),
                });
            return new ActionResult(
                startMinute,
                State.CurrentMinute,
                State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
                ActionStatus.Refused);
        }

        if (State.PlaceAccessStates.TryGetValue(destinationPlaceId, out var admissionState) &&
            admissionState.LastAdmissionMinute == State.CurrentMinute &&
            GetAccessRule(destinationPlaceId).MayQueue)
        {
            return QueueAccess(actor, destinationPlaceId, startMinute, firstEventSequence, requested);
        }

        var origin = actor.LocationId;
        State.MoveActorToPlace(actor.Id, destinationPlaceId);
        RecordAdmission(destinationPlaceId, actor.Id);
        MarkActorPositionDetailDirty(actor.Id, origin, null);
        _ = AppendEvent(
            "place_entered",
            State.CurrentMinute,
            destinationPlaceId,
            [actorId, destinationPlaceId],
            [requested.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_rule_id"] = GetPlace(destinationPlaceId).AccessRuleId,
                ["origin"] = origin,
            });
        return new ActionResult(
            startMinute,
            State.CurrentMinute,
            State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray());
    }

    private AccessDecision EvaluateAccess(ActorState actor, string destinationPlaceId)
    {
        var place = GetPlace(destinationPlaceId);
        if (!State.AccessRules.TryGetValue(place.AccessRuleId, out var rule))
        {
            throw new InvalidOperationException($"Place '{place.Id}' has unknown access rule '{place.AccessRuleId}'.");
        }

        if (State.PlaceAccessStates.TryGetValue(destinationPlaceId, out var placeState) && !placeState.Open)
        {
            return new AccessDecision(false, "place_closed");
        }

        return EvaluateAccessEligibility(actor, destinationPlaceId);
    }

    private AccessDecision EvaluateAccessEligibility(ActorState actor, string destinationPlaceId)
    {
        var place = GetPlace(destinationPlaceId);
        if (!State.AccessRules.TryGetValue(place.AccessRuleId, out var rule))
        {
            throw new InvalidOperationException($"Place '{place.Id}' has unknown access rule '{place.AccessRuleId}'.");
        }

        if (rule.Requirements.Count == 0)
        {
            return new AccessDecision(true, "public");
        }

        if (rule.Requirements.Contains("explicit_palace_escort", StringComparer.Ordinal))
        {
            return new AccessDecision(false, "explicit_palace_escort_required");
        }

        if (rule.Requirements.Contains("gate_open", StringComparer.Ordinal))
        {
            return new AccessDecision(true, "gate_open");
        }

        var controllerId = State.PlaceAccessStates.TryGetValue(destinationPlaceId, out var runtimeState)
            ? runtimeState.ControllerId ?? place.ControllerId
            : place.ControllerId;
        var isControllerMember = controllerId is not null && actor.Memberships.Any(
            membership => string.Equals(membership.OrganizationId, controllerId, StringComparison.Ordinal));
        if (isControllerMember || HasValidCredential(actor.Id, rule.Id))
        {
            return new AccessDecision(true, "authorized");
        }

        return new AccessDecision(false, $"requirements_not_met:{rule.Id}");
    }

    private bool HasValidCredential(string actorId, string accessRuleId) => State.Items.Values
        .Where(item => string.Equals(item.HolderId, actorId, StringComparison.Ordinal))
        .Where(item => item.ValidForAccessRuleIds.Contains(accessRuleId, StringComparer.Ordinal))
        .Any(item => item.ExpiresAtMinute is null || item.ExpiresAtMinute >= State.CurrentMinute);

    private bool AreAdjacent(string fromPlaceId, string toPlaceId) => State.Routes.Values.Any(route =>
        string.Equals(route.FromPlaceId, fromPlaceId, StringComparison.Ordinal) &&
        string.Equals(route.ToPlaceId, toPlaceId, StringComparison.Ordinal) ||
        route.Bidirectional &&
        string.Equals(route.ToPlaceId, fromPlaceId, StringComparison.Ordinal) &&
        string.Equals(route.FromPlaceId, toPlaceId, StringComparison.Ordinal));

    private WorldEvent ProcessPlaceAccessChange(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("place_id", out var placeId) ||
            !scheduled.Details.TryGetValue("open", out var openText) ||
            !bool.TryParse(openText, out var open) ||
            !State.Places.ContainsKey(placeId))
        {
            throw new InvalidOperationException($"Scheduled access event '{scheduled.Id}' has invalid details.");
        }

        if (!State.PlaceAccessStates.TryGetValue(placeId, out var placeState))
        {
            placeState = new PlaceAccessState(placeId, open: true, queueCount: 0, securityPosture: "normal");
            State.AddPlaceAccessState(placeState);
        }

        placeState.Open = open;
        if (scheduled.Details.TryGetValue("security_posture", out var securityPosture))
        {
            placeState.SecurityPosture = securityPosture;
        }

        if (scheduled.Details.TryGetValue("controller_id", out var controllerId))
        {
            placeState.ControllerId = string.IsNullOrWhiteSpace(controllerId) ? null : controllerId;
        }

        return AppendEvent(
            "place_access_changed",
            scheduled.DueMinute,
            placeId,
            [placeId],
            scheduled.CauseIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["open"] = open.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                ["scheduled_event_id"] = scheduled.Id,
                ["security_posture"] = placeState.SecurityPosture,
                ["controller_id"] = placeState.ControllerId ?? string.Empty,
            });
    }

    private ActionResult QueueAccess(
        ActorState actor,
        string destinationPlaceId,
        long startMinute,
        long firstEventSequence,
        WorldEvent requested)
    {
        var placeState = GetOrCreatePlaceAccessState(destinationPlaceId);
        placeState.QueueCount++;
        var queued = AppendEvent(
            "access_queued",
            State.CurrentMinute,
            actor.LocationId,
            [actor.Id, destinationPlaceId],
            [requested.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["policy_version"] = AccessQueuePolicy.Version,
                ["queue_position"] = placeState.QueueCount.ToString(CultureInfo.InvariantCulture),
                ["request_minute"] = startMinute.ToString(CultureInfo.InvariantCulture),
            });
        ScheduleAccessQueueReview(
            actor.Id,
            actor.LocationId,
            destinationPlaceId,
            startMinute,
            requested.Id,
            queued.Id,
            checked(State.CurrentMinute + AccessQueuePolicy.ReviewIntervalMinutes));
        return new ActionResult(
            startMinute,
            State.CurrentMinute,
            State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
            ActionStatus.Scheduled);
    }

    private WorldEvent ProcessAccessQueueReview(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("origin_place_id", out var originPlaceId) ||
            !scheduled.Details.TryGetValue("destination_place_id", out var destinationPlaceId) ||
            !scheduled.Details.TryGetValue("request_event_id", out var requestEventId) ||
            !scheduled.Details.TryGetValue("request_minute", out var requestMinuteText) ||
            !long.TryParse(requestMinuteText, NumberStyles.None, CultureInfo.InvariantCulture, out var requestMinute))
        {
            throw new InvalidOperationException($"Access queue review '{scheduled.Id}' has invalid details.");
        }

        if (!scheduled.Details.TryGetValue("actor_id", out var actorId))
        {
            throw new InvalidOperationException($"Access queue review '{scheduled.Id}' has no actor ID.");
        }

        var actor = GetActor(actorId);
        var placeState = GetOrCreatePlaceAccessState(destinationPlaceId);
        var waitedMinutes = scheduled.DueMinute - requestMinute;
        if (!string.Equals(actor.PlaceId, originPlaceId, StringComparison.Ordinal) || FindActiveAction(actor.Id) is not null)
        {
            placeState.QueueCount = Math.Max(0, placeState.QueueCount - 1);
            return AppendEvent(
                "access_queue_cancelled",
                scheduled.DueMinute,
                actor.PlaceId,
                [actor.Id, destinationPlaceId],
                [requestEventId, .. scheduled.CauseIds],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "actor_unavailable",
                    ["waited_minutes"] = waitedMinutes.ToString(CultureInfo.InvariantCulture),
                });
        }

        var eligibility = EvaluateAccessEligibility(actor, destinationPlaceId);
        if (!eligibility.Allowed)
        {
            placeState.QueueCount = Math.Max(0, placeState.QueueCount - 1);
            return AppendEvent(
                "access_refused",
                scheduled.DueMinute,
                originPlaceId,
                [actor.Id, destinationPlaceId],
                [requestEventId, .. scheduled.CauseIds],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = eligibility.Reason,
                    ["waited_minutes"] = waitedMinutes.ToString(CultureInfo.InvariantCulture),
                });
        }

        var decision = EvaluateAccess(actor, destinationPlaceId);
        var admissionAvailable = placeState.LastAdmissionMinute != scheduled.DueMinute;
        if (decision.Allowed && admissionAvailable)
        {
            placeState.QueueCount = Math.Max(0, placeState.QueueCount - 1);
            State.MoveActorToPlace(actor.Id, destinationPlaceId);
            RecordAdmission(destinationPlaceId, actor.Id);
            MarkActorPositionDetailDirty(actor.Id, originPlaceId, null);
            return AppendEvent(
                "place_entered",
                scheduled.DueMinute,
                destinationPlaceId,
                [actor.Id, destinationPlaceId],
                [requestEventId, .. scheduled.CauseIds],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["access_rule_id"] = GetPlace(destinationPlaceId).AccessRuleId,
                    ["origin"] = originPlaceId,
                    ["policy_version"] = AccessQueuePolicy.Version,
                    ["waited_minutes"] = waitedMinutes.ToString(CultureInfo.InvariantCulture),
                });
        }

        if (waitedMinutes >= AccessQueuePolicy.MaximumWaitMinutes)
        {
            placeState.QueueCount = Math.Max(0, placeState.QueueCount - 1);
            return AppendEvent(
                "access_refused",
                scheduled.DueMinute,
                originPlaceId,
                [actor.Id, destinationPlaceId],
                [requestEventId, .. scheduled.CauseIds],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = decision.Allowed ? "admission_capacity_timeout" : decision.Reason,
                    ["waited_minutes"] = waitedMinutes.ToString(CultureInfo.InvariantCulture),
                });
        }

        var reviewed = AppendEvent(
            "access_queue_reviewed",
            scheduled.DueMinute,
            originPlaceId,
            [actor.Id, destinationPlaceId],
            [requestEventId, .. scheduled.CauseIds],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["admission_available"] = admissionAvailable.ToString().ToLowerInvariant(),
                ["reason"] = decision.Reason,
                ["waited_minutes"] = waitedMinutes.ToString(CultureInfo.InvariantCulture),
            });
        ScheduleAccessQueueReview(
            actor.Id,
            originPlaceId,
            destinationPlaceId,
            requestMinute,
            requestEventId,
            reviewed.Id,
            checked(scheduled.DueMinute + AccessQueuePolicy.ReviewIntervalMinutes));
        return reviewed;
    }

    private void ScheduleAccessQueueReview(
        string actorId,
        string originPlaceId,
        string destinationPlaceId,
        long requestMinute,
        string requestEventId,
        string causeId,
        long dueMinute)
    {
        _ = Schedule(
            dueMinute,
            ScheduledEventPhase.ArrivalAndDeparture,
            $"access-request.{requestEventId}",
            "access_queue_review",
            originPlaceId,
            causeIds: [causeId],
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination_place_id"] = destinationPlaceId,
                ["actor_id"] = actorId,
                ["origin_place_id"] = originPlaceId,
                ["request_event_id"] = requestEventId,
                ["request_minute"] = requestMinute.ToString(CultureInfo.InvariantCulture),
            });
    }

    private AccessRuleDefinition GetAccessRule(string destinationPlaceId)
    {
        var place = GetPlace(destinationPlaceId);
        return State.AccessRules.TryGetValue(place.AccessRuleId, out var rule)
            ? rule
            : throw new InvalidOperationException($"Place '{place.Id}' has unknown access rule '{place.AccessRuleId}'.");
    }

    private PlaceAccessState GetOrCreatePlaceAccessState(string placeId)
    {
        if (State.PlaceAccessStates.TryGetValue(placeId, out var placeState))
        {
            return placeState;
        }

        placeState = new PlaceAccessState(placeId, true, 0, "normal", GetPlace(placeId).ControllerId);
        State.AddPlaceAccessState(placeState);
        return placeState;
    }

    private void RecordAdmission(string placeId, string actorId)
    {
        var placeState = GetOrCreatePlaceAccessState(placeId);
        placeState.LastAdmissionMinute = State.CurrentMinute;
        placeState.LastAdmittedActorId = actorId;
    }

    private bool HasPendingAccessRequest(string actorId) => State.ScheduledEvents.Any(item =>
        item.Kind == "access_queue_review" &&
        item.Details.TryGetValue("actor_id", out var queuedActorId) &&
        string.Equals(queuedActorId, actorId, StringComparison.Ordinal));

    internal sealed record AccessDecision(bool Allowed, string Reason);
}
