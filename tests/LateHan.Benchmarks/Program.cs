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
    case "all":
        RunDelivery();
        RunIdleWorld();
        RunCityCrisis();
        RunMessageFanout();
        RunLodRoundTrips();
        break;
    default:
        throw new ArgumentException("Usage: delivery|b1-idle|b2-city|b3-messages|b4-lod|all");
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
    _ = engine.RebalanceActorDetailLevels(actions.Select(item => item.ActorId));
    var playerAction = actions.Single(item => item.ActorId == engine.State.PlayerActorId);
    _ = engine.AdvanceAction(playerAction.Id);
    if (engine.State.CurrentMinute < twelveHours)
    {
        _ = engine.Wait(twelveHours - engine.State.CurrentMinute);
    }

    var finalDetail = engine.RebalanceActorDetailLevels();
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
        $"processed_events={engine.State.Events.Count} final_detail_changes={finalDetail.Events.Count} " +
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
