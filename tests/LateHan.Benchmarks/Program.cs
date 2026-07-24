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
    case "all":
        RunDelivery();
        RunIdleWorld();
        RunLodRoundTrips();
        break;
    default:
        throw new ArgumentException("Usage: delivery|b1-idle|b4-lod|all");
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
