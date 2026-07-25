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
}
