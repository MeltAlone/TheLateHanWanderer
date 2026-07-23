using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class WorldSnapshotStoreTests
{
    [Fact]
    public void SnapshotRoundTripPreservesStateAndDeterministicContinuation()
    {
        var store = new WorldSnapshotStore();
        var original = RepositoryFixture.CreateEngine();
        original.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"latehan-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(original.State, snapshotPath);
            var restored = new WorldEngine(store.Load(snapshotPath));

            original.Deliver("person.player_clerk", "item.sealed_note_to_yuan_shao", "person.yuan_shao");
            restored.Deliver("person.player_clerk", "item.sealed_note_to_yuan_shao", "person.yuan_shao");

            Assert.Equal(original.State.CurrentMinute, restored.State.CurrentMinute);
            Assert.Equal(original.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(
                original.State.Items["item.sealed_note_to_yuan_shao"].HolderId,
                restored.State.Items["item.sealed_note_to_yuan_shao"].HolderId);
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }

            if (File.Exists($"{snapshotPath}.tmp"))
            {
                File.Delete($"{snapshotPath}.tmp");
            }
        }
    }
}
