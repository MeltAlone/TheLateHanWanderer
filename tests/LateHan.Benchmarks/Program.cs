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
    case "b3-scale":
        RunMessageTopologyScale();
        break;
    case "scale":
        RunCityCrisisScale();
        RunMessageTopologyScale();
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
            "Usage: delivery|b1-idle|b2-city|b3-messages|b4-lod|b2-scale|b3-scale|scale|all");
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
        new ScaleOutcome(fingerprint, workloadResult.Correctness));
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

internal sealed record ScaleWorkloadResult(string Correctness);

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
    ScaleOutcome Outcome);
