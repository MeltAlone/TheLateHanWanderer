using System.Diagnostics;
using LateHan.Core;
using LateHan.Scenarios;

var scenarioDirectory = FindScenarioDirectory();
var loader = new ScenarioLoader();
const int iterations = 100;

_ = loader.Load(scenarioDirectory);
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
Console.WriteLine($"scenario_load_and_delivery iterations={iterations}");
Console.WriteLine($"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2}");
Console.WriteLine($"mean_ms={stopwatch.Elapsed.TotalMilliseconds / iterations:F3}");
Console.WriteLine($"fingerprint={fingerprint}");

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
