using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public enum LogCategory
{
    Personal,
    Career,
    Travel,
    Encounter,
    World,
    Period,
    Commitment,
}

public sealed record GameLogEntry(GameDate Date, LogCategory Category, string Text);

public sealed record CharacterState(Character Profile, string SettlementId, string UrbanLocationId);

public sealed record PlayerState(
    string Name,
    PlayerBackground Background,
    string SettlementId,
    string UrbanLocationId,
    int Money,
    Abilities Abilities,
    IReadOnlyDictionary<string, RelationshipState> SocialConnections,
    IReadOnlyList<string> KnownTopicIds);

public sealed record CharacterLocationSnapshot(string CharacterId, string SettlementId, string UrbanLocationId);

public sealed record GameSnapshot(
    int SchemaVersion,
    string ScenarioId,
    GameDate Date,
    string BackgroundId,
    string PlayerName,
    string SettlementId,
    string UrbanLocationId,
    int Money,
    Abilities Abilities,
    IReadOnlyDictionary<string, int>? Relationships,
    IReadOnlyDictionary<string, RelationshipState>? SocialConnections,
    IReadOnlyList<string>? KnownTopicIds,
    IReadOnlyList<MeetingCommitment>? Commitments,
    IReadOnlyDictionary<string, SettlementState>? SettlementStates,
    IReadOnlyDictionary<string, RoadState>? RoadStates,
    IReadOnlyDictionary<string, LocalPressure>? PendingLocalPressures,
    IReadOnlyDictionary<string, int>? PendingRoadPressures,
    IReadOnlyDictionary<string, CharacterPlanState>? CharacterPlans,
    CareerState? Career,
    IReadOnlyDictionary<string, HistoricalBranchState>? HistoricalBranches,
    IReadOnlyList<CharacterLocationSnapshot> CharacterLocations,
    IReadOnlyList<GameLogEntry> Log,
    IReadOnlyDictionary<string, SettlementResourceLedger>? ResourceLedgers = null,
    IReadOnlyDictionary<string, OrganizationState>? Organizations = null,
    IReadOnlyList<OrganizationCommissionState>? OrganizationCommissions = null);

public sealed class GameSession
{
    private sealed record CharacterPlanCandidate(
        OrganizationState Organization,
        OrganizationNeedKind Need,
        int Score,
        int TravelDays);

    private readonly Dictionary<string, CharacterState> characters;
    private readonly Dictionary<string, RelationshipState> socialConnections = new(StringComparer.Ordinal);
    private readonly HashSet<string> knownTopicIds = new(StringComparer.Ordinal);
    private readonly List<MeetingCommitment> commitments = [];
    private readonly Dictionary<string, SettlementState> settlementStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoadState> roadStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LocalPressure> pendingLocalPressures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> pendingRoadPressures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CharacterPlanState> characterPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HistoricalBranchState> historicalBranches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SettlementResourceLedger> resourceLedgers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OrganizationState> organizations = new(StringComparer.Ordinal);
    private readonly List<OrganizationCommissionState> organizationCommissions = [];
    private readonly List<GameLogEntry> log = [];

    public GameSession(GameScenario scenario, PlayerBackground background, string playerName = "无名")
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(background);

        Scenario = scenario;
        Date = scenario.StartDate;
        characters = scenario.Characters.ToDictionary(
            item => item.Id,
            item => new CharacterState(item, item.SettlementId, item.UrbanLocationId),
            StringComparer.Ordinal);
        InitializeLocalWorldState();
        InitializeOrganizationWorldState();
        Career = CreateInitialCareerState(background);
        InitializeHistoricalBranches();

        foreach (var character in scenario.Characters)
        {
            var recognition = StartingRecognition(background, character);
            if (recognition != RecognitionLevel.Unknown)
            {
                socialConnections[character.Id] = RelationshipState.Unknown with { Recognition = recognition };
            }
        }

        foreach (var topicId in StartingTopicIds(background))
        {
            knownTopicIds.Add(topicId);
        }

