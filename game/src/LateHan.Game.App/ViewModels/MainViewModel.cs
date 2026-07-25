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
    public MapNodeViewModel(
        Settlement settlement,
        SettlementState localState,
        SettlementResourceLedger ledger,
        bool isCurrent,
        Road? road,
        RoadState? roadState,
        Action<string> travel)
    {
        Id = settlement.Id;
        Name = settlement.Name;
        Region = settlement.RegionName;
        Coordinate = $"{settlement.Coordinate.X}, {settlement.Coordinate.Y}";
        IsCurrent = isCurrent;
        IsReachable = road is not null;
        Status = isCurrent ? "当前位置" : road is null ? "尚无直达道路" : $"{road.TravelDays}日可达 · 风险 {roadState!.Risk}";
        Condition = $"治安 {localState.Security} · 粮价 {localState.GrainPrice}\n粮储 {ledger.Grain} · 府库 {ledger.Treasury}";
        TravelCommand = new RelayCommand(() => travel(Id), () => IsReachable);
    }

    public string Id { get; }

    public string Name { get; }

    public string Region { get; }

    public string Coordinate { get; }

    public string Status { get; }

    public string Condition { get; }

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
    public CharacterViewModel(CharacterState state, RelationshipState relationship, Action<CharacterViewModel> select)
    {
        Id = state.Profile.Id;
        Name = relationship.Recognition == RecognitionLevel.Unknown ? $"一名{state.Profile.Identity}" : state.Profile.Name;
        HistoricalName = state.Profile.Name;
        Identity = state.Profile.Identity;
        Affiliation = state.Profile.Affiliation ?? "无明确归属";
        Recognition = RecognitionName(relationship.Recognition);
        Favor = relationship.Favor;
        Trust = relationship.Trust;
        Obligation = relationship.Obligation;
        Traits = relationship.Recognition >= RecognitionLevel.Met
            ? string.Join("、", state.Profile.Traits)
            : "尚未通过相处了解";
        Motivations = relationship.Recognition >= RecognitionLevel.Acquainted
            ? string.Join("；", state.Profile.Motivations)
            : "尚不清楚";
        var ability = state.Profile.Abilities;
        AbilitySummary = relationship.Recognition >= RecognitionLevel.Met
            ? $"统率 {ability.Command}　武艺 {ability.Martial}　智略 {ability.Strategy}\n" +
                $"政务 {ability.Administration}　交涉 {ability.Diplomacy}　学识 {ability.Learning}"
            : "能力尚未掌握";
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Id { get; }

    public string Name { get; }

    public string HistoricalName { get; }

    public string Identity { get; }

    public string Affiliation { get; }

    public string Recognition { get; }

    public int Favor { get; }

    public int Trust { get; }

    public int Obligation { get; }

    public string RelationshipSummary => $"{Recognition}　好感 {Favor}　信任 {Trust}";

    public string Traits { get; }

    public string Motivations { get; }

    public string AbilitySummary { get; }

    public IRelayCommand SelectCommand { get; }

    private static string RecognitionName(RecognitionLevel recognition) => recognition switch
    {
        RecognitionLevel.Unknown => "陌生",
        RecognitionLevel.HeardOf => "有所耳闻",
        RecognitionLevel.Met => "见过",
        RecognitionLevel.Acquainted => "相识",
        RecognitionLevel.Trusted => "信赖",
        _ => "未知",
    };
}

public sealed class ContextActionViewModel
{
    public ContextActionViewModel(AvailableAction action, Action<AvailableAction> execute)
    {
        Action = action;
        Title = action.DurationDays > 0 ? $"{action.Title}（{action.DurationDays}日）" : action.Title;
        Description = action.IsEnabled
            ? action.Description
            : $"{action.Description}\n受阻：{action.BlockReason}";
        IsEnabled = action.IsEnabled;
        ExecuteCommand = new RelayCommand(() => execute(action), () => action.IsEnabled);
    }

    public AvailableAction Action { get; }

    public string Title { get; }

    public string Description { get; }

    public bool IsEnabled { get; }

