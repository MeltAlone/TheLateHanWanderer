using System.Text.Json;
using System.Text.Json.Serialization;
using LateHan.Core;

namespace LateHan.Persistence;

public sealed class WorldSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public void Save(WorldState world, string path)
    {
        var snapshot = WorldSnapshot.FromWorld(world);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{fullPath}.tmp";
        File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions));
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public WorldState Load(string path)
    {
        var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new InvalidDataException($"Snapshot '{path}' is empty.");
        return snapshot.ToWorld();
    }
}

internal sealed class WorldSnapshot
{
    public string SnapshotSchemaVersion { get; init; } = "0.5";

    public string ScenarioId { get; init; } = string.Empty;

    public string ScenarioVersion { get; init; } = string.Empty;

    public string RulesetVersion { get; init; } = string.Empty;

    public string RngVersion { get; init; } = string.Empty;

    public string EngineVersion { get; init; } = string.Empty;

    public string ContentHash { get; init; } = string.Empty;

    public string PlayerActorId { get; init; } = string.Empty;

    public long CurrentMinute { get; init; }

    public long NextEventSequence { get; init; }

    public string RngRootSeedHex { get; init; } = string.Empty;

    public string RngDerivation { get; init; } = string.Empty;

    public long NextScheduledEventSequence { get; init; }

    public bool ReplayModified { get; init; }

    public long NextActionSequence { get; init; }

    public List<ActorSnapshot> Actors { get; init; } = [];

    public List<PlaceSnapshot> Places { get; init; } = [];

    public List<RouteSnapshot> Routes { get; init; } = [];

    public List<ItemSnapshot> Items { get; init; } = [];

    public List<BeliefSnapshot> Beliefs { get; init; } = [];

    public List<PlanSnapshot> Plans { get; init; } = [];

    public List<AccessRuleSnapshot> AccessRules { get; init; } = [];

    public List<PlaceAccessSnapshot> PlaceAccessStates { get; init; } = [];

    public List<MessageSnapshot> Messages { get; init; } = [];

    public List<CommitmentSnapshot> Commitments { get; init; } = [];

    public List<EventSnapshot> Events { get; init; } = [];

    public List<RandomStreamSnapshot> RandomStreams { get; init; } = [];

    public List<ScheduledEventSnapshot> ScheduledEvents { get; init; } = [];

    public List<ActionSnapshot> Actions { get; init; } = [];

