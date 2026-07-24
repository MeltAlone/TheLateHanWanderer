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

public sealed class GroupState
{
    public GroupState(
        string id,
        string name,
        string kind,
        int count,
        string locationId,
        string? organizationId,
        string promotionProfileId)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Id = id;
        Name = name;
        Kind = kind;
        Count = count;
        LocationId = locationId;
        OrganizationId = organizationId;
        PromotionProfileId = promotionProfileId;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public int Count { get; internal set; }

    public string LocationId { get; internal set; }

    public string? OrganizationId { get; }

    public string PromotionProfileId { get; }

    public SimulationDetailLevel DetailLevel => SimulationDetailLevel.L3;
}

public sealed record PromotionResult(ActorState Actor, WorldEvent Event);

public sealed partial class WorldEngine
{
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
        group.Count--;
        State.NextPromotionSequence++;
        var promoted = AppendEvent(
            "group_member_promoted",
            State.CurrentMinute,
            group.LocationId,
            [group.Id, actor.Id],
            causeIds ?? [],
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
            causeIds ?? [],
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
        var previous = actor.DetailLevel;
        actor.DetailLevel = detailLevel;
        return AppendEvent(
            "actor_detail_level_changed",
            State.CurrentMinute,
            actor.PlaceId,
            [actor.Id],
            causeIds ?? [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = previous.ToString().ToLowerInvariant(),
                ["to"] = detailLevel.ToString().ToLowerInvariant(),
            });
    }

    private string ComputeIdentitySeed(string groupId, long sequence)
    {
        var material = $"{State.ContentHash}\0{groupId}\0{sequence.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()[..16];
    }
}
