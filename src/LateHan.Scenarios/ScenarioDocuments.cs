using System.Text.Json.Serialization;

namespace LateHan.Scenarios;

internal sealed class ManifestDocument
{
    public string SchemaVersion { get; init; } = string.Empty;

    public string ScenarioId { get; init; } = string.Empty;

    public string ScenarioVersion { get; init; } = string.Empty;

    public string RulesetVersion { get; init; } = string.Empty;

    public RngDocument Rng { get; init; } = new();

    public string ContentHash { get; init; } = string.Empty;

    public string PlayerActorId { get; init; } = string.Empty;

    public ScenarioStartDocument Start { get; init; } = new();

    public List<string> Components { get; init; } = [];
}

internal sealed class RngDocument
{
    public string Version { get; init; } = string.Empty;

    public string RootSeedHex { get; init; } = string.Empty;

    public string Derivation { get; init; } = string.Empty;
}

internal sealed class ScenarioStartDocument
{
    public long Minute { get; init; }
}

internal abstract class ComponentDocument
{
    public string SchemaVersion { get; init; } = string.Empty;

    public string ScenarioId { get; init; } = string.Empty;
}

internal sealed class WorldDocument : ComponentDocument
{
    public List<NamedDocument> Organizations { get; init; } = [];

    public List<AccessRuleDocument> AccessRules { get; init; } = [];

    public List<PlaceDocument> Places { get; init; } = [];

    public List<RouteDocument> Routes { get; init; } = [];
}

internal sealed class ActorsDocument : ComponentDocument
{
    public List<PersonDocument> Persons { get; init; } = [];

    public List<GroupDocument> Groups { get; init; } = [];
}

internal sealed class StateDocument : ComponentDocument
{
    public List<PlaceStateDocument> PlaceStates { get; init; } = [];

    public List<ItemDocument> Items { get; init; } = [];

    public List<NamedDocument> Propositions { get; init; } = [];

    public List<BeliefDocument> Beliefs { get; init; } = [];

    public List<CommitmentDocument> Commitments { get; init; } = [];

    public List<PlanDocument> Plans { get; init; } = [];
}

internal class NamedDocument
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}

internal sealed class PlaceDocument : NamedDocument
{
    public string AccessRule { get; init; } = string.Empty;

    public string? Controller { get; init; }
}

internal sealed class AccessRuleDocument : NamedDocument
{
    public List<string> Requirements { get; init; } = [];

    public bool MayQueue { get; init; }
}

internal sealed class PlaceStateDocument
{
    public string Place { get; init; } = string.Empty;

    public bool Open { get; init; }

    public int QueueCount { get; init; }

    public string SecurityPosture { get; init; } = string.Empty;
}

internal sealed class RouteDocument : NamedDocument
{
    public string From { get; init; } = string.Empty;

    public string To { get; init; } = string.Empty;

    public int DistanceLiQ10 { get; init; }

    public Dictionary<string, int> Minutes { get; init; } = new(StringComparer.Ordinal);

    public bool Bidirectional { get; init; }
}

internal sealed class PersonDocument : NamedDocument
{
    public string Location { get; init; } = string.Empty;

    public List<MembershipDocument> Memberships { get; init; } = [];
}

internal sealed class MembershipDocument
{
    public string Organization { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;
}

internal sealed class GroupDocument : NamedDocument
{
    public string Location { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public int Count { get; init; }

    public string? Organization { get; init; }

    public string PromotionProfile { get; init; } = string.Empty;
}

internal sealed class ItemDocument : NamedDocument
{
    public string Kind { get; init; } = string.Empty;

    public string Holder { get; init; } = string.Empty;

    public string? Author { get; init; }

    public string? IntendedRecipient { get; init; }

    public List<string> PropositionIds { get; init; } = [];

    public List<string> ValidFor { get; init; } = [];

    public long? ExpiresAtMinute { get; init; }
}

internal sealed class BeliefDocument : NamedDocument
{
    public string Holder { get; init; } = string.Empty;

    public string Proposition { get; init; } = string.Empty;

    public int ConfidenceBp { get; init; }

    public string Source { get; init; } = string.Empty;

    public long AcquiredAtMinute { get; init; }
}

internal sealed class CommitmentDocument : NamedDocument
{
    public string Debtor { get; init; } = string.Empty;

    public string Creditor { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Recipient { get; init; } = string.Empty;

    public long DueMinute { get; init; }

    public string Status { get; init; } = string.Empty;
}

internal sealed class PlanDocument : NamedDocument
{
    public string Owner { get; init; } = string.Empty;

    public List<string> BeliefRequirements { get; init; } = [];

    public string Intent { get; init; } = string.Empty;

    public string Stage { get; init; } = string.Empty;

    public long NextEvaluationMinute { get; init; }

    public string? EvaluationRule { get; init; }

    public string? TriggerItemId { get; init; }

    public string? TriggerPropositionId { get; init; }

    public string? DestinationPlaceId { get; init; }

    public int ConfidenceThresholdBp { get; init; }

    public int ReevaluationIntervalMinutes { get; init; } = 60;
}
