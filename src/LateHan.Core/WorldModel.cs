using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LateHan.Core;

public static class EngineMetadata
{
    public const string Version = "0.2.0-spike";
}

public enum TravelMode
{
    Walk,
    Horse,
    WithGroup,
}

public sealed class ActorState
{
    public ActorState(string id, string name, string locationId)
    {
        Id = id;
        Name = name;
        LocationId = locationId;
    }

    public string Id { get; }

    public string Name { get; }

    public string LocationId { get; internal set; }
}

public sealed record PlaceDefinition(string Id, string Name, string AccessRuleId, string? ControllerId);

public sealed class RouteDefinition
{
    private readonly IReadOnlyDictionary<TravelMode, int> _minutesByMode;

    public RouteDefinition(
        string id,
        string fromPlaceId,
        string toPlaceId,
        int distanceLiQ10,
        bool bidirectional,
        IReadOnlyDictionary<TravelMode, int> minutesByMode)
    {
        Id = id;
        FromPlaceId = fromPlaceId;
        ToPlaceId = toPlaceId;
        DistanceLiQ10 = distanceLiQ10;
        Bidirectional = bidirectional;
        _minutesByMode = new ReadOnlyDictionary<TravelMode, int>(
            new Dictionary<TravelMode, int>(minutesByMode));
    }

    public string Id { get; }

    public string FromPlaceId { get; }

    public string ToPlaceId { get; }

    public int DistanceLiQ10 { get; }

    public bool Bidirectional { get; }

    public IReadOnlyDictionary<TravelMode, int> MinutesByMode => _minutesByMode;

    public int GetMinutes(TravelMode mode)
    {
        if (!_minutesByMode.TryGetValue(mode, out var minutes))
        {
            throw new DomainCommandException("travel_mode_unavailable", $"Route '{Id}' does not support {mode}.");
        }

        return minutes;
    }
}

public sealed class ItemState
{
    public ItemState(string id, string name, string kind, string holderId)
    {
        Id = id;
        Name = name;
        Kind = kind;
        HolderId = holderId;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string HolderId { get; internal set; }
}

public sealed class CommitmentState
{
    public CommitmentState(
        string id,
        string debtorId,
        string creditorId,
        string action,
        string targetId,
        string recipientId,
        long dueMinute,
        string status)
    {
        Id = id;
        DebtorId = debtorId;
        CreditorId = creditorId;
        Action = action;
        TargetId = targetId;
        RecipientId = recipientId;
        DueMinute = dueMinute;
        Status = status;
    }

    public string Id { get; }

    public string DebtorId { get; }

    public string CreditorId { get; }

    public string Action { get; }

    public string TargetId { get; }

    public string RecipientId { get; }

    public long DueMinute { get; }

    public string Status { get; internal set; }
}

public sealed record WorldEvent(
    long Sequence,
    string Id,
    string Type,
    long Minute,
    string? LocationId,
    IReadOnlyList<string> SubjectIds,
    IReadOnlyList<string> CauseIds,
    IReadOnlyDictionary<string, string> Details);

public sealed class WorldState
{
    private readonly SortedDictionary<string, ActorState> _actors;
    private readonly SortedDictionary<string, PlaceDefinition> _places;
    private readonly SortedDictionary<string, RouteDefinition> _routes;
    private readonly SortedDictionary<string, ItemState> _items;
    private readonly SortedDictionary<string, CommitmentState> _commitments;
    private readonly List<WorldEvent> _events;
    private readonly SortedSet<ScheduledWorldEvent> _scheduledEvents;

