using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LateHan.Core;

public enum SimulationDetailLevel
{
    L0,
    L1,
    L2,
    L3,
}

public static class SimulationDetailPolicy
{
    public const string Version = "attention-and-causal-debt.v1";

    public const long RecentMessageRetentionMinutes = 7 * 24 * 60;
}

public sealed record DetailLevelAssessment(
    string ActorId,
    SimulationDetailLevel CurrentLevel,
    SimulationDetailLevel RecommendedLevel,
    IReadOnlyList<string> Reasons);

public sealed record DetailRebalanceResult(
    IReadOnlyList<DetailLevelAssessment> Assessments,
    IReadOnlyList<WorldEvent> Events);

public sealed class GroupState
{
    public GroupState(
        string id,
        string name,
        string kind,
        int count,
        string locationId,
        string? organizationId,
        string promotionProfileId,
        long foodStockUnits = 0,
        int dailyFoodProductionPerThousand = 0,
        int dailyFoodConsumptionPerThousand = 0,
        long lastRemoteSettlementMinute = 0,
        long foodProductionRemainder = 0,
        long foodDemandRemainder = 0,
        long cumulativeFoodProduced = 0,
        long cumulativeFoodDemand = 0,
        long cumulativeFoodConsumed = 0,
        long cumulativeFoodUnmet = 0,
        int foodShortageBp = 0)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (foodStockUnits < 0 ||
            dailyFoodProductionPerThousand < 0 ||
            dailyFoodConsumptionPerThousand < 0 ||
            lastRemoteSettlementMinute < 0 ||
            foodProductionRemainder < 0 ||
            foodDemandRemainder < 0 ||
            cumulativeFoodProduced < 0 ||
            cumulativeFoodDemand < 0 ||
            cumulativeFoodConsumed < 0 ||
            cumulativeFoodUnmet < 0 ||
            foodShortageBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(foodStockUnits), "Remote aggregate values cannot be negative or invalid.");
        }

        if (foodProductionRemainder >= RemoteSimulationPolicy.FlowDenominator ||
            foodDemandRemainder >= RemoteSimulationPolicy.FlowDenominator ||
            cumulativeFoodDemand != cumulativeFoodConsumed + cumulativeFoodUnmet ||
            (Int128)foodStockUnits + cumulativeFoodConsumed < cumulativeFoodProduced)
        {
            throw new ArgumentException("Remote aggregate ledger values are inconsistent.", nameof(foodProductionRemainder));
        }

        Id = id;
        Name = name;
        Kind = kind;
        Count = count;
        LocationId = locationId;
        OrganizationId = organizationId;
        PromotionProfileId = promotionProfileId;
        FoodStockUnits = foodStockUnits;
        DailyFoodProductionPerThousand = dailyFoodProductionPerThousand;
        DailyFoodConsumptionPerThousand = dailyFoodConsumptionPerThousand;
        LastRemoteSettlementMinute = lastRemoteSettlementMinute;
        FoodProductionRemainder = foodProductionRemainder;
        FoodDemandRemainder = foodDemandRemainder;
        CumulativeFoodProduced = cumulativeFoodProduced;
        CumulativeFoodDemand = cumulativeFoodDemand;
        CumulativeFoodConsumed = cumulativeFoodConsumed;
        CumulativeFoodUnmet = cumulativeFoodUnmet;
        FoodShortageBp = foodShortageBp;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public int Count { get; internal set; }

    public string LocationId { get; internal set; }

    public string? OrganizationId { get; }

    public string PromotionProfileId { get; }

    public long FoodStockUnits { get; internal set; }

    public int DailyFoodProductionPerThousand { get; }

    public int DailyFoodConsumptionPerThousand { get; }

    public long LastRemoteSettlementMinute { get; internal set; }

    public long FoodProductionRemainder { get; internal set; }

    public long FoodDemandRemainder { get; internal set; }

    public long CumulativeFoodProduced { get; internal set; }

    public long CumulativeFoodDemand { get; internal set; }

    public long CumulativeFoodConsumed { get; internal set; }

    public long CumulativeFoodUnmet { get; internal set; }

    public int FoodShortageBp { get; internal set; }

    public SimulationDetailLevel DetailLevel => SimulationDetailLevel.L3;
}

public sealed record PromotionResult(ActorState Actor, WorldEvent Event);