    public IRelayCommand ExecuteCommand { get; }
}

public sealed class CareerOpportunityViewModel
{
    public CareerOpportunityViewModel(CareerOpportunity opportunity, Action<CareerOpportunity> execute)
    {
        Opportunity = opportunity;
        var cost = opportunity.MoneyCost > 0 ? $"，耗{opportunity.MoneyCost}钱" : string.Empty;
        var reward = opportunity.RewardMoney > 0 ? $"，得{opportunity.RewardMoney}钱" : string.Empty;
        var duration = opportunity.IsAcceptance ? "立即立约" : $"{opportunity.DurationDays}日";
        Title = $"{opportunity.Title}（{duration}{cost}{reward}）";
        Description = opportunity.IsEnabled
            ? opportunity.Description
            : $"{opportunity.Description}\n受阻：{opportunity.BlockReason}";
        IsEnabled = opportunity.IsEnabled;
        ExecuteCommand = new RelayCommand(() => execute(opportunity), () => opportunity.IsEnabled);
    }

    public CareerOpportunity Opportunity { get; }

    public string Title { get; }

    public string Description { get; }

    public bool IsEnabled { get; }

    public IRelayCommand ExecuteCommand { get; }
}

public sealed record HistoricalBranchViewModel(string Title, string Status, string Timing, string Description);

