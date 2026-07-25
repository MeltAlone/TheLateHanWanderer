using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LateHan.Game.Content;
using LateHan.Game.Domain;
using LateHan.Game.Persistence;
using LateHan.Game.Simulation;

namespace LateHan.Game.App.ViewModels;

public sealed class BackgroundOptionViewModel
{
    public BackgroundOptionViewModel(PlayerBackground background, Action<PlayerBackground> start)
    {
        Background = background;
        StartCommand = new RelayCommand(() => start(background));
    }

    public PlayerBackground Background { get; }

    public string Name => Background.Name;

    public string Description => Background.Description;

    public string AbilitySummary => $"统率 {Background.StartingAbilities.Command}  武艺 {Background.StartingAbilities.Martial}  " +
        $"智略 {Background.StartingAbilities.Strategy}  政务 {Background.StartingAbilities.Administration}  " +
        $"交涉 {Background.StartingAbilities.Diplomacy}  学识 {Background.StartingAbilities.Learning}";

    public IRelayCommand StartCommand { get; }
}

public sealed class MapNodeViewModel
{
    public MapNodeViewModel(Settlement settlement, bool isCurrent, Road? road, Action<string> travel)
    {
        Id = settlement.Id;
        Name = settlement.Name;
        Region = settlement.RegionName;
        Coordinate = $"{settlement.Coordinate.X}, {settlement.Coordinate.Y}";
        IsCurrent = isCurrent;
        IsReachable = road is not null;
        Status = isCurrent ? "当前位置" : road is null ? "尚无直达道路" : $"{road.TravelDays}日可达";
        TravelCommand = new RelayCommand(() => travel(Id), () => IsReachable);
    }

    public string Id { get; }

    public string Name { get; }

    public string Region { get; }

    public string Coordinate { get; }

    public string Status { get; }

    public bool IsCurrent { get; }

    public bool IsReachable { get; }

    public IRelayCommand TravelCommand { get; }
}

public sealed class UrbanLocationViewModel
{
    public UrbanLocationViewModel(UrbanLocation location, bool isCurrent, Action<string> enter)
    {
        Id = location.Id;
        Name = location.Name;
        Description = location.Description;
        IsCurrent = isCurrent;
        EnterCommand = new RelayCommand(() => enter(Id), () => !IsCurrent);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsCurrent { get; }

    public string Status => IsCurrent ? "正在此处" : "前往";

    public IRelayCommand EnterCommand { get; }
}

public sealed class CharacterViewModel
{
    public CharacterViewModel(CharacterState state, int relationship, Action<CharacterViewModel> select)
    {
        Id = state.Profile.Id;
        Name = state.Profile.Name;
        Identity = state.Profile.Identity;
        Affiliation = state.Profile.Affiliation ?? "无明确归属";
        Relationship = relationship;
        Traits = string.Join("、", state.Profile.Traits);
        Motivations = string.Join("；", state.Profile.Motivations);
        var ability = state.Profile.Abilities;
        AbilitySummary = $"统率 {ability.Command}　武艺 {ability.Martial}　智略 {ability.Strategy}\n" +
            $"政务 {ability.Administration}　交涉 {ability.Diplomacy}　学识 {ability.Learning}";
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Id { get; }

    public string Name { get; }

    public string Identity { get; }

    public string Affiliation { get; }

    public int Relationship { get; }

    public string Traits { get; }

    public string Motivations { get; }

    public string AbilitySummary { get; }

    public IRelayCommand SelectCommand { get; }
}

public sealed record LogEntryViewModel(string Date, string Category, string Text);

public partial class MainViewModel : ViewModelBase
{
    private readonly GameScenario scenario = DemoScenarioFactory.Create();
    private readonly IGameSaveStore saveStore = new JsonGameSaveStore();
    private readonly string savePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HanMoFusheng",
        "savegame.json");
    private GameSession? session;

    public MainViewModel()
    {
        ScenarioName = scenario.Name;
        ScenarioDescription = scenario.Description;
        Backgrounds = scenario.Backgrounds
            .Select(item => new BackgroundOptionViewModel(item, StartGame))
            .ToArray();
        CanLoad = File.Exists(savePath);
    }

    public IReadOnlyList<BackgroundOptionViewModel> Backgrounds { get; }

    public ObservableCollection<MapNodeViewModel> MapNodes { get; } = [];

    public ObservableCollection<UrbanLocationViewModel> UrbanLocations { get; } = [];

    public ObservableCollection<CharacterViewModel> LocalCharacters { get; } = [];

    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = [];

    [ObservableProperty]
    public partial bool IsGameStarted { get; set; }

    [ObservableProperty]
    public partial bool CanLoad { get; set; }

    [ObservableProperty]
    public partial string ScenarioName { get; set; }

