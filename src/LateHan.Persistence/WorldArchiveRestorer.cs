using System.Text.Json;
using System.Security.Cryptography;
using LateHan.Core;

namespace LateHan.Persistence;

public sealed record WorldArchiveRestoreResult(
    WorldState World,
    EventArchiveCheckpoint Checkpoint,
    int ReplayedEventCount,
    IReadOnlyList<string> RejectedCheckpointEventIds);

public static class WorldArchiveRestorer
{
    public static WorldArchiveRestoreResult Restore(
        WorldEventArchive archive,
        WorldSnapshotStore snapshotStore,
        int maximumTailEvents = 25_000,
        int maximumCheckpointCandidates = 64)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTailEvents, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCheckpointCandidates, 1);

        var checkpoints = archive.ReadCheckpointsNewestFirst(maximumCheckpointCandidates);
        if (checkpoints.Count == 0)
        {
            throw new InvalidDataException("Event archive has no checkpoint to restore.");
        }

        var rejected = new List<string>();
        var failures = new List<Exception>();
        foreach (var checkpoint in checkpoints)
        {
            try
            {
                var world = snapshotStore.Load(checkpoint.SnapshotPayload);
                ValidateCheckpoint(world, checkpoint);
                var tail = archive.ReadAfter(checkpoint.EventSequence, checked(maximumTailEvents + 1));
                if (tail.Count > maximumTailEvents)
                {
                    throw new InvalidDataException(
                        $"Archive tail after '{checkpoint.EventId}' exceeds restore limit '{maximumTailEvents}'.");
                }

                var replay = new WorldEngine(world).ReplayEvents(tail);
                return new WorldArchiveRestoreResult(world, checkpoint, replay.EventCount, rejected);
            }
            catch (Exception exception) when (IsRecoverableCheckpointFailure(exception))
            {
                rejected.Add(checkpoint.EventId);
                failures.Add(exception);
            }
        }

        throw new InvalidDataException(
            $"None of the newest {checkpoints.Count} archive checkpoints could be restored.",
            new AggregateException(failures));
    }

    private static void ValidateCheckpoint(WorldState world, EventArchiveCheckpoint checkpoint)
    {
        var payloadHash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(checkpoint.SnapshotPayload))}";
        if (!string.Equals(payloadHash, checkpoint.SnapshotSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Checkpoint '{checkpoint.EventId}' snapshot payload hash does not match the archive.");
        }

        var lastEvent = world.Events.LastOrDefault();
        if (world.EventSequenceCursor - 1 != checkpoint.EventSequence ||
            lastEvent is null ||
            !string.Equals(lastEvent.Id, checkpoint.EventId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Checkpoint '{checkpoint.EventId}' does not match its snapshot event cursor.");
        }

        var fingerprint = world.ComputeEventFingerprint();
        if (!string.Equals(fingerprint, checkpoint.EventFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Checkpoint '{checkpoint.EventId}' snapshot fingerprint does not match the archive.");
        }
    }

    private static bool IsRecoverableCheckpointFailure(Exception exception) => exception is
        InvalidDataException or
        InvalidOperationException or
        ArgumentException or
        JsonException or
        NotSupportedException;
}