    public static WorldSnapshot FromWorld(WorldState world)
    {
        return new WorldSnapshot
        {
            ScenarioId = world.ScenarioId,
            ScenarioVersion = world.ScenarioVersion,
            RulesetVersion = world.RulesetVersion,
            RngVersion = world.RngVersion,
            EngineVersion = world.EngineVersion,
            ContentHash = world.ContentHash,
            PlayerActorId = world.PlayerActorId,
            CurrentMinute = world.CurrentMinute,
            NextEventSequence = world.EventSequenceCursor,
            RngRootSeedHex = world.RandomStreams.RootSeedHex,
            RngDerivation = world.RandomStreams.Derivation,
            NextScheduledEventSequence = world.ScheduledEventSequenceCursor,
            ReplayModified = world.ReplayModified,
            NextActionSequence = world.ActionSequenceCursor,
            Actors = world.Actors.Values.Select(item => new ActorSnapshot(
                item.Id,
                item.Name,
                item.PlaceId,
                item.Transit is null
                    ? null
                    : new TransitPositionSnapshot(
                        item.Transit.ActionId,
                        item.Transit.RouteId,
                        item.Transit.FromPlaceId,
                        item.Transit.ToPlaceId,
                        item.Transit.ProgressQ1000),
                item.Memberships.Select(membership => new MembershipSnapshot(
                    membership.OrganizationId,
                    membership.RoleId)).ToList())).ToList(),
            Places = world.Places.Values.Select(item => new PlaceSnapshot(item.Id, item.Name, item.AccessRuleId, item.ControllerId)).ToList(),
            Routes = world.Routes.Values.Select(item => new RouteSnapshot(
                item.Id,
                item.FromPlaceId,
                item.ToPlaceId,
                item.DistanceLiQ10,
                item.Bidirectional,
                item.MinutesByMode.ToDictionary(pair => pair.Key, pair => pair.Value))).ToList(),
            Items = world.Items.Values.Select(item => new ItemSnapshot(
                item.Id,
                item.Name,
                item.Kind,
                item.HolderId,
                item.AuthorId,
                item.IntendedRecipientId,
                item.PropositionIds.ToList(),
                item.ValidForAccessRuleIds.ToList(),
                item.ExpiresAtMinute)).ToList(),
            Beliefs = world.Beliefs.Values.Select(item => new BeliefSnapshot(
                item.Id,
                item.HolderId,
                item.PropositionId,
                item.ConfidenceBp,
                item.Source,
                item.AcquiredAtMinute,
                item.SourceEventId)).ToList(),
            Plans = world.Plans.Values.Select(item => new PlanSnapshot(
                item.Id,
                item.OwnerId,
                item.Intent,
                item.Stage,
                item.BeliefRequirementIds.ToList(),
                item.NextEvaluationMinute,
                item.EvaluationRule,
                item.TriggerItemId,
                item.TriggerPropositionId,
                item.DestinationPlaceId,
                item.ConfidenceThresholdBp,
                item.ReevaluationIntervalMinutes,
                item.Status,
                item.PendingScheduledEventId,
                item.ActiveActionId,
                item.LastEvaluationMinute)).ToList(),
            AccessRules = world.AccessRules.Values.Select(item => new AccessRuleSnapshot(
                item.Id,
                item.Name,
                item.Requirements.ToList(),
                item.MayQueue)).ToList(),
            PlaceAccessStates = world.PlaceAccessStates.Values.Select(item => new PlaceAccessSnapshot(
                item.PlaceId,
                item.Open,
                item.QueueCount,
                item.SecurityPosture)).ToList(),
            Messages = world.Messages.Values.Select(item => new MessageSnapshot(
                item.Id,
                item.PropositionId,
                item.SenderId,
                item.RecipientId,
                item.ConfidenceBp,
                item.CreatedAtMinute,
                item.CreatedEventId,
                item.DeliveredEventId,
                item.ParentMessageId)).ToList(),
            Commitments = world.Commitments.Values.Select(item => new CommitmentSnapshot(
                item.Id,
                item.DebtorId,
                item.CreditorId,
                item.Action,
                item.TargetId,
                item.RecipientId,
                item.DueMinute,
                item.Status)).ToList(),
            Events = world.Events.Select(item => new EventSnapshot(
                item.Sequence,
                item.Id,
                item.Type,
                item.Minute,
                item.LocationId,
                item.SubjectIds.ToList(),
                item.CauseIds.ToList(),
                item.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))).ToList(),
            RandomStreams = world.RandomStreams.Streams.Values.Select(item => new RandomStreamSnapshot(
                item.Key,
                item.State0,
                item.State1,
                item.State2,
                item.State3,
                item.DrawCount)).ToList(),
            ScheduledEvents = world.ScheduledEvents.Select(item => new ScheduledEventSnapshot(
                item.Sequence,
                item.Id,
                item.DueMinute,
                item.Phase,
                item.StableSubjectId,
                item.Kind,
                item.LocationId,
                item.InterruptsPlayer,
                item.CauseIds.ToList(),
                item.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))).ToList(),
            Actions = world.Actions.Values.Select(item => new ActionSnapshot(
                item.Sequence,
                item.Id,
                item.ActorId,
                item.Kind,
                item.Status,
                item.StartedMinute,
                item.LastUpdatedMinute,
                item.Phase,
                item.StartedByEventId,
                new TravelActionSnapshot(
                    item.Travel.OriginPlaceId,
                    item.Travel.DestinationPlaceId,
                    item.Travel.Mode,
                    item.Travel.Legs.Select(leg => new TravelLegSnapshot(
                        leg.RouteId,
                        leg.FromPlaceId,
                        leg.ToPlaceId,
                        leg.WalkMinutes,
                        leg.HorseMinutes,
                        leg.WithGroupMinutes)).ToList(),
                    item.Travel.CurrentLegIndex,
                    item.Travel.CurrentLegProgressQ1000,
                    item.Travel.SegmentStartedMinute,
                    item.Travel.SegmentRemainingMinutes,
                    item.Travel.ElapsedMinutes,
                    item.Travel.PendingScheduledEventId,
                    item.Travel.InterruptionEventId))).ToList(),
        };
    }

    public WorldState ToWorld()
    {
        if (!string.Equals(SnapshotSchemaVersion, "0.5", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported snapshot schema '{SnapshotSchemaVersion}'.");
        }

        if (!string.Equals(EngineVersion, EngineMetadata.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Snapshot engine version '{EngineVersion}' is incompatible with '{EngineMetadata.Version}'.");
        }

        return new WorldState(
            ScenarioId,
            ScenarioVersion,
            RulesetVersion,
            RngVersion,
            EngineVersion,
            ContentHash,
            PlayerActorId,
            CurrentMinute,
            Actors.Select(item => new ActorState(
                item.Id,
                item.Name,
                item.PlaceId,
                item.Transit is null
                    ? null
                    : new TransitPositionState(
                        item.Transit.ActionId,
                        item.Transit.RouteId,
                        item.Transit.FromPlaceId,
                        item.Transit.ToPlaceId,
                        item.Transit.ProgressQ1000),
                item.Memberships.Select(membership => new ActorMembership(
                    membership.OrganizationId,
                    membership.RoleId)))),
            Places.Select(item => new PlaceDefinition(item.Id, item.Name, item.AccessRuleId, item.ControllerId)),
            Routes.Select(item => new RouteDefinition(
                item.Id,
                item.FromPlaceId,
                item.ToPlaceId,
                item.DistanceLiQ10,
                item.Bidirectional,
                item.MinutesByMode)),
            Items.Select(item => new ItemState(
                item.Id,
                item.Name,
                item.Kind,
                item.HolderId,
                item.AuthorId,
                item.IntendedRecipientId,
                item.PropositionIds,
                item.ValidForAccessRuleIds,
                item.ExpiresAtMinute)),
            Commitments.Select(item => new CommitmentState(
                item.Id,
                item.DebtorId,
                item.CreditorId,
                item.Action,
                item.TargetId,
                item.RecipientId,
                item.DueMinute,
                item.Status)),
            Events.Select(item => new WorldEvent(
                item.Sequence,
                item.Id,
                item.Type,
                item.Minute,
                item.LocationId,
                item.SubjectIds,
                item.CauseIds,
                item.Details)),
            NextEventSequence,
            RngRootSeedHex,
            RngDerivation,
            RandomStreams.Select(item => new RandomStreamState(
                item.Key,
                item.State0,
                item.State1,
                item.State2,
                item.State3,
                item.DrawCount)),
            ScheduledEvents.Select(item => new ScheduledWorldEvent(
                item.Sequence,
                item.Id,
                item.DueMinute,
                item.Phase,
                item.StableSubjectId,
                item.Kind,
                item.LocationId,
                item.InterruptsPlayer,
                item.CauseIds,
                item.Details)),
            NextScheduledEventSequence,
            ReplayModified,
            Actions.Select(item => new ActionInstanceState(
                item.Sequence,
                item.Id,
                item.ActorId,
                item.Kind,
                item.Status,
                item.StartedMinute,
                item.LastUpdatedMinute,
                item.Phase,
                item.StartedByEventId,
                new TravelActionState(
                    item.Travel.OriginPlaceId,
                    item.Travel.DestinationPlaceId,
                    item.Travel.Mode,
                    item.Travel.Legs.Select(leg => new TravelLegState(
                        leg.RouteId,
                        leg.FromPlaceId,
                        leg.ToPlaceId,
                        leg.WalkMinutes,
                        leg.HorseMinutes,
                        leg.WithGroupMinutes)),
                    item.Travel.CurrentLegIndex,
                    item.Travel.CurrentLegProgressQ1000,
                    item.Travel.SegmentStartedMinute,
                    item.Travel.SegmentRemainingMinutes,
                    item.Travel.ElapsedMinutes,
                    item.Travel.PendingScheduledEventId,
                    item.Travel.InterruptionEventId))),
            NextActionSequence,
            Beliefs.Select(item => new BeliefState(
                item.Id,
                item.HolderId,
                item.PropositionId,
                item.ConfidenceBp,
                item.Source,
                item.AcquiredAtMinute,
                item.SourceEventId)),
            Plans.Select(item => new PlanState(
                item.Id,
                item.OwnerId,
                item.Intent,
                item.Stage,
                item.BeliefRequirementIds,
                item.NextEvaluationMinute,
                item.EvaluationRule,
                item.TriggerItemId,
                item.TriggerPropositionId,
                item.DestinationPlaceId,
                item.ConfidenceThresholdBp,
                item.ReevaluationIntervalMinutes,
                item.Status,
                item.PendingScheduledEventId,
                item.ActiveActionId,
                item.LastEvaluationMinute)),
            AccessRules.Select(item => new AccessRuleDefinition(
                item.Id,
                item.Name,
                item.Requirements,
                item.MayQueue)),
            PlaceAccessStates.Select(item => new PlaceAccessState(
                item.PlaceId,
                item.Open,
                item.QueueCount,
                item.SecurityPosture)),
            Messages.Select(item => new MessageState(
                item.Id,
                item.PropositionId,
                item.SenderId,
                item.RecipientId,
                item.ConfidenceBp,
                item.CreatedAtMinute,
                item.CreatedEventId,
                item.DeliveredEventId,
                item.ParentMessageId)));
    }
}

internal sealed record ActorSnapshot(
    string Id,
    string Name,
    string? PlaceId,
    TransitPositionSnapshot? Transit,
    List<MembershipSnapshot> Memberships);

internal sealed record MembershipSnapshot(string OrganizationId, string RoleId);

internal sealed record TransitPositionSnapshot(
    string ActionId,
    string RouteId,
    string FromPlaceId,
    string ToPlaceId,
    int ProgressQ1000);

internal sealed record PlaceSnapshot(string Id, string Name, string AccessRuleId, string? ControllerId);

internal sealed record RouteSnapshot(
    string Id,
    string FromPlaceId,
    string ToPlaceId,
    int DistanceLiQ10,
    bool Bidirectional,
    Dictionary<TravelMode, int> MinutesByMode);

internal sealed record ItemSnapshot(
    string Id,
    string Name,
    string Kind,
    string HolderId,
    string? AuthorId,
    string? IntendedRecipientId,
    List<string> PropositionIds,
    List<string> ValidForAccessRuleIds,
    long? ExpiresAtMinute);

internal sealed record AccessRuleSnapshot(
    string Id,
    string Name,
    List<string> Requirements,
    bool MayQueue);

internal sealed record PlaceAccessSnapshot(
    string PlaceId,
    bool Open,
    int QueueCount,
    string SecurityPosture);

internal sealed record MessageSnapshot(
    string Id,
    string PropositionId,
    string SenderId,
    string RecipientId,
    int ConfidenceBp,
    long CreatedAtMinute,
    string CreatedEventId,
    string DeliveredEventId,
    string? ParentMessageId);

internal sealed record BeliefSnapshot(
    string Id,
    string HolderId,
    string PropositionId,
    int ConfidenceBp,
    string Source,
    long AcquiredAtMinute,
    string? SourceEventId);

internal sealed record PlanSnapshot(
    string Id,
    string OwnerId,
    string Intent,
    string Stage,
    List<string> BeliefRequirementIds,
    long NextEvaluationMinute,
    PlanEvaluationRule EvaluationRule,
    string? TriggerItemId,
    string? TriggerPropositionId,
    string? DestinationPlaceId,
    int ConfidenceThresholdBp,
    int ReevaluationIntervalMinutes,
    PlanStatus Status,
    string? PendingScheduledEventId,
    string? ActiveActionId,
    long? LastEvaluationMinute);

internal sealed record CommitmentSnapshot(
    string Id,
    string DebtorId,
    string CreditorId,
    string Action,
    string TargetId,
    string RecipientId,
    long DueMinute,
    string Status);

internal sealed record EventSnapshot(
    long Sequence,
    string Id,
    string Type,
    long Minute,
    string? LocationId,
    List<string> SubjectIds,
    List<string> CauseIds,
    Dictionary<string, string> Details);

internal sealed record RandomStreamSnapshot(
    string Key,
    ulong State0,
    ulong State1,
    ulong State2,
    ulong State3,
    ulong DrawCount);

internal sealed record ScheduledEventSnapshot(
    long Sequence,
    string Id,
    long DueMinute,
    ScheduledEventPhase Phase,
    string StableSubjectId,
    string Kind,
    string? LocationId,
    bool InterruptsPlayer,
    List<string> CauseIds,
    Dictionary<string, string> Details);

internal sealed record ActionSnapshot(
    long Sequence,
    string Id,
    string ActorId,
    WorldActionKind Kind,
    ActionStatus Status,
    long StartedMinute,
    long LastUpdatedMinute,
    string Phase,
    string StartedByEventId,
    TravelActionSnapshot Travel);

internal sealed record TravelActionSnapshot(
    string OriginPlaceId,
    string DestinationPlaceId,
    TravelMode Mode,
    List<TravelLegSnapshot> Legs,
    int CurrentLegIndex,
    int CurrentLegProgressQ1000,
    long SegmentStartedMinute,
    int SegmentRemainingMinutes,
    long ElapsedMinutes,
    string? PendingScheduledEventId,
    string? InterruptionEventId);

internal sealed record TravelLegSnapshot(
    string RouteId,
    string FromPlaceId,
    string ToPlaceId,
    int WalkMinutes,
    int HorseMinutes,
    int WithGroupMinutes);
