using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class WorldEventReplayTests
{
    [Fact]
    public void FullReplayAndMinute180SnapshotReachTheSameMinute6060State()
    {
        var snapshotStore = new WorldSnapshotStore();
        var canonical = RepositoryFixture.CreateEngine();
        _ = canonical.Wait(180);
        var minute180Snapshot = snapshotStore.Serialize(canonical.State);
        var minute180Sequence = canonical.State.EventSequenceCursor;
        _ = canonical.Wait(6060 - canonical.State.CurrentMinute);

        var allEvents = canonical.State.Events.ToArray();
        var tailEvents = allEvents.Where(worldEvent => worldEvent.Sequence >= minute180Sequence).ToArray();
        var fromMinuteZero = RepositoryFixture.CreateEngine();
        var fullReplay = fromMinuteZero.ReplayEvents(allEvents);
        var fromMinute180 = new WorldEngine(snapshotStore.Load(minute180Snapshot));
        var tailReplay = fromMinute180.ReplayEvents(tailEvents);

        Assert.Equal(allEvents.Length, fullReplay.EventCount);
        Assert.Equal(tailEvents.Length, tailReplay.EventCount);
        Assert.Equal(6060, fromMinuteZero.State.CurrentMinute);
        Assert.Equal(6060, fromMinute180.State.CurrentMinute);
        Assert.Equal(snapshotStore.Serialize(canonical.State), snapshotStore.Serialize(fromMinuteZero.State));
        Assert.Equal(snapshotStore.Serialize(canonical.State), snapshotStore.Serialize(fromMinute180.State));
    }

    [Fact]
    public void ReplayRejectsAnEventThatDoesNotMatchDeterministicExecution()
    {
        var canonical = RepositoryFixture.CreateEngine();
        _ = canonical.Wait(10);
        var events = canonical.State.Events.ToArray();
        events[0] = events[0] with
        {
            Details = new Dictionary<string, string>(events[0].Details, StringComparer.Ordinal)
            {
                ["duration_minutes"] = "11",
            },
        };

        var replay = RepositoryFixture.CreateEngine();
        Assert.Throws<InvalidDataException>(() => replay.ReplayEvents(events));
    }

    [Fact]
    public void ReplayReconstructsTheCompleteDeliveryCommandPath()
    {
        var snapshotStore = new WorldSnapshotStore();
        var canonical = RepositoryFixture.CreateEngine();
        _ = canonical.Move(
            "person.player_clerk",
            "place.luoyang.sili_office",
            TravelMode.Walk);
        _ = canonical.Deliver(
            "person.player_clerk",
            "item.sealed_note_to_yuan_shao",
            "person.yuan_shao");
        _ = canonical.Move(
            "person.player_clerk",
            "place.luoyang.general_in_chief_office",
            TravelMode.Walk);
        _ = canonical.Tell(
            "person.player_clerk",
            "person.li_wen",
            "proposition.general_office_requests_status");

        var replay = RepositoryFixture.CreateEngine();
        _ = replay.ReplayEvents(canonical.State.Events.ToArray());

        Assert.Equal(snapshotStore.Serialize(canonical.State), snapshotStore.Serialize(replay.State));
    }

    [Fact]
    public void ArchiveFallsBackFromDamagedLatestSnapshotAndReplaysItsTail()
    {
        var path = Path.Combine(Path.GetTempPath(), $"latehan-replay-{Guid.NewGuid():N}.db");
        try
        {
            var snapshotStore = new WorldSnapshotStore();
            var canonical = RepositoryFixture.CreateEngine();
            _ = canonical.Wait(180);
            var firstCheckpointSequence = canonical.State.EventSequenceCursor - 1;
            var firstCheckpointFingerprint = canonical.State.ComputeEventFingerprint();
            var firstCheckpointPayload = snapshotStore.Serialize(canonical.State);
            var firstEvents = canonical.State.Events.ToArray();
            _ = canonical.Wait(6060 - canonical.State.CurrentMinute);
            var allEvents = canonical.State.Events.ToArray();

            using var archive = new WorldEventArchive(path);
            archive.Append(firstEvents);
            archive.CreateCheckpoint(
                firstCheckpointSequence,
                firstCheckpointFingerprint,
                firstCheckpointPayload);
            archive.Append(allEvents[firstEvents.Length..]);
            archive.CreateCheckpoint(
                canonical.State.EventSequenceCursor - 1,
                canonical.State.ComputeEventFingerprint(),
                "damaged snapshot"u8);

            var restored = WorldArchiveRestorer.Restore(archive, snapshotStore);

            Assert.Equal(new[] { allEvents[^1].Id }, restored.RejectedCheckpointEventIds);
            Assert.Equal(firstCheckpointSequence, restored.Checkpoint.EventSequence);
            Assert.Equal(allEvents.Length - firstEvents.Length, restored.ReplayedEventCount);
            Assert.Equal(snapshotStore.Serialize(canonical.State), snapshotStore.Serialize(restored.World));
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var candidate = path + suffix;
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }
}
