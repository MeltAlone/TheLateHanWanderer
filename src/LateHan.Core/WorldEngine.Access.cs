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

        if (FindActiveAction(actorId) is not null)
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

        var decision = EvaluateAccess(actor, destinationPlaceId);
        if (!decision.Allowed)
        {
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

        var origin = actor.LocationId;
        actor.LocationId = destinationPlaceId;
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

        var isControllerMember = place.ControllerId is not null && actor.Memberships.Any(
            membership => string.Equals(membership.OrganizationId, place.ControllerId, StringComparison.Ordinal));
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
            });
    }

    internal sealed record AccessDecision(bool Allowed, string Reason);
}