public sealed record CommitmentViewModel(string CharacterName, string DueDate, string Place, string Status);

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

    public ObservableCollection<ContextActionViewModel> ContextActions { get; } = [];

    public ObservableCollection<CareerOpportunityViewModel> CareerOpportunities { get; } = [];

    public ObservableCollection<HistoricalBranchViewModel> HistoricalBranches { get; } = [];

    public ObservableCollection<CommitmentViewModel> Commitments { get; } = [];

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
    public partial string SettlementStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CareerGoalText { get; set; } = string.Empty;

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
        Replace(ContextActions, session?.GetActionsForCharacter(character.Id)
            .Select(item => new ContextActionViewModel(item, ExecuteInteraction)) ?? []);
        StatusMessage = $"你开始留意{character.Name}。下方行动由身份、认识、日程、地点和已知话题共同决定。";
    }

    private void ExecuteInteraction(AvailableAction action) =>
        RunAction(current => current.ExecuteInteraction(action.Id));

    private void ExecuteCareerOpportunity(CareerOpportunity opportunity) =>
        RunAction(current => current.ExecuteCareerOpportunity(opportunity.Id));

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
            $"声望 {session.Career.Reputation}　人脉 {session.Career.Network}　生计压力 {session.Career.FinancialPressure}";
        CareerGoalText = $"阶段目标：{session.Career.Goal.Title}　" +
            $"进度 {session.Career.Goal.Progress}/{session.Career.Goal.Target}　" +
            $"期限 {session.Career.Goal.Deadline}　{CareerGoalStatusName(session.Career.Goal.Status)}";
        SettlementName = $"{session.CurrentSettlement.Name}｜{session.CurrentSettlement.RegionName}";
        SettlementDescription = session.CurrentSettlement.Description;
        LocationDescription = session.CurrentUrbanLocation.Description;
        var localState = session.CurrentSettlementState;
        var ledger = session.CurrentResourceLedger;
        var localOrganizations = session.OrganizationsAtCurrentLocation;
        var organizationText = localOrganizations.Count == 0
            ? "当前地点没有常驻组织"
            : $"当前组织：{string.Join("、", localOrganizations.Select(item => $"{item.Name}（钱财 {item.Treasury}／粮秣 {item.Grain}／人手 {item.Personnel}／影响 {item.Influence}）"))}";
        SettlementStateText = $"治安 {localState.Security}　粮价 {localState.GrainPrice}　" +
            $"繁荣 {localState.Prosperity}　官府控制 {localState.GovernmentControl}\n" +
            $"资源账本（0–100，D级玩法标定）：人口 {ledger.Population}　粮储 {ledger.Grain}　府库 {ledger.Treasury}　可用人力 {ledger.Labor}\n" +
            organizationText;

        var destinations = session.AvailableDestinations.ToDictionary(item => item.Destination.Id, item => item.Road, StringComparer.Ordinal);
        Replace(MapNodes, scenario.Map.Settlements.Select(item => new MapNodeViewModel(
            item,
            session.StateOf(item.Id),
            session.ResourceLedgers[item.Id],
            item.Id == session.Player.SettlementId,
            destinations.GetValueOrDefault(item.Id),
            destinations.TryGetValue(item.Id, out var road) ? session.StateOfRoad(road.Id) : null,
            Travel)));
        Replace(UrbanLocations, session.CurrentSettlement.UrbanLocations.Select(item => new UrbanLocationViewModel(
            item,
            item.Id == session.Player.UrbanLocationId,
            EnterLocation)));
        Replace(LocalCharacters, session.CharactersAtCurrentLocation.Select(item => new CharacterViewModel(
            item,
            session.ConnectionWith(item.Profile.Id),
            SelectCharacter)));
        var meetingCommitments = session.Commitments
            .Where(item => item.Status == CommitmentStatus.Scheduled)
            .Select(item =>
            {
                var character = scenario.Characters.Single(character => character.Id == item.CharacterId);
                var settlement = scenario.Map.GetSettlement(item.SettlementId);
                var location = settlement.UrbanLocations.Single(location => location.Id == item.UrbanLocationId);
                return new CommitmentViewModel(character.Name, item.DueDate.ToString(), $"{settlement.Name}·{location.Name}", "待履行");
            });
        var organizationCommitments = session.OrganizationCommissions
            .Where(item => item.Status == OrganizationCommissionStatus.Accepted)
            .Select(item =>
            {
                var organization = session.Organizations[item.OrganizationId];
                var settlement = scenario.Map.GetSettlement(item.SettlementId);
                var location = settlement.UrbanLocations.Single(location => location.Id == item.UrbanLocationId);
                return new CommitmentViewModel($"{organization.Name}：{item.Title}", item.DueDate.ToString(), $"{settlement.Name}·{location.Name}", "待交付");
            });
        Replace(Commitments, meetingCommitments.Concat(organizationCommitments));
        Replace(CareerOpportunities, session.CareerOpportunities.Select(item =>
            new CareerOpportunityViewModel(item, ExecuteCareerOpportunity)));
        Replace(HistoricalBranches, session.HistoricalBranches.Select(item => new HistoricalBranchViewModel(
            item.Title,
            BranchStatusName(item),
            $"{item.OpensOn} 至 {item.ResolvesOn}",
            item.Status == HistoricalBranchStatus.Resolved ? item.Result : item.Description)));
        Replace(LogEntries, session.Log.Reverse().Take(80).Select(item => new LogEntryViewModel(
            item.Date.ToString(),
            CategoryName(item.Category),
            item.Text)));
        SelectedCharacter = null;
        ContextActions.Clear();
    }

    private static string CategoryName(LogCategory category) => category switch
    {
        LogCategory.Personal => "个人",
        LogCategory.Career => "生涯",
        LogCategory.Travel => "行旅",
        LogCategory.Encounter => "交游",
        LogCategory.World => "天下",
        LogCategory.Period => "旬报",
        LogCategory.Commitment => "约定",
        _ => "记录",
    };

    private static string CareerGoalStatusName(CareerGoalStatus status) => status switch
    {
        CareerGoalStatus.Active => "进行中",
        CareerGoalStatus.Completed => "已完成",
        CareerGoalStatus.Failed => "已失败",
        _ => "未知",
    };

    private static string BranchStatusName(HistoricalBranchState branch) => branch.Status switch
    {
        HistoricalBranchStatus.Upcoming => "尚未发生",
        HistoricalBranchStatus.Active => "正在发展",
        HistoricalBranchStatus.Resolved when branch.Outcome == HistoricalBranchOutcome.PlayerInfluenced => "已受你影响",
        HistoricalBranchStatus.Resolved => "已自行收束",
        _ => "未知",
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
