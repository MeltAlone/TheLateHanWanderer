using System.Globalization;
using System.Text;
using LateHan.Core;
using LateHan.Persistence;
using LateHan.Scenarios;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

try
{
    var options = CliOptions.Parse(args);
    var scenarioPath = options.ScenarioPath ?? RepositoryPaths.FindDefaultScenario();
    var loaded = new ScenarioLoader().Load(scenarioPath);
    var session = new CliSession(new WorldEngine(loaded.World), new WorldSnapshotStore());

    Console.WriteLine($"已加载 {loaded.World.ScenarioId} {loaded.World.ScenarioVersion}");
    Console.WriteLine($"内容哈希 {loaded.ComputedContentHash}");

    if (options.Commands.Count > 0)
    {
        foreach (var command in options.Commands)
        {
            Console.WriteLine($"> {command}");
            if (!session.Execute(command))
            {
                break;
            }
        }
    }
    else
    {
        session.PrintHelp();
        while (true)
        {
            Console.Write("\n> ");
            var command = Console.ReadLine();
            if (command is null || !session.Execute(command))
            {
                break;
            }
        }
    }

    return 0;
}
catch (ScenarioValidationException exception)
{
    Console.Error.WriteLine("场景加载失败：");
    foreach (var error in exception.Errors)
    {
        Console.Error.WriteLine($"  {error}");
    }

    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

internal sealed class CliSession
{
    private readonly WorldSnapshotStore _snapshotStore;
    private WorldEngine _engine;

    public CliSession(WorldEngine engine, WorldSnapshotStore snapshotStore)
    {
        _engine = engine;
        _snapshotStore = snapshotStore;
    }

    public bool Execute(string input)
    {
        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return true;
        }

        try
        {
            switch (tokens[0].ToLowerInvariant())
            {
                case "look":
                    PrintLook();
                    break;
                case "status":
                    PrintStatus();
                    break;
                case "commitments":
                    PrintCommitments();
                    break;
                case "beliefs":
                    PrintBeliefs(tokens);
                    break;
                case "plans":
                    PrintPlans(tokens);
                    break;
                case "go":
                    ExecuteGo(tokens);
                    break;
                case "enter":
                    ExecuteEnter(tokens);
                    break;
                case "travel":
                    ExecuteTravel(tokens);
                    break;
                case "actions":
                    PrintActions();
                    break;
                case "advance":
                    ExecuteAdvance(tokens);
                    break;
                case "resume":
                    ExecuteResume(tokens);
                    break;
                case "cancel":
                    ExecuteCancel(tokens);
                    break;
                case "give":
                    ExecuteGive(tokens);
                    break;
                case "tell":
                    ExecuteTell(tokens);
                    break;
                case "wait":
                    ExecuteWait(tokens);
                    break;
                case "history":
                    PrintHistory();
                    break;
                case "messages":
                    PrintMessages(tokens);
                    break;
                case "groups":
                    PrintGroups();
                    break;
                case "detail":
                    PrintActorDetail(tokens);
                    break;
                case "dev":
                    ExecuteDeveloperCommand(tokens);
                    break;
                case "save":
                    ExecuteSave(tokens);
                    break;
                case "load":
                    ExecuteLoad(tokens);
                    break;
                case "help":
                    PrintHelp();
                    break;
                case "quit":
                case "exit":
                    return false;
                default:
                    Console.WriteLine("invalid:unknown_command 未知命令；输入 help 查看当前尖峰支持的命令。");
                    break;
            }
        }
        catch (DomainCommandException exception)
        {
            Console.WriteLine($"blocked:{exception.Code} {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.WriteLine($"error:persistence {exception.Message}");
        }

        return true;
    }

    public void PrintHelp()
    {
        Console.WriteLine("当前技术尖峰命令：");
        Console.WriteLine("  look");
        Console.WriteLine("  status");
        Console.WriteLine("  commitments");
        Console.WriteLine("  beliefs [person-id]");
        Console.WriteLine("  plans [person-id]");
        Console.WriteLine("  go <place-id> [walk|horse|with-group]");
        Console.WriteLine("  enter <place-id>");
        Console.WriteLine("  travel start <place-id> [walk|horse|with-group]");
        Console.WriteLine("  actions");
        Console.WriteLine("  advance <action-id>");
        Console.WriteLine("  resume <action-id> [walk|horse|with-group]");
        Console.WriteLine("  cancel <action-id>");
        Console.WriteLine("  give <item-id> to <person-id>");
        Console.WriteLine("  tell <person-id> <proposition-id>");
        Console.WriteLine("  wait <minutes|Nh|Nm>");
        Console.WriteLine("  history");
        Console.WriteLine("  messages [person-id]");
        Console.WriteLine("  groups");
        Console.WriteLine("  detail [person-id]");
        Console.WriteLine("  dev queue");
        Console.WriteLine("  dev rng <domain> <entity-id> [count]");
        Console.WriteLine("  dev schedule <minute> <phase> <subject-id> <kind> [interrupt]");
        Console.WriteLine("  dev interrupt-travel <action-id> <minute> <reason>");
        Console.WriteLine("  dev promote <group-id> [l0|l1|l2]");
        Console.WriteLine("  dev demote <person-id>");
        Console.WriteLine("  dev detail-dirty");
        Console.WriteLine("  dev rebalance-detail [dirty|person-id]");
        Console.WriteLine("  save <path>");
        Console.WriteLine("  load <path>");
        Console.WriteLine("  quit");
    }

    private void PrintLook()
    {
        var view = _engine.Look();
        Console.WriteLine($"时间：开局后 {view.Minute} 分钟");
        Console.WriteLine($"地点：{view.PlaceName} [{view.PlaceId}]");
        if (view.VisibleActors.Count == 0)
        {
            Console.WriteLine("此处没有你能直接辨认的其他人物。");
            return;
        }

        Console.WriteLine("可见人物：");
        foreach (var actor in view.VisibleActors)
        {
            Console.WriteLine($"  {actor.Name} [{actor.Id}]");
        }
    }

    private void PrintStatus()
    {
        var view = _engine.Status();
        Console.WriteLine($"{view.ActorName} [{view.ActorId}]");
        Console.WriteLine($"开局后 {view.Minute} 分钟；{view.PlaceName} [{view.PlaceId}]");
        Console.WriteLine("持有：");
        foreach (var item in view.HeldItems)
        {
            Console.WriteLine($"  {item.Name} [{item.Id}]");
        }

        Console.WriteLine($"开放承诺：{view.OpenCommitments.Count}");
    }

    private void PrintCommitments()
    {
        var commitments = _engine.Status().OpenCommitments;
        if (commitments.Count == 0)
        {
            Console.WriteLine("没有开放承诺。");
            return;
        }

        foreach (var commitment in commitments)
        {
            Console.WriteLine(
                $"{commitment.Id}: {commitment.Action} {commitment.TargetId} -> {commitment.RecipientId}; " +
                $"期限 {commitment.DueMinute}; 状态 {commitment.Status}");
        }
    }

    private void PrintBeliefs(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            Console.WriteLine("invalid:syntax usage: beliefs [person-id]");
            return;
        }

        var holderId = tokens.Length == 2 ? tokens[1] : _engine.State.PlayerActorId;
        var beliefs = _engine.State.Beliefs.Values
            .Where(item => string.Equals(item.HolderId, holderId, StringComparison.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (beliefs.Length == 0)
        {
            Console.WriteLine($"no beliefs for {holderId}");
            return;
        }

        var conflictTopics = _engine.GetBeliefConflicts(holderId)
            .Select(item => item.TopicId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var belief in beliefs)
        {
            var proposition = _engine.State.Propositions[belief.PropositionId];
            Console.WriteLine(
                $"{belief.Id} confidence={belief.ConfidenceBp} source={belief.Source} " +
                $"acquired={belief.AcquiredAtMinute} proposition={belief.PropositionId} " +
                $"topic={proposition.TopicId} stance={proposition.Stance} " +
                $"conflict={conflictTopics.Contains(proposition.TopicId).ToString().ToLowerInvariant()}");
        }
    }

    private void PrintPlans(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            Console.WriteLine("invalid:syntax usage: plans [person-id]");
            return;
        }

        var ownerId = tokens.Length == 2 ? tokens[1] : _engine.State.PlayerActorId;
        var plans = _engine.State.Plans.Values
            .Where(item => string.Equals(item.OwnerId, ownerId, StringComparison.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (plans.Length == 0)
        {
            Console.WriteLine($"no plans for {ownerId}");
            return;
        }

        foreach (var plan in plans)
        {
            Console.WriteLine(
                $"{plan.Id} status={plan.Status.ToString().ToLowerInvariant()} stage={plan.Stage} " +
                $"action={plan.ActiveActionId ?? "-"} next={plan.NextEvaluationMinute}");
        }
    }

    private void ExecuteGo(string[] tokens)
    {
        if (tokens.Length is < 2 or > 3)
        {
            Console.WriteLine("invalid:syntax 用法：go <place-id> [walk|horse|with-group]");
            return;
        }

        var mode = tokens.Length == 3 ? ParseMode(tokens[2]) : TravelMode.Walk;
        var result = _engine.Move(_engine.State.PlayerActorId, tokens[1], mode);
        if (result.Status == ActionStatus.Interrupted)
        {
            Console.WriteLine(
                $"旅行被中断；耗时 {result.EndMinute - result.StartMinute} 分钟。" +
                $"使用 actions 查看并用 resume 恢复。");
            return;
        }

        Console.WriteLine($"抵达；耗时 {result.EndMinute - result.StartMinute} 分钟。事件 {result.Events[^1].Id}");
    }

    private void ExecuteEnter(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax usage: enter <place-id>");
            return;
        }

        var result = _engine.Enter(_engine.State.PlayerActorId, tokens[1]);
        Console.WriteLine(
            $"access status={result.Status.ToString().ToLowerInvariant()} " +
            $"minute={result.EndMinute} event={result.Events[^1].Id}");
    }

    private void ExecuteTravel(string[] tokens)
    {
        if (tokens.Length is < 3 or > 4 || !string.Equals(tokens[1], "start", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("invalid:syntax 用法：travel start <place-id> [walk|horse|with-group]");
            return;
        }

        var mode = tokens.Length == 4 ? ParseMode(tokens[3]) : TravelMode.Walk;
        var action = _engine.BeginTravel(_engine.State.PlayerActorId, tokens[2], mode);
        Console.WriteLine($"已开始旅行 {action.Id}；使用 advance {action.Id} 推进到完成或中断。");
    }

    private void PrintActions()
    {
        if (_engine.State.Actions.Count == 0)
        {
            Console.WriteLine("尚无行动实例。");
            return;
        }

        foreach (var action in _engine.State.Actions.Values)
        {
            Console.WriteLine(
                $"{action.Id} actor={action.ActorId} kind={action.Kind.ToString().ToLowerInvariant()} " +
                $"status={action.Status.ToString().ToLowerInvariant()} phase={action.Phase} " +
                $"elapsed={action.Travel.ElapsedMinutes} route={action.Travel.CurrentLeg.RouteId} " +
                $"progress={action.Travel.CurrentLegProgressQ1000}/1000");
        }
    }

    private void ExecuteAdvance(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax 用法：advance <action-id>");
            return;
        }

        var result = _engine.AdvanceAction(tokens[1]);
        Console.WriteLine(
            $"行动 {tokens[1]} 状态 {result.Status.ToString().ToLowerInvariant()}；" +
            $"当前为开局后 {_engine.State.CurrentMinute} 分钟。");
    }

    private void ExecuteResume(string[] tokens)
    {
        if (tokens.Length is < 2 or > 3)
        {
            Console.WriteLine("invalid:syntax 用法：resume <action-id> [walk|horse|with-group]");
            return;
        }

        var mode = tokens.Length == 3 ? ParseMode(tokens[2]) : TravelMode.Walk;
        var result = _engine.ResumeTravel(tokens[1], mode);
        Console.WriteLine(
            $"行动 {tokens[1]} 状态 {result.Status.ToString().ToLowerInvariant()}；" +
            $"当前为开局后 {_engine.State.CurrentMinute} 分钟。");
    }

    private void ExecuteCancel(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax 用法：cancel <action-id>");
            return;
        }

        var result = _engine.CancelAction(tokens[1]);
        Console.WriteLine($"行动 {tokens[1]} 状态 {result.Status.ToString().ToLowerInvariant()}。");
    }

    private void ExecuteGive(string[] tokens)
    {
        if (tokens.Length != 4 || !string.Equals(tokens[2], "to", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("invalid:syntax 用法：give <item-id> to <person-id>");
            return;
        }

        var result = _engine.Deliver(_engine.State.PlayerActorId, tokens[1], tokens[3]);
        Console.WriteLine($"已经实际交付；耗时 {result.EndMinute - result.StartMinute} 分钟。");
    }

    private void ExecuteTell(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            Console.WriteLine("invalid:syntax 用法：tell <person-id> <proposition-id>");
            return;
        }

        var result = _engine.Tell(_engine.State.PlayerActorId, tokens[1], tokens[2]);
        Console.WriteLine($"已经当面陈述；耗时 {result.EndMinute - result.StartMinute} 分钟。");
    }

    private void ExecuteWait(string[] tokens)
    {
        if (tokens.Length != 2 || !TryParseDuration(tokens[1], out var minutes))
        {
            Console.WriteLine("invalid:syntax 用法：wait <minutes|Nh|Nm>");
            return;
        }

        var result = _engine.Wait(minutes);
        if (result.Status == ActionStatus.Interrupted)
        {
            Console.WriteLine($"等待被中断；当前为开局后 {_engine.State.CurrentMinute} 分钟。");
            return;
        }

        Console.WriteLine($"等待结束；当前为开局后 {_engine.State.CurrentMinute} 分钟。");
    }

    private void PrintHistory()
    {
        if (_engine.State.Events.Count == 0)
        {
            Console.WriteLine("尚无运行时事件。");
            return;
        }

        foreach (var worldEvent in _engine.State.Events)
        {
            Console.WriteLine(
                $"{worldEvent.Id} t={worldEvent.Minute} {worldEvent.Type} " +
                $"subjects=[{string.Join(',', worldEvent.SubjectIds)}] causes=[{string.Join(',', worldEvent.CauseIds)}]");
        }

        Console.WriteLine($"事件指纹 {_engine.State.ComputeEventFingerprint()}");
        Console.WriteLine($"回放状态 {(_engine.State.ReplayModified ? "modified" : "canonical")}");
    }

    private void PrintMessages(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            Console.WriteLine("invalid:syntax usage: messages [person-id]");
            return;
        }

        var personId = tokens.Length == 2 ? tokens[1] : _engine.State.PlayerActorId;
        var messages = _engine.State.Messages.Values
            .Where(item => string.Equals(item.SenderId, personId, StringComparison.Ordinal) ||
                           string.Equals(item.RecipientId, personId, StringComparison.Ordinal))
            .OrderBy(item => item.CreatedAtMinute)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (messages.Length == 0)
        {
            Console.WriteLine($"no messages for {personId}");
            return;
        }

        foreach (var message in messages)
        {
            Console.WriteLine(
                $"{message.Id} {message.SenderId}->{message.RecipientId} " +
                $"source_proposition={message.SourcePropositionId} proposition={message.PropositionId} " +
                $"confidence={message.ConfidenceBp} distorted={message.WasDistorted.ToString().ToLowerInvariant()} " +
                $"parent={message.ParentMessageId ?? "-"} rule={message.PropagationRuleVersion}");
        }
    }

    private void PrintGroups()
    {
        foreach (var group in _engine.State.Groups.Values)
        {
            Console.WriteLine(
                $"{group.Id} count={group.Count} location={group.LocationId} " +
                $"detail=l3 profile={group.PromotionProfileId}");
        }
    }

    private void PrintActorDetail(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            Console.WriteLine("invalid:syntax usage: detail [person-id]");
            return;
        }

        var actorId = tokens.Length == 2 ? tokens[1] : _engine.State.PlayerActorId;
        if (!_engine.State.Actors.TryGetValue(actorId, out var actor))
        {
            throw new DomainCommandException("unknown_actor", $"Unknown actor '{actorId}'.");
        }

        Console.WriteLine(
            $"{actor.Id} detail={actor.DetailLevel.ToString().ToLowerInvariant()} " +
            $"temporary={actor.IsTemporaryPromotion.ToString().ToLowerInvariant()} " +
            $"dirty={_engine.State.DetailDirtyActorIds.Contains(actor.Id).ToString().ToLowerInvariant()} " +
            $"from={actor.PromotedFromGroupId ?? "-"} seed={actor.IdentitySeedHex ?? "-"}");
    }

    private void ExecuteDeveloperCommand(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            Console.WriteLine("invalid:syntax 用法：dev <queue|rng|schedule|interrupt-travel|promote|demote|detail-dirty|rebalance-detail> ...");
            return;
        }

        switch (tokens[1].ToLowerInvariant())
        {
            case "queue":
                PrintScheduledQueue(tokens);
                break;
            case "rng":
                PreviewRandomStream(tokens);
                break;
            case "schedule":
                ScheduleDeveloperEvent(tokens);
                break;
            case "interrupt-travel":
                ScheduleTravelInterruption(tokens);
                break;
            case "promote":
                PromoteGroupMember(tokens);
                break;
            case "demote":
                DemoteActor(tokens);
                break;
            case "rebalance-detail":
                RebalanceDetailLevels(tokens);
                break;
            case "detail-dirty":
                PrintDirtyDetailActors(tokens);
                break;
            default:
                Console.WriteLine("invalid:syntax 用法：dev <queue|rng|schedule|interrupt-travel|promote|demote|detail-dirty|rebalance-detail> ...");
                break;
        }
    }

    private void PromoteGroupMember(string[] tokens)
    {
        if (tokens.Length is < 3 or > 4)
        {
            Console.WriteLine("invalid:syntax usage: dev promote <group-id> [l0|l1|l2]");
            return;
        }

        var detail = tokens.Length == 4 ? ParseDetailLevel(tokens[3]) : SimulationDetailLevel.L0;
        var result = _engine.PromoteGroupMember(tokens[2], detailLevel: detail);
        Console.WriteLine(
            $"promoted {result.Actor.Id} from={tokens[2]} detail={result.Actor.DetailLevel.ToString().ToLowerInvariant()} " +
            $"seed={result.Actor.IdentitySeedHex}");
    }

    private void DemoteActor(string[] tokens)
    {
        if (tokens.Length != 3)
        {
            Console.WriteLine("invalid:syntax usage: dev demote <person-id>");
            return;
        }

        var result = _engine.DemotePromotedActor(tokens[2]);
        Console.WriteLine($"demoted {tokens[2]} event={result.Id}");
    }

    private void RebalanceDetailLevels(string[] tokens)
    {
        if (tokens.Length is < 2 or > 3)
        {
            Console.WriteLine("invalid:syntax usage: dev rebalance-detail [dirty|person-id]");
            return;
        }

        var result = tokens.Length == 3 && string.Equals(tokens[2], "dirty", StringComparison.OrdinalIgnoreCase)
            ? _engine.RebalanceDirtyActorDetailLevels()
            : _engine.RebalanceActorDetailLevels(tokens.Length == 3 ? new[] { tokens[2] } : null);
        Console.WriteLine(
            $"detail_rebalanced policy={SimulationDetailPolicy.Version} changed={result.Events.Count} " +
            $"l0={result.Assessments.Count(item => item.RecommendedLevel == SimulationDetailLevel.L0)} " +
            $"l1={result.Assessments.Count(item => item.RecommendedLevel == SimulationDetailLevel.L1)} " +
            $"l2={result.Assessments.Count(item => item.RecommendedLevel == SimulationDetailLevel.L2)}");
    }

    private void PrintDirtyDetailActors(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax usage: dev detail-dirty");
            return;
        }

        Console.WriteLine($"detail_dirty count={_engine.State.DetailDirtyActorIds.Count}");
        foreach (var actorId in _engine.State.DetailDirtyActorIds)
        {
            Console.WriteLine($"  {actorId}");
        }
    }

    private void PrintScheduledQueue(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax 用法：dev queue");
            return;
        }

        if (_engine.State.ScheduledEvents.Count == 0)
        {
            Console.WriteLine("事件队列为空。");
            return;
        }

        foreach (var item in _engine.State.ScheduledEvents)
        {
            Console.WriteLine(
                $"{item.Id} t={item.DueMinute} {ToPhaseId(item.Phase)} {item.Kind} " +
                $"subject={item.StableSubjectId} interrupt={item.InterruptsPlayer.ToString().ToLowerInvariant()}");
        }
    }

    private void PreviewRandomStream(string[] tokens)
    {
        if (tokens.Length is < 4 or > 5 ||
            (tokens.Length == 5 && !int.TryParse(tokens[4], NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            Console.WriteLine("invalid:syntax 用法：dev rng <domain> <entity-id> [count]");
            return;
        }

        var count = tokens.Length == 5
            ? int.Parse(tokens[4], NumberStyles.None, CultureInfo.InvariantCulture)
            : 1;
        try
        {
            var values = _engine.State.RandomStreams.PreviewUInt64(tokens[2], tokens[3], count);
            Console.WriteLine($"{tokens[2]}:{tokens[3]} preview=[{string.Join(',', values)}]");
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"invalid:rng {exception.Message}");
        }
    }

    private void ScheduleDeveloperEvent(string[] tokens)
    {
        if (tokens.Length is < 6 or > 7 ||
            !long.TryParse(tokens[2], NumberStyles.None, CultureInfo.InvariantCulture, out var minute) ||
            !TryParsePhase(tokens[3], out var phase) ||
            (tokens.Length == 7 && !string.Equals(tokens[6], "interrupt", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("invalid:syntax 用法：dev schedule <minute> <phase> <subject-id> <kind> [interrupt]");
            return;
        }

        var interrupts = tokens.Length == 7;
        var scheduled = _engine.ScheduleExternalIntervention(
            minute,
            phase,
            tokens[4],
            tokens[5],
            interruptsPlayer: interrupts);
        Console.WriteLine($"已排定 {scheduled.Id}；回放已标记为 modified。");
    }

    private void ScheduleTravelInterruption(string[] tokens)
    {
        if (tokens.Length != 5 ||
            !long.TryParse(tokens[3], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
        {
            Console.WriteLine("invalid:syntax 用法：dev interrupt-travel <action-id> <minute> <reason>");
            return;
        }

        var scheduled = _engine.ScheduleExternalTravelInterruption(tokens[2], minute, tokens[4]);
        Console.WriteLine($"已排定旅行中断 {scheduled.Id}；回放已标记为 modified。");
    }

    private void ExecuteSave(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax 用法：save <path>");
            return;
        }

        _snapshotStore.Save(_engine.State, tokens[1]);
        Console.WriteLine($"已保存到 {Path.GetFullPath(tokens[1])}");
    }

    private void ExecuteLoad(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Console.WriteLine("invalid:syntax 用法：load <path>");
            return;
        }

        _engine = new WorldEngine(_snapshotStore.Load(tokens[1]));
        Console.WriteLine($"已加载；当前为开局后 {_engine.State.CurrentMinute} 分钟。");
    }

    private static TravelMode ParseMode(string value) => value switch
    {
        "walk" => TravelMode.Walk,
        "horse" => TravelMode.Horse,
        "with-group" => TravelMode.WithGroup,
        _ => throw new DomainCommandException("unknown_travel_mode", $"Unknown travel mode '{value}'."),
    };

    private static SimulationDetailLevel ParseDetailLevel(string value) => value.ToLowerInvariant() switch
    {
        "l0" => SimulationDetailLevel.L0,
        "l1" => SimulationDetailLevel.L1,
        "l2" => SimulationDetailLevel.L2,
        _ => throw new DomainCommandException("unknown_detail_level", $"Unknown detail level '{value}'."),
    };

    private static bool TryParsePhase(string value, out ScheduledEventPhase phase)
    {
        phase = value switch
        {
            "death_or_removal" => ScheduledEventPhase.DeathOrRemoval,
            "access_and_control_change" => ScheduledEventPhase.AccessAndControlChange,
            "arrival_and_departure" => ScheduledEventPhase.ArrivalAndDeparture,
            "delivery_and_transfer" => ScheduledEventPhase.DeliveryAndTransfer,
            "perception_and_belief" => ScheduledEventPhase.PerceptionAndBelief,
            "plan_evaluation" => ScheduledEventPhase.PlanEvaluation,
            "summary_and_notification" => ScheduledEventPhase.SummaryAndNotification,
            _ => (ScheduledEventPhase)(-1),
        };
        return Enum.IsDefined(phase);
    }

    private static string ToPhaseId(ScheduledEventPhase phase) => phase switch
    {
        ScheduledEventPhase.DeathOrRemoval => "death_or_removal",
        ScheduledEventPhase.AccessAndControlChange => "access_and_control_change",
        ScheduledEventPhase.ArrivalAndDeparture => "arrival_and_departure",
        ScheduledEventPhase.DeliveryAndTransfer => "delivery_and_transfer",
        ScheduledEventPhase.PerceptionAndBelief => "perception_and_belief",
        ScheduledEventPhase.PlanEvaluation => "plan_evaluation",
        ScheduledEventPhase.SummaryAndNotification => "summary_and_notification",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    private static bool TryParseDuration(string value, out long minutes)
    {
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
            return minutes > 0;
        }

        if (value.EndsWith('h') &&
            long.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
        {
            minutes = hours * 60;
            return minutes > 0;
        }

        if (value.EndsWith('m') &&
            long.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes))
        {
            return minutes > 0;
        }

        minutes = 0;
        return false;
    }
}

internal sealed record CliOptions(string? ScenarioPath, IReadOnlyList<string> Commands)
{
    public static CliOptions Parse(string[] args)
    {
        string? scenarioPath = null;
        var commands = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--scenario" when index + 1 < args.Length:
                    scenarioPath = args[++index];
                    break;
                case "--command" when index + 1 < args.Length:
                    commands.Add(args[++index]);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option '{args[index]}'.");
            }
        }

        return new CliOptions(scenarioPath, commands);
    }
}

internal static class RepositoryPaths
{
    public static string FindDefaultScenario()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "data", "scenarios", "189-luoyang-crisis");
                if (File.Exists(Path.Combine(candidate, "manifest.json")))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate data/scenarios/189-luoyang-crisis. Use --scenario <path>.");
    }
}
