using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public enum LogCategory
{
    Personal,
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
    IReadOnlyList<CharacterLocationSnapshot> CharacterLocations,
    IReadOnlyList<GameLogEntry> Log);

public sealed class GameSession
{
    private readonly Dictionary<string, CharacterState> characters;
    private readonly Dictionary<string, RelationshipState> socialConnections = new(StringComparer.Ordinal);
    private readonly HashSet<string> knownTopicIds = new(StringComparer.Ordinal);
    private readonly List<MeetingCommitment> commitments = [];
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
        if (snapshot.SchemaVersion is < 1 or > 2)
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
    }

    public GameScenario Scenario { get; }

    public GameDate Date { get; private set; }

    public PlayerState Player { get; private set; }

    public Settlement CurrentSettlement => Scenario.Map.GetSettlement(Player.SettlementId);

    public UrbanLocation CurrentUrbanLocation => CurrentSettlement.UrbanLocations.Single(item => item.Id == Player.UrbanLocationId);

    public IReadOnlyList<GameLogEntry> Log => log;

    public IReadOnlyList<MeetingCommitment> Commitments => commitments;

    public IReadOnlyList<ConversationTopic> KnownTopics => Scenario.Topics
        .Where(item => knownTopicIds.Contains(item.Id))
        .ToArray();

    public IReadOnlyList<CharacterState> CharactersAtCurrentLocation => characters.Values
        .Where(item => item.SettlementId == Player.SettlementId && item.UrbanLocationId == Player.UrbanLocationId)
        .OrderBy(item => item.Profile.Name, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<(Settlement Destination, Road Road)> AvailableDestinations =>
        Scenario.Map.GetDestinations(Player.SettlementId);

    public int RelationshipWith(string characterId) => ConnectionWith(characterId).Favor;

    public RelationshipState ConnectionWith(string characterId) =>
        socialConnections.GetValueOrDefault(characterId, RelationshipState.Unknown);

    public static GameSession Restore(GameScenario scenario, GameSnapshot snapshot) => new(scenario, snapshot);

    public GameSnapshot CreateSnapshot() => new(
        2,
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
        characters.Values
            .OrderBy(item => item.Profile.Id, StringComparer.Ordinal)
            .Select(item => new CharacterLocationSnapshot(item.Profile.Id, item.SettlementId, item.UrbanLocationId))
            .ToArray(),
        log.ToArray());

    public void TravelTo(string destinationId)
    {
        var route = AvailableDestinations.SingleOrDefault(item => item.Destination.Id == destinationId);
        if (route.Destination is null || route.Road is null)
        {
            throw new InvalidOperationException("此处没有通往该地点的直接道路。");
        }

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
                AddLog(LogCategory.Encounter, $"你向{character.Profile.Name}说明身份与来意。对方记住了你（相识，好感 +1）。");
                break;
            case InteractionActionKind.Visit:
                UpdateConnection(action.CharacterId, state => state.Improve(3, 1, RecognitionLevel.Acquainted));
                AdvanceDays(1);
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
            default:
                throw new ArgumentOutOfRangeException(nameof(actionId));
        }
    }

    public void GatherInformation()
    {
        var unknownTopic = Scenario.Topics.FirstOrDefault(item => !knownTopicIds.Contains(item.Id));
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
        AddLog(LogCategory.Personal, "你闭门读书三日，学识有所精进（学识 +1）。");
    }

    public void Work()
    {
        AdvanceDays(3);
        var income = Player.Background.Id switch
        {
            "background.clerk" => 160,
            "background.ranger" => 110,
            _ => 80,
        };
        Player = Player with { Money = Player.Money + income };
        AddLog(LogCategory.Personal, $"你用三日谋生，挣得{income}钱。身份和能力决定了你能接触到什么差事。");
    }

    public void Rest(int days = 1)
    {
        AdvanceDays(days);
        AddLog(LogCategory.Personal, $"你休整了{days}日，也留心观察周围的变化。");
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
        AddLog(LogCategory.Commitment, $"你依约与{character.Profile.Name}会面，双方都记住了这次守约（好感 +3，信任 +2）。");
    }

    private void DiscussTopic(CharacterState character, string topicId)
    {
        var topic = KnownTopics.Single(item => item.Id == topicId);
        UpdateConnection(character.Profile.Id, state => state.Improve(1, 1, RecognitionLevel.Acquainted));
        AdvanceDays(1);
        AddLog(LogCategory.Encounter, $"你与{character.Profile.Name}谈论“{topic.Title}”。对方愿意认真回应，彼此的判断多了一处交集（好感 +1，信任 +1）。");
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
            RunDailyWorldStep();

            if (Date.Period != previous.Period || Date.Month != previous.Month)
            {
                AddLog(LogCategory.Period, $"{Date.Year}年{Date.Month}月{Date.Period.ToChinese()}开始，各地势力重新评估粮秣、治安与人事。旬结算已经发生。");
            }
        }
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

    private void RunDailyWorldStep()
    {
        if (Date.Day % 5 != 0 || characters.Count == 0)
        {
            return;
        }

        var ordered = characters.Values
            .Where(item => !commitments.Any(commitment =>
                commitment.CharacterId == item.Profile.Id && commitment.Status == CommitmentStatus.Scheduled))
            .OrderBy(item => item.Profile.Id, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            return;
        }

        var actor = ordered[(Date.Day + Date.Month) % ordered.Length];
        var destinations = Scenario.Map.GetDestinations(actor.SettlementId);
        if (destinations.Count == 0)
        {
            return;
        }

        var destination = destinations[(Date.Day / 5) % destinations.Count].Destination;
        characters[actor.Profile.Id] = actor with
        {
            SettlementId = destination.Id,
            UrbanLocationId = destination.UrbanLocations[0].Id,
        };
        AddLog(LogCategory.World, $"传闻：{actor.Profile.Name}离开{Scenario.Map.GetSettlement(actor.SettlementId).Name}，动身前往{destination.Name}。这件事并非由玩家触发。");
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
