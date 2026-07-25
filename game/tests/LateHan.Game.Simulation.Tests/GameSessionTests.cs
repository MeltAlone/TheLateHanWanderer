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
    public void ThreeTenDayPeriodsProduceDifferentLocalStatesWithExplanations()
    {
        var session = CreateSession();
        var initial = session.SettlementStates.ToDictionary(item => item.Key, item => item.Value);

        session.Rest(23);

        var changed = session.SettlementStates.Values
            .Where(item => item != initial[item.SettlementId])
            .ToArray();
        Assert.True(changed.Length >= 3);
        Assert.Contains(session.Log, item =>
            item.Category == LogCategory.Period &&
            item.Text.Contains("缘由：", StringComparison.Ordinal) &&
            item.Text.Contains("治安", StringComparison.Ordinal));
        Assert.Contains(session.CharacterPlans.Values, item => item.LastIntent.Contains("整顿", StringComparison.Ordinal));
    }

    [Fact]
    public void PlayerWorkChangesTheNextPeriodOutcome()
    {
        var scenario = DemoScenarioFactory.Create();
        var passive = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        var active = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));

        passive.Rest(3);
        active.Work();

        var passiveState = passive.StateOf("settlement.luoyang");
        var activeState = active.StateOf("settlement.luoyang");
        Assert.True(activeState.GovernmentControl > passiveState.GovernmentControl);
        Assert.Contains(active.Log, item => item.Category == LogCategory.Period && item.Text.Contains("玩家参与官府文书", StringComparison.Ordinal));
    }

    [Fact]
    public void TravelContributesToRoadRiskAtPeriodBoundary()
    {
        var session = CreateSession();
        var initialRisk = session.StateOfRoad("road.luoyang.mengjin").Risk;

        session.TravelTo("settlement.mengjin");
        session.Rest();

        Assert.True(session.StateOfRoad("road.luoyang.mengjin").Risk > initialRisk);
    }

    [Fact]
    public void BackgroundChangesAccessToTheSameOfficial()
    {
        var scenario = DemoScenarioFactory.Create();
        var scholar = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.scholar"));
        var clerk = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        scholar.Rest();
        clerk.Rest();
        scholar.EnterUrbanLocation("luoyang.government");
        clerk.EnterUrbanLocation("luoyang.government");

        var scholarAction = scholar.GetActionsForCharacter("character.wang_yun")
            .Single(item => item.Kind == InteractionActionKind.Introduce);
        var clerkAction = clerk.GetActionsForCharacter("character.wang_yun")
            .Single(item => item.Kind == InteractionActionKind.Introduce);

        Assert.False(scholarAction.IsEnabled);
        Assert.Contains("官署", scholarAction.BlockReason, StringComparison.Ordinal);
        Assert.True(clerkAction.IsEnabled);
    }

    [Fact]
    public void BusyCharacterCanBeScheduledAndMetByAppointment()
    {
        var session = CreateSession();
        session.EnterUrbanLocation("luoyang.school");

        var initialActions = session.GetActionsForCharacter("character.cai_yan");
        Assert.False(initialActions.Single(item => item.Kind == InteractionActionKind.Introduce).IsEnabled);
        var schedule = initialActions.Single(item => item.Kind == InteractionActionKind.ScheduleMeeting);
        session.ExecuteInteraction(schedule.Id);

        var commitment = Assert.Single(session.Commitments);
        Assert.Equal(new(189, 8, 20), commitment.DueDate);
        Assert.Equal(new(189, 8, 19), session.Date);

        session.Rest();
        var attend = Assert.Single(session.GetActionsForCharacter("character.cai_yan"));
        Assert.Equal(InteractionActionKind.AttendMeeting, attend.Kind);
        Assert.True(attend.IsEnabled);
        session.ExecuteInteraction(attend.Id);

        var connection = session.ConnectionWith("character.cai_yan");
        Assert.Equal(RecognitionLevel.Acquainted, connection.Recognition);
        Assert.Equal(3, connection.Favor);
        Assert.Equal(2, connection.Trust);
        Assert.Equal(CommitmentStatus.Fulfilled, Assert.Single(session.Commitments).Status);
    }

    [Fact]
    public void MissingAppointmentLeavesAWorldFact()
    {
        var session = CreateSession();
        session.EnterUrbanLocation("luoyang.school");
        var schedule = session.GetActionsForCharacter("character.cai_yan")
            .Single(item => item.Kind == InteractionActionKind.ScheduleMeeting);
        session.ExecuteInteraction(schedule.Id);

        session.Rest(2);

        Assert.Equal(CommitmentStatus.Missed, Assert.Single(session.Commitments).Status);
        Assert.Contains(session.Log, item => item.Category == LogCategory.Commitment && item.Text.Contains("失约", StringComparison.Ordinal));
    }

    [Fact]
    public void KnownTopicsGenerateContextualConversationActions()
    {
        var session = CreateSession();
        session.EnterUrbanLocation("luoyang.school");
        var schedule = session.GetActionsForCharacter("character.cai_yan")
            .Single(item => item.Kind == InteractionActionKind.ScheduleMeeting);
        session.ExecuteInteraction(schedule.Id);
        session.Rest();
        var attend = Assert.Single(session.GetActionsForCharacter("character.cai_yan"));
        session.ExecuteInteraction(attend.Id);

        var topicAction = session.GetActionsForCharacter("character.cai_yan")
            .Single(item => item.Kind == InteractionActionKind.DiscussTopic && item.TopicId == "topic.court_upheaval");
        Assert.True(topicAction.IsEnabled);
        session.ExecuteInteraction(topicAction.Id);

        Assert.Equal(3, session.ConnectionWith("character.cai_yan").Trust);
        Assert.Contains(session.Log, item => item.Text.Contains("京师变局", StringComparison.Ordinal));
    }

    [Fact]
    public void InformationGatheringAddsAUsableTopic()
    {
        var session = CreateSession();
        var initialTopics = session.KnownTopics.Count;

        session.GatherInformation();

        Assert.Equal(initialTopics + 1, session.KnownTopics.Count);
        Assert.Contains(session.Log, item => item.Text.Contains("开始了解", StringComparison.Ordinal));
    }

    [Fact]
    public void NonColocatedCharacterHasNoInteractionMenu()
    {
        var session = CreateSession();

        Assert.Empty(session.GetActionsForCharacter("character.xun_yu"));
        Assert.Throws<InvalidOperationException>(() => session.ExecuteInteraction("interaction:visit:character.xun_yu"));
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
