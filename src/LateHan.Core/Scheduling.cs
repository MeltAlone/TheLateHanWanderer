using System.Collections.ObjectModel;

namespace LateHan.Core;

public enum ScheduledEventPhase
{
    DeathOrRemoval = 0,
    AccessAndControlChange = 1,
    ArrivalAndDeparture = 2,
    DeliveryAndTransfer = 3,
    PerceptionAndBelief = 4,
    PlanEvaluation = 5,
    SummaryAndNotification = 6,
}

public sealed record ScheduledWorldEvent(
    long Sequence,
    string Id,
    long DueMinute,
    ScheduledEventPhase Phase,
    string StableSubjectId,
    string Kind,
    string? LocationId,
    bool InterruptsPlayer,
    IReadOnlyList<string> CauseIds,
    IReadOnlyDictionary<string, string> Details)
{
    public static ScheduledWorldEvent Create(
        long sequence,
        long dueMinute,
        ScheduledEventPhase phase,
        string stableSubjectId,
        string kind,
        string? locationId,
        bool interruptsPlayer,
        IReadOnlyList<string>? causeIds = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        if (dueMinute < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dueMinute));
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        if (string.IsNullOrWhiteSpace(stableSubjectId))
        {
            throw new ArgumentException("Stable subject ID cannot be empty.", nameof(stableSubjectId));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Scheduled event kind cannot be empty.", nameof(kind));
        }

        var detailSnapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (details is not null)
        {
            foreach (var detail in details)
            {
                detailSnapshot.Add(detail.Key, detail.Value);
            }
        }

        return new ScheduledWorldEvent(
            sequence,
            $"scheduled.{sequence:D8}",
            dueMinute,
            phase,
            stableSubjectId,
            kind,
            locationId,
            interruptsPlayer,
            (causeIds ?? []).ToArray(),
            new ReadOnlyDictionary<string, string>(detailSnapshot));
    }
}

internal sealed class ScheduledWorldEventComparer : IComparer<ScheduledWorldEvent>
{
    public static ScheduledWorldEventComparer Instance { get; } = new();

    public int Compare(ScheduledWorldEvent? x, ScheduledWorldEvent? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var dueMinute = x.DueMinute.CompareTo(y.DueMinute);
        if (dueMinute != 0)
        {
            return dueMinute;
        }

        var phase = x.Phase.CompareTo(y.Phase);
        if (phase != 0)
        {
            return phase;
        }

        var subject = StringComparer.Ordinal.Compare(x.StableSubjectId, y.StableSubjectId);
        return subject != 0 ? subject : x.Sequence.CompareTo(y.Sequence);
    }
}