    public WorldState(
        string scenarioId,
        string scenarioVersion,
        string rulesetVersion,
        string rngVersion,
        string engineVersion,
        string contentHash,
        string playerActorId,
        long currentMinute,
        IEnumerable<ActorState> actors,
        IEnumerable<PlaceDefinition> places,
        IEnumerable<RouteDefinition> routes,
        IEnumerable<ItemState> items,
        IEnumerable<CommitmentState> commitments,
        IEnumerable<WorldEvent>? events = null,
        long nextEventSequence = 1,
        string rngRootSeedHex = "0000000000000001",
        string rngDerivation = RandomMetadata.Sha256LittleEndianV1,
        IEnumerable<RandomStreamState>? randomStreams = null,
        IEnumerable<ScheduledWorldEvent>? scheduledEvents = null,
        long nextScheduledEventSequence = 1,
        bool replayModified = false)
    {
        ScenarioId = scenarioId;
        ScenarioVersion = scenarioVersion;
        RulesetVersion = rulesetVersion;
        RngVersion = rngVersion;
        EngineVersion = engineVersion;
        ContentHash = contentHash;
        PlayerActorId = playerActorId;
        CurrentMinute = currentMinute;
        _actors = new SortedDictionary<string, ActorState>(actors.ToDictionary(actor => actor.Id), StringComparer.Ordinal);
        _places = new SortedDictionary<string, PlaceDefinition>(places.ToDictionary(place => place.Id), StringComparer.Ordinal);
        _routes = new SortedDictionary<string, RouteDefinition>(routes.ToDictionary(route => route.Id), StringComparer.Ordinal);
        _items = new SortedDictionary<string, ItemState>(items.ToDictionary(item => item.Id), StringComparer.Ordinal);
        _commitments = new SortedDictionary<string, CommitmentState>(commitments.ToDictionary(item => item.Id), StringComparer.Ordinal);
        _events = events?.OrderBy(worldEvent => worldEvent.Sequence).ToList() ?? [];
        _scheduledEvents = new SortedSet<ScheduledWorldEvent>(scheduledEvents ?? [], ScheduledWorldEventComparer.Instance);
        if (_scheduledEvents.Any(item => item.DueMinute < currentMinute))
        {
            throw new ArgumentException("Scheduled events cannot be earlier than the current world minute.", nameof(scheduledEvents));
        }

        if (nextEventSequence <= _events.Select(item => item.Sequence).DefaultIfEmpty(0).Max())
        {
            throw new ArgumentOutOfRangeException(nameof(nextEventSequence), "Event sequence cursor must exceed every event sequence.");
        }

        if (nextScheduledEventSequence <= _scheduledEvents.Select(item => item.Sequence).DefaultIfEmpty(0).Max())
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextScheduledEventSequence),
                "Scheduled event sequence cursor must exceed every queued sequence.");
        }

        NextEventSequence = nextEventSequence;
        NextScheduledEventSequence = nextScheduledEventSequence;
        RandomStreams = new RandomStreamRegistry(rngVersion, rngRootSeedHex, rngDerivation, randomStreams);
        ReplayModified = replayModified;
    }

    public string ScenarioId { get; }

    public string ScenarioVersion { get; }

    public string RulesetVersion { get; }

    public string RngVersion { get; }

    public string EngineVersion { get; }

    public string ContentHash { get; }

    public string PlayerActorId { get; }

    public long CurrentMinute { get; internal set; }

    public IReadOnlyDictionary<string, ActorState> Actors => _actors;

    public IReadOnlyDictionary<string, PlaceDefinition> Places => _places;

    public IReadOnlyDictionary<string, RouteDefinition> Routes => _routes;

    public IReadOnlyDictionary<string, ItemState> Items => _items;

    public IReadOnlyDictionary<string, CommitmentState> Commitments => _commitments;

    public IReadOnlyList<WorldEvent> Events => _events;

    public IReadOnlyList<ScheduledWorldEvent> ScheduledEvents => _scheduledEvents.ToArray();

    public RandomStreamRegistry RandomStreams { get; }

    public bool ReplayModified { get; internal set; }

    public long EventSequenceCursor => NextEventSequence;

    public long ScheduledEventSequenceCursor => NextScheduledEventSequence;

    internal long NextEventSequence { get; set; }

    internal long NextScheduledEventSequence { get; set; }

    internal void AddEvent(WorldEvent worldEvent) => _events.Add(worldEvent);

    internal void AddScheduledEvent(ScheduledWorldEvent scheduledEvent)
    {
        if (!_scheduledEvents.Add(scheduledEvent))
        {
            throw new InvalidOperationException($"Duplicate scheduled event '{scheduledEvent.Id}'.");
        }
    }

    internal ScheduledWorldEvent? PeekScheduledEvent() => _scheduledEvents.Count == 0 ? null : _scheduledEvents.Min;

    internal bool RemoveScheduledEvent(ScheduledWorldEvent scheduledEvent) => _scheduledEvents.Remove(scheduledEvent);

    public string ComputeEventFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var worldEvent in _events.OrderBy(item => item.Sequence))
        {
            Append(hash, worldEvent.Sequence.ToString(CultureInfo.InvariantCulture));
            Append(hash, worldEvent.Id);
            Append(hash, worldEvent.Type);
            Append(hash, worldEvent.Minute.ToString(CultureInfo.InvariantCulture));
            Append(hash, worldEvent.LocationId ?? string.Empty);
            foreach (var subject in worldEvent.SubjectIds)
            {
                Append(hash, subject);
            }

            foreach (var cause in worldEvent.CauseIds)
            {
                Append(hash, cause);
            }

            foreach (var detail in worldEvent.Details.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Append(hash, detail.Key);
                Append(hash, detail.Value);
            }
        }

        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}

public sealed class DomainCommandException : Exception
{
    public DomainCommandException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
