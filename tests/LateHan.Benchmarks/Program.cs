using System.Diagnostics;
using LateHan.Core;
using LateHan.Scenarios;

var scenarioDirectory = FindScenarioDirectory();
var loader = new ScenarioLoader();
var workload = args.FirstOrDefault()?.ToLowerInvariant() ?? "delivery";

switch (workload)
{
    case "delivery":
        RunDelivery();
        break;
    case "b1-idle":
        RunIdleWorld();
        break;
    case "b4-lod":
        RunLodRoundTrips();
        break;
    case "b2-city":
        RunCityCrisis();
        break;
    case "b3-messages":
        RunMessageFanout();
        break;
    case "b2-scale":
        RunCityCrisisScale();
        break;
    case "b2-mixed":
        RunMixedCityCrisisScale();
        break;
    case "b3-scale":
        RunMessageTopologyScale();
        break;
    case "b3-conflict":
        RunConflictingMessageScale();
        break;
    case "scale":
        RunCityCrisisScale();
        RunMixedCityCrisisScale();
        RunMessageTopologyScale();
        RunConflictingMessageScale();
        break;
    case "all":
        RunDelivery();
        RunIdleWorld();
        RunCityCrisis();
        RunMessageFanout();
        RunLodRoundTrips();
        break;
    default:
        throw new ArgumentException(
            "Usage: delivery|b1-idle|b2-city|b3-messages|b4-lod|b2-scale|b2-mixed|b3-scale|b3-conflict|scale|all");
}

