using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class WorldBranchStoreTests
{
    [Fact]
    public void BranchesShareImmutableBaseAndKeepStateEventsAndRandomStreamsIsolated()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-branches-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var snapshotStore = new WorldSnapshotStore();
            var baseEngine = RepositoryFixture.CreateEngine();
            _ = baseEngine.Wait(5);
            var baseArchivePath = Path.Combine(directory, "base.db");
            using (var baseArchive = new WorldEventArchive(baseArchivePath))
            {
                baseArchive.Append(baseEngine.State.Events);
                baseArchive.CreateCheckpoint(
                    baseEngine.State.EventSequenceCursor - 1,
                    baseEngine.State.ComputeEventFingerprint(),
                    snapshotStore.Serialize(baseEngine.State));
            }

            var branchAPath = Path.Combine(directory, "a");
            var branchBPath = Path.Combine(directory, "b");
            using (var branchA = WorldBranchStore.Create(baseArchivePath, branchAPath, "a"))
            using (var branchB = WorldBranchStore.Create(baseArchivePath, branchBPath, "b"))
            {
                var engineA = new WorldEngine(branchA.Load());
                var engineB = new WorldEngine(branchB.Load());
                var firstTailSequence = engineA.State.EventSequenceCursor;
                _ = engineA.State.RandomStreams.NextUInt64("branch-test", "shared");
                _ = engineB.State.RandomStreams.NextUInt64("branch-test", "shared");
                _ = engineB.State.RandomStreams.NextUInt64("branch-test", "shared");
                engineA.Schedule(
                    engineA.State.CurrentMinute + 1,
                    ScheduledEventPhase.SummaryAndNotification,
                    "branch.a",
                    "branch_a_event",
                    causeIds: [baseEngine.State.Events[^1].Id]);
                engineB.Schedule(
                    engineB.State.CurrentMinute + 1,
                    ScheduledEventPhase.SummaryAndNotification,
                    "branch.b",
                    "branch_b_event");
                _ = engineA.Wait(1);
                _ = engineB.Wait(1);
                branchA.Save(engineA.State);
                branchB.Save(engineB.State);

                Assert.Equal(firstTailSequence, branchA.TailArchive.StartingSequence + 1);
                Assert.Equal(firstTailSequence, branchB.TailArchive.StartingSequence + 1);
                Assert.Contains(
                    branchA.TailArchive.ReadAfter(branchA.TailArchive.StartingSequence),
                    worldEvent => worldEvent.Type == "branch_a_event");
                Assert.Contains(
                    branchB.TailArchive.ReadAfter(branchB.TailArchive.StartingSequence),
                    worldEvent => worldEvent.Type == "branch_b_event");
                Assert.Equal(baseEngine.State.Events[0].Id, branchA.Find(baseEngine.State.Events[0].Id)?.Id);
                var branchAEvent = branchA.TailArchive.ReadAfter(branchA.TailArchive.StartingSequence)
                    .Single(worldEvent => worldEvent.Type == "branch_a_event");
                Assert.Equal(
                    new[] { "branch_a_event", baseEngine.State.Events[^1].Type },
                    branchA.Why(branchAEvent.Id, maximumDepth: 1)
                        .Select(item => item.Event.Type));
            }

            using var reopenedA = WorldBranchStore.Open(branchAPath);
            using var reopenedB = WorldBranchStore.Open(branchBPath);
            var restoredA = reopenedA.Load();
            var restoredB = reopenedB.Load();
            Assert.NotEqual(restoredA.ComputeEventFingerprint(), restoredB.ComputeEventFingerprint());
            Assert.Equal(1UL, restoredA.RandomStreams.Streams["branch-test:shared"].DrawCount);
            Assert.Equal(2UL, restoredB.RandomStreams.Streams["branch-test:shared"].DrawCount);
            using var reopenedBase = new WorldEventArchive(baseArchivePath);
            Assert.Equal(baseEngine.State.ComputeEventFingerprint(), reopenedBase.LatestCheckpoint?.EventFingerprint);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