public sealed partial class WorldEngine
{
    public DetailLevelAssessment AssessActorDetailLevel(string actorId)
    {
        var actor = GetActor(actorId);
        var player = GetActor(State.PlayerActorId);
        var reasons = new List<string>();
        var recommendedLevel = SimulationDetailLevel.L2;

        if (string.Equals(actor.Id, player.Id, StringComparison.Ordinal))
        {
            reasons.Add("player_actor");
            recommendedLevel = SimulationDetailLevel.L0;
        }

        if (!string.Equals(actor.Id, player.Id, StringComparison.Ordinal) &&
            OccupiesSameAttentionSpace(actor, player))
        {
            reasons.Add("player_colocated");
            recommendedLevel = SimulationDetailLevel.L0;
        }
        else if (recommendedLevel != SimulationDetailLevel.L0 && IsAdjacentToPlayer(actor, player))
        {
            reasons.Add("player_adjacent");
            recommendedLevel = SimulationDetailLevel.L1;
        }

        if (FindActiveAction(actor.Id) is not null)
        {
            reasons.Add("active_action");
            recommendedLevel = MoreDetailed(recommendedLevel, SimulationDetailLevel.L1);
        }

        if (actor.IsInTransit)
        {
            reasons.Add("in_transit");
            recommendedLevel = MoreDetailed(recommendedLevel, SimulationDetailLevel.L1);
        }

        if (State.Plans.Values.Any(item =>
                string.Equals(item.OwnerId, actor.Id, StringComparison.Ordinal) &&
                item.Status is PlanStatus.Active or PlanStatus.Running))
        {
            reasons.Add("active_plan");
            recommendedLevel = MoreDetailed(recommendedLevel, SimulationDetailLevel.L1);
        }

        if (State.Commitments.Values.Any(item =>
                string.Equals(item.Status, "open", StringComparison.Ordinal) &&
                (string.Equals(item.DebtorId, actor.Id, StringComparison.Ordinal) ||
                 string.Equals(item.CreditorId, actor.Id, StringComparison.Ordinal) ||
                 string.Equals(item.RecipientId, actor.Id, StringComparison.Ordinal))))
        {
            reasons.Add("open_commitment");
            recommendedLevel = MoreDetailed(recommendedLevel, SimulationDetailLevel.L1);
        }

        if (State.Messages.Values.Any(item =>
                item.CreatedAtMinute <= State.CurrentMinute &&
                State.CurrentMinute - item.CreatedAtMinute <= SimulationDetailPolicy.RecentMessageRetentionMinutes &&
                (string.Equals(item.SenderId, actor.Id, StringComparison.Ordinal) ||
                 string.Equals(item.RecipientId, actor.Id, StringComparison.Ordinal))))
        {
            reasons.Add("recent_message");
            recommendedLevel = MoreDetailed(recommendedLevel, SimulationDetailLevel.L1);
        }

        if (reasons.Count == 0)
        {
            reasons.Add("background_named_actor");
        }

        return new DetailLevelAssessment(actor.Id, actor.DetailLevel, recommendedLevel, reasons.ToArray());
    }

