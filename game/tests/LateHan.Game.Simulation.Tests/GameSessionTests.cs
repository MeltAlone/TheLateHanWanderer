using LateHan.Game.Content;
using LateHan.Game.Simulation;

namespace LateHan.Game.Simulation.Tests;

public sealed class GameSessionTests
{
    [Fact]
    public void TravelConsumesRouteDaysAndMovesPlayer()
    {
        var session = CreateSession();

        session.TravelTo("settlement.mengjin");

        Assert.Equal(new(189, 8, 20), session.Date);
        Assert.Equal("settlement.mengjin", session.Player.SettlementId);
        Assert.Contains(session.Log, item => item.Category == LogCategory.Travel && item.Text.Contains("抵达孟津", StringComparison.Ordinal));
    }

    [Fact]
    public void TenDaysOfWaitingRunsPeriodAndNpcWorldSteps()
    {
        var session = CreateSession();

        session.Rest(10);

        Assert.Equal(new(189, 8, 28), session.Date);
        Assert.Contains(session.Log, item => item.Category == LogCategory.Period);
        Assert.Contains(session.Log, item => item.Category == LogCategory.World && item.Text.Contains("并非由玩家触发", StringComparison.Ordinal));
    }

    [Fact]
    public void VisitingRequiresCoLocationAndBuildsRelationship()
    {
        var session = CreateSession();
        session.EnterUrbanLocation("luoyang.school");

        session.VisitCharacter("character.cai_yan");

        Assert.Equal(3, session.RelationshipWith("character.cai_yan"));
        Assert.Equal(new(189, 8, 19), session.Date);
        Assert.Throws<InvalidOperationException>(() => session.VisitCharacter("character.xun_yu"));
    }

    [Fact]
    public void TrainingAndWorkConsumeDaysAndChangePlayerState()
    {
        var session = CreateSession();
        var learning = session.Player.Abilities.Learning;
        var money = session.Player.Money;

        session.Train();
        session.Work();

        Assert.Equal(learning + 1, session.Player.Abilities.Learning);
        Assert.True(session.Player.Money > money);
        Assert.Equal(new(189, 8, 24), session.Date);
    }

    private static GameSession CreateSession()
    {
        var scenario = DemoScenarioFactory.Create();
        return new GameSession(scenario, scenario.Backgrounds[0]);
    }
}
