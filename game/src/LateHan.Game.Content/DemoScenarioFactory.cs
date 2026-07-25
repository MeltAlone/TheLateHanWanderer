using LateHan.Game.Domain;

namespace LateHan.Game.Content;

public static class DemoScenarioFactory
{
    public const string ScenarioPathEnvironmentVariable = "LATEHAN_SCENARIO_PATH";
    public const string DefaultScenarioFileName = "189-central-plains.v1.json";

    public static GameScenario Create(string? scenarioPath = null) =>
        ScenarioJsonLoader.Load(ResolveScenarioPath(scenarioPath));

    public static string ResolveScenarioPath(string? scenarioPath = null)
    {
        if (!string.IsNullOrWhiteSpace(scenarioPath))
        {
            return Path.GetFullPath(scenarioPath);
        }

        var configuredPath = Environment.GetEnvironmentVariable(ScenarioPathEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.Combine(AppContext.BaseDirectory, "Data", DefaultScenarioFileName);
    }
}
