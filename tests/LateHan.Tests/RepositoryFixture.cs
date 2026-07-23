using LateHan.Core;
using LateHan.Scenarios;

namespace LateHan.Tests;

internal static class RepositoryFixture
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ScenarioDirectory { get; } = Path.Combine(
        RepositoryRoot,
        "data",
        "scenarios",
        "189-luoyang-crisis");

    public static WorldEngine CreateEngine()
    {
        return new WorldEngine(new ScenarioLoader().Load(ScenarioDirectory).World);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "LateHanWanderer.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate LateHanWanderer.sln.");
    }
}
