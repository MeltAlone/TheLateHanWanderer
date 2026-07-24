using System.Collections.ObjectModel;

namespace LateHan.Core;

public enum WorldActionKind
{
    Travel,
}

public sealed record TravelLegState(
    string RouteId,
    string FromPlaceId,
    string ToPlaceId,
    int WalkMinutes,
    int HorseMinutes,
    int WithGroupMinutes)
{
    public int GetMinutes(TravelMode mode) => mode switch
    {
        TravelMode.Walk when WalkMinutes > 0 => WalkMinutes,
        TravelMode.Horse when HorseMinutes > 0 => HorseMinutes,
        TravelMode.WithGroup when WithGroupMinutes > 0 => WithGroupMinutes,
        _ => throw new DomainCommandException(
            "travel_mode_unavailable",
            $"Route '{RouteId}' does not support {mode}."),
    };
}

public sealed class TransitPositionState
{
    public TransitPositionState(
        string actionId,
        string routeId,
        string fromPlaceId,
        string toPlaceId,
        int progressQ1000)
    {
        if (progressQ1000 is < 0 or >= 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(progressQ1000));
        }

        ActionId = actionId;
        RouteId = routeId;
        FromPlaceId = fromPlaceId;
        ToPlaceId = toPlaceId;
        ProgressQ1000 = progressQ1000;
    }

    public string ActionId { get; }

    public string RouteId { get; }

    public string FromPlaceId { get; }

    public string ToPlaceId { get; }

    public int ProgressQ1000 { get; internal set; }
}

public sealed class TravelActionState
{
    private readonly IReadOnlyList<TravelLegState> _legs;

    public TravelActionState(
        string originPlaceId,
        string destinationPlaceId,
        TravelMode mode,
        IEnumerable<TravelLegState> legs,
        int currentLegIndex = 0,
        int currentLegProgressQ1000 = 0,
        long segmentStartedMinute = 0,
        int segmentRemainingMinutes = 0,
        long elapsedMinutes = 0,
        string? pendingScheduledEventId = null,
        string? interruptionEventId = null)
    {
        var legArray = legs.ToArray();
        if (legArray.Length == 0)
        {
            throw new ArgumentException("Travel requires at least one route leg.", nameof(legs));
        }

        if (currentLegIndex < 0 || currentLegIndex >= legArray.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(currentLegIndex));
        }

        if (currentLegProgressQ1000 is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(currentLegProgressQ1000));
        }

        if (segmentRemainingMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentRemainingMinutes));
        }

        OriginPlaceId = originPlaceId;
        DestinationPlaceId = destinationPlaceId;
        Mode = mode;
        _legs = new ReadOnlyCollection<TravelLegState>(legArray);
        CurrentLegIndex = currentLegIndex;
        CurrentLegProgressQ1000 = currentLegProgressQ1000;
        SegmentStartedMinute = segmentStartedMinute;
        SegmentRemainingMinutes = segmentRemainingMinutes;
        ElapsedMinutes = elapsedMinutes;
        PendingScheduledEventId = pendingScheduledEventId;
        InterruptionEventId = interruptionEventId;
    }

    public string OriginPlaceId { get; }

    public string DestinationPlaceId { get; }

    public TravelMode Mode { get; internal set; }

    public IReadOnlyList<TravelLegState> Legs => _legs;

    public int CurrentLegIndex { get; internal set; }

    public int CurrentLegProgressQ1000 { get; internal set; }

    public long SegmentStartedMinute { get; internal set; }

    public int SegmentRemainingMinutes { get; internal set; }

    public long ElapsedMinutes { get; internal set; }

    public string? PendingScheduledEventId { get; internal set; }

    public string? InterruptionEventId { get; internal set; }

    public TravelLegState CurrentLeg => _legs[CurrentLegIndex];
}

public sealed class ActionInstanceState
{
    public ActionInstanceState(
        long sequence,
        string id,
        string actorId,
        WorldActionKind kind,
        ActionStatus status,
        long startedMinute,
        long lastUpdatedMinute,
        string phase,
        string startedByEventId,
        TravelActionState travel)
    {
        Sequence = sequence;
        Id = id;
        ActorId = actorId;
        Kind = kind;
        Status = status;
        StartedMinute = startedMinute;
        LastUpdatedMinute = lastUpdatedMinute;
        Phase = phase;
        StartedByEventId = startedByEventId;
        Travel = travel;
    }

    public long Sequence { get; }

    public string Id { get; }

    public string ActorId { get; }

    public WorldActionKind Kind { get; }

    public ActionStatus Status { get; internal set; }

    public long StartedMinute { get; }

    public long LastUpdatedMinute { get; internal set; }

    public string Phase { get; internal set; }

    public string StartedByEventId { get; }

    public TravelActionState Travel { get; }
}