        Player = CreatePlayerState(
            playerName,
            background,
            scenario.StartSettlementId,
            scenario.StartUrbanLocationId,
            background.StartingMoney,
            background.StartingAbilities);
        AddLog(LogCategory.World, $"你以“{background.Name}”的身份来到{CurrentSettlement.Name}。天下正在运转，而你尚未决定自己的道路。");
    }

    private GameSession(GameScenario scenario, GameSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < 1 or > 6)
        {
            throw new InvalidOperationException($"不支持的存档版本：{snapshot.SchemaVersion}");
        }

        if (snapshot.ScenarioId != scenario.Id)
        {
            throw new InvalidOperationException("存档属于另一个场景，不能载入当前世界。");
        }

        Scenario = scenario;
        Date = snapshot.Date;
        var background = scenario.Backgrounds.Single(item => item.Id == snapshot.BackgroundId);
        InitializeLocalWorldState();
        InitializeOrganizationWorldState();
        Career = CreateInitialCareerState(background);
        InitializeHistoricalBranches();

        if (snapshot.SchemaVersion == 1)
        {
            foreach (var pair in snapshot.Relationships ?? new Dictionary<string, int>())
            {
                socialConnections[pair.Key] = new RelationshipState(
                    pair.Value > 0 ? RecognitionLevel.Acquainted : RecognitionLevel.Unknown,
                    pair.Value,
                    0,
                    0);
            }

            foreach (var topicId in StartingTopicIds(background))
            {
                knownTopicIds.Add(topicId);
            }
        }
        else
        {
            foreach (var pair in snapshot.SocialConnections ?? new Dictionary<string, RelationshipState>())
            {
                socialConnections[pair.Key] = pair.Value;
            }

            foreach (var topicId in snapshot.KnownTopicIds ?? [])
            {
                knownTopicIds.Add(topicId);
            }

            commitments.AddRange(snapshot.Commitments ?? []);
        }

        if (snapshot.SchemaVersion >= 3)
        {
            ReplaceDictionary(settlementStates, snapshot.SettlementStates);
            ReplaceDictionary(roadStates, snapshot.RoadStates);
            ReplaceDictionary(pendingLocalPressures, snapshot.PendingLocalPressures);
            ReplaceDictionary(pendingRoadPressures, snapshot.PendingRoadPressures);
            ReplaceDictionary(characterPlans, snapshot.CharacterPlans);
        }

        if (snapshot.SchemaVersion >= 4)
        {
            Career = snapshot.Career ?? Career;
            ReplaceDictionary(historicalBranches, snapshot.HistoricalBranches);
        }

        if (snapshot.SchemaVersion >= 5)
        {
            ReplaceDictionary(resourceLedgers, snapshot.ResourceLedgers);
            ReplaceDictionary(organizations, snapshot.Organizations);
            organizationCommissions.AddRange(snapshot.OrganizationCommissions ?? []);
        }

        var savedLocations = snapshot.CharacterLocations.ToDictionary(item => item.CharacterId, StringComparer.Ordinal);
        characters = scenario.Characters.ToDictionary(
            item => item.Id,
            item => savedLocations.TryGetValue(item.Id, out var location)
                ? new CharacterState(item, location.SettlementId, location.UrbanLocationId)
                : new CharacterState(item, item.SettlementId, item.UrbanLocationId),
            StringComparer.Ordinal);
        Player = CreatePlayerState(
            snapshot.PlayerName,
            background,
            snapshot.SettlementId,
            snapshot.UrbanLocationId,
            snapshot.Money,
            snapshot.Abilities);
        log.AddRange(snapshot.Log);
        if (snapshot.SchemaVersion < 4)
        {
            AlignMigratedCareerWorldState();
        }
    }

    public GameScenario Scenario { get; }

    public GameDate Date { get; private set; }

    public PlayerState Player { get; private set; }

    public CareerState Career { get; private set; }

    public Settlement CurrentSettlement => Scenario.Map.GetSettlement(Player.SettlementId);

    public UrbanLocation CurrentUrbanLocation => CurrentSettlement.UrbanLocations.Single(item => item.Id == Player.UrbanLocationId);

    public IReadOnlyList<GameLogEntry> Log => log;

    public IReadOnlyList<MeetingCommitment> Commitments => commitments;

    public IReadOnlyDictionary<string, SettlementState> SettlementStates => settlementStates;

    public IReadOnlyDictionary<string, RoadState> RoadStates => roadStates;

    public IReadOnlyDictionary<string, CharacterPlanState> CharacterPlans => characterPlans;

    public IReadOnlyDictionary<string, SettlementResourceLedger> ResourceLedgers => resourceLedgers;

    public IReadOnlyDictionary<string, OrganizationState> Organizations => organizations;

    public IReadOnlyList<OrganizationCommissionState> OrganizationCommissions => organizationCommissions;

    public SettlementResourceLedger CurrentResourceLedger => resourceLedgers[Player.SettlementId];

    public IReadOnlyList<OrganizationState> OrganizationsAtCurrentLocation => organizations.Values
        .Where(item => item.SettlementId == Player.SettlementId && item.UrbanLocationId == Player.UrbanLocationId)
        .OrderBy(item => item.Name, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<HistoricalBranchState> HistoricalBranches => historicalBranches.Values
        .OrderBy(item => item.OpensOn.Year)
        .ThenBy(item => item.OpensOn.Month)
        .ThenBy(item => item.OpensOn.Day)
        .ToArray();

    public IReadOnlyList<CareerOpportunity> CareerOpportunities => BuildCareerOpportunities();

    public SettlementState CurrentSettlementState => settlementStates[Player.SettlementId];

    public IReadOnlyList<ConversationTopic> KnownTopics => Scenario.Topics
        .Where(item => knownTopicIds.Contains(item.Id))
        .ToArray();

    public IReadOnlyList<CharacterState> CharactersAtCurrentLocation => characters.Values
        .Where(item =>
            item.SettlementId == Player.SettlementId &&
            item.UrbanLocationId == Player.UrbanLocationId &&
            characterPlans[item.Profile.Id].Stage != CharacterPlanStage.Traveling)
        .OrderBy(item => item.Profile.Name, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<(Settlement Destination, Road Road)> AvailableDestinations =>
        Scenario.Map.GetDestinations(Player.SettlementId);

    public int RelationshipWith(string characterId) => ConnectionWith(characterId).Favor;

    public RelationshipState ConnectionWith(string characterId) =>
        socialConnections.GetValueOrDefault(characterId, RelationshipState.Unknown);

    public SettlementState StateOf(string settlementId) => settlementStates[settlementId];

    public RoadState StateOfRoad(string roadId) => roadStates[roadId];

    public static GameSession Restore(GameScenario scenario, GameSnapshot snapshot) => new(scenario, snapshot);

    public GameSnapshot CreateSnapshot() => new(
        6,
        Scenario.Id,
        Date,
        Player.Background.Id,
        Player.Name,
        Player.SettlementId,
        Player.UrbanLocationId,
        Player.Money,
        Player.Abilities,
        socialConnections.ToDictionary(item => item.Key, item => item.Value.Favor, StringComparer.Ordinal),
        new Dictionary<string, RelationshipState>(socialConnections, StringComparer.Ordinal),
        knownTopicIds.Order(StringComparer.Ordinal).ToArray(),
        commitments.ToArray(),
        new Dictionary<string, SettlementState>(settlementStates, StringComparer.Ordinal),
        new Dictionary<string, RoadState>(roadStates, StringComparer.Ordinal),
        new Dictionary<string, LocalPressure>(pendingLocalPressures, StringComparer.Ordinal),
        new Dictionary<string, int>(pendingRoadPressures, StringComparer.Ordinal),
        new Dictionary<string, CharacterPlanState>(characterPlans, StringComparer.Ordinal),
        Career,
        new Dictionary<string, HistoricalBranchState>(historicalBranches, StringComparer.Ordinal),
        characters.Values
            .OrderBy(item => item.Profile.Id, StringComparer.Ordinal)
            .Select(item => new CharacterLocationSnapshot(item.Profile.Id, item.SettlementId, item.UrbanLocationId))
            .ToArray(),
        log.ToArray(),
        new Dictionary<string, SettlementResourceLedger>(resourceLedgers, StringComparer.Ordinal),
        new Dictionary<string, OrganizationState>(organizations, StringComparer.Ordinal),
        organizationCommissions.ToArray());

    public void TravelTo(string destinationId)
    {
        var route = AvailableDestinations.SingleOrDefault(item => item.Destination.Id == destinationId);
        if (route.Destination is null || route.Road is null)
        {
            throw new InvalidOperationException("此处没有通往该地点的直接道路。");
        }

        AddRoadPressure(route.Road.Id, 1);
        AddLocalPressure(Player.SettlementId, 0, 0, 1, 0, $"玩家沿官道前往{route.Destination.Name}，带来行旅需求");
        AddLog(LogCategory.Travel, $"你离开{CurrentSettlement.Name}，沿{route.Road.Description}前往{route.Destination.Name}。预计用时{route.Road.TravelDays}日。");
        AdvanceDays(route.Road.TravelDays);
        Player = Player with
        {
            SettlementId = route.Destination.Id,
            UrbanLocationId = route.Destination.UrbanLocations[0].Id,
        };
        AddLog(LogCategory.Travel, $"你抵达{route.Destination.Name}，暂在{CurrentUrbanLocation.Name}落脚。");
    }

    public void EnterUrbanLocation(string locationId)
    {
        var location = CurrentSettlement.UrbanLocations.SingleOrDefault(item => item.Id == locationId)
            ?? throw new InvalidOperationException("当前城邑没有这个地点。");
        Player = Player with { UrbanLocationId = location.Id };
        AddLog(LogCategory.Personal, $"你来到{location.Name}。{location.Description}");
    }

    public IReadOnlyList<AvailableAction> GetActionsForCharacter(string characterId)
    {
        var character = CharactersAtCurrentLocation.SingleOrDefault(item => item.Profile.Id == characterId);
        if (character is null)
        {
            return [];
        }

        var connection = ConnectionWith(characterId);
        var activeMeeting = commitments
            .Where(item => item.CharacterId == characterId && item.Status == CommitmentStatus.Scheduled)
            .OrderBy(item => item.DueDate.Year)
            .ThenBy(item => item.DueDate.Month)
            .ThenBy(item => item.DueDate.Day)
            .FirstOrDefault();

        if (activeMeeting is not null)
        {
            var atMeetingPlace = activeMeeting.SettlementId == Player.SettlementId &&
                activeMeeting.UrbanLocationId == Player.UrbanLocationId;
            var isDue = activeMeeting.DueDate == Date;
            var place = Scenario.Map.GetSettlement(activeMeeting.SettlementId);
            var location = place.UrbanLocations.Single(item => item.Id == activeMeeting.UrbanLocationId);
            var reason = isDue
                ? atMeetingPlace ? null : $"约见地点是{place.Name}·{location.Name}。"
                : $"约定日期为{activeMeeting.DueDate}。";
            return
            [
                new AvailableAction(
                    $"interaction:attend:{activeMeeting.Id}",
                    InteractionActionKind.AttendMeeting,
                    characterId,
                    "依约会面",
                    $"履行与{character.Profile.Name}的约见。",
                    1,
                    isDue && atMeetingPlace,
                    reason,
                    CommitmentId: activeMeeting.Id),
            ];
        }

        var (hasAccess, accessReason) = CanApproach(character, connection);
        var available = IsAvailableForMeeting(character);
        var blockReason = !hasAccess ? accessReason : !available ? "此人今日另有日程，不能长谈。" : null;
        var actions = new List<AvailableAction>();

        if (connection.Recognition < RecognitionLevel.Met)
        {
            actions.Add(new AvailableAction(
                $"interaction:introduce:{characterId}",
                InteractionActionKind.Introduce,
                characterId,
                connection.Recognition == RecognitionLevel.HeardOf ? "请求引见" : "上前结识",
                "说明自己的身份与来意，尝试建立第一次正式接触。",
                1,
                blockReason is null,
                blockReason));
        }
        else
        {
            actions.Add(new AvailableAction(
                $"interaction:visit:{characterId}",
                InteractionActionKind.Visit,
                characterId,
                "拜访交谈",
                "花一日与对方交谈，关系结果取决于既有认识。",
                1,
                blockReason is null,
                blockReason));

            foreach (var topic in KnownTopics)
            {
                actions.Add(new AvailableAction(
                    $"interaction:topic:{characterId}:{topic.Id}",
                    InteractionActionKind.DiscussTopic,
                    characterId,
                    $"谈论：{topic.Title}",
                    topic.Summary,
                    1,
                    blockReason is null,
                    blockReason,
                    TopicId: topic.Id));
            }
        }

        if (blockReason is not null)
        {
            actions.Add(new AvailableAction(
                $"interaction:schedule:{characterId}",
                InteractionActionKind.ScheduleMeeting,
                characterId,
                "请求另约日期",
                "托门人通报，约定两日后在此会面。请求本身耗时一日。",
                1,
                true));
        }

        AddPlanInterventions(character, connection, blockReason, actions);

        return actions;
    }

    public void ExecuteInteraction(string actionId)
    {
        var action = CharactersAtCurrentLocation
            .SelectMany(item => GetActionsForCharacter(item.Profile.Id))
            .SingleOrDefault(item => item.Id == actionId)
            ?? throw new InvalidOperationException("这个行动已经不再适用，请重新观察当前情况。");
        if (!action.IsEnabled)
        {
            throw new InvalidOperationException(action.BlockReason ?? "当前不能执行这个行动。");
        }

        var character = characters[action.CharacterId];
        switch (action.Kind)
        {
            case InteractionActionKind.Introduce:
                UpdateConnection(action.CharacterId, state => state.Improve(1, 0, RecognitionLevel.Met));
                AdvanceDays(1);
                GainCareer(0, 0, 1);
                AddLog(LogCategory.Encounter, $"你向{character.Profile.Name}说明身份与来意。对方记住了你（相识，好感 +1）。");
                break;
            case InteractionActionKind.Visit:
                UpdateConnection(action.CharacterId, state => state.Improve(3, 1, RecognitionLevel.Acquainted));
                AdvanceDays(1);
                GainCareer(0, 1, 1);
                AddLog(LogCategory.Encounter, $"你拜访了{character.Profile.Name}。交谈让彼此更为熟悉（好感 +3，信任 +1）。");
                break;
            case InteractionActionKind.ScheduleMeeting:
                ScheduleMeeting(character);
                break;
            case InteractionActionKind.AttendMeeting:
                AttendMeeting(character, action.CommitmentId!);
                break;
            case InteractionActionKind.DiscussTopic:
                DiscussTopic(character, action.TopicId!);
                break;
            case InteractionActionKind.SupportPlan:
                SupportCharacterPlan(character);
                break;
            case InteractionActionKind.DissuadePlan:
                DissuadeCharacterPlan(character);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(actionId));
        }
    }

    public void GatherInformation()
    {
        var unknownTopic = Scenario.Topics.FirstOrDefault(item => !knownTopicIds.Contains(item.Id));
        AddLocalPressure(Player.SettlementId, 0, 0, 1, 0, "玩家在市井搜集并转述消息");
        AdvanceDays(1);
        if (unknownTopic is not null)
        {
            knownTopicIds.Add(unknownTopic.Id);
            RefreshPlayerSocialState();
            AddLog(LogCategory.Encounter, $"你花了一日打听消息，开始了解“{unknownTopic.Title}”：{unknownTopic.Summary}");
            return;
        }

        var nearby = AvailableDestinations.FirstOrDefault();
        var clue = nearby.Destination is null
            ? "近日道路上的行旅稀少，没有可靠的新消息。"
            : $"旅人谈起{nearby.Destination.Name}：{nearby.Destination.Description}";
        AddLog(LogCategory.Encounter, $"你花了一日打听消息。{clue}");
    }

    public void Train()
    {
        AdvanceDays(3);
        Player = Player with { Abilities = Player.Abilities.ImproveLearning(1) };
        GainCareer(Player.Background.Id == "background.scholar" ? 1 : 0, 1, 0);
        AddLog(LogCategory.Personal, "你闭门读书三日，学识有所精进（学识 +1）。");
    }

    public void Work()
    {
        var contribution = Player.Background.Id switch
        {
            "background.clerk" => (Security: 1, Grain: 0, Prosperity: 0, Control: 2, Source: "玩家参与官府文书与地方差事"),
            "background.ranger" => (Security: 2, Grain: 0, Prosperity: 0, Control: -1, Source: "玩家以游侠人脉协助维持市井秩序"),
            _ => (Security: 0, Grain: -1, Prosperity: 2, Control: 0, Source: "玩家以学识协助商旅与地方士人"),
        };
        AddLocalPressure(
            Player.SettlementId,
            contribution.Security,
            contribution.Grain,
            contribution.Prosperity,
            contribution.Control,
            contribution.Source);
        AdvanceDays(3);
        var income = Player.Background.Id switch
        {
            "background.clerk" => 160,
            "background.ranger" => 110,
            _ => 80,
        };
        Player = Player with { Money = Player.Money + income };
        var careerProgress = Player.Background.Id == "background.clerk" ? 2 : 1;
        GainCareer(careerProgress, 1, 1);
        AddLog(LogCategory.Personal, $"你用三日谋生，挣得{income}钱。身份和能力决定了你能接触到什么差事。");
    }

    public void Rest(int days = 1)
    {
        AdvanceDays(days);
        AddLog(LogCategory.Personal, $"你休整了{days}日，也留心观察周围的变化。");
    }

    public void ExecuteCareerOpportunity(string opportunityId)
    {
        var opportunity = CareerOpportunities.SingleOrDefault(item => item.Id == opportunityId)
            ?? throw new InvalidOperationException("这个机会已经不再适用，请重新观察当前局势。");
        if (!opportunity.IsEnabled)
        {
            throw new InvalidOperationException(opportunity.BlockReason ?? "你目前无法抓住这个机会。");
        }

        if (opportunity.Kind == CareerOpportunityKind.OrganizationCommission)
        {
            if (opportunity.IsAcceptance)
            {
                AcceptOrganizationCommission(opportunity);
            }
            else
            {
                FulfillOrganizationCommission(opportunity);
            }

            return;
        }

        if (opportunity.Kind == CareerOpportunityKind.BranchIntervention)
        {
            InterveneInBranch(opportunity);
            return;
        }

        ExecuteCareerPath(opportunity);
    }

    private PlayerState CreatePlayerState(
        string name,
        PlayerBackground background,
        string settlementId,
        string urbanLocationId,
        int money,
        Abilities abilities) => new(
            name,
            background,
            settlementId,
            urbanLocationId,
            money,
            abilities,
            new Dictionary<string, RelationshipState>(socialConnections, StringComparer.Ordinal),
            knownTopicIds.Order(StringComparer.Ordinal).ToArray());

    private void ScheduleMeeting(CharacterState character)
    {
        var dueDate = Date.AddDays(2);
        var commitment = new MeetingCommitment(
            $"meeting.{commitments.Count + 1:D4}",
            character.Profile.Id,
            Player.SettlementId,
            Player.UrbanLocationId,
            dueDate,
            CommitmentStatus.Scheduled);
        commitments.Add(commitment);
        AdvanceDays(1);
        AddLog(LogCategory.Commitment, $"门人收下了你的请求。你与{character.Profile.Name}约定于{dueDate}在{CurrentSettlement.Name}·{CurrentUrbanLocation.Name}会面。");
    }

    private void AttendMeeting(CharacterState character, string commitmentId)
    {
        var index = commitments.FindIndex(item => item.Id == commitmentId && item.Status == CommitmentStatus.Scheduled);
        if (index < 0)
        {
            throw new InvalidOperationException("这项约见已经失效。");
        }

        commitments[index] = commitments[index] with { Status = CommitmentStatus.Fulfilled };
        UpdateConnection(character.Profile.Id, state => state.Improve(3, 2, RecognitionLevel.Acquainted));
        AdvanceDays(1);
        GainCareer(0, 1, 2);
        AddLog(LogCategory.Commitment, $"你依约与{character.Profile.Name}会面，双方都记住了这次守约（好感 +3，信任 +2）。");
    }

    private void DiscussTopic(CharacterState character, string topicId)
    {
        var topic = KnownTopics.Single(item => item.Id == topicId);
        UpdateConnection(character.Profile.Id, state => state.Improve(1, 1, RecognitionLevel.Acquainted));
        AdvanceDays(1);
        GainCareer(0, 1, 1);
        AddLog(LogCategory.Encounter, $"你与{character.Profile.Name}谈论“{topic.Title}”。对方愿意认真回应，彼此的判断多了一处交集（好感 +1，信任 +1）。");
    }

    private void AddPlanInterventions(
        CharacterState character,
        RelationshipState connection,
        string? meetingBlockReason,
        List<AvailableAction> actions)
    {
        var plan = characterPlans[character.Profile.Id];
        if (connection.Recognition < RecognitionLevel.Met ||
            plan.Stage is not (CharacterPlanStage.Preparing or CharacterPlanStage.Executing) ||
            plan.TargetOrganizationId is null)
        {
            return;
        }

        var organization = organizations[plan.TargetOrganizationId];
        var supportBlock = meetingBlockReason ?? (plan.PlayerSupport > 0 ? "你已经为这项计划出过力。" : null);
        actions.Add(new AvailableAction(
            $"interaction:support-plan:{character.Profile.Id}",
            InteractionActionKind.SupportPlan,
            character.Profile.Id,
            "协助其正在推进的计划",
            $"{character.Profile.Name}正在为{organization.Name}推进“{PlanNeedName(plan.Need!.Value)}”。投入一日协助会提高成功把握，并留下关系结果。",
            1,
            supportBlock is null,
            supportBlock));

        var canPersuade = connection.Recognition >= RecognitionLevel.Acquainted &&
            Player.Abilities.Diplomacy + connection.Trust >= 60;
        var dissuadeBlock = meetingBlockReason ?? (canPersuade
            ? null
            : "需要至少相识，并以交涉能力与既有信任取得足够分量。 ");
        actions.Add(new AvailableAction(
            $"interaction:dissuade-plan:{character.Profile.Id}",
            InteractionActionKind.DissuadePlan,
            character.Profile.Id,
            "劝其暂缓当前计划",
            $"说明局势已经变化，尝试让{character.Profile.Name}取消对{organization.Name}的这项承诺。成功会改变组织后续行动，也可能损伤彼此好感。",
            1,
            dissuadeBlock is null,
            dissuadeBlock));
    }

    private void SupportCharacterPlan(CharacterState character)
    {
        var plan = characterPlans[character.Profile.Id];
        if (plan.Stage is not (CharacterPlanStage.Preparing or CharacterPlanStage.Executing) || plan.PlayerSupport > 0)
        {
            throw new InvalidOperationException("这项人物计划现在不能再接受协助。");
        }

        characterPlans[character.Profile.Id] = plan with
        {
            PlayerSupport = 15,
            LastIntent = $"获得玩家协助，继续推进{PlanNeedName(plan.Need!.Value)}",
        };
        UpdateConnection(character.Profile.Id, state => state.Improve(2, 1, RecognitionLevel.Acquainted));
        GainCareer(1, 1, 1);
        AdvanceDays(1);
        AddLog(LogCategory.Encounter, $"你用一日协助{character.Profile.Name}推进计划。对方会把你的投入计入最后结果（好感 +2，信任 +1）。");
    }

    private void DissuadeCharacterPlan(CharacterState character)
    {
        var plan = characterPlans[character.Profile.Id];
        if (plan.Stage is not (CharacterPlanStage.Preparing or CharacterPlanStage.Executing) ||
            ConnectionWith(character.Profile.Id).Recognition < RecognitionLevel.Acquainted ||
            Player.Abilities.Diplomacy + ConnectionWith(character.Profile.Id).Trust < 60)
        {
            throw new InvalidOperationException("你现在没有足够关系和分量让对方改变计划。");
        }

        ReleaseReservedPlanResources(plan, 1);
        characterPlans[character.Profile.Id] = plan with
        {
            Stage = CharacterPlanStage.Cancelled,
            NextStepOn = Date.AddDays(10),
            LastIntent = "接受玩家劝说，暂缓原有计划",
            Result = "玩家当面说明局势变化后，人物取消了承诺。",
        };
        UpdateConnection(character.Profile.Id, state => state.Improve(-1, 1, RecognitionLevel.Acquainted));
        AdvanceDays(1);
        AddLog(LogCategory.Commitment, $"你说服{character.Profile.Name}暂缓原有计划。组织收回了部分尚未耗用的资源；对方认可你的判断，但也记住了这次变更（好感 -1，信任 +1）。");
    }

    private void UpdateConnection(string characterId, Func<RelationshipState, RelationshipState> update)
    {
        socialConnections[characterId] = update(ConnectionWith(characterId));
        RefreshPlayerSocialState();
    }

    private void RefreshPlayerSocialState()
    {
        Player = Player with
        {
            SocialConnections = new Dictionary<string, RelationshipState>(socialConnections, StringComparer.Ordinal),
            KnownTopicIds = knownTopicIds.Order(StringComparer.Ordinal).ToArray(),
        };
    }

    private (bool HasAccess, string? Reason) CanApproach(CharacterState character, RelationshipState connection)
    {
        var allowed = CurrentUrbanLocation.Type switch
        {
            UrbanLocationType.GovernmentOffice => Player.Background.Id == "background.clerk" || connection.Recognition >= RecognitionLevel.Acquainted,
            UrbanLocationType.Residence => connection.Recognition >= RecognitionLevel.Acquainted,
            UrbanLocationType.Barracks => Player.Background.Id == "background.ranger" || connection.Recognition >= RecognitionLevel.Acquainted,
            UrbanLocationType.School => Player.Background.Id == "background.scholar" || connection.Recognition >= RecognitionLevel.HeardOf,
            _ => true,
        };
        if (allowed)
        {
            return (true, null);
        }

        var reason = CurrentUrbanLocation.Type switch
        {
            UrbanLocationType.GovernmentOffice => "官署不接待无职名、无引荐的私访者。",
            UrbanLocationType.Residence => "门人不会让陌生人直接进入私宅。",
            UrbanLocationType.Barracks => "军营不允许身份不明者接近将领。",
            UrbanLocationType.School => "你缺少师承或名望，暂时无人替你引见。",
            _ => $"你现在无法接近{character.Profile.Name}。",
        };
        return (false, reason);
    }

    private bool IsAvailableForMeeting(CharacterState character)
    {
        if (commitments.Any(item =>
                item.CharacterId == character.Profile.Id &&
                item.Status == CommitmentStatus.Scheduled &&
                item.DueDate == Date))
        {
            return true;
        }

        var roles = character.Profile.Roles;
        if (roles.HasFlag(CharacterRole.Official) && Date.Day % 3 == 0)
        {
            return false;
        }

        if (roles.HasFlag(CharacterRole.Scholar) && Date.Day % 4 == 2)
        {
            return false;
        }

        return !roles.HasFlag(CharacterRole.General) || Date.Day % 5 != 0;
    }

    private void AdvanceDays(int days)
    {
        for (var i = 0; i < days; i++)
        {
            var previous = Date;
            Date = Date.AddDays(1);
            UpdateCommitmentsForDate();
            UpdateOrganizationCommissionsForDate();
            UpdateCareerForDate();
            UpdateHistoricalBranchesForDate();
            RunDailyWorldStep();

            if (Date.Period != previous.Period || Date.Month != previous.Month)
            {
                ResolveTenDayPeriod();
            }
        }
    }

    private IReadOnlyList<CareerOpportunity> BuildCareerOpportunities()
    {
        var opportunities = new List<CareerOpportunity>
        {
            BuildBackgroundCareerOpportunity(),
        };
        opportunities.AddRange(BuildOrganizationOpportunities());
        opportunities.AddRange(historicalBranches.Values
            .Where(item => item.Status == HistoricalBranchStatus.Active && item.Outcome == HistoricalBranchOutcome.Undecided)
            .OrderBy(item => item.ResolvesOn.Year)
            .ThenBy(item => item.ResolvesOn.Month)
            .ThenBy(item => item.ResolvesOn.Day)
            .Select(BuildBranchOpportunity));
        return opportunities;
    }

    private IReadOnlyList<CareerOpportunity> BuildOrganizationOpportunities()
    {
        var active = organizationCommissions.SingleOrDefault(item => item.Status == OrganizationCommissionStatus.Accepted);
        if (active is not null)
        {
            var organization = organizations[active.OrganizationId];
            var atRequiredPlace = Player.SettlementId == active.SettlementId &&
                Player.UrbanLocationId == active.UrbanLocationId;
            var canFinishBeforeDeadline = Date.AddDays(active.DurationDays).DaysUntil(active.DueDate) >= 0;
            var settlement = Scenario.Map.GetSettlement(active.SettlementId);
            var location = settlement.UrbanLocations.Single(item => item.Id == active.UrbanLocationId);
            var blockReason = !atRequiredPlace
                ? $"需要前往{settlement.Name}·{location.Name}向{organization.Name}交付。"
                : !canFinishBeforeDeadline
                    ? $"期限是{active.DueDate}，已经来不及完成。"
                    : null;
            return
            [
                new CareerOpportunity(
                    $"commission:fulfill:{active.Id}",
                    CareerOpportunityKind.OrganizationCommission,
                    $"履行委托：{active.Title}",
                    $"{active.Description} 委托方：{organization.Name}；期限：{active.DueDate}；完成后得{active.RewardMoney}钱，并真实改变当地账本。",
                    active.DurationDays,
                    0,
                    blockReason is null,
                    blockReason,
                    OrganizationId: active.OrganizationId,
                    OrganizationNeed: active.Need,
                    CommissionId: active.Id,
                    RewardMoney: active.RewardMoney),
            ];
        }

        return OrganizationsAtCurrentLocation
            .Where(organization => !organizationCommissions.Any(commission =>
                commission.OrganizationId == organization.Id &&
                commission.AcceptedOn.DaysUntil(Date) < 10))
            .Select(BuildOrganizationOffer)
            .ToArray();
    }

    private CareerOpportunity BuildOrganizationOffer(OrganizationState organization)
    {
        var need = DetermineOrganizationNeed(organization);
        var definition = CommissionDefinition(need);
        return new CareerOpportunity(
            $"commission:accept:{organization.Id}:{need}",
            CareerOpportunityKind.OrganizationCommission,
            $"接受委托：{definition.Title}",
            $"{organization.Name}因{NeedReason(organization, need)}发出委托。{definition.Description} 接下后须在八日内回来交付。",
            0,
            0,
            true,
            OrganizationId: organization.Id,
            OrganizationNeed: need,
            IsAcceptance: true,
            RewardMoney: definition.Reward);
    }

    private (string Title, string Description, int Duration, int Reward) CommissionDefinition(OrganizationNeedKind need) =>
        (need, Player.Background.Id) switch
        {
            (OrganizationNeedKind.Grain, "background.clerk") => ("核清仓廪出入", "核对仓册、查明积压，使可用粮秣重新进入调拨。", 3, 150),
            (OrganizationNeedKind.Grain, "background.ranger") => ("护送一批粮车", "沿途联络乡里并护住粮车，把短缺的粮秣送入城中。", 3, 170),
            (OrganizationNeedKind.Grain, _) => ("联络赈粮", "拜访士人与富户，协调一批能够立即动用的粮秣。", 3, 130),
            (OrganizationNeedKind.Treasury, "background.clerk") => ("厘清积欠钱粮", "整理积欠与重复摊派，为组织追回能够动用的钱财。", 3, 160),
            (OrganizationNeedKind.Treasury, "background.ranger") => ("追回商路欠款", "凭地方人脉找到拖欠者，促成一笔旧账结清。", 3, 170),
            (OrganizationNeedKind.Treasury, _) => ("劝募地方助资", "以声望和书信联络地方人士，为眼前事务筹措经费。", 3, 140),
            (OrganizationNeedKind.Labor, "background.clerk") => ("重编差役名册", "剔除重名与逃户，让有限人力能被实际调度。", 3, 150),
            (OrganizationNeedKind.Labor, "background.ranger") => ("召集乡里帮手", "依靠乡里信用召集一批愿意承担急务的人手。", 3, 160),
            (OrganizationNeedKind.Labor, _) => ("协调乡里出力", "解释事务利害，减少推诿并组织可用人手。", 3, 140),
            (OrganizationNeedKind.RoadSecurity, "background.clerk") => ("查验沿途关津", "核对关津文书与巡检安排，压低沿路勒索和疏漏。", 3, 160),
            (OrganizationNeedKind.RoadSecurity, "background.ranger") => ("巡护邻近道路", "带熟悉地形的同伴巡护道路，震慑劫掠者。", 3, 180),
            _ => ("联络沿途坞壁", "以书信和地方关系协调沿路互保，减少行旅阻滞。", 3, 145),
        };

    private OrganizationNeedKind DetermineOrganizationNeed(OrganizationState organization)
    {
        var ledger = resourceLedgers[organization.SettlementId];
        var roadRisk = Scenario.Map.Roads
            .Where(item => item.Connects(organization.SettlementId))
            .Select(item => roadStates[item.Id].Risk)
            .DefaultIfEmpty(0)
            .Max();
        return organization.Kind switch
        {
            OrganizationKind.MerchantGuild when roadRisk >= 35 => OrganizationNeedKind.RoadSecurity,
            OrganizationKind.LocalMilitia => OrganizationNeedKind.RoadSecurity,
            OrganizationKind.ScholarNetwork when ledger.Labor < ledger.Grain => OrganizationNeedKind.Labor,
            OrganizationKind.ScholarNetwork => OrganizationNeedKind.Grain,
            _ when organization.Treasury <= organization.Grain && organization.Treasury <= organization.Personnel => OrganizationNeedKind.Treasury,
            _ when organization.Grain <= organization.Personnel => OrganizationNeedKind.Grain,
            _ => OrganizationNeedKind.Labor,
        };
    }

    private string NeedReason(OrganizationState organization, OrganizationNeedKind need)
    {
        var ledger = resourceLedgers[organization.SettlementId];
        return need switch
        {
            OrganizationNeedKind.Grain => $"当地粮储仅为 {ledger.Grain}/100",
            OrganizationNeedKind.Treasury => $"组织可用钱财仅为 {organization.Treasury}/100",
            OrganizationNeedKind.Labor => $"当地可用人力仅为 {ledger.Labor}/100",
            _ => $"邻近道路最高风险已达 {HighestAdjacentRoadRisk(organization.SettlementId)}/100",
        };
    }

    private int HighestAdjacentRoadRisk(string settlementId) => Scenario.Map.Roads
        .Where(item => item.Connects(settlementId))
        .Select(item => roadStates[item.Id].Risk)
        .DefaultIfEmpty(0)
        .Max();

    private void AcceptOrganizationCommission(CareerOpportunity opportunity)
    {
        if (organizationCommissions.Any(item => item.Status == OrganizationCommissionStatus.Accepted))
        {
            throw new InvalidOperationException("你已有一项组织委托尚未交付，不能同时作出另一项承诺。");
        }

        var organization = organizations[opportunity.OrganizationId!];
        var definition = CommissionDefinition(opportunity.OrganizationNeed!.Value);
        var commission = new OrganizationCommissionState(
            $"commission.{organizationCommissions.Count + 1:D4}",
            organization.Id,
            opportunity.OrganizationNeed.Value,
            definition.Title,
            definition.Description,
            organization.SettlementId,
            organization.UrbanLocationId,
            Date,
            Date.AddDays(8),
            definition.Duration,
            definition.Reward,
            OrganizationCommissionStatus.Accepted,
            string.Empty);
        organizationCommissions.Add(commission);
        AddLog(LogCategory.Commitment, $"你接下{organization.Name}的“{commission.Title}”，承诺在{commission.DueDate}前回到此处完成。委托来自真实资源缺口，不会永远停在原地等你。 ");
    }

    private void FulfillOrganizationCommission(CareerOpportunity opportunity)
    {
        var index = organizationCommissions.FindIndex(item =>
            item.Id == opportunity.CommissionId && item.Status == OrganizationCommissionStatus.Accepted);
        if (index < 0)
        {
            throw new InvalidOperationException("这项组织委托已经失效。");
        }

        var commission = organizationCommissions[index];
        var organization = organizations[commission.OrganizationId];
        AdvanceDays(commission.DurationDays);
        ApplyCommissionImpact(commission, succeeded: true);
        Player = Player with { Money = Player.Money + commission.RewardMoney };
        GainCareer(2, 2, 1);
        organizationCommissions[index] = commission with
        {
            Status = OrganizationCommissionStatus.Completed,
            Result = $"你完成了委托，{organization.Name}的缺口得到缓解。",
        };
        AddLog(LogCategory.Career, $"你向{organization.Name}交付“{commission.Title}”，获得{commission.RewardMoney}钱；当地资源账本和组织资产已经同步改变。阶段目标推进 2，声望 +2，人脉 +1。 ");
    }

    private void ApplyCommissionImpact(OrganizationCommissionState commission, bool succeeded)
    {
        var direction = succeeded ? 1 : -1;
        var ledger = resourceLedgers[commission.SettlementId];
        var organization = organizations[commission.OrganizationId];
        var cause = succeeded ? $"玩家完成{organization.Name}的“{commission.Title}”" : $"“{commission.Title}”逾期无人完成";
        resourceLedgers[commission.SettlementId] = commission.Need switch
        {
            OrganizationNeedKind.Grain => ledger.Apply(0, 12 * direction, 0, -2 * direction, cause),
            OrganizationNeedKind.Treasury => ledger.Apply(0, 0, 10 * direction, -1 * direction, cause),
            OrganizationNeedKind.Labor => ledger.Apply(0, 0, 0, 12 * direction, cause),
            _ => ledger.Apply(0, 2 * direction, 0, -3 * direction, cause),
        };
        organizations[commission.OrganizationId] = commission.Need switch
        {
            OrganizationNeedKind.Grain => organization.Apply(-2 * direction, 10 * direction, 1 * direction, 1 * direction),
            OrganizationNeedKind.Treasury => organization.Apply(10 * direction, 0, 1 * direction, 1 * direction),
            OrganizationNeedKind.Labor => organization.Apply(-2 * direction, 0, 10 * direction, 1 * direction),
            _ => organization.Apply(-3 * direction, -1 * direction, 3 * direction, 2 * direction),
        };

        if (commission.Need == OrganizationNeedKind.RoadSecurity)
        {
            foreach (var road in Scenario.Map.Roads.Where(item => item.Connects(commission.SettlementId)))
            {
                roadStates[road.Id] = roadStates[road.Id].Apply(succeeded ? -6 : 4);
            }
        }

        AddLocalPressure(
            commission.SettlementId,
            succeeded ? 2 : -2,
            commission.Need == OrganizationNeedKind.Grain ? (succeeded ? -2 : 2) : 0,
            succeeded ? 1 : -1,
            succeeded ? 1 : -1,
            cause);
    }

    private CareerOpportunity BuildBackgroundCareerOpportunity()
    {
        var definition = Player.Background.Id switch
        {
            "background.clerk" => (
                Title: "承办郡府急务",
                Description: "在官署清理积压文书并协调差役。可积累官场声望与人脉，但会加深对俸钱的依赖。",
                Duration: 4,
                Cost: 20,
                LocationType: UrbanLocationType.GovernmentOffice,
                Place: "官署"),
            "background.ranger" => (
                Title: "护送乡里商旅",
                Description: "借地方人脉和武艺护送行旅。可积累江湖声望与人脉，但需要先备办器械和盘缠。",
                Duration: 4,
                Cost: 50,
                LocationType: UrbanLocationType.Market,
                Place: "市集"),
            _ => (
                Title: "参与士人清议",
                Description: "在学舍讲论经义与时事。可积累士林声望与人脉，但需要承担书资和交游费用。",
                Duration: 4,
                Cost: 40,
                LocationType: UrbanLocationType.School,
                Place: "学舍"),
        };
        var blockReason = Career.Goal.Status != CareerGoalStatus.Active
            ? "当前阶段目标已经结算。"
            : Date.AddDays(definition.Duration).DaysUntil(Career.Goal.Deadline) <= 0
                ? "距离阶段目标期限太近，已经来不及完成这项行动。"
            : CurrentUrbanLocation.Type != definition.LocationType
                ? $"需要先前往{definition.Place}。"
                : Player.Money < definition.Cost
                    ? $"至少需要{definition.Cost}钱准备此事。"
                    : null;
        return new CareerOpportunity(
            $"career:{Player.Background.Id}",
            CareerOpportunityKind.CareerPath,
            definition.Title,
            definition.Description,
            definition.Duration,
            definition.Cost,
            blockReason is null,
            blockReason);
    }

    private CareerOpportunity BuildBranchOpportunity(HistoricalBranchState branch)
    {
        var definition = branch.Id switch
        {
            "branch.capital_refugees" => Player.Background.Id switch
            {
                "background.clerk" => ("登记安置流民", "借官署名义核验名籍、分派住处和口粮。", UrbanLocationType.GovernmentOffice, "洛阳官署", 30),
                "background.ranger" => ("护送流民出城", "联络乡里和商队，护送一批流民离开京畿。", UrbanLocationType.Market, "洛阳市集", 45),
                _ => ("为流民代写文书", "联络士人，为无籍流民书写名状并寻求担保。", UrbanLocationType.School, "洛阳学舍", 35),
            },
            "branch.eastern_supply" => Player.Background.Id switch
            {
                "background.clerk" => ("核验东道转运", "清查征发名册，阻止地方借机重复摊派。", UrbanLocationType.GovernmentOffice, "荥阳官署", 40),
                "background.ranger" => ("打通东道驿路", "召集熟悉道路的同伴，压低沿途劫掠和勒索。", UrbanLocationType.Inn, "荥阳客舍", 55),
                _ => ("联络东郡士人", "以书信和清议促成地方共同分担粮运。", UrbanLocationType.Inn, "荥阳客舍", 45),
            },
            _ => Player.Background.Id switch
            {
                "background.clerk" => ("整理征辟名册", "核对郡府征辟文书，为真正有才干者留下名字。", UrbanLocationType.GovernmentOffice, "颍川郡府", 45),
                "background.ranger" => ("护送应辟士人", "确保受召者安全到达郡府，不受豪强截留。", UrbanLocationType.Inn, "颍川客舍", 55),
                _ => ("参与颍川品评", "在书院公开评议受荐者，以才学争取士林认可。", UrbanLocationType.School, "颍川书院", 50),
            },
        };
        var requiredSettlement = branch.Id switch
        {
            "branch.capital_refugees" => "settlement.luoyang",
            "branch.eastern_supply" => "settlement.xingyang",
            _ => "settlement.yingchuan",
        };
        var blockReason = Player.SettlementId != requiredSettlement
            ? $"此事发生在{Scenario.Map.GetSettlement(requiredSettlement).Name}。"
            : Date.AddDays(5).DaysUntil(branch.ResolvesOn) <= 0
                ? "局势即将自行收束，已经来不及完成介入。"
            : CurrentUrbanLocation.Type != definition.Item3
                ? $"需要先前往{definition.Item4}。"
                : Player.Money < definition.Item5
                    ? $"至少需要{definition.Item5}钱筹措此事。"
                    : null;
        return new CareerOpportunity(
            $"branch:{branch.Id}",
            CareerOpportunityKind.BranchIntervention,
            definition.Item1,
            definition.Item2,
            5,
            definition.Item5,
            blockReason is null,
            blockReason,
            branch.Id);
    }

    private void ExecuteCareerPath(CareerOpportunity opportunity)
    {
        Player = Player with { Money = Player.Money - opportunity.MoneyCost };
        AdvanceDays(opportunity.DurationDays);
        var progress = Player.Background.Id switch
        {
            "background.clerk" => 2 + (Player.Abilities.Administration / 50),
            "background.ranger" => 2 + (Player.Abilities.Martial / 50),
            _ => 2 + (Player.Abilities.Learning / 50),
        };
        Career = Career with
        {
            Reputation = Math.Clamp(Career.Reputation + 2, 0, 100),
            Network = Math.Clamp(Career.Network + 1, 0, 100),
            FinancialPressure = Math.Clamp(Career.FinancialPressure + 1, 0, 100),
            Goal = Career.Goal with { Progress = Math.Min(Career.Goal.Target, Career.Goal.Progress + progress) },
        };
        AddLocalPressure(Player.SettlementId, 1, -1, 1, 1, $"玩家完成“{opportunity.Title}”");
        AddLog(LogCategory.Career, $"你完成了“{opportunity.Title}”，阶段目标推进 {progress}，声望 +2，人脉 +1。");
        CompleteCareerGoalIfReady();
    }

    private void InterveneInBranch(CareerOpportunity opportunity)
    {
        var branch = historicalBranches[opportunity.BranchId!];
        Player = Player with { Money = Player.Money - opportunity.MoneyCost };
        AdvanceDays(opportunity.DurationDays);
        Career = Career with
        {
            Reputation = Math.Clamp(Career.Reputation + 4, 0, 100),
            Network = Math.Clamp(Career.Network + 3, 0, 100),
            Goal = Career.Goal with { Progress = Math.Min(Career.Goal.Target, Career.Goal.Progress + 3) },
        };
        historicalBranches[branch.Id] = branch with
        {
            Status = HistoricalBranchStatus.Resolved,
            Outcome = HistoricalBranchOutcome.PlayerInfluenced,
            PlayerApproach = opportunity.Title,
            Result = $"你以“{opportunity.Title}”介入，使局势向更有秩序的一面收束。",
        };
        var settlementId = branch.Id switch
        {
            "branch.capital_refugees" => "settlement.luoyang",
            "branch.eastern_supply" => "settlement.xingyang",
            _ => "settlement.yingchuan",
        };
        AddLocalPressure(settlementId, 3, -2, 2, Player.Background.Id == "background.ranger" ? -1 : 2, $"玩家介入“{branch.Title}”");
        AddLog(LogCategory.World, $"局势分支“{branch.Title}”受到你的介入：{historicalBranches[branch.Id].Result}声望 +4，人脉 +3。");
        CompleteCareerGoalIfReady();
    }

    private void GainCareer(int progress, int reputation, int network)
    {
        var goal = Career.Goal.Status == CareerGoalStatus.Active
            ? Career.Goal with { Progress = Math.Min(Career.Goal.Target, Career.Goal.Progress + progress) }
            : Career.Goal;
        Career = Career with
        {
            Reputation = Math.Clamp(Career.Reputation + reputation, 0, 100),
            Network = Math.Clamp(Career.Network + network, 0, 100),
            Goal = goal,
        };
        CompleteCareerGoalIfReady();
    }

    private void CompleteCareerGoalIfReady()
    {
        if (Career.Goal.Status != CareerGoalStatus.Active || Career.Goal.Progress < Career.Goal.Target)
        {
            return;
        }

        Career = Career with { Goal = Career.Goal with { Status = CareerGoalStatus.Completed } };
        AddLog(LogCategory.Career, $"阶段目标完成：{Career.Goal.Title}。你的生涯已经获得一个可继续发展的立足点。");
    }

    private void UpdateCommitmentsForDate()
    {
        for (var i = 0; i < commitments.Count; i++)
        {
            var commitment = commitments[i];
            if (commitment.Status != CommitmentStatus.Scheduled)
            {
                continue;
            }

            var character = characters[commitment.CharacterId].Profile;
            if (commitment.DueDate == Date)
            {
                AddLog(LogCategory.Commitment, $"今日是你与{character.Name}约定会面的日子。你必须前往约定地点才能履约。");
            }
            else if (commitment.DueDate.DaysUntil(Date) > 0)
            {
                commitments[i] = commitment with { Status = CommitmentStatus.Missed };
                AddLog(LogCategory.Commitment, $"你错过了与{character.Name}约定的日期。这次失约已经成为双方关系中的事实。");
            }
        }
    }

    private void UpdateOrganizationCommissionsForDate()
    {
        for (var i = 0; i < organizationCommissions.Count; i++)
        {
            var commission = organizationCommissions[i];
            if (commission.Status != OrganizationCommissionStatus.Accepted || commission.DueDate.DaysUntil(Date) <= 0)
            {
                continue;
            }

            ApplyCommissionImpact(commission, succeeded: false);
            organizationCommissions[i] = commission with
            {
                Status = OrganizationCommissionStatus.Failed,
                Result = "你没有在期限内履行承诺，原有缺口进一步恶化。",
            };
            AddLog(LogCategory.Commitment, $"你错过了{organizations[commission.OrganizationId].Name}委托“{commission.Title}”的期限。承诺失败，组织资产、地方账本和你的生涯记录都留下了后果。 ");
            Career = Career with
            {
                Reputation = Math.Max(0, Career.Reputation - 2),
                Network = Math.Max(0, Career.Network - 1),
            };
        }
    }

    private void UpdateCareerForDate()
    {
        if (Date.Day is 1 or 11 or 21)
        {
            var upkeep = Player.Background.Id switch
            {
                "background.clerk" => 80,
                "background.ranger" => 70,
                _ => 90,
            };
            if (Player.Money >= upkeep)
            {
                Player = Player with { Money = Player.Money - upkeep };
                Career = Career with
                {
                    UpkeepPaid = Career.UpkeepPaid + upkeep,
                    FinancialPressure = Math.Max(0, Career.FinancialPressure - 1),
                };
                AddLog(LogCategory.Career, $"新一旬的食宿、交游和日常开销共用去{upkeep}钱。稳定谋生是个人生涯的一部分。");
            }
            else
            {
                Career = Career with { FinancialPressure = Math.Clamp(Career.FinancialPressure + 3, 0, 100) };
                AddLog(LogCategory.Career, $"你无力承担本旬约{upkeep}钱的日常开销，财富压力明显上升。若不谋生，许多机会会逐渐失去。");
            }
        }

        if (Career.Goal.Status == CareerGoalStatus.Active && Career.Goal.Deadline.DaysUntil(Date) >= 0)
        {
            Career = Career with { Goal = Career.Goal with { Status = CareerGoalStatus.Failed } };
            AddLog(LogCategory.Career, $"阶段目标失败：{Career.Goal.Title}。期限已到，而你只完成了 {Career.Goal.Progress}/{Career.Goal.Target}；世界仍会继续运行。");
        }
    }

    private void UpdateHistoricalBranchesForDate()
    {
        foreach (var pair in historicalBranches.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray())
        {
            var branch = pair.Value;
            if (branch.Status == HistoricalBranchStatus.Upcoming && branch.OpensOn.DaysUntil(Date) >= 0)
            {
                branch = branch with { Status = HistoricalBranchStatus.Active };
                historicalBranches[pair.Key] = branch;
                AddLog(LogCategory.World, $"局势出现：{branch.Title}。{branch.Description}你可以旁观，也可以在期限前前往当地介入。");
            }

            if (branch.Status != HistoricalBranchStatus.Active || branch.ResolvesOn.DaysUntil(Date) < 0)
            {
                continue;
            }

            var result = BystanderResult(branch.Id);
            historicalBranches[pair.Key] = branch with
            {
                Status = HistoricalBranchStatus.Resolved,
                Outcome = HistoricalBranchOutcome.Bystander,
                Result = result,
            };
            var settlementId = branch.Id switch
            {
                "branch.capital_refugees" => "settlement.luoyang",
                "branch.eastern_supply" => "settlement.xingyang",
                _ => "settlement.yingchuan",
            };
            AddLocalPressure(settlementId, -2, 2, -1, -1, $"“{branch.Title}”在无人介入下自行收束");
            AddLog(LogCategory.World, $"局势自行收束：{branch.Title}。{result}你的旁观同样成为历史的一部分。");
        }
    }

    private void RunDailyWorldStep()
    {
        foreach (var actor in characters.Values.OrderBy(item => item.Profile.Id, StringComparer.Ordinal).ToArray())
        {
            AdvanceCharacterPlan(actor);
        }
    }

    private void InitializeLocalWorldState()
    {
        foreach (var seed in Scenario.SettlementConditions)
        {
            settlementStates[seed.SettlementId] = new SettlementState(
                seed.SettlementId,
                seed.Security,
                seed.GrainPrice,
                seed.Prosperity,
                seed.GovernmentControl);
        }

        foreach (var seed in Scenario.RoadConditions)
        {
            roadStates[seed.RoadId] = new RoadState(seed.RoadId, seed.Risk);
        }

        foreach (var character in Scenario.Characters)
        {
            characterPlans[character.Id] = new CharacterPlanState(
                character.Id,
                GoalFor(character),
                [character.SettlementId],
                "尚未形成新的行动意图");
        }
    }

    private void InitializeOrganizationWorldState()
    {
        foreach (var settlement in Scenario.Map.Settlements)
        {
            var local = settlementStates[settlement.Id];
            var ledger = new SettlementResourceLedger(
                settlement.Id,
                Math.Clamp(40 + (local.Prosperity / 2), 0, 100),
                Math.Clamp(105 - local.GrainPrice, 0, 100),
                Math.Clamp((local.GovernmentControl + local.Prosperity) / 2, 0, 100),
                Math.Clamp((local.Security + local.GovernmentControl) / 2, 0, 100),
                "依据地方状态建立的 D 级玩法标定");
            resourceLedgers[settlement.Id] = ledger;

            var primaryLocations = settlement.UrbanLocations
                .Where(item => item.Type is UrbanLocationType.GovernmentOffice or UrbanLocationType.School or UrbanLocationType.Market or UrbanLocationType.Barracks)
                .Select(item => (Location: item, Kind: OrganizationKindFor(item.Type)))
                .ToList();
            if (!primaryLocations.Any(item => item.Kind == OrganizationKind.MerchantGuild))
            {
                var fallback = settlement.UrbanLocations.FirstOrDefault(item => item.Type is UrbanLocationType.Inn or UrbanLocationType.Tavern);
                if (fallback is not null)
                {
                    primaryLocations.Add((fallback, OrganizationKind.MerchantGuild));
                }
            }

            foreach (var (location, kind) in primaryLocations)
            {
                var id = $"organization.{settlement.Id["settlement.".Length..]}.{OrganizationKindId(kind)}";
                organizations[id] = CreateOrganization(id, settlement, location, kind, local, ledger);
            }
        }
    }

    private static OrganizationKind OrganizationKindFor(UrbanLocationType type) => type switch
    {
        UrbanLocationType.GovernmentOffice => OrganizationKind.Government,
        UrbanLocationType.School => OrganizationKind.ScholarNetwork,
        UrbanLocationType.Barracks => OrganizationKind.LocalMilitia,
        _ => OrganizationKind.MerchantGuild,
    };

    private static string OrganizationKindId(OrganizationKind kind) => kind switch
    {
        OrganizationKind.Government => "government",
        OrganizationKind.ScholarNetwork => "scholars",
        OrganizationKind.MerchantGuild => "merchants",
        OrganizationKind.LocalMilitia => "militia",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static OrganizationState CreateOrganization(
        string id,
        Settlement settlement,
        UrbanLocation location,
        OrganizationKind kind,
        SettlementState local,
        SettlementResourceLedger ledger)
    {
        var name = kind switch
        {
            OrganizationKind.Government => $"{settlement.Name}官署",
            OrganizationKind.ScholarNetwork => $"{settlement.Name}士人网络",
            OrganizationKind.MerchantGuild => $"{settlement.Name}商旅互助会",
            OrganizationKind.LocalMilitia => $"{settlement.Name}地方守备",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var assets = kind switch
        {
            OrganizationKind.Government => (ledger.Treasury, ledger.Grain, ledger.Labor, local.GovernmentControl),
            OrganizationKind.ScholarNetwork => (35 + (local.Prosperity / 4), ledger.Grain, 30 + (local.Prosperity / 3), local.Prosperity),
            OrganizationKind.MerchantGuild => (local.Prosperity, ledger.Grain, 35 + (local.Security / 3), local.Prosperity),
            OrganizationKind.LocalMilitia => (30 + (local.GovernmentControl / 3), ledger.Grain, local.Security, local.GovernmentControl),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new OrganizationState(
            id,
            name,
            kind,
            settlement.Id,
            location.Id,
            Math.Clamp(assets.Item1, 0, 100),
            Math.Clamp(assets.Item2, 0, 100),
            Math.Clamp(assets.Item3, 0, 100),
            Math.Clamp(assets.Item4, 0, 100));
    }

    private CareerState CreateInitialCareerState(PlayerBackground background)
    {
        var (title, description, target) = background.Id switch
        {
            "background.clerk" => ("在官府站稳脚跟", "六十日内通过差事、交游和局势介入积累十二点履历，避免始终只是可替代的小吏。", 12),
            "background.ranger" => ("建立可靠的乡里名声", "六十日内通过护送、谋生、交游和局势介入积累十二点事迹，使自由不再等于漂泊。", 12),
            _ => ("打开寒门士人的门路", "六十日内通过读书、清议、交游和局势介入积累十二点声名，为仕途或游学建立起点。", 12),
        };
        return new CareerState(
            background.Id,
            0,
            0,
            0,
            0,
            new CareerGoalState(
                $"goal:{background.Id}",
                title,
                description,
                0,
                target,
                Scenario.StartDate.AddDays(60),
                CareerGoalStatus.Active));
    }

    private void InitializeHistoricalBranches()
    {
        HistoricalBranchState[] branches =
        [
            new(
                "branch.capital_refugees",
                "京畿流民涌入",
                "京城变局使无籍流民进入洛阳，官署、里坊与乡里各有不同打算。",
                Scenario.StartDate.AddDays(3),
                Scenario.StartDate.AddDays(18),
                HistoricalBranchStatus.Upcoming,
                HistoricalBranchOutcome.Undecided,
                null,
                "尚未发生"),
            new(
                "branch.eastern_supply",
                "东道粮运争执",
                "荥阳一带的征发和转运互相牵制，地方官、商旅与沿途乡里都在承受压力。",
                Scenario.StartDate.AddDays(24),
                Scenario.StartDate.AddDays(39),
                HistoricalBranchStatus.Upcoming,
                HistoricalBranchOutcome.Undecided,
                null,
                "尚未发生"),
            new(
                "branch.yingchuan_recruitment",
                "颍川征辟品评",
                "颍川士人与郡府开始品评人才，门第、才学和地方声望会导向不同名单。",
                Scenario.StartDate.AddDays(44),
                Scenario.StartDate.AddDays(57),
                HistoricalBranchStatus.Upcoming,
                HistoricalBranchOutcome.Undecided,
                null,
                "尚未发生"),
        ];
        foreach (var branch in branches)
        {
            historicalBranches[branch.Id] = branch;
        }
    }

    private void AlignMigratedCareerWorldState()
    {
        if (Career.Goal.Deadline.DaysUntil(Date) >= 0)
        {
            Career = Career with { Goal = Career.Goal with { Status = CareerGoalStatus.Failed } };
        }

        foreach (var pair in historicalBranches.ToArray())
        {
            var branch = pair.Value;
            if (branch.ResolvesOn.DaysUntil(Date) >= 0)
            {
                historicalBranches[pair.Key] = branch with
                {
                    Status = HistoricalBranchStatus.Resolved,
                    Outcome = HistoricalBranchOutcome.Bystander,
                    Result = BystanderResult(branch.Id),
                };
            }
            else if (branch.OpensOn.DaysUntil(Date) >= 0)
            {
                historicalBranches[pair.Key] = branch with { Status = HistoricalBranchStatus.Active };
            }
        }
    }

    private static string BystanderResult(string branchId) => branchId switch
    {
        "branch.capital_refugees" => "无人统筹安置，流民与京城里坊之间的紧张继续累积。",
        "branch.eastern_supply" => "各方只顾自身摊派，东道粮运在反复征发中变得迟滞。",
        _ => "征辟仍由门第与旧识主导，一批有才干却缺少声名的人没有得到机会。",
    };

    private void AdvanceCharacterPlan(CharacterState actor)
    {
        var plan = characterPlans[actor.Profile.Id];
        switch (plan.Stage)
        {
            case CharacterPlanStage.Assessing:
                if ((Date.Day - 1) % 10 == StableScheduleDay(actor.Profile.Id) &&
                    !commitments.Any(item =>
                        item.CharacterId == actor.Profile.Id && item.Status == CommitmentStatus.Scheduled))
                {
                    FormCharacterPlan(actor, plan);
                }

                break;
            case CharacterPlanStage.Preparing when HasReached(plan.NextStepOn):
                BeginCharacterPlanExecution(actor, plan);
                break;
            case CharacterPlanStage.Traveling when HasReached(plan.NextStepOn):
                ArriveForCharacterPlan(actor, plan);
                break;
            case CharacterPlanStage.Executing when HasReached(plan.NextStepOn):
                ResolveCharacterPlan(actor, plan);
                break;
            case CharacterPlanStage.Completed or CharacterPlanStage.Failed or CharacterPlanStage.Cancelled
                when HasReached(plan.NextStepOn):
                characterPlans[actor.Profile.Id] = plan with
                {
                    Stage = CharacterPlanStage.Assessing,
                    TargetOrganizationId = null,
                    Need = null,
                    TargetSettlementId = null,
                    TargetUrbanLocationId = null,
                    StartedOn = null,
                    NextStepOn = null,
                    ReservedEffort = 0,
                    PlayerSupport = 0,
                    LastIntent = "重新观察已知地方，等待值得投入的机会",
                };
                break;
        }
    }

    private void FormCharacterPlan(CharacterState actor, CharacterPlanState plan)
    {
        var candidate = BuildCharacterPlanCandidates(actor, plan)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Organization.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate is null)
        {
            characterPlans[actor.Profile.Id] = plan with
            {
                LastIntent = "比较过已知机会，但没有组织能够承担眼前投入",
            };
            return;
        }

        ReservePlanResources(candidate.Organization.Id, candidate.Need);
        var target = Scenario.Map.GetSettlement(candidate.Organization.SettlementId);
        var intent = $"{PlanGoalVerb(plan.Goal)}，准备为{candidate.Organization.Name}处理{PlanNeedName(candidate.Need)}";
        characterPlans[actor.Profile.Id] = plan with
        {
            Stage = CharacterPlanStage.Preparing,
            TargetOrganizationId = candidate.Organization.Id,
            Need = candidate.Need,
            TargetSettlementId = candidate.Organization.SettlementId,
            TargetUrbanLocationId = candidate.Organization.UrbanLocationId,
            StartedOn = Date,
            NextStepOn = Date.AddDays(1),
            ReservedEffort = RequiredPlanEffort(candidate.Need),
            PlayerSupport = 0,
            LastIntent = intent,
            Result = string.Empty,
            KnownSettlementIds = plan.KnownSettlementIds
                .Append(actor.SettlementId)
                .Concat(Scenario.Map.GetDestinations(actor.SettlementId).Select(item => item.Destination.Id))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
        AddLog(
            LogCategory.World,
            $"传闻：{actor.Profile.Name}权衡目标、能力、组织资源与行程后，决定前往{target.Name}为{candidate.Organization.Name}处理{PlanNeedName(candidate.Need)}。这件事并非由玩家触发。");
    }

    private IReadOnlyList<CharacterPlanCandidate> BuildCharacterPlanCandidates(
        CharacterState actor,
        CharacterPlanState plan)
    {
        var destinations = Scenario.Map.GetDestinations(actor.SettlementId);
        var reachable = destinations.ToDictionary(item => item.Destination.Id, item => item.Road, StringComparer.Ordinal);
        var known = plan.KnownSettlementIds
            .Append(actor.SettlementId)
            .Concat(destinations.Select(item => item.Destination.Id))
            .ToHashSet(StringComparer.Ordinal);
        return organizations.Values
            .Where(item => known.Contains(item.SettlementId))
            .Where(item => item.SettlementId == actor.SettlementId || reachable.ContainsKey(item.SettlementId))
            .Select(organization =>
            {
                var need = DetermineOrganizationNeed(organization);
                var travelDays = organization.SettlementId == actor.SettlementId
                    ? 0
                    : reachable[organization.SettlementId].TravelDays;
                return new CharacterPlanCandidate(
                    organization,
                    need,
                    ScoreCharacterPlan(actor, plan.Goal, organization, need, travelDays),
                    travelDays);
            })
            .Where(item => CanReservePlanResources(item.Organization, item.Need))
            .ToArray();
    }

    private int ScoreCharacterPlan(
        CharacterState actor,
        CharacterGoal goal,
        OrganizationState organization,
        OrganizationNeedKind need,
        int travelDays)
    {
        var roleFit = (actor.Profile.Roles, organization.Kind) switch
        {
            (var roles, OrganizationKind.Government) when roles.HasFlag(CharacterRole.Official) => 22,
            (var roles, OrganizationKind.ScholarNetwork) when roles.HasFlag(CharacterRole.Scholar) => 22,
            (var roles, OrganizationKind.MerchantGuild) when roles.HasFlag(CharacterRole.Merchant) => 22,
            (var roles, OrganizationKind.LocalMilitia) when roles.HasFlag(CharacterRole.General) => 22,
            (var roles, _) when roles.HasFlag(CharacterRole.LocalNotable) => 8,
            _ => 0,
        };
        var goalFit = (goal, need) switch
        {
            (CharacterGoal.MaintainOrder, OrganizationNeedKind.RoadSecurity) => 35,
            (CharacterGoal.MaintainOrder, OrganizationNeedKind.Labor) => 15,
            (CharacterGoal.SecureSupplies, OrganizationNeedKind.Grain) => 35,
            (CharacterGoal.SecureSupplies, OrganizationNeedKind.RoadSecurity) => 20,
            (CharacterGoal.BuildInfluence, OrganizationNeedKind.Labor) => 30,
            (CharacterGoal.BuildInfluence, OrganizationNeedKind.Treasury) => 15,
            (CharacterGoal.SeekOpportunity, OrganizationNeedKind.Treasury or OrganizationNeedKind.Labor) => 22,
            _ => 5,
        };
        var roadPenalty = organization.SettlementId == actor.SettlementId
            ? 0
            : HighestAdjacentRoadRisk(actor.SettlementId) / 8;
        return NeedUrgency(organization, need) +
            goalFit +
            roleFit +
            (RelevantPlanAbility(actor.Profile.Abilities, need) / 5) -
            (travelDays * 6) -
            roadPenalty;
    }

    private int NeedUrgency(OrganizationState organization, OrganizationNeedKind need)
    {
        var ledger = resourceLedgers[organization.SettlementId];
        return need switch
        {
            OrganizationNeedKind.Grain => 100 - ledger.Grain,
            OrganizationNeedKind.Treasury => 100 - organization.Treasury,
            OrganizationNeedKind.Labor => 100 - ledger.Labor,
            _ => HighestAdjacentRoadRisk(organization.SettlementId),
        };
    }

    private static int RelevantPlanAbility(Abilities abilities, OrganizationNeedKind need) => need switch
    {
        OrganizationNeedKind.Grain => Math.Max(abilities.Administration, abilities.Strategy),
        OrganizationNeedKind.Treasury => Math.Max(abilities.Administration, abilities.Diplomacy),
        OrganizationNeedKind.Labor => Math.Max(abilities.Diplomacy, abilities.Administration),
        _ => Math.Max(abilities.Command, abilities.Martial),
    };

    private static bool CanReservePlanResources(OrganizationState organization, OrganizationNeedKind need) => need switch
    {
        OrganizationNeedKind.Grain => organization.Treasury >= 2 && organization.Personnel >= 2,
        OrganizationNeedKind.Treasury => organization.Personnel >= 3,
        OrganizationNeedKind.Labor => organization.Treasury >= 1 && organization.Influence >= 3,
        _ => organization.Treasury >= 3 && organization.Personnel >= 3,
    };

    private void ReservePlanResources(string organizationId, OrganizationNeedKind need)
    {
        var organization = organizations[organizationId];
        organizations[organizationId] = need switch
        {
            OrganizationNeedKind.Grain => organization.Apply(-2, 0, -2, 0),
            OrganizationNeedKind.Treasury => organization.Apply(0, 0, -3, 0),
            OrganizationNeedKind.Labor => organization.Apply(-1, 0, 0, -3),
            _ => organization.Apply(-3, 0, -3, 0),
        };
    }

    private void ReleaseReservedPlanResources(CharacterPlanState plan, int amount)
    {
        if (plan.TargetOrganizationId is null || plan.Need is null)
        {
            return;
        }

        var organization = organizations[plan.TargetOrganizationId];
        organizations[plan.TargetOrganizationId] = plan.Need.Value switch
        {
            OrganizationNeedKind.Grain => organization.Apply(amount, 0, amount, 0),
            OrganizationNeedKind.Treasury => organization.Apply(0, 0, amount, 0),
            OrganizationNeedKind.Labor => organization.Apply(amount, 0, 0, amount),
            _ => organization.Apply(amount, 0, amount, 0),
        };
    }

    private void BeginCharacterPlanExecution(CharacterState actor, CharacterPlanState plan)
    {
        var organization = organizations[plan.TargetOrganizationId!];
        if (DetermineOrganizationNeed(organization) != plan.Need)
        {
            ReleaseReservedPlanResources(plan, 1);
            characterPlans[actor.Profile.Id] = plan with
            {
                Stage = CharacterPlanStage.Cancelled,
                NextStepOn = Date.AddDays(10),
                LastIntent = "发现原有缺口已经变化，取消计划",
                Result = "组织需求在筹备期间改变，原计划失去必要性。",
            };
            AddLog(LogCategory.World, $"{actor.Profile.Name}取消了为{organization.Name}准备的计划，因为原有资源缺口已经变化。");
            return;
        }

        if (actor.SettlementId == organization.SettlementId)
        {
            characters[actor.Profile.Id] = actor with { UrbanLocationId = organization.UrbanLocationId };
            characterPlans[actor.Profile.Id] = plan with
            {
                Stage = CharacterPlanStage.Executing,
                NextStepOn = Date.AddDays(2),
                LastIntent = $"已经开始为{organization.Name}处理{PlanNeedName(plan.Need!.Value)}",
            };
            return;
        }

        var route = Scenario.Map.GetDestinations(actor.SettlementId)
            .SingleOrDefault(item => item.Destination.Id == organization.SettlementId);
        if (route.Destination is null || route.Road is null)
        {
            FailCharacterPlan(actor, plan, "已知行程发生变化，无法抵达目标地点");
            return;
        }

        AddRoadPressure(route.Road.Id, 1);
        characterPlans[actor.Profile.Id] = plan with
        {
            Stage = CharacterPlanStage.Traveling,
            NextStepOn = Date.AddDays(route.Road.TravelDays),
            LastIntent = $"沿{route.Road.Description}前往{route.Destination.Name}",
        };
        AddLog(LogCategory.World, $"传闻：{actor.Profile.Name}离开{Scenario.Map.GetSettlement(actor.SettlementId).Name}，沿道路前往{route.Destination.Name}履行对{organization.Name}的承诺。");
    }

    private void ArriveForCharacterPlan(CharacterState actor, CharacterPlanState plan)
    {
        var organization = organizations[plan.TargetOrganizationId!];
        characters[actor.Profile.Id] = actor with
        {
            SettlementId = organization.SettlementId,
            UrbanLocationId = organization.UrbanLocationId,
        };
        characterPlans[actor.Profile.Id] = plan with
        {
            Stage = CharacterPlanStage.Executing,
            NextStepOn = Date.AddDays(2),
            KnownSettlementIds = plan.KnownSettlementIds
                .Append(organization.SettlementId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            LastIntent = $"抵达{Scenario.Map.GetSettlement(organization.SettlementId).Name}，开始处理{PlanNeedName(plan.Need!.Value)}",
        };
        AddLog(LogCategory.World, $"{actor.Profile.Name}抵达{Scenario.Map.GetSettlement(organization.SettlementId).Name}，开始履行对{organization.Name}的承诺。");
    }

    private void ResolveCharacterPlan(CharacterState actor, CharacterPlanState plan)
    {
        var organization = organizations[plan.TargetOrganizationId!];
        var ability = RelevantPlanAbility(actor.Profile.Abilities, plan.Need!.Value);
        var executionScore = ability + plan.PlayerSupport + (organization.Influence / 5) + StableScheduleDay(actor.Profile.Id);
        var difficulty = 72 + (NeedUrgency(organization, plan.Need.Value) / 4);
        if (executionScore < difficulty)
        {
            FailCharacterPlan(actor, plan, $"能力与可用支援不足（把握 {executionScore}，难度 {difficulty}）");
            return;
        }

        ApplyCharacterPlanImpact(actor, plan, organization);
        characterPlans[actor.Profile.Id] = plan with
        {
            Stage = CharacterPlanStage.Completed,
            NextStepOn = Date.AddDays(10),
            LastIntent = $"完成了为{organization.Name}处理{PlanNeedName(plan.Need.Value)}的承诺",
            Result = $"计划成功（把握 {executionScore}，难度 {difficulty}）。",
        };
        AddLog(LogCategory.World, $"{actor.Profile.Name}完成了对{organization.Name}的承诺：{PlanNeedName(plan.Need.Value)}得到缓解。组织资产与地方账本已经同步变化。");
    }

    private void ApplyCharacterPlanImpact(
        CharacterState actor,
        CharacterPlanState plan,
        OrganizationState organization)
    {
        var ledger = resourceLedgers[organization.SettlementId];
        var cause = $"{actor.Profile.Name}完成{organization.Name}的{PlanNeedName(plan.Need!.Value)}计划";
        resourceLedgers[organization.SettlementId] = plan.Need.Value switch
        {
            OrganizationNeedKind.Grain => ledger.Apply(0, 6, 0, -1, cause),
            OrganizationNeedKind.Treasury => ledger.Apply(0, 0, 6, -1, cause),
            OrganizationNeedKind.Labor => ledger.Apply(0, 0, 0, 6, cause),
            _ => ledger.Apply(0, 1, 0, -1, cause),
        };
        organizations[organization.Id] = plan.Need.Value switch
        {
            OrganizationNeedKind.Grain => organization.Apply(0, 4, 1, 1),
            OrganizationNeedKind.Treasury => organization.Apply(5, 0, 1, 1),
            OrganizationNeedKind.Labor => organization.Apply(0, 0, 5, 1),
            _ => organization.Apply(0, 0, 2, 2),
        };
        if (plan.Need == OrganizationNeedKind.RoadSecurity)
        {
            foreach (var road in Scenario.Map.Roads.Where(item => item.Connects(organization.SettlementId)))
            {
                roadStates[road.Id] = roadStates[road.Id].Apply(-3);
            }
        }

        AddLocalPressure(
            organization.SettlementId,
            plan.Need == OrganizationNeedKind.RoadSecurity ? 2 : 0,
            plan.Need == OrganizationNeedKind.Grain ? -1 : 0,
            1,
            plan.Goal == CharacterGoal.MaintainOrder ? 1 : 0,
            cause);
    }

    private void FailCharacterPlan(CharacterState actor, CharacterPlanState plan, string reason)
    {
        var organization = organizations[plan.TargetOrganizationId!];
        var ledger = resourceLedgers[organization.SettlementId];
        resourceLedgers[organization.SettlementId] = plan.Need switch
        {
            OrganizationNeedKind.Grain => ledger.Apply(0, -2, 0, 0, $"{actor.Profile.Name}的计划失败"),
            OrganizationNeedKind.Treasury => ledger.Apply(0, 0, -2, 0, $"{actor.Profile.Name}的计划失败"),
            OrganizationNeedKind.Labor => ledger.Apply(0, 0, 0, -2, $"{actor.Profile.Name}的计划失败"),
            _ => ledger.Apply(0, -1, 0, -1, $"{actor.Profile.Name}的计划失败"),
        };
        characterPlans[actor.Profile.Id] = plan with
        {
            Stage = CharacterPlanStage.Failed,
            NextStepOn = Date.AddDays(10),
            LastIntent = $"未能完成对{organization.Name}的承诺",
            Result = reason,
        };
        AddLog(LogCategory.World, $"{actor.Profile.Name}未能完成对{organization.Name}的承诺：{reason}。已经投入的资源没有凭空返还，原有缺口进一步恶化。");
    }

    private bool HasReached(GameDate? date) => date is not null && date.Value.DaysUntil(Date) >= 0;

    private static int RequiredPlanEffort(OrganizationNeedKind need) =>
        need == OrganizationNeedKind.RoadSecurity ? 4 : 3;

    private static string PlanGoalVerb(CharacterGoal goal) => goal switch
    {
        CharacterGoal.MaintainOrder => "为整顿地方秩序寻找事务",
        CharacterGoal.SecureSupplies => "为调度粮货寻找事务",
        CharacterGoal.BuildInfluence => "为经营地方关系寻找事务",
        _ => "为自身前途寻找事务",
    };

    private static string PlanNeedName(OrganizationNeedKind need) => need switch
    {
        OrganizationNeedKind.Grain => "粮储短缺",
        OrganizationNeedKind.Treasury => "钱财不足",
        OrganizationNeedKind.Labor => "人力不足",
        _ => "道路治安",
    };

    private void ResolveTenDayPeriod()
    {
        var summaries = new List<string>();
        foreach (var settlement in Scenario.Map.Settlements.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var before = settlementStates[settlement.Id];
            var ledgerBefore = resourceLedgers[settlement.Id];
            var pressure = pendingLocalPressures.GetValueOrDefault(settlement.Id, LocalPressure.None);
            var structural = StructuralPressure(settlement.Id);
            var total = pressure.Add(
                structural.Security,
                structural.GrainPrice,
                structural.Prosperity,
                structural.GovernmentControl,
                structural.Source);
            var after = before.Apply(total);
            settlementStates[settlement.Id] = after;
            ApplyOrganizationPeriodActions(settlement.Id);
            ResolveResourceLedger(settlement.Id, after);
            var ledgerAfter = resourceLedgers[settlement.Id];
            if (before != after || ledgerBefore != ledgerAfter)
            {
                var sources = string.Join("、", total.Sources.Take(3));
                summaries.Add($"{settlement.Name}：治安 {Signed(after.Security - before.Security)}，粮价 {Signed(after.GrainPrice - before.GrainPrice)}，繁荣 {Signed(after.Prosperity - before.Prosperity)}，官府控制 {Signed(after.GovernmentControl - before.GovernmentControl)}；账本粮储 {Signed(ledgerAfter.Grain - ledgerBefore.Grain)}，府库 {Signed(ledgerAfter.Treasury - ledgerBefore.Treasury)}，人力 {Signed(ledgerAfter.Labor - ledgerBefore.Labor)}；缘由：{sources}、{ledgerAfter.LastCause}");
            }
        }

        foreach (var road in Scenario.Map.Roads.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var before = roadStates[road.Id];
            var usage = pendingRoadPressures.GetValueOrDefault(road.Id);
            var endpoints = new[] { settlementStates[road.FromSettlementId], settlementStates[road.ToSettlementId] };
            var insecurity = endpoints.Sum(item => 50 - item.Security) / 40;
            var delta = usage > 0
                ? Math.Clamp(usage + insecurity, 0, 4)
                : Math.Clamp(insecurity - 1, -2, 2);
            roadStates[road.Id] = before.Apply(delta);
        }

        pendingLocalPressures.Clear();
        pendingRoadPressures.Clear();
        AddLog(
            LogCategory.Period,
            summaries.Count == 0
                ? $"{Date.Year}年{Date.Month}月{Date.Period.ToChinese()}开始。本旬各地压力暂时相互抵消，没有形成明显变化。"
                : $"{Date.Year}年{Date.Month}月{Date.Period.ToChinese()}开始，旬结算完成；{summaries.Count}座城邑形成了可观察变化。");
        foreach (var summary in summaries)
        {
            AddLog(LogCategory.Period, summary);
        }
    }

    private void ApplyOrganizationPeriodActions(string settlementId)
    {
        foreach (var organization in organizations.Values
            .Where(item => item.SettlementId == settlementId)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray())
        {
            var ledger = resourceLedgers[settlementId];
            var need = DetermineOrganizationNeed(organization);
            switch (need)
            {
                case OrganizationNeedKind.Grain when organization.Grain >= 4:
                    organizations[organization.Id] = organization.Apply(0, -4, -1, 0);
                    resourceLedgers[settlementId] = ledger.Apply(0, 3, 0, 0, $"{organization.Name}动用粮秣平抑短缺");
                    break;
                case OrganizationNeedKind.Treasury when organization.Personnel >= 3:
                    organizations[organization.Id] = organization.Apply(3, 0, -3, 0);
                    resourceLedgers[settlementId] = ledger.Apply(0, 0, 2, -1, $"{organization.Name}催办积欠以补府库");
                    break;
                case OrganizationNeedKind.Labor when organization.Influence >= 3:
                    organizations[organization.Id] = organization.Apply(-1, 0, 2, -3);
                    resourceLedgers[settlementId] = ledger.Apply(0, 0, 0, 2, $"{organization.Name}消耗影响力组织人手");
                    break;
                case OrganizationNeedKind.RoadSecurity when
                    organization.Treasury >= 2 &&
                    !Scenario.Map.Roads.Any(item =>
                        item.Connects(settlementId) && pendingRoadPressures.GetValueOrDefault(item.Id) > 0):
                    organizations[organization.Id] = organization.Apply(-2, 0, -1, 0);
                    foreach (var road in Scenario.Map.Roads.Where(item => item.Connects(settlementId)))
                    {
                        roadStates[road.Id] = roadStates[road.Id].Apply(-1);
                    }
                    resourceLedgers[settlementId] = ledger.Apply(0, 1, 0, -1, $"{organization.Name}出资维持邻近道路");
                    break;
            }
        }
    }

    private void ResolveResourceLedger(string settlementId, SettlementState local)
    {
        var ledger = resourceLedgers[settlementId];
        var risk = HighestAdjacentRoadRisk(settlementId);
        var populationDelta = local.Security < 40 ? -1 : local.Prosperity >= 75 ? 1 : 0;
        var grainDelta = (local.Prosperity / 30) - (ledger.Population / 45) - (risk / 35);
        var treasuryDelta = (local.GovernmentControl / 35) + (local.Prosperity / 40) - 3;
        var laborDelta = (local.Security >= 60 ? 1 : 0) - (local.Prosperity >= 70 ? 1 : 0);
        resourceLedgers[settlementId] = ledger.Apply(
            populationDelta,
            grainDelta,
            treasuryDelta,
            laborDelta,
            risk >= 40 ? "道路风险、人口消耗与旬内收支共同结算" : "地方生产、人口消耗与旬内收支共同结算");
    }

    private (int Security, int GrainPrice, int Prosperity, int GovernmentControl, string Source) StructuralPressure(string settlementId)
    {
        var state = settlementStates[settlementId];
        var adjacentRisk = Scenario.Map.Roads
            .Where(item => item.Connects(settlementId))
            .Select(item => roadStates[item.Id].Risk)
            .DefaultIfEmpty(0)
            .Average();
        return (
            adjacentRisk >= 45 ? -2 : adjacentRisk <= 30 ? 1 : 0,
            adjacentRisk >= 40 ? 2 : -1,
            state.Security >= 65 ? 1 : state.Security < 45 ? -1 : 0,
            settlementId == "settlement.luoyang" ? -1 : 0,
            adjacentRisk >= 40 ? "道路风险与运输阻滞" : "地方既有秩序与商贸基础");
    }

    private void AddLocalPressure(
        string settlementId,
        int security,
        int grainPrice,
        int prosperity,
        int governmentControl,
        string source)
    {
        var current = pendingLocalPressures.GetValueOrDefault(settlementId, LocalPressure.None);
        pendingLocalPressures[settlementId] = current.Add(security, grainPrice, prosperity, governmentControl, source);
    }

    private void AddRoadPressure(string roadId, int amount) =>
        pendingRoadPressures[roadId] = pendingRoadPressures.GetValueOrDefault(roadId) + amount;

    private static CharacterGoal GoalFor(Character character)
    {
        if (character.Roles.HasFlag(CharacterRole.Official) || character.Roles.HasFlag(CharacterRole.General))
        {
            return CharacterGoal.MaintainOrder;
        }

        if (character.Roles.HasFlag(CharacterRole.Merchant))
        {
            return CharacterGoal.SecureSupplies;
        }

        return character.Roles.HasFlag(CharacterRole.LocalNotable)
            ? CharacterGoal.BuildInfluence
            : CharacterGoal.SeekOpportunity;
    }

    private static int StableScheduleDay(string characterId) =>
        characterId.Aggregate(0, (sum, character) => (sum + character) % 10);

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private static void ReplaceDictionary<T>(Dictionary<string, T> target, IReadOnlyDictionary<string, T>? source)
    {
        if (source is null)
        {
            return;
        }

        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static RecognitionLevel StartingRecognition(PlayerBackground background, Character character)
    {
        var relevantRole = background.Id switch
        {
            "background.scholar" => CharacterRole.Scholar,
            "background.clerk" => CharacterRole.Official,
            "background.ranger" => CharacterRole.General | CharacterRole.Ranger | CharacterRole.LocalNotable,
            _ => CharacterRole.None,
        };
        return (character.Roles & relevantRole) != 0 ? RecognitionLevel.HeardOf : RecognitionLevel.Unknown;
    }

    private static IReadOnlyList<string> StartingTopicIds(PlayerBackground background) => background.Id switch
    {
        "background.scholar" => ["topic.court_upheaval"],
        "background.clerk" => ["topic.court_upheaval", "topic.local_recruitment"],
        "background.ranger" => ["topic.eastern_roads"],
        _ => [],
    };

    private void AddLog(LogCategory category, string text) => log.Add(new GameLogEntry(Date, category, text));
}