void RunDelivery()
{
    const int iterations = 100;
    _ = loader.Load(scenarioDirectory);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    string? fingerprint = null;
    for (var index = 0; index < iterations; index++)
    {
        var engine = new WorldEngine(loader.Load(scenarioDirectory).World);
        engine.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        engine.Deliver("person.player_clerk", "item.sealed_note_to_yuan_shao", "person.yuan_shao");
        fingerprint ??= engine.State.ComputeEventFingerprint();
        if (!string.Equals(fingerprint, engine.State.ComputeEventFingerprint(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Deterministic fingerprint mismatch.");
        }
    }

    stopwatch.Stop();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    PrintResult("delivery", iterations, stopwatch, allocatedBytes, fingerprint!, correctness: "deterministic");
}

void RunIdleWorld()
{
    const long thirtyDays = 30 * 24 * 60;
    var engine = new WorldEngine(loader.Load(scenarioDirectory).World);
    var populationEquivalent = engine.State.Actors.Count + engine.State.Groups.Values.Sum(item => item.Count);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    _ = engine.Wait(thirtyDays);
    stopwatch.Stop();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var processedEvents = engine.State.Events.Count;
    PrintResult(
        "b1-idle-30d",
        1,
        stopwatch,
        allocatedBytes,
        engine.State.ComputeEventFingerprint(),
        $"population_equivalent={populationEquivalent} processed_events={processedEvents}");
}

void RunLodRoundTrips()
{
    const int iterations = 1000;
    var engine = new WorldEngine(loader.Load(scenarioDirectory).World);
    var group = engine.State.Groups["group.market_population"];
    var initialCount = group.Count;
    var initialActorCount = engine.State.Actors.Count;
    var initialStateFingerprint = engine.State.ComputeMaterialStateFingerprint();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    for (var index = 0; index < iterations; index++)
    {
        var promoted = engine.PromoteGroupMember(group.Id);
        _ = engine.DemotePromotedActor(promoted.Actor.Id);
    }

    stopwatch.Stop();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var finalStateFingerprint = engine.State.ComputeMaterialStateFingerprint();
    if (group.Count != initialCount ||
        engine.State.Actors.Count != initialActorCount ||
        !string.Equals(initialStateFingerprint, finalStateFingerprint, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("LOD population conservation failed.");
    }

    PrintResult(
        "b4-lod-roundtrip",
        iterations,
        stopwatch,
        allocatedBytes,
        engine.State.ComputeEventFingerprint(),
        $"population_conserved=true material_state_conserved=true promotion_cursor={engine.State.PromotionSequenceCursor}");
}

void RunCityCrisis()
{
    const long twelveHours = 12 * 60;
    const string destinationPlaceId = "place.luoyang.west_market";
    var engine = new WorldEngine(loader.Load(scenarioDirectory).World);
    _ = engine.RebalanceActorDetailLevels();
    var initialPopulation = engine.State.Actors.Count + engine.State.Groups.Values.Sum(item => item.Count);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    var actions = engine.State.Actors.Values
        .Where(item => item.PlaceId?.StartsWith("place.luoyang.", StringComparison.Ordinal) == true)
        .Where(item => !string.Equals(item.PlaceId, destinationPlaceId, StringComparison.Ordinal))
        .OrderBy(item => item.Id, StringComparer.Ordinal)
        .Select(item => engine.BeginTravel(item.Id, destinationPlaceId, TravelMode.Walk))
        .ToArray();
    var startDetail = engine.RebalanceDirtyActorDetailLevels();
    var playerAction = actions.Single(item => item.ActorId == engine.State.PlayerActorId);
    _ = engine.AdvanceAction(playerAction.Id);
    if (engine.State.CurrentMinute < twelveHours)
    {
        _ = engine.Wait(twelveHours - engine.State.CurrentMinute);
    }

    var finalDetail = engine.RebalanceDirtyActorDetailLevels();
    stopwatch.Stop();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var finalPopulation = engine.State.Actors.Count + engine.State.Groups.Values.Sum(item => item.Count);
    if (actions.Any(item => item.Status != ActionStatus.Completed) ||
        actions.Any(item => !string.Equals(
            engine.State.Actors[item.ActorId].PlaceId,
            destinationPlaceId,
            StringComparison.Ordinal)) ||
        finalPopulation != initialPopulation)
    {
        var incomplete = string.Join(',', actions
            .Where(item => item.Status != ActionStatus.Completed)
            .Select(item => $"{item.ActorId}:{item.Status}"));
        var misplaced = string.Join(',', actions
            .Select(item => engine.State.Actors[item.ActorId])
            .Where(item => !string.Equals(item.PlaceId, destinationPlaceId, StringComparison.Ordinal))
            .Select(item => $"{item.Id}:{item.PlaceId ?? item.Transit?.RouteId}"));
        throw new InvalidOperationException(
            $"B2 city crisis invariants failed. incomplete=[{incomplete}] misplaced=[{misplaced}] " +
            $"population={finalPopulation}/{initialPopulation} minute={engine.State.CurrentMinute}");
    }

    PrintResult(
        "b2-city-crisis-mini",
        1,
        stopwatch,
        allocatedBytes,
        engine.State.ComputeEventFingerprint(),
        $"named_actors={engine.State.Actors.Count} city_actors={actions.Length} " +
        $"processed_events={engine.State.Events.Count} " +
        $"detail_assessments={startDetail.Assessments.Count + finalDetail.Assessments.Count} " +
        $"final_detail_changes={finalDetail.Events.Count} " +
        "population_conserved=true");
}

void RunMessageFanout()
{
    var engine = new WorldEngine(loader.Load(scenarioDirectory).World);
    GatherActors(engine, "place.luoyang.west_market");
    var setupWorldMinute = engine.State.CurrentMinute;
    var carrierIds = engine.State.Actors.Keys
        .Where(item => !string.Equals(item, engine.State.PlayerActorId, StringComparison.Ordinal))
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();
    string[] propositions =
    [
        "proposition.palace_credential_required",
        "proposition.north_gate_closed",
        "proposition.north_gate_military_traffic_rising",
    ];
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    foreach (var propositionId in propositions)
    {
        var senderId = engine.State.PlayerActorId;
        foreach (var recipientId in carrierIds)
        {
            _ = engine.Tell(senderId, recipientId, propositionId);
            senderId = recipientId;
        }
    }

    stopwatch.Stop();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var expectedMessages = carrierIds.Length * propositions.Length;
    var linkedMessages = engine.State.Messages.Values.Count(item => item.ParentMessageId is not null);
    if (engine.State.Messages.Count != expectedMessages ||
        linkedMessages != expectedMessages - propositions.Length)
    {
        throw new InvalidOperationException("B3 message lineage invariants failed.");
    }

    PrintResult(
        "b3-message-fanout-mini",
        expectedMessages,
        stopwatch,
        allocatedBytes,
        engine.State.ComputeEventFingerprint(),
        $"carriers={carrierIds.Length} propositions={propositions.Length} setup_world_minute={setupWorldMinute} " +
        $"messages={engine.State.Messages.Count} linked_messages={linkedMessages} lineage_complete=true");
}

static void RunCityCrisisScale()
{
    RunSampledScale(
        "b2-city-crisis-target-population",
        SyntheticScaleWorldFactory.CreateCityCrisisWorld,
        ExecuteCityCrisisScale);
}

static ScaleWorkloadResult ExecuteCityCrisisScale(WorldEngine engine)
{
    const long twelveHours = 12 * 60;
    var initialActorCount = engine.State.Actors.Count;
    var initialGroupCount = engine.State.Groups.Count;
    var initialPopulationEquivalent = PopulationEquivalent(engine.State);
    var initialL0Count = engine.State.Actors.Values.Count(
        actor => actor.DetailLevel == SimulationDetailLevel.L0);
    var initialL1Count = engine.State.Actors.Values.Count(
        actor => actor.DetailLevel == SimulationDetailLevel.L1);
    if (initialL0Count != SyntheticScaleWorldFactory.CityCrisisL0Count ||
        initialL1Count != SyntheticScaleWorldFactory.CityCrisisL1Count ||
        initialActorCount != initialL0Count + initialL1Count)
    {
        throw new InvalidOperationException(
            $"B2 synthetic population is invalid: actors={initialActorCount} " +
            $"l0={initialL0Count} l1={initialL1Count}.");
    }

    var actions = engine.State.Actors.Values
        .Where(actor => !string.Equals(
            actor.PlaceId,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            StringComparison.Ordinal))
        .OrderBy(actor => actor.Id, StringComparer.Ordinal)
        .Select(actor => engine.BeginTravel(
            actor.Id,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            TravelMode.Walk))
        .ToArray();
    var dirtyAtStart = engine.State.DetailDirtyActorIds.ToArray();
    var startDetail = engine.RebalanceDirtyActorDetailLevels();
    AssertDirtyRebalance("B2 start", dirtyAtStart, startDetail);

    var playerAction = actions.Single(action =>
        string.Equals(action.ActorId, engine.State.PlayerActorId, StringComparison.Ordinal));
    _ = engine.AdvanceAction(playerAction.Id);
    if (engine.State.CurrentMinute < twelveHours)
    {
        _ = engine.Wait(twelveHours - engine.State.CurrentMinute);
    }

    var dirtyAtEnd = engine.State.DetailDirtyActorIds.ToArray();
    var finalDetail = engine.RebalanceDirtyActorDetailLevels();
    AssertDirtyRebalance("B2 finish", dirtyAtEnd, finalDetail);

    var finalPopulationEquivalent = PopulationEquivalent(engine.State);
    var allActorsAtDestination = engine.State.Actors.Values.All(actor => string.Equals(
        actor.PlaceId,
        SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
        StringComparison.Ordinal));
    var allActorsAtL0 = engine.State.Actors.Values.All(
        actor => actor.DetailLevel == SimulationDetailLevel.L0);
    var group = engine.State.Groups.Values.Single();
    if (actions.Any(action => action.Status != ActionStatus.Completed) ||
        !allActorsAtDestination ||
        !allActorsAtL0 ||
        engine.State.DetailDirtyActorIds.Count != 0 ||
        finalPopulationEquivalent != initialPopulationEquivalent ||
        engine.State.Actors.Count != initialActorCount ||
        engine.State.Groups.Count != initialGroupCount ||
        group.Count != SyntheticScaleWorldFactory.CityCrisisGroupPopulation)
    {
        throw new InvalidOperationException(
            "B2 target-population invariants failed: " +
            $"completed={actions.Count(action => action.Status == ActionStatus.Completed)}/{actions.Length} " +
            $"at_destination={allActorsAtDestination} all_l0={allActorsAtL0} " +
            $"population={finalPopulationEquivalent}/{initialPopulationEquivalent} " +
            $"group_population={group.Count} dirty={engine.State.DetailDirtyActorIds.Count}.");
    }

    return new ScaleWorkloadResult(
        $"places={engine.State.Places.Count} named_actors={initialActorCount} " +
        $"initial_l0={initialL0Count} initial_l1={initialL1Count} " +
        $"concurrent_travelers={actions.Length} population_equivalent={finalPopulationEquivalent} " +
        $"l3_group_population={group.Count} processed_events={engine.State.Events.Count} " +
        $"detail_assessments={startDetail.Assessments.Count + finalDetail.Assessments.Count} " +
        "incremental_dirty_exact=true population_conserved=true l3_not_expanded=true");
}

static void RunMixedCityCrisisScale()
{
    RunSampledScale(
        "b2-city-crisis-mixed",
        SyntheticScaleWorldFactory.CreateMixedCityCrisisWorld,
        ExecuteMixedCityCrisisScale);
}

static ScaleWorkloadResult ExecuteMixedCityCrisisScale(WorldEngine engine)
{
    const long twelveHours = 12 * 60;
    int[] playerInterruptMinutes = [20, 40];
    int[] remoteTickMinutes = [240, 480, 720];
    var initialActorCount = engine.State.Actors.Count;
    var initialGroupCount = engine.State.Groups.Count;
    var initialPopulationEquivalent = PopulationEquivalent(engine.State);
    var initialL0Count = engine.State.Actors.Values.Count(
        actor => actor.DetailLevel == SimulationDetailLevel.L0);
    var initialL1Count = engine.State.Actors.Values.Count(
        actor => actor.DetailLevel == SimulationDetailLevel.L1);
    var planOwnerIds = Enumerable.Range(0, SyntheticScaleWorldFactory.MixedCityCrisisPlanCount)
        .Select(SyntheticScaleWorldFactory.MixedCityCrisisPlanOwnerId)
        .ToHashSet(StringComparer.Ordinal);
    if (initialL0Count != SyntheticScaleWorldFactory.CityCrisisL0Count ||
        initialL1Count != SyntheticScaleWorldFactory.CityCrisisL1Count ||
        initialActorCount != initialL0Count + initialL1Count ||
        engine.State.Plans.Count != SyntheticScaleWorldFactory.MixedCityCrisisPlanCount)
    {
        throw new InvalidOperationException(
            $"Mixed B2 synthetic world is invalid: actors={initialActorCount} " +
            $"l0={initialL0Count} l1={initialL1Count} plans={engine.State.Plans.Count}.");
    }

    engine.InitializePlans();
    foreach (var minute in playerInterruptMinutes)
    {
        engine.Schedule(
            minute,
            ScheduledEventPhase.SummaryAndNotification,
            SyntheticScaleWorldFactory.RegionalPopulationGroupId,
            "city_crisis_alert",
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            interruptsPlayer: true,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alert_minute"] = minute.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    var initializedRemoteTicks = engine.InitializeRemoteSimulation(cadenceMinutes: 240);
    if (initializedRemoteTicks.Count != 1 || initializedRemoteTicks[0].DueMinute != remoteTickMinutes[0])
    {
        throw new InvalidOperationException("Mixed B2 remote simulation did not initialize exactly once.");
    }

    var actions = engine.State.Actors.Values
        .Where(actor => !string.Equals(
            actor.PlaceId,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            StringComparison.Ordinal))
        .OrderBy(actor => actor.Id, StringComparer.Ordinal)
        .Select(actor => engine.BeginTravel(
            actor.Id,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            TravelMode.Walk))
        .ToArray();
    var dirtyAtStart = engine.State.DetailDirtyActorIds.ToArray();
    var startDetail = engine.RebalanceDirtyActorDetailLevels();
    AssertDirtyRebalance("Mixed B2 start", dirtyAtStart, startDetail);

    var playerAction = actions.Single(action =>
        string.Equals(action.ActorId, engine.State.PlayerActorId, StringComparison.Ordinal));
    var firstAdvanceStopwatch = Stopwatch.StartNew();
    var firstAdvance = engine.AdvanceAction(playerAction.Id);
    firstAdvanceStopwatch.Stop();
    var firstResumeStopwatch = Stopwatch.StartNew();
    var firstResume = engine.ResumeTravel(playerAction.Id, TravelMode.Walk);
    firstResumeStopwatch.Stop();
    var secondResume = engine.ResumeTravel(playerAction.Id, TravelMode.Walk);
    if (firstAdvance.Status != ActionStatus.Interrupted || firstAdvance.EndMinute != playerInterruptMinutes[0] ||
        firstResume.Status != ActionStatus.Interrupted || firstResume.EndMinute != playerInterruptMinutes[1] ||
        secondResume.Status != ActionStatus.Completed)
    {
        throw new InvalidOperationException(
            "Mixed B2 player interruption protocol failed: " +
            $"first={firstAdvance.Status}@{firstAdvance.EndMinute} " +
            $"second={firstResume.Status}@{firstResume.EndMinute} final={secondResume.Status}.");
    }

    var visitorIds = engine.State.Actors.Values
        .Where(actor => string.Equals(
            actor.PlaceId,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            StringComparison.Ordinal))
        .Where(actor => !string.Equals(actor.Id, engine.State.PlayerActorId, StringComparison.Ordinal))
        .Where(actor => !planOwnerIds.Contains(actor.Id))
        .OrderBy(actor => actor.Id, StringComparer.Ordinal)
        .Take(SyntheticScaleWorldFactory.MixedCityCrisisVisitorCount)
        .Select(actor => actor.Id)
        .ToArray();
    foreach (var visitorId in visitorIds)
    {
        var entered = engine.Enter(visitorId, SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId);
        var returned = engine.Enter(visitorId, SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId);
        if (entered.Status != ActionStatus.Completed || returned.Status != ActionStatus.Completed)
        {
            throw new InvalidOperationException($"Mixed B2 visit failed for '{visitorId}'.");
        }
    }

    var messageRecipientIds = engine.State.Actors.Values
        .Where(actor => string.Equals(
            actor.PlaceId,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            StringComparison.Ordinal))
        .Where(actor => !string.Equals(actor.Id, engine.State.PlayerActorId, StringComparison.Ordinal))
        .Where(actor => !planOwnerIds.Contains(actor.Id))
        .OrderBy(actor => actor.Id, StringComparer.Ordinal)
        .Take(SyntheticScaleWorldFactory.MixedCityCrisisMessageCount)
        .Select(actor => actor.Id)
        .ToArray();
    var senderId = engine.State.PlayerActorId;
    foreach (var recipientId in messageRecipientIds)
    {
        _ = engine.Tell(senderId, recipientId, SyntheticScaleWorldFactory.OfficialPropositionId);
        senderId = recipientId;
    }

    if (engine.State.CurrentMinute < twelveHours)
    {
        _ = engine.Wait(twelveHours - engine.State.CurrentMinute);
    }

    var dirtyAtEnd = engine.State.DetailDirtyActorIds.ToArray();
    var finalDetail = engine.RebalanceDirtyActorDetailLevels();
    AssertDirtyRebalance("Mixed B2 finish", dirtyAtEnd, finalDetail);

    var events = engine.State.Events;
    var messages = engine.State.Messages.Values.ToArray();
    var finalPopulationEquivalent = PopulationEquivalent(engine.State);
    var group = engine.State.Groups[SyntheticScaleWorldFactory.RegionalPopulationGroupId];
    var linkedMessageCount = messages.Count(message => message.ParentMessageId is not null);
    var completedPlanCount = engine.State.Plans.Values.Count(plan => plan.Status == PlanStatus.Completed);
    var cityAlertCount = events.Count(worldEvent => worldEvent.Type == "city_crisis_alert");
    var playerInterruptedCount = events.Count(worldEvent =>
        worldEvent.Type == "travel_interrupted" &&
        worldEvent.SubjectIds.Contains(engine.State.PlayerActorId, StringComparer.Ordinal));
    var playerResumedCount = events.Count(worldEvent =>
        worldEvent.Type == "travel_resumed" &&
        worldEvent.SubjectIds.Contains(engine.State.PlayerActorId, StringComparer.Ordinal));
    var remoteTickCount = events.Count(worldEvent => worldEvent.Type == "remote_world_tick");
    var remoteTicksAuditable = events
        .Where(worldEvent => worldEvent.Type == "remote_world_tick")
        .All(worldEvent =>
            worldEvent.Details["policy_version"] == RemoteSimulationPolicy.Version &&
            worldEvent.Details["material_balance"] == "conserved");
    var accessRequestCount = events.Count(worldEvent => worldEvent.Type == "access_requested");
    var placeEnteredCount = events.Count(worldEvent => worldEvent.Type == "place_entered");
    var planOwnersAtDestination = planOwnerIds.All(ownerId => string.Equals(
        engine.State.Actors[ownerId].PlaceId,
        SyntheticScaleWorldFactory.MixedCityCrisisPlanDestinationPlaceId,
        StringComparison.Ordinal));
    var otherActorsAtCrisis = engine.State.Actors.Values
        .Where(actor => !planOwnerIds.Contains(actor.Id))
        .All(actor => string.Equals(
            actor.PlaceId,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            StringComparison.Ordinal));
    var allActionsCompleted = engine.State.Actions.Values.All(action => action.Status == ActionStatus.Completed);
    var allMessageBeliefsTraceable = messageRecipientIds.All(recipientId =>
        engine.State.Beliefs.Values.Any(belief =>
            string.Equals(belief.HolderId, recipientId, StringComparison.Ordinal) &&
            messages.Any(message => string.Equals(
                message.DeliveredEventId,
                belief.SourceEventId,
                StringComparison.Ordinal))));
    if (engine.State.CurrentMinute != twelveHours ||
        actions.Length != 540 ||
        visitorIds.Length != SyntheticScaleWorldFactory.MixedCityCrisisVisitorCount ||
        messageRecipientIds.Length != SyntheticScaleWorldFactory.MixedCityCrisisMessageCount ||
        actions.Any(action => action.Status != ActionStatus.Completed) ||
        !allActionsCompleted ||
        !planOwnersAtDestination ||
        !otherActorsAtCrisis ||
        cityAlertCount != playerInterruptMinutes.Length ||
        playerInterruptedCount != playerInterruptMinutes.Length ||
        playerResumedCount != playerInterruptMinutes.Length ||
        remoteTickCount != remoteTickMinutes.Length ||
        !remoteTicksAuditable ||
        accessRequestCount != SyntheticScaleWorldFactory.MixedCityCrisisVisitorCount * 2 ||
        placeEnteredCount != accessRequestCount ||
        completedPlanCount != SyntheticScaleWorldFactory.MixedCityCrisisPlanCount ||
        messages.Length != SyntheticScaleWorldFactory.MixedCityCrisisMessageCount ||
        linkedMessageCount != SyntheticScaleWorldFactory.MixedCityCrisisMessageCount - 1 ||
        !allMessageBeliefsTraceable ||
        engine.State.DetailDirtyActorIds.Count != 0 ||
        finalPopulationEquivalent != initialPopulationEquivalent ||
        engine.State.Actors.Count != initialActorCount ||
        engine.State.Groups.Count != initialGroupCount ||
        group.Count != SyntheticScaleWorldFactory.CityCrisisGroupPopulation ||
        group.FoodStockUnits != 4_900_000 ||
        group.CumulativeFoodProduced != 900_000 ||
        group.CumulativeFoodDemand != 1_000_000 ||
        group.CumulativeFoodConsumed != 1_000_000 ||
        group.CumulativeFoodUnmet != 0 ||
        group.FoodShortageBp != 0 ||
        group.LastRemoteSettlementMinute != twelveHours)
    {
        throw new InvalidOperationException(
            "Mixed B2 invariants failed: " +
            $"minute={engine.State.CurrentMinute}/{twelveHours} travelers={actions.Length} " +
            $"alerts={cityAlertCount} interrupted={playerInterruptedCount} resumed={playerResumedCount} " +
            $"visits={placeEnteredCount}/{accessRequestCount} messages={messages.Length}/{linkedMessageCount} " +
            $"plans={completedPlanCount} remote_ticks={remoteTickCount} " +
            $"positions={planOwnersAtDestination}/{otherActorsAtCrisis} actions={allActionsCompleted} " +
            $"beliefs={allMessageBeliefsTraceable} population={finalPopulationEquivalent}/{initialPopulationEquivalent} " +
            $"food={group.FoodStockUnits}/{group.CumulativeFoodProduced}/{group.CumulativeFoodConsumed}/{group.CumulativeFoodUnmet}.");
    }

    return new ScaleWorkloadResult(
        $"places={engine.State.Places.Count} named_actors={initialActorCount} " +
        $"initial_l0={initialL0Count} initial_l1={initialL1Count} " +
        $"concurrent_travelers={actions.Length} player_interruptions={playerInterruptedCount} " +
        $"access_round_trips={visitorIds.Length} messages={messages.Length} linked_messages={linkedMessageCount} " +
        $"completed_plans={completedPlanCount} remote_ticks={remoteTickCount} " +
        $"remote_food_stock={group.FoodStockUnits} remote_food_produced={group.CumulativeFoodProduced} " +
        $"remote_food_consumed={group.CumulativeFoodConsumed} remote_food_unmet={group.CumulativeFoodUnmet} " +
        $"population_equivalent={finalPopulationEquivalent} l3_group_population={group.Count} " +
        $"processed_events={events.Count} " +
        $"detail_assessments={startDetail.Assessments.Count + finalDetail.Assessments.Count} " +
        "incremental_dirty_exact=true lineage_complete=true player_interruptions_exact=true " +
        "population_conserved=true remote_material_conserved=true remote_ticks_auditable=true l3_not_expanded=true",
        [firstAdvanceStopwatch.Elapsed.TotalMilliseconds, firstResumeStopwatch.Elapsed.TotalMilliseconds]);
}

static void RunMessageTopologyScale()
{
    RunSampledScale(
        "b3-message-target-topology",
        SyntheticScaleWorldFactory.CreateMessageTopologyWorld,
        ExecuteMessageTopologyScale);
}

static ScaleWorkloadResult ExecuteMessageTopologyScale(WorldEngine engine)
{
    var expectedCarrierCount = SyntheticScaleWorldFactory.PlaceCount *
                               SyntheticScaleWorldFactory.MessageCarriersPerPlace;
    if (engine.State.Actors.Count != expectedCarrierCount + 1)
    {
        throw new InvalidOperationException(
            $"B3 synthetic carrier count is invalid: {engine.State.Actors.Count - 1}/{expectedCarrierCount}.");
    }

    for (var placeIndex = 0; placeIndex < SyntheticScaleWorldFactory.PlaceCount; placeIndex++)
    {
        var placeId = SyntheticScaleWorldFactory.PlaceId(placeIndex);
        if (!string.Equals(engine.State.Actors[engine.State.PlayerActorId].PlaceId, placeId, StringComparison.Ordinal))
        {
            engine.Move(engine.State.PlayerActorId, placeId, TravelMode.Walk);
        }

        var senderId = engine.State.PlayerActorId;
        for (var carrierIndex = 0;
             carrierIndex < SyntheticScaleWorldFactory.MessageCarriersPerPlace;
             carrierIndex++)
        {
            var recipientId = SyntheticScaleWorldFactory.CarrierId(placeIndex, carrierIndex);
            _ = engine.Tell(senderId, recipientId, SyntheticScaleWorldFactory.OfficialPropositionId);
            senderId = recipientId;
        }
    }

    var expectedMessageCount = expectedCarrierCount;
    var expectedLinkedMessageCount = SyntheticScaleWorldFactory.PlaceCount *
                                     (SyntheticScaleWorldFactory.MessageCarriersPerPlace - 1);
    var messages = engine.State.Messages.Values.ToArray();
    var linkedMessageCount = messages.Count(message => message.ParentMessageId is not null);
    var messagesById = messages.ToDictionary(message => message.Id, StringComparer.Ordinal);
    var lineageIsValid = messages
        .Where(message => message.ParentMessageId is not null)
        .All(message =>
        {
            var parent = messagesById[message.ParentMessageId!];
            return string.Equals(parent.RecipientId, message.SenderId, StringComparison.Ordinal) &&
                   string.Equals(parent.PropositionId, message.PropositionId, StringComparison.Ordinal);
        });
    var everyCarrierHasMessageBackedBelief = engine.State.Actors.Keys
        .Where(actorId => !string.Equals(actorId, engine.State.PlayerActorId, StringComparison.Ordinal))
        .All(actorId => engine.State.Beliefs.Values.Any(belief =>
            string.Equals(belief.HolderId, actorId, StringComparison.Ordinal) &&
            string.Equals(belief.PropositionId, SyntheticScaleWorldFactory.OfficialPropositionId, StringComparison.Ordinal) &&
            belief.SourceEventId is { } sourceEventId &&
            messages.Any(message =>
                string.Equals(message.RecipientId, actorId, StringComparison.Ordinal) &&
                string.Equals(message.DeliveredEventId, sourceEventId, StringComparison.Ordinal))));
    if (messages.Length != expectedMessageCount ||
        linkedMessageCount != expectedLinkedMessageCount ||
        !lineageIsValid ||
        !everyCarrierHasMessageBackedBelief)
    {
        throw new InvalidOperationException(
            "B3 target-topology invariants failed: " +
            $"messages={messages.Length}/{expectedMessageCount} " +
            $"linked={linkedMessageCount}/{expectedLinkedMessageCount} " +
            $"lineage={lineageIsValid} message_backed_beliefs={everyCarrierHasMessageBackedBelief}.");
    }

    return new ScaleWorkloadResult(
        $"places={engine.State.Places.Count} carriers={expectedCarrierCount} " +
        $"messages={messages.Length} linked_messages={linkedMessageCount} " +
        $"processed_events={engine.State.Events.Count} lineage_complete=true " +
        "message_backed_beliefs=true no_bulk_belief_write=true");
}

static void RunConflictingMessageScale()
{
    RunSampledScale(
        "b3-message-conflict-semantics",
        SyntheticScaleWorldFactory.CreateConflictingMessageWorld,
        ExecuteConflictingMessageScale);
}

static ScaleWorkloadResult ExecuteConflictingMessageScale(WorldEngine engine)
{
    var expectedCarrierCount = SyntheticScaleWorldFactory.PlaceCount *
                               SyntheticScaleWorldFactory.MessageCarriersPerPlace;
    const int messagesPerPlace = 6;
    const int linkedMessagesPerPlace = 3;
    const int distortedMessagesPerPlace = 2;
    const int conflictHoldersPerPlace = 2;

    for (var placeIndex = 0; placeIndex < SyntheticScaleWorldFactory.PlaceCount; placeIndex++)
    {
        var placeId = SyntheticScaleWorldFactory.PlaceId(placeIndex);
        if (!string.Equals(engine.State.Actors[engine.State.PlayerActorId].PlaceId, placeId, StringComparison.Ordinal))
        {
            _ = engine.Move(engine.State.PlayerActorId, placeId, TravelMode.Walk);
        }

        var carrier0 = SyntheticScaleWorldFactory.CarrierId(placeIndex, 0);
        var carrier1 = SyntheticScaleWorldFactory.CarrierId(placeIndex, 1);
        var carrier2 = SyntheticScaleWorldFactory.CarrierId(placeIndex, 2);
        var carrier3 = SyntheticScaleWorldFactory.CarrierId(placeIndex, 3);
        _ = engine.Tell(
            engine.State.PlayerActorId,
            carrier0,
            SyntheticScaleWorldFactory.ConflictReportPropositionId);
        _ = engine.Tell(
            carrier0,
            carrier1,
            SyntheticScaleWorldFactory.ConflictUncertainPropositionId);
        _ = engine.Tell(
            carrier1,
            carrier2,
            SyntheticScaleWorldFactory.ConflictClosedRumorPropositionId);
        _ = engine.Tell(
            engine.State.PlayerActorId,
            carrier2,
            SyntheticScaleWorldFactory.ConflictConfirmedOpenPropositionId);
        _ = engine.Tell(
            carrier2,
            carrier3,
            SyntheticScaleWorldFactory.ConflictClosedRumorPropositionId);
        _ = engine.Tell(
            engine.State.PlayerActorId,
            carrier3,
            SyntheticScaleWorldFactory.ConflictConfirmedOpenPropositionId);
    }

    var messages = engine.State.Messages.Values.ToArray();
    var messagesById = messages.ToDictionary(message => message.Id, StringComparer.Ordinal);
    var linkedMessages = messages.Where(message => message.ParentMessageId is not null).ToArray();
    var distortedMessages = messages.Where(message => message.WasDistorted).ToArray();
    var conflicts = engine.State.Actors.Keys
        .Where(actorId => !string.Equals(actorId, engine.State.PlayerActorId, StringComparison.Ordinal))
        .SelectMany(engine.GetBeliefConflicts)
        .ToArray();
    var conflictEventCount = engine.State.Events.Count(item => item.Type == "belief_conflict_detected");
    var expectedMessageCount = SyntheticScaleWorldFactory.PlaceCount * messagesPerPlace;
    var expectedLinkedMessageCount = SyntheticScaleWorldFactory.PlaceCount * linkedMessagesPerPlace;
    var expectedDistortedMessageCount = SyntheticScaleWorldFactory.PlaceCount * distortedMessagesPerPlace;
    var expectedConflictHolderCount = SyntheticScaleWorldFactory.PlaceCount * conflictHoldersPerPlace;
    var lineageIsValid = linkedMessages.All(message =>
    {
        var parent = messagesById[message.ParentMessageId!];
        return string.Equals(parent.RecipientId, message.SenderId, StringComparison.Ordinal) &&
               string.Equals(parent.PropositionId, message.SourcePropositionId, StringComparison.Ordinal);
    });
    var allMessagesUseVersionedRule = messages.All(message =>
        string.Equals(
            message.PropagationRuleVersion,
            MessagePropagationPolicy.Version,
            StringComparison.Ordinal));
    var allDistortionsAuditable = distortedMessages.All(message =>
        message.DistortionDrawBp is >= 0 and < 10000 &&
        !string.Equals(message.SourcePropositionId, message.PropositionId, StringComparison.Ordinal));
    var everyMessageHasBackedBelief = messages.All(message =>
        engine.State.Beliefs.Values.Any(belief =>
            string.Equals(belief.HolderId, message.RecipientId, StringComparison.Ordinal) &&
            string.Equals(belief.PropositionId, message.PropositionId, StringComparison.Ordinal) &&
            string.Equals(belief.SourceEventId, message.DeliveredEventId, StringComparison.Ordinal)));
    var everyConflictHasDistinctStances = conflicts.All(conflict => conflict.Beliefs
        .Select(belief => engine.State.Propositions[belief.PropositionId].Stance)
        .Distinct(StringComparer.Ordinal)
        .Skip(1)
        .Any());

    if (engine.State.Actors.Count != expectedCarrierCount + 1 ||
        messages.Length != expectedMessageCount ||
        linkedMessages.Length != expectedLinkedMessageCount ||
        distortedMessages.Length != expectedDistortedMessageCount ||
        conflicts.Length != expectedConflictHolderCount ||
        conflictEventCount != expectedConflictHolderCount ||
        !lineageIsValid ||
        !allMessagesUseVersionedRule ||
        !allDistortionsAuditable ||
        !everyMessageHasBackedBelief ||
        !everyConflictHasDistinctStances)
    {
        throw new InvalidOperationException(
            "B3 conflict-semantics invariants failed: " +
            $"carriers={engine.State.Actors.Count - 1}/{expectedCarrierCount} " +
            $"messages={messages.Length}/{expectedMessageCount} " +
            $"linked={linkedMessages.Length}/{expectedLinkedMessageCount} " +
            $"distorted={distortedMessages.Length}/{expectedDistortedMessageCount} " +
            $"conflicts={conflicts.Length}/{expectedConflictHolderCount} " +
            $"conflict_events={conflictEventCount}/{expectedConflictHolderCount} " +
            $"lineage={lineageIsValid} rules={allMessagesUseVersionedRule} " +
            $"audit={allDistortionsAuditable} beliefs={everyMessageHasBackedBelief} " +
            $"stances={everyConflictHasDistinctStances}.");
    }

    return new ScaleWorkloadResult(
        $"places={engine.State.Places.Count} carriers={expectedCarrierCount} " +
        $"messages={messages.Length} linked_messages={linkedMessages.Length} " +
        $"distorted_messages={distortedMessages.Length} conflict_holders={conflicts.Length} " +
        $"conflict_events={conflictEventCount} processed_events={engine.State.Events.Count} " +
        $"propagation_rule={MessagePropagationPolicy.Version} lineage_complete=true " +
        "distortion_auditable=true message_backed_beliefs=true conflicts_exact=true " +
        "no_world_truth_read=true no_bulk_belief_write=true");
}

static void AssertDirtyRebalance(
    string phase,
    IReadOnlyList<string> dirtyActorIds,
    DetailRebalanceResult result)
{
    var assessedActorIds = result.Assessments.Select(assessment => assessment.ActorId).ToArray();
    if (!dirtyActorIds.SequenceEqual(assessedActorIds, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            $"{phase} detail rebalance did not assess exactly the dirty actor set.");
    }
}

static long PopulationEquivalent(WorldState state) =>
    state.Actors.Count + state.Groups.Values.Sum(group => (long)group.Count);

static void RunSampledScale(
    string workload,
    Func<WorldState> worldFactory,
    Func<WorldEngine, ScaleWorkloadResult> execute)
{
    const int warmupIterations = 1;
    const int sampleCount = 5;
    for (var index = 0; index < warmupIterations; index++)
    {
        _ = MeasureScaleSample(worldFactory, execute);
    }

    var samples = new ScaleSample[sampleCount];
    for (var index = 0; index < samples.Length; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        samples[index] = MeasureScaleSample(worldFactory, execute);
    }

    var fingerprints = samples.Select(sample => sample.Outcome.Fingerprint).Distinct(StringComparer.Ordinal).ToArray();
    var correctnessResults = samples.Select(sample => sample.Outcome.Correctness).Distinct(StringComparer.Ordinal).ToArray();
    if (fingerprints.Length != 1 || correctnessResults.Length != 1)
    {
        throw new InvalidOperationException(
            $"Scale samples diverged: fingerprints={fingerprints.Length} correctness={correctnessResults.Length}.");
    }

    PrintSampledResult(workload, warmupIterations, samples);
}

static ScaleSample MeasureScaleSample(
    Func<WorldState> worldFactory,
    Func<WorldEngine, ScaleWorkloadResult> execute)
{
    var setupAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var setupStopwatch = Stopwatch.StartNew();
    var engine = new WorldEngine(worldFactory());
    setupStopwatch.Stop();
    var setupAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - setupAllocatedBefore;

    var gc0Before = GC.CollectionCount(0);
    var gc1Before = GC.CollectionCount(1);
    var gc2Before = GC.CollectionCount(2);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    var workloadResult = execute(engine);
    stopwatch.Stop();
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

    var verificationAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var verificationStopwatch = Stopwatch.StartNew();
    var fingerprint = engine.State.ComputeEventFingerprint();
    verificationStopwatch.Stop();
    var verificationAllocatedBytes =
        GC.GetTotalAllocatedBytes(precise: true) - verificationAllocatedBefore;

    return new ScaleSample(
        setupStopwatch.Elapsed.TotalMilliseconds,
        stopwatch.Elapsed.TotalMilliseconds,
        verificationStopwatch.Elapsed.TotalMilliseconds,
        setupAllocatedBytes,
        allocatedBytes,
        verificationAllocatedBytes,
        Process.GetCurrentProcess().WorkingSet64,
        GC.CollectionCount(0) - gc0Before,
        GC.CollectionCount(1) - gc1Before,
        GC.CollectionCount(2) - gc2Before,
        new ScaleOutcome(fingerprint, workloadResult.Correctness),
        workloadResult.PlayerAdvanceElapsedMilliseconds ?? []);
}

static void PrintSampledResult(
    string workload,
    int warmupIterations,
    IReadOnlyList<ScaleSample> samples)
{
    var setupTimes = samples.Select(sample => sample.SetupElapsedMilliseconds).Order().ToArray();
    var elapsedTimes = samples.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
    var verificationTimes = samples.Select(sample => sample.VerificationElapsedMilliseconds).Order().ToArray();
    var setupAllocations = samples.Select(sample => sample.SetupAllocatedBytes).Order().ToArray();
    var allocations = samples.Select(sample => sample.AllocatedBytes).Order().ToArray();
    var verificationAllocations = samples.Select(sample => sample.VerificationAllocatedBytes).Order().ToArray();

    Console.WriteLine($"workload={workload}");
    Console.WriteLine($"warmup_iterations={warmupIterations}");
    Console.WriteLine($"samples={samples.Count}");
    Console.WriteLine($"setup_elapsed_samples_ms={JoinDoubles(samples.Select(sample => sample.SetupElapsedMilliseconds))}");
    Console.WriteLine($"setup_median_ms={Percentile(setupTimes, 0.50):F3}");
    Console.WriteLine($"setup_p95_ms={Percentile(setupTimes, 0.95):F3}");
    Console.WriteLine($"elapsed_samples_ms={JoinDoubles(samples.Select(sample => sample.ElapsedMilliseconds))}");
    Console.WriteLine($"median_ms={Percentile(elapsedTimes, 0.50):F3}");
    Console.WriteLine($"p95_ms={Percentile(elapsedTimes, 0.95):F3}");
    Console.WriteLine($"max_ms={elapsedTimes[^1]:F3}");
    Console.WriteLine($"stddev_ms={StandardDeviation(elapsedTimes):F3}");
    Console.WriteLine($"verification_elapsed_samples_ms={JoinDoubles(samples.Select(sample => sample.VerificationElapsedMilliseconds))}");
    Console.WriteLine($"verification_median_ms={Percentile(verificationTimes, 0.50):F3}");
    Console.WriteLine($"verification_p95_ms={Percentile(verificationTimes, 0.95):F3}");
    Console.WriteLine($"setup_allocated_samples_bytes={string.Join(',', samples.Select(sample => sample.SetupAllocatedBytes))}");
    Console.WriteLine($"setup_allocated_median_bytes={PercentileLong(setupAllocations, 0.50)}");
    Console.WriteLine($"allocated_samples_bytes={string.Join(',', samples.Select(sample => sample.AllocatedBytes))}");
    Console.WriteLine($"allocated_median_bytes={PercentileLong(allocations, 0.50)}");
    Console.WriteLine($"allocated_max_bytes={allocations[^1]}");
    Console.WriteLine($"verification_allocated_samples_bytes={string.Join(',', samples.Select(sample => sample.VerificationAllocatedBytes))}");
    Console.WriteLine($"verification_allocated_median_bytes={PercentileLong(verificationAllocations, 0.50)}");
    Console.WriteLine($"working_set_samples_bytes={string.Join(',', samples.Select(sample => sample.WorkingSetBytes))}");
    Console.WriteLine($"working_set_max_bytes={samples.Max(sample => sample.WorkingSetBytes)}");
    Console.WriteLine($"gc_gen0_samples={string.Join(',', samples.Select(sample => sample.Gen0Collections))}");
    Console.WriteLine($"gc_gen1_samples={string.Join(',', samples.Select(sample => sample.Gen1Collections))}");
    Console.WriteLine($"gc_gen2_samples={string.Join(',', samples.Select(sample => sample.Gen2Collections))}");
    Console.WriteLine($"fingerprint={samples[0].Outcome.Fingerprint}");
    Console.WriteLine($"fingerprint_consistent=true");
    Console.WriteLine($"correctness={samples[0].Outcome.Correctness}");
    var playerAdvanceTimes = samples
        .SelectMany(sample => sample.PlayerAdvanceElapsedMilliseconds)
        .Order()
        .ToArray();
    if (playerAdvanceTimes.Length > 0)
    {
        Console.WriteLine($"player_advance_samples={playerAdvanceTimes.Length}");
        Console.WriteLine($"player_advance_elapsed_samples_ms={JoinDoubles(playerAdvanceTimes)}");
        Console.WriteLine($"player_advance_median_ms={Percentile(playerAdvanceTimes, 0.50):F3}");
        Console.WriteLine($"player_advance_p95_ms={Percentile(playerAdvanceTimes, 0.95):F3}");
        Console.WriteLine($"player_advance_max_ms={playerAdvanceTimes[^1]:F3}");
    }
}

static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
{
    var index = Math.Max(0, (int)Math.Ceiling(percentile * sortedValues.Count) - 1);
    return sortedValues[index];
}

static long PercentileLong(IReadOnlyList<long> sortedValues, double percentile)
{
    var index = Math.Max(0, (int)Math.Ceiling(percentile * sortedValues.Count) - 1);
    return sortedValues[index];
}

static double StandardDeviation(IReadOnlyList<double> values)
{
    var mean = values.Average();
    return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / values.Count);
}

static string JoinDoubles(IEnumerable<double> values) =>
    string.Join(',', values.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));

static void GatherActors(WorldEngine engine, string destinationPlaceId)
{
    var actions = engine.State.Actors.Values
        .Where(item => !string.Equals(item.PlaceId, destinationPlaceId, StringComparison.Ordinal))
        .OrderBy(item => item.Id, StringComparer.Ordinal)
        .Select(item => engine.BeginTravel(item.Id, destinationPlaceId, TravelMode.Walk))
        .ToArray();
    var playerAction = actions.Single(item => item.ActorId == engine.State.PlayerActorId);
    _ = engine.AdvanceAction(playerAction.Id);
    while (actions.Any(item => item.Status == ActionStatus.Running))
    {
        var nextDueMinute = engine.State.ScheduledEvents
            .Where(item => item.Kind is "travel_segment_completed" or "travel_completed")
            .Min(item => item.DueMinute);
        _ = engine.Wait(nextDueMinute - engine.State.CurrentMinute);
    }
}

static void PrintResult(
    string workload,
    int iterations,
    Stopwatch stopwatch,
    long allocatedBytes,
    string fingerprint,
    string correctness)
{
    Console.WriteLine($"workload={workload}");
    Console.WriteLine($"iterations={iterations}");
    Console.WriteLine($"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2}");
    Console.WriteLine($"mean_ms={stopwatch.Elapsed.TotalMilliseconds / iterations:F3}");
    Console.WriteLine($"allocated_bytes={allocatedBytes}");
    Console.WriteLine($"working_set_bytes={Environment.WorkingSet}");
    Console.WriteLine($"fingerprint={fingerprint}");
    Console.WriteLine($"correctness={correctness}");
}

static string FindScenarioDirectory()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, "data", "scenarios", "189-luoyang-crisis");
        if (File.Exists(Path.Combine(candidate, "manifest.json")))
        {
            return candidate;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Cannot locate the scenario directory.");
}

internal sealed record ScaleOutcome(string Fingerprint, string Correctness);

internal sealed record ScaleWorkloadResult(
    string Correctness,
    IReadOnlyList<double>? PlayerAdvanceElapsedMilliseconds = null);

internal sealed record ScaleSample(
    double SetupElapsedMilliseconds,
    double ElapsedMilliseconds,
    double VerificationElapsedMilliseconds,
    long SetupAllocatedBytes,
    long AllocatedBytes,
    long VerificationAllocatedBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    ScaleOutcome Outcome,
    IReadOnlyList<double> PlayerAdvanceElapsedMilliseconds);