    public DetailRebalanceResult RebalanceActorDetailLevels(
        IEnumerable<string>? actorIds = null,
        IReadOnlyList<string>? causeIds = null)
    {
        var candidateIds = (actorIds ?? State.Actors.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var assessments = candidateIds.Select(AssessActorDetailLevel).ToArray();
        var events = new List<WorldEvent>();
        foreach (var assessment in assessments.Where(item => item.CurrentLevel != item.RecommendedLevel))
        {
            var actor = GetActor(assessment.ActorId);
            events.Add(ApplyActorDetailLevel(
                actor,
                assessment.RecommendedLevel,
                assessment.Reasons,
                causeIds ?? []));
        }

        foreach (var actorId in candidateIds)
        {
            State.ClearActorDetailDirty(actorId);
        }

        return new DetailRebalanceResult(assessments, events);
    }

    public DetailRebalanceResult RebalanceDirtyActorDetailLevels(IReadOnlyList<string>? causeIds = null)
    {
        return RebalanceActorDetailLevels(State.DetailDirtyActorIds, causeIds);
    }

    public void InvalidateActorDetailLevels(IEnumerable<string> actorIds)
    {
        var validatedIds = actorIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        foreach (var actorId in validatedIds)
        {
            _ = GetActor(actorId);
        }

        foreach (var actorId in validatedIds)
        {
            State.MarkActorDetailDirty(actorId);
        }
    }

    public PromotionResult PromoteGroupMember(
        string groupId,
        IReadOnlyList<string>? causeIds = null,
        SimulationDetailLevel detailLevel = SimulationDetailLevel.L0)
    {
        if (!State.Groups.TryGetValue(groupId, out var group))
        {
            throw new DomainCommandException("unknown_group", $"Unknown group '{groupId}'.");
        }

        if (group.Count == 0)
        {
            throw new DomainCommandException("group_empty", $"Group '{groupId}' has no member to promote.");
        }

        if (detailLevel == SimulationDetailLevel.L3)
        {
            throw new DomainCommandException("invalid_detail_level", "A named actor cannot be promoted at L3.");
        }

        var settlementEvents = SettleRemoteGroupBeforePopulationChange(group, causeIds ?? []);
        var promotionCauses = new List<string>(causeIds ?? []);
        if (settlementEvents.Count > 0)
        {
            promotionCauses.Add(settlementEvents[0].Id);
        }

        var sequence = State.NextPromotionSequence;
        var actorId = $"person.promoted.{sequence:D8}";
        var identitySeedHex = ComputeIdentitySeed(group.Id, sequence);
        var memberships = group.OrganizationId is null
            ? Array.Empty<ActorMembership>()
            : [new ActorMembership(group.OrganizationId, $"promoted:{group.PromotionProfileId}")];
        var actor = new ActorState(
            actorId,
            $"{group.Name} {sequence:D4}",
            group.LocationId,
            null,
            memberships,
            detailLevel,
            group.Id,
            identitySeedHex,
            isTemporaryPromotion: true);

        State.AddActor(actor);
        State.MarkActorDetailDirty(actor.Id);
        group.Count--;
        State.NextPromotionSequence++;
        var promoted = AppendEvent(
            "group_member_promoted",
            State.CurrentMinute,
            group.LocationId,
            [group.Id, actor.Id],
            promotionCauses,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["detail_level"] = detailLevel.ToString().ToLowerInvariant(),
                ["group_remaining_count"] = group.Count.ToString(CultureInfo.InvariantCulture),
                ["identity_seed_hex"] = identitySeedHex,
                ["promotion_profile_id"] = group.PromotionProfileId,
            });

        foreach (var groupBelief in State.Beliefs.Values
                     .Where(item => string.Equals(item.HolderId, group.Id, StringComparison.Ordinal))
                     .OrderBy(item => item.Id, StringComparer.Ordinal)
                     .ToArray())
        {
            State.AddBelief(new BeliefState(
                $"belief.promoted.{sequence:D8}.{groupBelief.Id}",
                actor.Id,
                groupBelief.PropositionId,
                groupBelief.ConfidenceBp,
                "promoted_group_belief",
                State.CurrentMinute,
                promoted.Id));
        }

        return new PromotionResult(actor, promoted);
    }

