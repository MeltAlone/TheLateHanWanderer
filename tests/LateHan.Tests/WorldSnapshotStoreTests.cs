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

    [Fact]
    public void SnapshotPreservesScheduledQueueRandomCursorAndInterruption()
    {
        var store = new WorldSnapshotStore();
        var original = RepositoryFixture.CreateEngine();
        _ = original.State.RandomStreams.NextUInt64("travel", "person.player_clerk");
        original.Schedule(
            95,
            ScheduledEventPhase.SummaryAndNotification,
            "person.player_clerk",
            "urgent_recall",
            "place.luoyang.general_in_chief_office",
            interruptsPlayer: true);
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"latehan-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(original.State, snapshotPath);
            var restored = new WorldEngine(store.Load(snapshotPath));

            Assert.Equal(
                original.State.RandomStreams.NextUInt64("travel", "person.player_clerk"),
                restored.State.RandomStreams.NextUInt64("travel", "person.player_clerk"));
            var originalWait = original.Wait(240);
            var restoredWait = restored.Wait(240);

            Assert.Equal(ActionStatus.Interrupted, restoredWait.Status);
            Assert.Equal(95, restored.State.CurrentMinute);
            Assert.Equal(originalWait.Events.Select(item => item.Type), restoredWait.Events.Select(item => item.Type));
            Assert.Equal(original.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(original.State.ScheduledEventSequenceCursor, restored.State.ScheduledEventSequenceCursor);
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

    [Fact]
    public void SnapshotPreservesModifiedReplayMarker()
    {
        var store = new WorldSnapshotStore();
        var original = RepositoryFixture.CreateEngine();
        original.ScheduleExternalIntervention(
            10,
            ScheduledEventPhase.PlanEvaluation,
            "person.dong_zhuo",
            "plan_evaluated");
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"latehan-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(original.State, snapshotPath);

            var restored = store.Load(snapshotPath);

            Assert.True(restored.ReplayModified);
            Assert.Equal(2, restored.EventSequenceCursor);
            Assert.Equal(2, restored.ScheduledEventSequenceCursor);
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

    [Fact]
    public void InterruptedTravelSaveLoadAndResumeMatchesContinuousRun()
    {
        var store = new WorldSnapshotStore();
        var original = RepositoryFixture.CreateEngine();
        var action = original.BeginTravel(
            "person.player_clerk",
            "place.luoyang.eastern_road",
            TravelMode.Horse);
        original.ScheduleTravelRiskCheck(action.Id, 40, "horse_injured", ulong.MaxValue);
        _ = original.AdvanceAction(action.Id);
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"latehan-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(original.State, snapshotPath);
            var restored = new WorldEngine(store.Load(snapshotPath));

            _ = original.ResumeTravel(action.Id, TravelMode.Walk);
            _ = restored.ResumeTravel(action.Id, TravelMode.Walk);

            Assert.Equal(original.State.CurrentMinute, restored.State.CurrentMinute);
            Assert.Equal(original.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(original.State.ActionSequenceCursor, restored.State.ActionSequenceCursor);
            Assert.Equal(original.State.ScheduledEventSequenceCursor, restored.State.ScheduledEventSequenceCursor);
            Assert.Equal(
                original.State.RandomStreams.Streams[$"travel-risk:{action.Id}"].DrawCount,
                restored.State.RandomStreams.Streams[$"travel-risk:{action.Id}"].DrawCount);
            Assert.Equal(
                original.State.Actors["person.player_clerk"].LocationId,
                restored.State.Actors["person.player_clerk"].LocationId);
            Assert.Equal(
                original.State.Actions[action.Id].Travel.ElapsedMinutes,
                restored.State.Actions[action.Id].Travel.ElapsedMinutes);
            Assert.Equal(ActionStatus.Completed, restored.State.Actions[action.Id].Status);
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

    [Fact]
    public void RunningTravelSnapshotPreservesPendingCompletion()
    {
        var store = new WorldSnapshotStore();
        var original = RepositoryFixture.CreateEngine();
        var action = original.BeginTravel(
            "person.player_clerk",
            "place.luoyang.sili_office",
            TravelMode.Walk);
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"latehan-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(original.State, snapshotPath);
            var restored = new WorldEngine(store.Load(snapshotPath));

            _ = original.AdvanceAction(action.Id);
            _ = restored.AdvanceAction(action.Id);

            Assert.Equal(original.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(22, restored.State.CurrentMinute);
            Assert.Equal(ActionStatus.Completed, restored.State.Actions[action.Id].Status);
            Assert.Equal(
                "place.luoyang.sili_office",
                restored.State.Actors["person.player_clerk"].LocationId);
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