    [ObservableProperty]
    public partial string ScenarioDescription { get; set; }

    [ObservableProperty]
    public partial string DateText { get; set; } = "尚未开局";

    [ObservableProperty]
    public partial string PlaceText { get; set; } = "请选择身份";

    [ObservableProperty]
    public partial string PlayerSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SettlementName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SettlementDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LocationDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "选择一个出身，进入中平六年的动态世界。";

    [ObservableProperty]
    public partial CharacterViewModel? SelectedCharacter { get; set; }

    public bool HasSelectedCharacter => SelectedCharacter is not null;

    public bool HasNoSelectedCharacter => SelectedCharacter is null;

    partial void OnSelectedCharacterChanged(CharacterViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCharacter));
        OnPropertyChanged(nameof(HasNoSelectedCharacter));
    }

    [RelayCommand]
    private void GatherInformation() => RunAction(current => current.GatherInformation());

    [RelayCommand]
    private void Train() => RunAction(current => current.Train());

    [RelayCommand]
    private void Work() => RunAction(current => current.Work());

    [RelayCommand]
    private void RestOneDay() => RunAction(current => current.Rest());

    [RelayCommand]
    private void RestTenDays() => RunAction(current => current.Rest(10));

    [RelayCommand]
    private void VisitSelectedCharacter()
    {
        if (SelectedCharacter is null)
        {
            StatusMessage = "请先从当前地点的人物中选择一人。";
            return;
        }

        RunAction(current => current.VisitCharacter(SelectedCharacter.Id));
    }

    [RelayCommand]
    private void SaveGame()
    {
        if (session is null)
        {
            return;
        }

        saveStore.Save(savePath, session.CreateSnapshot());
        CanLoad = true;
        StatusMessage = $"游戏已保存：{savePath}";
    }

    [RelayCommand]
    private void LoadGame()
    {
        try
        {
            session = GameSession.Restore(scenario, saveStore.Load(savePath));
            IsGameStarted = true;
            StatusMessage = "存档已载入。世界从保存的日期继续运行。";
            Refresh();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            StatusMessage = $"载入失败：{exception.Message}";
        }
    }

    private void StartGame(PlayerBackground background)
    {
        session = new GameSession(scenario, background);
        IsGameStarted = true;
        StatusMessage = $"你选择了{background.Name}。先观察周围，再决定去留。";
        Refresh();
    }

    private void Travel(string destinationId) => RunAction(current => current.TravelTo(destinationId));

    private void EnterLocation(string locationId) => RunAction(current => current.EnterUrbanLocation(locationId));

    private void SelectCharacter(CharacterViewModel character)
    {
        SelectedCharacter = character;
        StatusMessage = $"你开始留意{character.Name}。是否拜访，要看你的身份与打算。";
    }

    private void RunAction(Action<GameSession> action)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            action(session);
            StatusMessage = session.Log[^1].Text;
            Refresh();
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void Refresh()
    {
        if (session is null)
        {
            return;
        }

        DateText = session.Date.ToString();
        PlaceText = $"{session.CurrentSettlement.Name} · {session.CurrentUrbanLocation.Name}";
        PlayerSummary = $"{session.Player.Background.Name}　钱财 {session.Player.Money}　" +
            $"学识 {session.Player.Abilities.Learning}";
        SettlementName = $"{session.CurrentSettlement.Name}｜{session.CurrentSettlement.RegionName}";
        SettlementDescription = session.CurrentSettlement.Description;
        LocationDescription = session.CurrentUrbanLocation.Description;

        var destinations = session.AvailableDestinations.ToDictionary(item => item.Destination.Id, item => item.Road, StringComparer.Ordinal);
        Replace(MapNodes, scenario.Map.Settlements.Select(item => new MapNodeViewModel(
            item,
            item.Id == session.Player.SettlementId,
            destinations.GetValueOrDefault(item.Id),
            Travel)));
        Replace(UrbanLocations, session.CurrentSettlement.UrbanLocations.Select(item => new UrbanLocationViewModel(
            item,
            item.Id == session.Player.UrbanLocationId,
            EnterLocation)));
        Replace(LocalCharacters, session.CharactersAtCurrentLocation.Select(item => new CharacterViewModel(
            item,
            session.RelationshipWith(item.Profile.Id),
            SelectCharacter)));
        Replace(LogEntries, session.Log.Reverse().Take(80).Select(item => new LogEntryViewModel(
            item.Date.ToString(),
            CategoryName(item.Category),
            item.Text)));
        SelectedCharacter = null;
    }

    private static string CategoryName(LogCategory category) => category switch
    {
        LogCategory.Personal => "个人",
        LogCategory.Travel => "行旅",
        LogCategory.Encounter => "交游",
        LogCategory.World => "天下",
        LogCategory.Period => "旬报",
        _ => "记录",
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
