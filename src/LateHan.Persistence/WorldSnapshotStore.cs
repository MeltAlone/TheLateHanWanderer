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
    public string SnapshotSchemaVersion { get; init; } = "0.1";

    public string ScenarioId { get; init; } = string.Empty;

    public string ScenarioVersion { get; init; } = string.Empty;

    public string RulesetVersion { get; init; } = string.Empty;

    public string RngVersion { get; init; } = string.Empty;

    public string EngineVersion { get; init; } = string.Empty;

    public string ContentHash { get; init; } = string.Empty;

    public string PlayerActorId { get; init; } = string.Empty;

    public long CurrentMinute { get; init; }

    public long NextEventSequence { get; init; }

    public List<ActorSnapshot> Actors { get; init; } = [];

    public List<PlaceSnapshot> Places { get; init; } = [];

    public List<RouteSnapshot> Routes { get; init; } = [];

    public List<ItemSnapshot> Items { get; init; } = [];

    public List<CommitmentSnapshot> Commitments { get; init; } = [];

    public List<EventSnapshot> Events { get; init; } = [];

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
            NextEventSequence = world.Events.Count == 0 ? 1 : world.Events.Max(item => item.Sequence) + 1,
            Actors = world.Actors.Values.Select(item => new ActorSnapshot(item.Id, item.Name, item.LocationId)).ToList(),
            Places = world.Places.Values.Select(item => new PlaceSnapshot(item.Id, item.Name, item.AccessRuleId, item.ControllerId)).ToList(),
            Routes = world.Routes.Values.Select(item => new RouteSnapshot(
                item.Id,
                item.FromPlaceId,
                item.ToPlaceId,
                item.DistanceLiQ10,
                item.Bidirectional,
                item.MinutesByMode.ToDictionary(pair => pair.Key, pair => pair.Value))).ToList(),
            Items = world.Items.Values.Select(item => new ItemSnapshot(item.Id, item.Name, item.Kind, item.HolderId)).ToList(),
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
        };
    }

    public WorldState ToWorld()
    {
        if (!string.Equals(SnapshotSchemaVersion, "0.1", StringComparison.Ordinal))
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
            Actors.Select(item => new ActorState(item.Id, item.Name, item.LocationId)),
            Places.Select(item => new PlaceDefinition(item.Id, item.Name, item.AccessRuleId, item.ControllerId)),
            Routes.Select(item => new RouteDefinition(
                item.Id,
                item.FromPlaceId,
                item.ToPlaceId,
                item.DistanceLiQ10,
                item.Bidirectional,
                item.MinutesByMode)),
            Items.Select(item => new ItemState(item.Id, item.Name, item.Kind, item.HolderId)),
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
            NextEventSequence);
    }
}

internal sealed record ActorSnapshot(string Id, string Name, string LocationId);

internal sealed record PlaceSnapshot(string Id, string Name, string AccessRuleId, string? ControllerId);

internal sealed record RouteSnapshot(
    string Id,
    string FromPlaceId,
    string ToPlaceId,
    int DistanceLiQ10,
    bool Bidirectional,
    Dictionary<TravelMode, int> MinutesByMode);

internal sealed record ItemSnapshot(string Id, string Name, string Kind, string HolderId);

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
