using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public enum LogCategory
{
    Personal,
    Travel,
    Encounter,
    World,
    Period,
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
    IReadOnlyDictionary<string, int> Relationships);

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
    IReadOnlyDictionary<string, int> Relationships,
    IReadOnlyList<CharacterLocationSnapshot> CharacterLocations,
    IReadOnlyList<GameLogEntry> Log);

public sealed class GameSession
{
    private readonly Dictionary<string, CharacterState> characters;
    private readonly Dictionary<string, int> relationships = new(StringComparer.Ordinal);
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
        Player = new PlayerState(
            playerName,
            background,
            scenario.StartSettlementId,
            scenario.StartUrbanLocationId,
            background.StartingMoney,
            background.StartingAbilities,
            relationships);
        AddLog(LogCategory.World, $"你以“{background.Name}”的身份来到{CurrentSettlement.Name}。天下正在运转，而你尚未决定自己的道路。");
    }

    private GameSession(GameScenario scenario, GameSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1)
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
        foreach (var pair in snapshot.Relationships)
        {
            relationships[pair.Key] = pair.Value;
        }

        var savedLocations = snapshot.CharacterLocations.ToDictionary(item => item.CharacterId, StringComparer.Ordinal);
        characters = scenario.Characters.ToDictionary(
            item => item.Id,
            item => savedLocations.TryGetValue(item.Id, out var location)
                ? new CharacterState(item, location.SettlementId, location.UrbanLocationId)
                : new CharacterState(item, item.SettlementId, item.UrbanLocationId),
            StringComparer.Ordinal);
        Player = new PlayerState(
            snapshot.PlayerName,
            background,
            snapshot.SettlementId,
            snapshot.UrbanLocationId,
            snapshot.Money,
            snapshot.Abilities,
            new Dictionary<string, int>(relationships, StringComparer.Ordinal));
        log.AddRange(snapshot.Log);
    }

    public GameScenario Scenario { get; }

    public GameDate Date { get; private set; }

    public PlayerState Player { get; private set; }

    public Settlement CurrentSettlement => Scenario.Map.GetSettlement(Player.SettlementId);

    public UrbanLocation CurrentUrbanLocation => CurrentSettlement.UrbanLocations.Single(item => item.Id == Player.UrbanLocationId);

    public IReadOnlyList<GameLogEntry> Log => log;

    public IReadOnlyList<CharacterState> CharactersAtCurrentLocation => characters.Values
        .Where(item => item.SettlementId == Player.SettlementId && item.UrbanLocationId == Player.UrbanLocationId)
        .OrderBy(item => item.Profile.Name, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<(Settlement Destination, Road Road)> AvailableDestinations =>
        Scenario.Map.GetDestinations(Player.SettlementId);

    public int RelationshipWith(string characterId) => relationships.GetValueOrDefault(characterId);

    public static GameSession Restore(GameScenario scenario, GameSnapshot snapshot) => new(scenario, snapshot);

    public GameSnapshot CreateSnapshot() => new(
        1,
        Scenario.Id,
        Date,
        Player.Background.Id,
        Player.Name,
        Player.SettlementId,
        Player.UrbanLocationId,
        Player.Money,
        Player.Abilities,
        new Dictionary<string, int>(relationships, StringComparer.Ordinal),
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

    public void VisitCharacter(string characterId)
    {
        var character = CharactersAtCurrentLocation.SingleOrDefault(item => item.Profile.Id == characterId)
            ?? throw new InvalidOperationException("此人现在不在这里。");
        AdvanceDays(1);
        relationships[characterId] = RelationshipWith(characterId) + 3;
        Player = Player with { Relationships = new Dictionary<string, int>(relationships, StringComparer.Ordinal) };
        AddLog(LogCategory.Encounter, $"你拜访了{character.Profile.Name}。交谈虽不算深入，但彼此多了一分印象（关系 +3）。");
    }

    public void GatherInformation()
    {
        AdvanceDays(1);
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

    private void AdvanceDays(int days)
    {
        for (var i = 0; i < days; i++)
        {
            var previous = Date;
            Date = Date.AddDays(1);
            RunDailyWorldStep();

            if (Date.Period != previous.Period || Date.Month != previous.Month)
            {
                AddLog(LogCategory.Period, $"{Date.Year}年{Date.Month}月{Date.Period.ToChinese()}开始，各地势力重新评估粮秣、治安与人事。旬结算已经发生。");
            }
        }
    }

    private void RunDailyWorldStep()
    {
        if (Date.Day % 5 != 0 || characters.Count == 0)
        {
            return;
        }

        var ordered = characters.Values.OrderBy(item => item.Profile.Id, StringComparer.Ordinal).ToArray();
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

    private void AddLog(LogCategory category, string text) => log.Add(new GameLogEntry(Date, category, text));
}
