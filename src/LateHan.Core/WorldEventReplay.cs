using System.Globalization;

namespace LateHan.Core;

public sealed record WorldEventReplayResult(
    int EventCount,
    string EventFingerprint,
    long FinalMinute);

public sealed partial class WorldEngine
{
    public WorldEventReplayResult ReplayEvents(IReadOnlyList<WorldEvent> expectedEvents)
    {
        if (expectedEvents.Count == 0)
        {
            return new WorldEventReplayResult(0, WorldEventFingerprint.Compute([]), State.CurrentMinute);
        }

        ValidateReplaySequence(expectedEvents);
        var replayed = 0;
        while (replayed < expectedEvents.Count)
        {
            var expected = expectedEvents[replayed];
            var firstGeneratedSequence = State.EventSequenceCursor;
            if (IsReplayCommandRoot(expected))
            {
                ReplayCommand(expected);
            }
            else
            {
                _ = AdvanceTimeTo(expected.Minute);
            }

            var generated = State.Events
                .Where(worldEvent => worldEvent.Sequence >= firstGeneratedSequence)
                .OrderBy(worldEvent => worldEvent.Sequence)
                .ToArray();
            if (generated.Length == 0)
            {
                throw new InvalidDataException(
                    $"Event '{expected.Id}' cannot be derived from the restored world state.");
            }

            if (replayed + generated.Length > expectedEvents.Count)
            {
                throw new InvalidDataException(
                    $"Replay command at '{expected.Id}' produced events beyond the archived tail.");
            }

            for (var offset = 0; offset < generated.Length; offset++)
            {
                EnsureReplayEventMatches(expectedEvents[replayed + offset], generated[offset]);
            }

            replayed += generated.Length;
        }

        return new WorldEventReplayResult(
            replayed,
            WorldEventFingerprint.Compute(expectedEvents),
            State.CurrentMinute);
    }

    private void ReplayCommand(WorldEvent expected)
    {
        try
        {
            switch (expected.Type)
            {
                case "wait_started":
                    _ = Wait(ParsePositiveLong(expected, "duration_minutes"), Subject(expected, 0));
                    break;
                case "delivery_started":
                    _ = Deliver(Subject(expected, 0), Subject(expected, 2), Subject(expected, 1));
                    break;
                case "document_reading_started":
                    _ = Read(Subject(expected, 0), Subject(expected, 1));
                    break;
                case "message_created":
                    _ = Tell(
                        Subject(expected, 0),
                        Subject(expected, 1),
                        Detail(expected, "proposition_id"));
                    break;
                case "access_requested":
                    _ = Enter(Subject(expected, 0), Detail(expected, "destination"));
                    break;
                case "travel_started":
                    _ = BeginTravel(
                        Subject(expected, 0),
                        Detail(expected, "destination"),
                        ParseTravelMode(Detail(expected, "mode")));
                    break;
                case "travel_resumed":
                    _ = ResumeTravel(
                        Detail(expected, "action_id"),
                        ParseTravelMode(Detail(expected, "mode")));
                    break;
                case "travel_cancelled":
                    _ = CancelAction(Detail(expected, "action_id"));
                    break;
                case "plan_cancelled":
                    _ = CancelPlan(Subject(expected, 0), Detail(expected, "reason"));
                    break;
                case "group_member_promoted":
                    _ = PromoteGroupMember(
                        Subject(expected, 0),
                        detailLevel: ParseDetailLevel(Detail(expected, "detail_level")));
                    break;
                case "promoted_actor_demoted":
                    _ = DemotePromotedActor(Subject(expected, 0));
                    break;
                default:
                    throw new InvalidDataException(
                        $"Event '{expected.Id}' uses unsupported replay command '{expected.Type}'.");
            }
        }
        catch (Exception exception) when (exception is DomainCommandException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"Event '{expected.Id}' could not be replayed from the restored world state.",
                exception);
        }
    }

    private static bool IsReplayCommandRoot(WorldEvent worldEvent) =>
        worldEvent.CauseIds.Count == 0 && worldEvent.Type is
            "wait_started" or
            "delivery_started" or
            "document_reading_started" or
            "message_created" or
            "access_requested" or
            "travel_started" or
            "travel_resumed" or
            "travel_cancelled" or
            "plan_cancelled" or
            "group_member_promoted" or
            "promoted_actor_demoted";

    private void ValidateReplaySequence(IReadOnlyList<WorldEvent> expectedEvents)
    {
        var expectedSequence = State.EventSequenceCursor;
        var previousMinute = State.CurrentMinute;
        foreach (var worldEvent in expectedEvents)
        {
            if (worldEvent.Sequence != expectedSequence ||
                !string.Equals(worldEvent.Id, $"event.{expectedSequence:D8}", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Replay tail must continue at event sequence '{expectedSequence}'.");
            }

            if (worldEvent.Minute < previousMinute)
            {
                throw new InvalidDataException(
                    $"Replay event '{worldEvent.Id}' moves backward from minute '{previousMinute}'.");
            }

            expectedSequence++;
            previousMinute = worldEvent.Minute;
        }
    }

    private static void EnsureReplayEventMatches(WorldEvent expected, WorldEvent generated)
    {
        var expectedFingerprint = WorldEventFingerprint.Compute([expected]);
        var generatedFingerprint = WorldEventFingerprint.Compute([generated]);
        if (!string.Equals(expectedFingerprint, generatedFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Replay diverged at '{expected.Id}': generated '{generated.Id}' ({generated.Type}) does not match the archive.");
        }
    }

    private static string Subject(WorldEvent worldEvent, int index)
    {
        if (index >= worldEvent.SubjectIds.Count)
        {
            throw new InvalidDataException(
                $"Replay event '{worldEvent.Id}' is missing subject index '{index}'.");
        }

        return worldEvent.SubjectIds[index];
    }

    private static string Detail(WorldEvent worldEvent, string key) =>
        worldEvent.Details.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException(
                $"Replay event '{worldEvent.Id}' is missing detail '{key}'.");

    private static long ParsePositiveLong(WorldEvent worldEvent, string key)
    {
        var value = Detail(worldEvent, key);
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidDataException(
                $"Replay event '{worldEvent.Id}' has invalid detail '{key}'.");
        }

        return parsed;
    }

    private static TravelMode ParseTravelMode(string mode) => mode switch
    {
        "walk" => TravelMode.Walk,
        "horse" => TravelMode.Horse,
        "with-group" => TravelMode.WithGroup,
        _ => throw new InvalidDataException($"Unknown replay travel mode '{mode}'."),
    };

    private static SimulationDetailLevel ParseDetailLevel(string detailLevel) => detailLevel switch
    {
        "l0" => SimulationDetailLevel.L0,
        "l1" => SimulationDetailLevel.L1,
        "l2" => SimulationDetailLevel.L2,
        _ => throw new InvalidDataException($"Unknown replay detail level '{detailLevel}'."),
    };
}
