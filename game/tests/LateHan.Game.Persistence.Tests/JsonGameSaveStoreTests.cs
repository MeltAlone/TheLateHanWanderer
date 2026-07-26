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
            Assert.Equal(session.SettlementStates, restored.SettlementStates);
            Assert.Equal(session.RoadStates, restored.RoadStates);
            Assert.Equal(session.Career, restored.Career);
            Assert.Equal(session.HistoricalBranches, restored.HistoricalBranches);
            AssertPlansEqual(session.CharacterPlans, restored.CharacterPlans);
            Assert.Equal(session.ResourceLedgers, restored.ResourceLedgers);
            Assert.Equal(session.Organizations, restored.Organizations);
            Assert.Equal(session.OrganizationCommissions, restored.OrganizationCommissions);
            AssertDevelopmentEqual(session.PlayerDevelopment, restored.PlayerDevelopment);
            AssertDevelopmentsEqual(session.CharacterDevelopments, restored.CharacterDevelopments);
            Assert.Equal(session.CharacterRelationships, restored.CharacterRelationships);

            session.Rest(5);
            restored.Rest(5);
            AssertPlansEqual(session.CharacterPlans, restored.CharacterPlans);
            Assert.Equal(session.ResourceLedgers, restored.ResourceLedgers);
            Assert.Equal(session.Organizations, restored.Organizations);
            AssertDevelopmentsEqual(session.CharacterDevelopments, restored.CharacterDevelopments);
            Assert.Equal(session.CharacterRelationships, restored.CharacterRelationships);
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
    public void VersionTwoSnapshotReceivesScenarioLocalStateDefaults()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[0]);
        var versionTwo = session.CreateSnapshot() with
        {
            SchemaVersion = 2,
            SettlementStates = null,
            RoadStates = null,
            PendingLocalPressures = null,
            PendingRoadPressures = null,
            CharacterPlans = null,
        };

        var restored = GameSession.Restore(scenario, versionTwo);

        Assert.Equal(42, restored.StateOf("settlement.luoyang").Security);
        Assert.Equal(38, restored.StateOfRoad("road.luoyang.mengjin").Risk);
        Assert.Equal(scenario.Characters.Count, restored.CharacterPlans.Count);
        Assert.Equal(CareerGoalStatus.Active, restored.Career.Goal.Status);
        Assert.Equal(3, restored.HistoricalBranches.Count);
    }

    [Fact]
    public void VersionThreeSnapshotReceivesCareerAndBranchDefaults()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[1]);
        session.Rest(30);
        var versionThree = session.CreateSnapshot() with
        {
            SchemaVersion = 3,
            Career = null,
            HistoricalBranches = null,
        };

        var restored = GameSession.Restore(scenario, versionThree);

        Assert.Equal("background.clerk", restored.Career.BackgroundId);
        Assert.Equal(new(189, 10, 18), restored.Career.Goal.Deadline);
        Assert.Equal(HistoricalBranchStatus.Resolved, restored.HistoricalBranches[0].Status);
        Assert.Equal(HistoricalBranchStatus.Active, restored.HistoricalBranches[1].Status);
        Assert.Equal(HistoricalBranchStatus.Upcoming, restored.HistoricalBranches[2].Status);
    }

    [Fact]
    public void VersionFourSnapshotReceivesOrganizationWorldDefaults()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[1]);
        var versionFour = session.CreateSnapshot() with
        {
            SchemaVersion = 4,
            ResourceLedgers = null,
            Organizations = null,
            OrganizationCommissions = null,
        };

        var restored = GameSession.Restore(scenario, versionFour);

        Assert.Equal(scenario.Map.Settlements.Count, restored.ResourceLedgers.Count);
        Assert.True(restored.Organizations.Count >= scenario.Map.Settlements.Count);
        Assert.Empty(restored.OrganizationCommissions);
    }

    [Fact]
    public void VersionFiveSavePreservesAnAcceptedOrganizationCommission()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[1]);
        session.EnterUrbanLocation("luoyang.government");
        var offer = session.CareerOpportunities.Single(item =>
            item.Kind == CareerOpportunityKind.OrganizationCommission && item.IsAcceptance);
        session.ExecuteCareerOpportunity(offer.Id);
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-game-organization-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "save.json");

        try
        {
            var store = new JsonGameSaveStore();
            store.Save(path, session.CreateSnapshot());
            var restored = GameSession.Restore(scenario, store.Load(path));

            Assert.Equal(session.OrganizationCommissions, restored.OrganizationCommissions);
            Assert.Equal(session.ResourceLedgers, restored.ResourceLedgers);
            Assert.Contains(restored.CareerOpportunities, item =>
                item.Kind == CareerOpportunityKind.OrganizationCommission && !item.IsAcceptance);
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
    public void VersionFiveCharacterPlansReceiveMultiStepDefaults()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[0]);
        var legacyPlans = session.CharacterPlans.ToDictionary(
            item => item.Key,
            item => new CharacterPlanState(
                item.Value.CharacterId,
                item.Value.Goal,
                item.Value.KnownSettlementIds,
                item.Value.LastIntent),
            StringComparer.Ordinal);
        var versionFive = session.CreateSnapshot() with
        {
            SchemaVersion = 5,
            CharacterPlans = legacyPlans,
        };

        var restored = GameSession.Restore(scenario, versionFive);

        Assert.All(restored.CharacterPlans.Values, item => Assert.Equal(CharacterPlanStage.Assessing, item.Stage));
    }

    [Fact]
    public void VersionSixSnapshotReceivesDevelopmentAndRelationshipDefaults()
    {
        var scenario = DemoScenarioFactory.Create();
        var session = new GameSession(scenario, scenario.Backgrounds[0]);
        var versionSix = session.CreateSnapshot() with
        {
            SchemaVersion = 6,
            PlayerDevelopment = null,
            CharacterDevelopments = null,
            CharacterRelationships = null,
        };

        var restored = GameSession.Restore(scenario, versionSix);

        Assert.Equal(session.Player.Abilities, restored.PlayerDevelopment.Abilities);
        Assert.Equal(0, restored.PlayerDevelopment.TotalExperience);
        Assert.Equal(scenario.Characters.Count, restored.CharacterDevelopments.Count);
        Assert.Empty(restored.CharacterRelationships);
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

    private static void AssertPlansEqual(
        IReadOnlyDictionary<string, CharacterPlanState> expected,
        IReadOnlyDictionary<string, CharacterPlanState> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var characterId in expected.Keys)
        {
            Assert.Equal(expected[characterId].KnownSettlementIds, actual[characterId].KnownSettlementIds);
            Assert.Equal(
                expected[characterId] with { KnownSettlementIds = [] },
                actual[characterId] with { KnownSettlementIds = [] });
        }
    }

    private static void AssertDevelopmentsEqual(
        IReadOnlyDictionary<string, CharacterDevelopmentState> expected,
        IReadOnlyDictionary<string, CharacterDevelopmentState> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var characterId in expected.Keys)
        {
            AssertDevelopmentEqual(expected[characterId], actual[characterId]);
        }
    }

    private static void AssertDevelopmentEqual(
        CharacterDevelopmentState expected,
        CharacterDevelopmentState actual)
    {
        Assert.Equal(expected.RecentExperiences, actual.RecentExperiences);
        Assert.Equal(
            expected with { RecentExperiences = [] },
            actual with { RecentExperiences = [] });
    }
}