    public WorldEvent DemotePromotedActor(string actorId, IReadOnlyList<string>? causeIds = null)
    {
        var actor = GetActor(actorId);
        if (!actor.IsTemporaryPromotion || actor.PromotedFromGroupId is null)
        {
            throw new DomainCommandException("actor_not_demotable", $"Actor '{actorId}' is not a temporary promotion.");
        }

        if (actor.IsInTransit || FindActiveAction(actor.Id) is not null ||
            actor.RemoteCycleCount > 0 ||
            State.Items.Values.Any(item => string.Equals(item.HolderId, actor.Id, StringComparison.Ordinal)) ||
            State.Commitments.Values.Any(item =>
                item.Status == "open" &&
                (item.DebtorId == actor.Id || item.CreditorId == actor.Id || item.RecipientId == actor.Id)) ||
            State.Plans.Values.Any(item => item.OwnerId == actor.Id && item.Status is PlanStatus.Active or PlanStatus.Running) ||
            State.Messages.Values.Any(item => item.SenderId == actor.Id || item.RecipientId == actor.Id))
        {
            throw new DomainCommandException(
                "actor_has_independent_state",
                $"Actor '{actorId}' has state that cannot be merged into a group.");
        }

        var beliefs = State.Beliefs.Values
            .Where(item => string.Equals(item.HolderId, actor.Id, StringComparison.Ordinal))
            .ToArray();
        if (beliefs.Any(item => !string.Equals(item.Source, "promoted_group_belief", StringComparison.Ordinal)))
        {
            throw new DomainCommandException(
                "actor_has_independent_state",
                $"Actor '{actorId}' has independent beliefs.");
        }

        var group = State.Groups[actor.PromotedFromGroupId];
        if (!string.Equals(group.LocationId, actor.LocationId, StringComparison.Ordinal))
        {
            throw new DomainCommandException(
                "incompatible_group_location",
                $"Actor '{actorId}' is not at group '{group.Id}' location.");
        }

        if (WouldUpdateActorInRemoteSettlement(actor, group, State.CurrentMinute))
        {
            throw new DomainCommandException(
                "actor_has_independent_state",
                $"Actor '{actorId}' has unsettled remote history that cannot be merged into a group.");
        }

        var settlementEvents = SettleRemoteGroupBeforePopulationChange(group, causeIds ?? []);
        var demotionCauses = new List<string>(causeIds ?? []);
        if (settlementEvents.Count > 0)
        {
            demotionCauses.Add(settlementEvents[0].Id);
        }

        foreach (var belief in beliefs)
        {
            State.RemoveBelief(belief.Id);
        }

        group.Count++;
        var demoted = AppendEvent(
            "promoted_actor_demoted",
            State.CurrentMinute,
            group.LocationId,
            [actor.Id, group.Id],
            demotionCauses,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["group_count"] = group.Count.ToString(CultureInfo.InvariantCulture),
                ["identity_seed_hex"] = actor.IdentitySeedHex ?? string.Empty,
            });
        State.RemoveActor(actor.Id);
        return demoted;
    }

    public WorldEvent SetActorDetailLevel(
        string actorId,
        SimulationDetailLevel detailLevel,
        IReadOnlyList<string>? causeIds = null)
    {
        if (detailLevel == SimulationDetailLevel.L3)
        {
            throw new DomainCommandException("invalid_detail_level", "Named actors cannot use L3 aggregation.");
        }

        var actor = GetActor(actorId);
        return ApplyActorDetailLevel(actor, detailLevel, ["manual_override"], causeIds ?? []);
    }

    private WorldEvent ApplyActorDetailLevel(
        ActorState actor,
        SimulationDetailLevel detailLevel,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> causeIds)
    {
        var previous = actor.DetailLevel;
        actor.DetailLevel = detailLevel;
        return AppendEvent(
            "actor_detail_level_changed",
            State.CurrentMinute,
            actor.PlaceId,
            [actor.Id],
            causeIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = previous.ToString().ToLowerInvariant(),
                ["policy_version"] = SimulationDetailPolicy.Version,
                ["reasons"] = string.Join(',', reasons),
                ["to"] = detailLevel.ToString().ToLowerInvariant(),
            });
    }

    private bool IsAdjacentToPlayer(ActorState actor, ActorState player)
    {
        if (actor.PlaceId is null || player.PlaceId is null)
        {
            return false;
        }

        return State.Routes.Values.Any(route =>
            (string.Equals(route.FromPlaceId, actor.PlaceId, StringComparison.Ordinal) &&
             string.Equals(route.ToPlaceId, player.PlaceId, StringComparison.Ordinal)) ||
            (string.Equals(route.ToPlaceId, actor.PlaceId, StringComparison.Ordinal) &&
             string.Equals(route.FromPlaceId, player.PlaceId, StringComparison.Ordinal)));
    }

    private static bool OccupiesSameAttentionSpace(ActorState actor, ActorState player)
    {
        if (actor.PlaceId is not null && player.PlaceId is not null)
        {
            return string.Equals(actor.PlaceId, player.PlaceId, StringComparison.Ordinal);
        }

        return actor.Transit is { } actorTransit &&
               player.Transit is { } playerTransit &&
               string.Equals(actorTransit.RouteId, playerTransit.RouteId, StringComparison.Ordinal);
    }

    private static SimulationDetailLevel MoreDetailed(
        SimulationDetailLevel current,
        SimulationDetailLevel candidate)
    {
        return current < candidate ? current : candidate;
    }

    private void MarkActorPositionDetailDirty(
        string actorId,
        string? previousPlaceId,
        string? previousRouteId)
    {
        State.MarkActorDetailDirty(actorId);
        if (!string.Equals(actorId, State.PlayerActorId, StringComparison.Ordinal))
        {
            return;
        }

        MarkAttentionNeighborhoodDirty(previousPlaceId, previousRouteId);
        var player = GetActor(State.PlayerActorId);
        MarkAttentionNeighborhoodDirty(player.PlaceId, player.Transit?.RouteId);
    }

    private void MarkAttentionNeighborhoodDirty(string? placeId, string? routeId)
    {
        if (routeId is not null)
        {
            InvalidateActorDetailLevels(State.GetActorIdsOnRoute(routeId));
        }

        if (placeId is null)
        {
            return;
        }

        InvalidateActorDetailLevels(State.GetActorIdsAtPlace(placeId));
        foreach (var adjacentPlaceId in State.GetAdjacentPlaceIds(placeId))
        {
            InvalidateActorDetailLevels(State.GetActorIdsAtPlace(adjacentPlaceId));
        }
    }

    private WorldEvent ProcessDetailMessageRetentionExpired(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("sender_id", out var senderId) ||
            !scheduled.Details.TryGetValue("recipient_id", out var recipientId))
        {
            throw new InvalidOperationException(
                $"Detail retention event '{scheduled.Id}' has no sender or recipient.");
        }

        InvalidateActorDetailLevels([senderId, recipientId]);
        return AppendScheduledEvent(scheduled);
    }

    private string ComputeIdentitySeed(string groupId, long sequence)
    {
        var material = $"{State.ContentHash}\0{groupId}\0{sequence.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()[..16];
    }
}
