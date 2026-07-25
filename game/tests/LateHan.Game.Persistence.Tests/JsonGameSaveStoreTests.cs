using LateHan.Game.Content;
using LateHan.Game.Persistence;
using LateHan.Game.Simulation;

namespace LateHan.Game.Persistence.Tests;

public sealed class JsonGameSaveStoreTests
{
    [Fact]
    public void SaveAndLoadRestoresPlayableWorldState()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[2], "测试者");
        session.TravelTo("settlement.mengjin");
        session.Rest(10);
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-game-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "save.json");

        try
        {
            var store = new JsonGameSaveStore();
            store.Save(path, session.CreateSnapshot());
            var restored = GameSession.Restore(scenario, store.Load(path));

            Assert.Equal(session.Date, restored.Date);
            Assert.Equal(session.Player.SettlementId, restored.Player.SettlementId);
            Assert.Equal(session.Player.Money, restored.Player.Money);
            Assert.Equal(session.Log, restored.Log);

            restored.TravelTo("settlement.henei");
            Assert.Equal("settlement.henei", restored.Player.SettlementId);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void VersionTwoSavePreservesSocialStateAndAppointments()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[0]);
        session.EnterUrbanLocation("luoyang.school");
        var schedule = session.GetActionsForCharacter("character.cai_yan")
            .Single(item => item.Kind == InteractionActionKind.ScheduleMeeting);
        session.ExecuteInteraction(schedule.Id);
        var store = new JsonGameSaveStore();
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-game-social-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "save.json");

        try
        {
            store.Save(path, session.CreateSnapshot());
            var restored = GameSession.Restore(scenario, store.Load(path));

            Assert.Equal(RecognitionLevel.HeardOf, restored.ConnectionWith("character.cai_yan").Recognition);
            Assert.Equal(session.KnownTopics, restored.KnownTopics);
            Assert.Equal(session.Commitments, restored.Commitments);
            Assert.Contains(restored.GetActionsForCharacter("character.cai_yan"), item => item.Kind == InteractionActionKind.AttendMeeting);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void VersionOneSnapshotMigratesLegacyFavor()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[0]);
        var legacy = session.CreateSnapshot() with
        {
            SchemaVersion = 1,
            Relationships = new Dictionary<string, int> { ["character.cai_yan"] = 3 },
            SocialConnections = null,
            KnownTopicIds = null,
            Commitments = null,
        };

        var restored = GameSession.Restore(scenario, legacy);

        Assert.Equal(RecognitionLevel.Acquainted, restored.ConnectionWith("character.cai_yan").Recognition);
        Assert.Equal(3, restored.ConnectionWith("character.cai_yan").Favor);
        Assert.Contains(restored.KnownTopics, item => item.Id == "topic.court_upheaval");
    }
}
