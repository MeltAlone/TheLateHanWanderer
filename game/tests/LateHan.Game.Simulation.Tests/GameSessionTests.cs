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

        Assert.Equal(learning + 2, session.Player.Abilities.Learning);
        Assert.Equal(20, session.PlayerDevelopment.LearningExperience);
        Assert.Equal(2, session.PlayerDevelopment.RecentExperiences.Count);
        Assert.Contains(session.Log, item => item.Category == LogCategory.Growth);
        Assert.NotEqual(money, session.Player.Money);
        Assert.Equal(90, session.Career.UpkeepPaid);
        Assert.Equal(new(189, 8, 24), session.Date);
    }

    [Fact]
    public void BackgroundsReceiveDifferentCareerPathsAndAccessRequirements()
    {
        var scenario = DemoScenarioFactory.Create();
        var scholar = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.scholar"));
        var clerk = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        var ranger = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.ranger"));

        var scholarPath = Assert.Single(scholar.CareerOpportunities);
        var clerkPath = Assert.Single(clerk.CareerOpportunities);
        var rangerPath = Assert.Single(ranger.CareerOpportunities);

        Assert.Equal("参与士人清议", scholarPath.Title);
        Assert.Equal("承办郡府急务", clerkPath.Title);
        Assert.Equal("护送乡里商旅", rangerPath.Title);
        Assert.All(new[] { scholarPath, clerkPath, rangerPath }, item => Assert.False(item.IsEnabled));
    }

    [Theory]
    [InlineData("background.scholar", "luoyang.school")]
    [InlineData("background.clerk", "luoyang.government")]
    [InlineData("background.ranger", "luoyang.market")]
    public void EachBackgroundCanCompleteItsSixtyDayCareerGoal(string backgroundId, string locationId)
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == backgroundId));
        session.EnterUrbanLocation(locationId);

        for (var i = 0; i < 4; i++)
        {
            var opportunity = session.CareerOpportunities.Single(item => item.Kind == CareerOpportunityKind.CareerPath);
            Assert.True(opportunity.IsEnabled);
            session.ExecuteCareerOpportunity(opportunity.Id);
        }

        Assert.Equal(CareerGoalStatus.Completed, session.Career.Goal.Status);
        Assert.True(session.Career.Reputation >= 8);
        Assert.True(session.Career.Network >= 4);
        Assert.True(session.Date.DaysUntil(session.Career.Goal.Deadline) > 0);

        session.Rest(44);

        Assert.Equal(session.Career.Goal.Deadline, session.Date);
        Assert.Equal(CareerGoalStatus.Completed, session.Career.Goal.Status);
        Assert.All(session.HistoricalBranches, branch => Assert.Equal(HistoricalBranchStatus.Resolved, branch.Status));
    }

    [Fact]
    public void SixtyDaysOfBystandingFailsTheGoalAndResolvesAllBranches()
    {
        var session = CreateSession();

        session.Rest(61);

        Assert.Equal(CareerGoalStatus.Failed, session.Career.Goal.Status);
        Assert.All(session.HistoricalBranches, branch =>
        {
            Assert.Equal(HistoricalBranchStatus.Resolved, branch.Status);
            Assert.Equal(HistoricalBranchOutcome.Bystander, branch.Outcome);
        });
        Assert.Contains(session.Log, item => item.Category == LogCategory.World && item.Text.Contains("旁观同样成为历史", StringComparison.Ordinal));
    }

    [Fact]
    public void PlayerCanInterveneInAnActiveBranchAndChangeItsOutcome()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        session.EnterUrbanLocation("luoyang.government");
        session.Rest(3);

        var intervention = session.CareerOpportunities.Single(item => item.BranchId == "branch.capital_refugees");
        Assert.True(intervention.IsEnabled);
        session.ExecuteCareerOpportunity(intervention.Id);

        var branch = session.HistoricalBranches.Single(item => item.Id == "branch.capital_refugees");
        Assert.Equal(HistoricalBranchOutcome.PlayerInfluenced, branch.Outcome);
        Assert.Contains("登记安置流民", branch.PlayerApproach, StringComparison.Ordinal);
        Assert.True(session.Career.Reputation >= 4);
        Assert.Contains(session.Log, item => item.Text.Contains("受到你的介入", StringComparison.Ordinal));
    }

    [Fact]
    public void LocalOrganizationCreatesACommissionFromItsResourceShortage()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        session.EnterUrbanLocation("luoyang.government");

        var offer = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);

        Assert.Equal(OrganizationNeedKind.Grain, offer.OrganizationNeed);
        Assert.Contains("粮储仅为", offer.Description, StringComparison.Ordinal);
        Assert.Contains("八日内", offer.Description, StringComparison.Ordinal);
        Assert.True(offer.RewardMoney > 0);
    }

    [Fact]
    public void AcceptingAndFulfillingACommissionChangesLedgerAndOrganizationAssets()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        session.EnterUrbanLocation("luoyang.government");
        var grainBefore = session.CurrentResourceLedger.Grain;
        var offer = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);

        session.ExecuteCareerOpportunity(offer.Id);

        var accepted = Assert.Single(session.OrganizationCommissions);
        Assert.Equal(OrganizationCommissionStatus.Accepted, accepted.Status);
        Assert.Equal(session.Date.AddDays(8), accepted.DueDate);
        var fulfillment = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && !item.IsAcceptance);
        Assert.Contains("期限", fulfillment.Description, StringComparison.Ordinal);

        session.ExecuteCareerOpportunity(fulfillment.Id);

        Assert.Equal(OrganizationCommissionStatus.Completed, Assert.Single(session.OrganizationCommissions).Status);
        Assert.True(session.CurrentResourceLedger.Grain >= grainBefore + 10);
        Assert.True(session.Player.Money > scenario.Backgrounds.Single(item => item.Id == "background.clerk").StartingMoney);
        Assert.Contains(session.Log, item =>
            item.Category == LogCategory.Career && item.Text.Contains("资源账本", StringComparison.Ordinal));
        Assert.DoesNotContain(session.CareerOpportunities, item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);
    }

    [Fact]
    public void CompletedCommissionReturnsOnlyAfterItsTenDayCooldown()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        session.EnterUrbanLocation("luoyang.government");
        var offer = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);
        session.ExecuteCareerOpportunity(offer.Id);
        var fulfillment = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && !item.IsAcceptance);
        session.ExecuteCareerOpportunity(fulfillment.Id);

        session.Rest(6);

        Assert.DoesNotContain(session.CareerOpportunities, item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);

        session.Rest(1);

        Assert.Contains(session.CareerOpportunities, item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);
    }

    [Fact]
    public void MissingACommissionDeadlineWorsensTheOriginalShortage()
    {
        var scenario = DemoScenarioFactory.Create();
        var control = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        control.EnterUrbanLocation("luoyang.government");
        session.EnterUrbanLocation("luoyang.government");
        var offer = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);
        session.ExecuteCareerOpportunity(offer.Id);

        control.Rest(9);
        session.Rest(9);

        Assert.Equal(OrganizationCommissionStatus.Failed, Assert.Single(session.OrganizationCommissions).Status);
        Assert.True(session.CurrentResourceLedger.Grain < control.CurrentResourceLedger.Grain);
        Assert.Contains(session.Log, item =>
            item.Category == LogCategory.Commitment && item.Text.Contains("组织资产", StringComparison.Ordinal));
    }

    [Fact]
    public void NinetyDaysProduceDistinctResourceAndOrganizationTrajectories()
    {
        var session = CreateSession();
        var initialOrganizations = session.Organizations.ToDictionary(item => item.Key, item => item.Value);

        session.Rest(90);

        Assert.True(session.ResourceLedgers.Values
            .Select(item => (item.Population, item.Grain, item.Treasury, item.Labor))
            .Distinct()
            .Count() >= 3);
        Assert.Contains(session.Organizations, item => item.Value != initialOrganizations[item.Key]);
        Assert.Contains(session.Log, item =>
            item.Category == LogCategory.Period && item.Text.Contains("账本粮储", StringComparison.Ordinal));
    }

    [Fact]
    public void NpcPlansCompareKnownOpportunitiesAndReserveOrganizationResources()
    {
        var session = CreateSession();
        var initialOrganizations = session.Organizations.ToDictionary(item => item.Key, item => item.Value);

        session.Rest(10);

        var formedPlans = session.CharacterPlans.Values
            .Where(item => item.TargetOrganizationId is not null)
            .ToArray();
        Assert.NotEmpty(formedPlans);
        Assert.Contains(formedPlans, item => item.KnownSettlementIds.Count > 1);
        Assert.Contains(formedPlans, item =>
            session.Organizations[item.TargetOrganizationId!] != initialOrganizations[item.TargetOrganizationId!]);
        Assert.Contains(session.Log, item =>
            item.Category == LogCategory.World &&
            item.Text.Contains("权衡目标、能力、组织资源与行程", StringComparison.Ordinal));
    }

    [Fact]
    public void NpcTravelFollowsAChosenPlanInsteadOfRandomMigration()
    {
        var session = CreateSession();

        for (var day = 0; day < 20 && !session.CharacterPlans.Values.Any(item => item.Stage == CharacterPlanStage.Traveling); day++)
        {
            session.Rest();
        }

        var traveler = Assert.Single(session.CharacterPlans.Values.Where(item => item.Stage == CharacterPlanStage.Traveling).Take(1));
        Assert.NotNull(traveler.TargetSettlementId);
        Assert.NotNull(traveler.NextStepOn);
        Assert.True(session.Date.DaysUntil(traveler.NextStepOn.Value) > 0);
        Assert.Contains(session.Log, item =>
            item.Text.Contains("履行对", StringComparison.Ordinal) && item.Text.Contains("承诺", StringComparison.Ordinal));
    }

    [Fact]
    public void PlayerCanSupportOrCancelAVisibleNpcPlan()
    {
        var supported = CreatePlanInterventionSession();
        var supportAction = supported.GetActionsForCharacter("character.wang_yun")
            .Single(item => item.Kind == InteractionActionKind.SupportPlan);

        supported.ExecuteInteraction(supportAction.Id);

        Assert.Equal(15, supported.CharacterPlans["character.wang_yun"].PlayerSupport);
        Assert.Equal(2, supported.ConnectionWith("character.wang_yun").Favor);

        var cancelled = CreatePlanInterventionSession();
        var cancelAction = cancelled.GetActionsForCharacter("character.wang_yun")
            .Single(item => item.Kind == InteractionActionKind.DissuadePlan);

        cancelled.ExecuteInteraction(cancelAction.Id);

        Assert.Equal(CharacterPlanStage.Cancelled, cancelled.CharacterPlans["character.wang_yun"].Stage);
        Assert.Contains(cancelled.Log, item =>
            item.Category == LogCategory.Commitment && item.Text.Contains("暂缓", StringComparison.Ordinal));
    }

    [Fact]
    public void IdenticalInputsReplayNpcPlansDeterministically()
    {
        var first = CreateSession();
        var second = CreateSession();

        first.Rest(90);
        second.Rest(90);

        Assert.Equal(first.Date, second.Date);
        Assert.Equal(first.ResourceLedgers, second.ResourceLedgers);
        Assert.Equal(first.Organizations, second.Organizations);
        Assert.Equal(first.CharacterRelationships, second.CharacterRelationships);
        foreach (var characterId in first.CharacterPlans.Keys)
        {
            var firstPlan = first.CharacterPlans[characterId];
            var secondPlan = second.CharacterPlans[characterId];
            Assert.Equal(firstPlan.KnownSettlementIds, secondPlan.KnownSettlementIds);
            Assert.Equal(
                firstPlan with { KnownSettlementIds = [] },
                secondPlan with { KnownSettlementIds = [] });
            var firstDevelopment = first.CharacterDevelopments[characterId];
            var secondDevelopment = second.CharacterDevelopments[characterId];
            Assert.Equal(firstDevelopment.RecentExperiences, secondDevelopment.RecentExperiences);
            Assert.Equal(
                firstDevelopment with { RecentExperiences = [] },
                secondDevelopment with { RecentExperiences = [] });
        }

        Assert.Equal(first.Log, second.Log);
    }

    [Fact]
    public void NpcPlansCreateGrowthMemoriesAndPersistentWorkingRelationships()
    {
        var session = CreateSession();

        session.Rest(30);

        Assert.Contains(session.CharacterDevelopments.Values, item => item.TotalExperience > 0);
        Assert.Contains(session.CharacterDevelopments.Values, item => item.RecentExperiences.Count > 0);
        Assert.NotEmpty(session.CharacterRelationships);
        Assert.All(session.CharacterRelationships.Values, item =>
        {
            Assert.True(item.SharedExperiences > 0);
            Assert.Contains("共同", item.LastReason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TrustedNpcCoworkersContributeToPlanResolution()
    {
        var session = CreateSession();
        var snapshot = session.CreateSnapshot();
        var plans = snapshot.CharacterPlans!.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var resolveOn = snapshot.Date.AddDays(1);
        foreach (var characterId in new[] { "character.cai_yong", "character.wang_yun" })
        {
            plans[characterId] = plans[characterId] with
            {
                Stage = CharacterPlanStage.Executing,
                TargetOrganizationId = "organization.luoyang.government",
                Need = OrganizationNeedKind.Grain,
                TargetSettlementId = "settlement.luoyang",
                TargetUrbanLocationId = "luoyang.government",
                StartedOn = snapshot.Date,
                NextStepOn = resolveOn,
                ReservedEffort = 3,
            };
        }

        var relationship = CharacterRelationshipState.Create(
            "character.cai_yong",
            "character.wang_yun",
            snapshot.Date,
            "此前共同处理官署事务") with
        {
            Favor = 6,
            Trust = 10,
            SharedExperiences = 2,
        };
        var relationships = new Dictionary<string, CharacterRelationshipState>(StringComparer.Ordinal)
        {
            [CharacterRelationshipState.KeyFor(relationship.FirstCharacterId, relationship.SecondCharacterId)] = relationship,
        };
        var restored = GameSession.Restore(
            session.Scenario,
            snapshot with
            {
                CharacterPlans = plans,
                CharacterRelationships = relationships,
            });

        restored.Rest();

        Assert.Contains("同伴协力 5", restored.CharacterPlans["character.cai_yong"].Result, StringComparison.Ordinal);
        Assert.True(restored.CharacterRelationships.Values.Single().SharedExperiences >= 3);
    }

    private static GameSession CreatePlanInterventionSession()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds.Single(item => item.Id == "background.clerk"));
        var testDate = session.Date.AddDays(1);
        var plans = session.CharacterPlans.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        plans["character.wang_yun"] = plans["character.wang_yun"] with
        {
            Stage = CharacterPlanStage.Preparing,
            TargetOrganizationId = "organization.luoyang.government",
            Need = OrganizationNeedKind.Grain,
            TargetSettlementId = "settlement.luoyang",
            TargetUrbanLocationId = "luoyang.government",
            StartedOn = testDate,
            NextStepOn = testDate.AddDays(3),
            ReservedEffort = 3,
            LastIntent = "准备核查洛阳粮储",
        };
        var connections = session.CreateSnapshot().SocialConnections!
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        connections["character.wang_yun"] = new RelationshipState(RecognitionLevel.Acquainted, 0, 20, 0);
        var snapshot = session.CreateSnapshot() with
        {
            Date = testDate,
            UrbanLocationId = "luoyang.government",
            SocialConnections = connections,
            CharacterPlans = plans,
        };
        return GameSession.Restore(scenario, snapshot);
    }

    private static GameSession CreateSession()
    {
        var scenario = DemoScenarioFactory.Create();
        return new GameSession(scenario, scenario.Backgrounds[0]);
    }
}
