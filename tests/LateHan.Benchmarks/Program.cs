using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text;
using LateHan.Core;
using LateHan.Persistence;
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
    case "b4-scale":
        RunLodInteractionScale();
        break;
    case "b1-scale":
        RunIdleTargetScale();
        break;
    case "b5-scale":
        RunLongTermEventArchiveScale();
        break;
    case "scale":
        RunCityCrisisScale();
        RunMixedCityCrisisScale();
        RunMessageTopologyScale();
        RunConflictingMessageScale();
        RunLodInteractionScale();
        RunIdleTargetScale();
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
            "Usage: delivery|b1-idle|b1-scale|b2-city|b3-messages|b4-lod|b2-scale|b2-mixed|b3-scale|b3-conflict|b4-scale|b5-scale|scale|all");
}

void RunLongTermEventArchiveScale()
{
    const int eventCount = 1_000_000;
    const int checkpointInterval = 25_000;
    const int checkpointCount = eventCount / checkpointInterval;
    var archiveDirectory = Path.Combine(Path.GetTempPath(), $"latehan-b5-{Guid.NewGuid():N}");
    var archivePath = Path.Combine(archiveDirectory, "events.db");
    Directory.CreateDirectory(archiveDirectory);
    try
    {
        var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var appendStopwatch = Stopwatch.StartNew();
        string expectedFingerprint;
        using (var archive = new WorldEventArchive(archivePath))
        using (var fingerprint = new WorldEventFingerprint())
        {
            for (var firstSequence = 1; firstSequence <= eventCount; firstSequence += checkpointInterval)
            {
                var events = CreateArchiveEventBatch(firstSequence, checkpointInterval);
                archive.Append(events);
                foreach (var worldEvent in events)
                {
                    fingerprint.Append(worldEvent);
                }

                var lastEvent = events[^1];
                archive.CreateCheckpoint(
                    lastEvent.Sequence,
                    $"checkpoint:{lastEvent.Id}",
                    Encoding.UTF8.GetBytes($"projection_cursor={lastEvent.Sequence};minute={lastEvent.Minute}"));
            }

            expectedFingerprint = fingerprint.Complete();
            archive.Flush();
        }

        appendStopwatch.Stop();
        var appendAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore;
        var storageBytes = Directory.EnumerateFiles(archiveDirectory).Sum(path => new FileInfo(path).Length);

        using var reopened = new WorldEventArchive(archivePath);
        var compressionStopwatch = Stopwatch.StartNew();
        var compressedStorageBytes = reopened.CreateCompressedBackup(Path.Combine(archiveDirectory, "events.db.gz"));
        compressionStopwatch.Stop();
        var directQueryStopwatch = Stopwatch.StartNew();
        var direct = reopened.Find("event.01000000");
        directQueryStopwatch.Stop();

        var whyStopwatch = Stopwatch.StartNew();
        var why = reopened.Why("event.01000000", maximumDepth: 8, maximumEvents: 32);
        whyStopwatch.Stop();

        var restoreStopwatch = Stopwatch.StartNew();
        var restored = reopened.RestoreLatest();
        restoreStopwatch.Stop();

        var auditStopwatch = Stopwatch.StartNew();
        var audit = reopened.Audit();
        auditStopwatch.Stop();

        if (reopened.EventCount != eventCount ||
            reopened.LastSequence != eventCount ||
            reopened.CheckpointCount != checkpointCount ||
            direct?.Sequence != eventCount ||
            why.Count != 9 ||
            why[0].Event.Sequence != eventCount ||
            why[^1].Event.Sequence != eventCount - 8 ||
            restored?.Checkpoint.EventSequence != eventCount ||
            restored.EventsAfterCheckpoint.Count != 0 ||
            audit.EventCount != eventCount ||
            audit.LastSequence != eventCount ||
            !string.Equals(audit.EventFingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "B5 archive invariants failed: " +
                $"events={reopened.EventCount}/{audit.EventCount} last={reopened.LastSequence}/{audit.LastSequence} " +
                $"checkpoints={reopened.CheckpointCount} direct={direct?.Sequence} why={why.Count} " +
                $"restore={restored?.Checkpoint.EventSequence}/{restored?.EventsAfterCheckpoint.Count} " +
                $"fingerprint={audit.EventFingerprint == expectedFingerprint}.");
        }

        Console.WriteLine("workload=b5-long-term-event-archive");
        Console.WriteLine($"events={eventCount}");
        Console.WriteLine($"checkpoint_interval={checkpointInterval}");
        Console.WriteLine($"checkpoints={checkpointCount}");
        Console.WriteLine($"append_elapsed_ms={appendStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"append_allocated_bytes={appendAllocatedBytes}");
        Console.WriteLine($"direct_query_elapsed_ms={directQueryStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"why_query_elapsed_ms={whyStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"why_events={why.Count}");
        Console.WriteLine($"restore_elapsed_ms={restoreStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"restore_tail_events={restored.EventsAfterCheckpoint.Count}");
        Console.WriteLine($"audit_elapsed_ms={auditStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"storage_bytes={storageBytes}");
        Console.WriteLine($"compression_elapsed_ms={compressionStopwatch.Elapsed.TotalMilliseconds:F3}");
        Console.WriteLine($"compressed_storage_bytes={compressedStorageBytes}");
        Console.WriteLine($"working_set_bytes={Environment.WorkingSet}");
        Console.WriteLine($"fingerprint={audit.EventFingerprint}");
        Console.WriteLine("correctness=sequential_append=true checkpoints_exact=true direct_query=true bounded_why=true latest_restore=true full_audit=true");
    }
    finally
    {
        Directory.Delete(archiveDirectory, recursive: true);
    }
}

static WorldEvent[] CreateArchiveEventBatch(int firstSequence, int count)
{
    var events = new WorldEvent[count];
    for (var index = 0; index < count; index++)
    {
        var sequence = firstSequence + index;
        events[index] = new WorldEvent(
            sequence,
            $"event.{sequence:D8}",
            sequence % 10 == 0 ? "remote_named_actor_batch_updated" : "ambient_world_event",
            sequence * 5L,
            $"place.synthetic.{sequence % 50:D2}",
            [$"person.synthetic.{sequence % 20_000:D5}"],
            sequence == 1 ? [] : [$"event.{sequence - 1:D8}"],
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cycle"] = (sequence / 20_000).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["organization_id"] = $"organization.synthetic.{sequence % 800:D3}",
            }));
    }

    return events;
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

void RunIdleTargetScale()
{
    RunSampledScale(
        "b1-idle-target-world",
        SyntheticScaleWorldFactory.CreateIdleTargetWorld,
        ExecuteIdleTargetScale);
}

static ScaleWorkloadResult ExecuteIdleTargetScale(WorldEngine engine)
{
    const long thirtyDays = 30 * 24 * 60;
    var initialPopulation = PopulationEquivalent(engine.State);
    var initialFoodStock = engine.State.Groups.Values.Sum(group => group.FoodStockUnits);
    var initialActorCount = engine.State.Actors.Count;
    var initialScheduled = engine.InitializeRemoteSimulation();
    if (initialScheduled.Count != SyntheticScaleWorldFactory.IdleWorldGroupCount)
    {
        throw new InvalidOperationException("B1 remote tick initialization count is incorrect.");
    }

    for (var index = 0; index < SyntheticScaleWorldFactory.IdleWorldAmbientEventCount; index++)
    {
        var dueMinute = 1L + index * (thirtyDays - 1) / SyntheticScaleWorldFactory.IdleWorldAmbientEventCount;
        engine.Schedule(
            dueMinute,
            ScheduledEventPhase.SummaryAndNotification,
            $"ambient.synthetic.{index:D5}",
            "ambient_world_event",
            SyntheticScaleWorldFactory.PlaceId(index % SyntheticScaleWorldFactory.PlaceCount));
    }

    _ = engine.Wait(thirtyDays);
    var remoteTickCount = engine.State.Events.Count(item => item.Type == "remote_world_tick");
    var remoteActorBatchCount = engine.State.Events.Count(item => item.Type == "remote_named_actor_batch_updated");
    var ambientEventCount = engine.State.Events.Count(item => item.Type == "ambient_world_event");
    var finalFoodStock = engine.State.Groups.Values.Sum(group => group.FoodStockUnits);
    var allL2CyclesExact = engine.State.Actors.Values
        .Where(actor => actor.DetailLevel == SimulationDetailLevel.L2)
        .All(actor => actor.RemoteCycleCount == 30 && actor.LastRemoteUpdateMinute == thirtyDays);
    var ledgersConserved = engine.State.Groups.Values.All(group =>
        group.LastRemoteSettlementMinute == thirtyDays &&
        group.CumulativeFoodProduced == 600_000 &&
        group.CumulativeFoodDemand == 600_000 &&
        group.CumulativeFoodConsumed == 600_000 &&
        group.CumulativeFoodUnmet == 0 &&
        group.FoodShortageBp == 0);
    if (initialActorCount != SyntheticScaleWorldFactory.IdleWorldL0Count +
                             SyntheticScaleWorldFactory.IdleWorldL1Count +
                             SyntheticScaleWorldFactory.IdleWorldL2Count ||
        engine.State.Groups.Count != SyntheticScaleWorldFactory.IdleWorldGroupCount ||
        remoteTickCount != SyntheticScaleWorldFactory.IdleWorldGroupCount * 30 ||
        remoteActorBatchCount != remoteTickCount ||
        ambientEventCount != SyntheticScaleWorldFactory.IdleWorldAmbientEventCount ||
        engine.State.Events.Count != remoteTickCount + remoteActorBatchCount + ambientEventCount + 2 ||
        engine.State.ScheduledEvents.Count != SyntheticScaleWorldFactory.IdleWorldGroupCount ||
        !allL2CyclesExact ||
        !ledgersConserved ||
        finalFoodStock != initialFoodStock ||
        PopulationEquivalent(engine.State) != initialPopulation)
    {
        throw new InvalidOperationException(
            "B1 target invariants failed: " +
            $"actors={engine.State.Actors.Count}/{initialActorCount} groups={engine.State.Groups.Count} " +
            $"remote={remoteTickCount}/{remoteActorBatchCount} ambient={ambientEventCount} " +
            $"events={engine.State.Events.Count} future={engine.State.ScheduledEvents.Count} " +
            $"cycles={allL2CyclesExact} ledgers={ledgersConserved} " +
            $"food={finalFoodStock}/{initialFoodStock} population={PopulationEquivalent(engine.State)}/{initialPopulation}.");
    }

    return new ScaleWorkloadResult(
        $"places={engine.State.Places.Count} l0={SyntheticScaleWorldFactory.IdleWorldL0Count} " +
        $"l1={SyntheticScaleWorldFactory.IdleWorldL1Count} l2={SyntheticScaleWorldFactory.IdleWorldL2Count} " +
        $"l3_population={engine.State.Groups.Values.Sum(group => group.Count)} " +
        $"remote_ticks={remoteTickCount} remote_actor_batches={remoteActorBatchCount} " +
        $"ambient_events={ambientEventCount} processed_events={engine.State.Events.Count} " +
        $"future_events={engine.State.ScheduledEvents.Count} population_equivalent={initialPopulation} " +
        "event_driven=true l2_cycles_exact=true population_conserved=true " +
        "remote_material_conserved=true l3_not_expanded=true");
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

void RunLodInteractionScale()
{
    RunSampledScale(
        "b4-lod-target-interactions",
        SyntheticScaleWorldFactory.CreateLodInteractionWorld,
        ExecuteLodInteractionScale);
}

static ScaleWorkloadResult ExecuteLodInteractionScale(WorldEngine engine)
{
    var groupIds = engine.State.Groups.Keys.Order(StringComparer.Ordinal).ToArray();
    var initialPopulation = PopulationEquivalent(engine.State);
    var initialGroupPopulation = engine.State.Groups.Values.Sum(group => group.Count);
    var initialActorCount = engine.State.Actors.Count;
    var retainedActorIds = new List<string>();
    var promotedActorIds = new HashSet<string>(StringComparer.Ordinal);
    var crossPlaceRoundTrips = 0;
    for (var index = 0; index < SyntheticScaleWorldFactory.LodPromotionCount; index++)
    {
        var draw = engine.State.RandomStreams.NextUInt64("b4-group-selection", $"promotion.{index:D4}");
        var group = engine.State.Groups[groupIds[(int)(draw % (ulong)groupIds.Length)]];
        var promoted = engine.PromoteGroupMember(group.Id, detailLevel: SimulationDetailLevel.L1);
        if (!promotedActorIds.Add(promoted.Actor.Id))
        {
            throw new InvalidOperationException($"B4 reused promoted actor ID '{promoted.Actor.Id}'.");
        }

        if (index % 10 == 0)
        {
            var placeIndex = int.Parse(group.LocationId[^2..], System.Globalization.CultureInfo.InvariantCulture);
            _ = engine.Tell(
                SyntheticScaleWorldFactory.LodAnchorId(placeIndex),
                promoted.Actor.Id,
                SyntheticScaleWorldFactory.OfficialPropositionId);
            var blocked = false;
            try
            {
                _ = engine.DemotePromotedActor(promoted.Actor.Id);
            }
            catch (DomainCommandException exception) when (exception.Code == "actor_has_independent_state")
            {
                blocked = true;
            }

            if (!blocked)
            {
                throw new InvalidOperationException("B4 retained actor was incorrectly merged after interaction.");
            }

            retainedActorIds.Add(promoted.Actor.Id);
            continue;
        }

        if (index % 4 == 0)
        {
            var originIndex = int.Parse(group.LocationId[^2..], System.Globalization.CultureInfo.InvariantCulture);
            var adjacentPlaceId = SyntheticScaleWorldFactory.PlaceId((originIndex + 1) % SyntheticScaleWorldFactory.PlaceCount);
            _ = engine.Move(promoted.Actor.Id, adjacentPlaceId, TravelMode.Walk);
            _ = engine.Move(promoted.Actor.Id, group.LocationId, TravelMode.Walk);
            crossPlaceRoundTrips++;
        }

        _ = engine.DemotePromotedActor(promoted.Actor.Id);
    }

    var promotionCount = engine.State.Events.Count(item => item.Type == "group_member_promoted");
    var demotionCount = engine.State.Events.Count(item => item.Type == "promoted_actor_demoted");
    var retainedMessages = engine.State.Messages.Values.Count(message => retainedActorIds.Contains(message.RecipientId));
    var completedTravelCount = engine.State.Actions.Values.Count(action => action.Status == ActionStatus.Completed);
    var finalGroupPopulation = engine.State.Groups.Values.Sum(group => group.Count);
    var allRetainedActorsTraceable = retainedActorIds.All(actorId =>
        engine.State.Actors.TryGetValue(actorId, out var actor) &&
        actor.IsTemporaryPromotion &&
        engine.State.Messages.Values.Any(message =>
            string.Equals(message.RecipientId, actorId, StringComparison.Ordinal) &&
            message.DeliveredEventId is not null));
    if (groupIds.Length != SyntheticScaleWorldFactory.LodGroupCount ||
        promotionCount != SyntheticScaleWorldFactory.LodPromotionCount ||
        demotionCount != SyntheticScaleWorldFactory.LodPromotionCount - SyntheticScaleWorldFactory.LodRetainedActorCount ||
        retainedActorIds.Count != SyntheticScaleWorldFactory.LodRetainedActorCount ||
        crossPlaceRoundTrips != SyntheticScaleWorldFactory.LodCrossPlaceRoundTripCount ||
        retainedMessages != SyntheticScaleWorldFactory.LodRetainedActorCount ||
        completedTravelCount != SyntheticScaleWorldFactory.LodCrossPlaceRoundTripCount * 2 ||
        !allRetainedActorsTraceable ||
        finalGroupPopulation != initialGroupPopulation - SyntheticScaleWorldFactory.LodRetainedActorCount ||
        engine.State.Actors.Count != initialActorCount + SyntheticScaleWorldFactory.LodRetainedActorCount ||
        PopulationEquivalent(engine.State) != initialPopulation)
    {
        throw new InvalidOperationException(
            "B4 target invariants failed: " +
            $"groups={groupIds.Length} promotions={promotionCount} demotions={demotionCount} " +
            $"retained={retainedActorIds.Count}/{retainedMessages}/{allRetainedActorsTraceable} " +
            $"round_trips={crossPlaceRoundTrips} completed_travel={completedTravelCount} " +
            $"group_population={finalGroupPopulation}/{initialGroupPopulation} " +
            $"population={PopulationEquivalent(engine.State)}/{initialPopulation}.");
    }

    return new ScaleWorkloadResult(
        $"groups={groupIds.Length} initial_group_population={initialGroupPopulation} " +
        $"promotions={promotionCount} clean_demotions={demotionCount} " +
        $"cross_place_round_trips={crossPlaceRoundTrips} retained_actors={retainedActorIds.Count} " +
        $"retained_messages={retainedMessages} completed_travel_actions={completedTravelCount} " +
        $"processed_events={engine.State.Events.Count} population_equivalent={initialPopulation} " +
        "stable_random_selection=true identity_unique=true population_conserved=true " +
        "interaction_retention_exact=true cross_place_merge_safe=true l3_not_expanded=true");
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

    var explicitlyCancelledPlan = engine.CancelPlan(
        SyntheticScaleWorldFactory.MixedCityCrisisPlanId(4),
        "crisis_priority_changed");
    if (explicitlyCancelledPlan.Status != ActionStatus.Cancelled)
    {
        throw new InvalidOperationException("Mixed B2 explicit plan cancellation failed.");
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
    var queueCloseMinute = engine.State.CurrentMinute;
    engine.Schedule(
        queueCloseMinute,
        ScheduledEventPhase.AccessAndControlChange,
        SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
        "place_access_changed",
        SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
        details: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["open"] = "false",
            ["place_id"] = SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
            ["security_posture"] = "crisis_closed",
        });
    engine.Schedule(
        queueCloseMinute + 10,
        ScheduledEventPhase.AccessAndControlChange,
        SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
        "place_access_changed",
        SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
        details: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["open"] = "true",
            ["place_id"] = SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
            ["security_posture"] = "crisis_screening",
        });
    _ = engine.Wait(1, visitorIds[0]);
    var firstQueued = engine.Enter(visitorIds[0], SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId);
    var secondQueued = engine.Enter(visitorIds[1], SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId);
    _ = engine.Wait(AccessQueuePolicy.ReviewIntervalMinutes, visitorIds[1]);
    if (firstQueued.Status != ActionStatus.Scheduled || secondQueued.Status != ActionStatus.Scheduled)
    {
        throw new InvalidOperationException("Mixed B2 competitive access requests did not queue.");
    }

    foreach (var visitorId in visitorIds)
    {
        var entered = string.Equals(
            engine.State.Actors[visitorId].PlaceId,
            SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId,
            StringComparison.Ordinal)
            ? new ActionResult(engine.State.CurrentMinute, engine.State.CurrentMinute, [])
            : engine.Enter(visitorId, SyntheticScaleWorldFactory.MixedCityCrisisVisitPlaceId);
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
    var cancelledPlanCount = engine.State.Plans.Values.Count(plan => plan.Status == PlanStatus.Cancelled);
    var planConflictCount = events.Count(worldEvent => worldEvent.Type == "plan_resource_conflict");
    var planReplacementCount = events.Count(worldEvent =>
        worldEvent.Type == "plan_cancelled" &&
        worldEvent.Details["reason"].StartsWith("replaced_by:", StringComparison.Ordinal));
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
    var accessQueuedCount = events.Count(worldEvent => worldEvent.Type == "access_queued");
    var planOwnersInExpectedPositions = engine.State.Plans.Values.All(plan =>
    {
        var owner = engine.State.Actors[plan.OwnerId];
        return plan.Status == PlanStatus.Completed
            ? string.Equals(
                owner.PlaceId,
                SyntheticScaleWorldFactory.MixedCityCrisisPlanDestinationPlaceId,
                StringComparison.Ordinal)
            : plan.Status == PlanStatus.Cancelled &&
              (owner.Transit is not null &&
               engine.State.Actions.TryGetValue(owner.Transit.ActionId, out var cancelledAction) &&
               cancelledAction.Status == ActionStatus.Cancelled ||
               owner.Transit is null &&
               string.Equals(
                   owner.PlaceId,
                   SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
                   StringComparison.Ordinal));
    });
    var otherActorsAtCrisis = engine.State.Actors.Values
        .Where(actor => !planOwnerIds.Contains(actor.Id))
        .All(actor => string.Equals(
            actor.PlaceId,
            SyntheticScaleWorldFactory.CityCrisisDestinationPlaceId,
            StringComparison.Ordinal));
    var cancelledActionCount = engine.State.Actions.Values.Count(action => action.Status == ActionStatus.Cancelled);
    var allActionsSettled = engine.State.Actions.Values.All(action =>
        action.Status is ActionStatus.Completed or ActionStatus.Cancelled);
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
        !allActionsSettled ||
        cancelledActionCount != 1 ||
        !planOwnersInExpectedPositions ||
        !otherActorsAtCrisis ||
        cityAlertCount != playerInterruptMinutes.Length ||
        playerInterruptedCount != playerInterruptMinutes.Length ||
        playerResumedCount != playerInterruptMinutes.Length ||
        remoteTickCount != remoteTickMinutes.Length ||
        !remoteTicksAuditable ||
        accessRequestCount != SyntheticScaleWorldFactory.MixedCityCrisisVisitorCount * 2 ||
        placeEnteredCount != accessRequestCount ||
        accessQueuedCount != 2 ||
        completedPlanCount != SyntheticScaleWorldFactory.MixedCityCrisisPlanCount - 2 ||
        cancelledPlanCount != 2 ||
        planConflictCount != 2 ||
        planReplacementCount != 1 ||
        engine.State.PlanResourceLocks.Count != 0 ||
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
            $"visits={placeEnteredCount}/{accessRequestCount} queued={accessQueuedCount} messages={messages.Length}/{linkedMessageCount} " +
            $"plans={completedPlanCount}/{cancelledPlanCount}/{planConflictCount}/{planReplacementCount} remote_ticks={remoteTickCount} " +
            $"positions={planOwnersInExpectedPositions}/{otherActorsAtCrisis} actions={allActionsSettled}/{cancelledActionCount} " +
            $"beliefs={allMessageBeliefsTraceable} population={finalPopulationEquivalent}/{initialPopulationEquivalent} " +
            $"food={group.FoodStockUnits}/{group.CumulativeFoodProduced}/{group.CumulativeFoodConsumed}/{group.CumulativeFoodUnmet}.");
    }

    return new ScaleWorkloadResult(
        $"places={engine.State.Places.Count} named_actors={initialActorCount} " +
        $"initial_l0={initialL0Count} initial_l1={initialL1Count} " +
        $"concurrent_travelers={actions.Length} player_interruptions={playerInterruptedCount} " +
        $"access_round_trips={visitorIds.Length} queued_access_requests={accessQueuedCount} " +
        $"messages={messages.Length} linked_messages={linkedMessageCount} " +
        $"completed_plans={completedPlanCount} cancelled_plans={cancelledPlanCount} " +
        $"plan_resource_conflicts={planConflictCount} plan_replacements={planReplacementCount} " +
        $"remote_ticks={remoteTickCount} " +
        $"remote_food_stock={group.FoodStockUnits} remote_food_produced={group.CumulativeFoodProduced} " +
        $"remote_food_consumed={group.CumulativeFoodConsumed} remote_food_unmet={group.CumulativeFoodUnmet} " +
        $"population_equivalent={finalPopulationEquivalent} l3_group_population={group.Count} " +
        $"processed_events={events.Count} " +
        $"detail_assessments={startDetail.Assessments.Count + finalDetail.Assessments.Count} " +
        "incremental_dirty_exact=true lineage_complete=true player_interruptions_exact=true " +
        "population_conserved=true plan_resources_released=true remote_material_conserved=true " +
        "remote_ticks_auditable=true l3_not_expanded=true",
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
