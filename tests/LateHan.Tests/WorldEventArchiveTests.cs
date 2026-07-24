using System.Collections.ObjectModel;
using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class WorldEventArchiveTests
{
    [Fact]
    public void ArchiveReopensQueriesCausalityAndAuditsFingerprint()
    {
        var path = TemporaryArchivePath();
        try
        {
            var events = CreateEvents(1, 8);
            var fingerprint = WorldEventFingerprint.Compute(events);
            using (var archive = new WorldEventArchive(path))
            {
                archive.Append(events[..4]);
                archive.CreateCheckpoint(4, WorldEventFingerprint.Compute(events[..4]), [1, 2, 3, 4]);
                archive.Append(events[4..]);
            }

            using var reopened = new WorldEventArchive(path);
            Assert.Equal(8, reopened.EventCount);
            Assert.Equal(8, reopened.LastSequence);
            Assert.Equal("event.00000008", reopened.Find("event.00000008")?.Id);
            Assert.Null(reopened.Find("event.missing"));

            var why = reopened.Why("event.00000008", maximumDepth: 3);
            Assert.Equal(
                new[] { "event.00000008", "event.00000007", "event.00000006", "event.00000005" },
                why.Select(item => item.Event.Id));
            Assert.Equal(new[] { 0, 1, 2, 3 }, why.Select(item => item.Depth));

            var restored = Assert.IsType<EventArchiveRestore>(reopened.RestoreLatest());
            Assert.Equal(4, restored.Checkpoint.EventSequence);
            Assert.StartsWith("sha256:", restored.Checkpoint.SnapshotSha256, StringComparison.Ordinal);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, restored.Checkpoint.SnapshotPayload);
            Assert.Equal(
                WorldEventFingerprint.Compute(events[4..]),
                WorldEventFingerprint.Compute(restored.EventsAfterCheckpoint));
            Assert.Throws<InvalidDataException>(() => reopened.RestoreLatest(maximumTailEvents: 3));

            var audit = reopened.Audit();
            Assert.Equal(8, audit.EventCount);
            Assert.Equal(8, audit.LastSequence);
            Assert.Equal(fingerprint, audit.EventFingerprint);

            var backupPath = $"{path}.gz";
            Assert.True(reopened.CreateCompressedBackup(backupPath) > 0);
            Assert.True(File.Exists(backupPath));

            using var readOnly = WorldEventArchive.OpenReadOnly(path);
            Assert.Throws<InvalidOperationException>(() => readOnly.Append(CreateEvents(9, 1)));
        }
        finally
        {
            DeleteArchiveFiles(path);
        }
    }

    [Fact]
    public void ArchiveRejectsSequenceGapsWithoutPartialAppend()
    {
        var path = TemporaryArchivePath();
        try
        {
            using var archive = new WorldEventArchive(path);
            archive.Append(CreateEvents(1, 2));
            var invalidBatch = CreateEvents(3, 2);
            invalidBatch[1] = invalidBatch[1] with { Sequence = 5, Id = "event.00000005" };

            Assert.Throws<InvalidDataException>(() => archive.Append(invalidBatch));
            Assert.Equal(2, archive.EventCount);
            Assert.Equal(2, archive.LastSequence);
        }
        finally
        {
            DeleteArchiveFiles(path);
        }
    }

    [Fact]
    public void ArchiveRejectsDuplicateCheckpoint()
    {
        var path = TemporaryArchivePath();
        try
        {
            using var archive = new WorldEventArchive(path);
            var events = CreateEvents(1, 1);
            archive.Append(events);
            var fingerprint = WorldEventFingerprint.Compute(events);
            archive.CreateCheckpoint(1, fingerprint, [1]);

            Assert.Throws<InvalidDataException>(() => archive.CreateCheckpoint(1, fingerprint, [2]));
        }
        finally
        {
            DeleteArchiveFiles(path);
        }
    }

    private static WorldEvent[] CreateEvents(long firstSequence, int count)
    {
        var events = new WorldEvent[count];
        for (var index = 0; index < count; index++)
        {
            var sequence = firstSequence + index;
            events[index] = new WorldEvent(
                sequence,
                $"event.{sequence:D8}",
                "archive_test_event",
                sequence * 5,
                "place.test",
                ["person.test"],
                sequence == 1 ? [] : [$"event.{sequence - 1:D8}"],
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sequence"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));
        }

        return events;
    }

    private static string TemporaryArchivePath() =>
        Path.Combine(Path.GetTempPath(), $"latehan-event-archive-{Guid.NewGuid():N}.db");

    private static void DeleteArchiveFiles(string path)
    {
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal", ".gz" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
