using System.Text.Json;
using LateHan.Scenarios;

namespace LateHan.Tests;

public sealed class ScenarioLoaderTests
{
    [Fact]
    public void LoadBuildsTheExpectedMvpWorld()
    {
        var loaded = new ScenarioLoader().Load(RepositoryFixture.ScenarioDirectory);

        Assert.Equal("scenario.189.luoyang_crisis.mvp", loaded.World.ScenarioId);
        Assert.Equal("person.player_clerk", loaded.World.PlayerActorId);
        Assert.Equal(0, loaded.World.CurrentMinute);
        Assert.Equal(15, loaded.World.Actors.Count);
        Assert.Equal(20, loaded.World.Places.Count);
        Assert.Equal(18, loaded.World.Routes.Count);
        Assert.Equal(8, loaded.World.Items.Count);
        Assert.Equal(2, loaded.World.Commitments.Count);
        Assert.Equal("xoshiro256ss.v1", loaded.World.RngVersion);
        Assert.Equal("18908D2400000001", loaded.World.RandomStreams.RootSeedHex);
        Assert.Equal("sha256-le.v1", loaded.World.RandomStreams.Derivation);
        Assert.Equal("0.9.0-spike", loaded.World.EngineVersion);
        Assert.Equal(6, loaded.World.AccessRules.Count);
        Assert.Equal(4, loaded.World.PlaceAccessStates.Count);
        Assert.Equal(7, loaded.World.Groups.Count);
        Assert.Equal(240, loaded.World.Groups["group.market_population"].Count);
        Assert.NotEmpty(loaded.World.Beliefs);
        Assert.Contains("belief.wang_yun.traffic", loaded.World.Beliefs.Keys);
        Assert.NotEmpty(loaded.World.Plans);
        Assert.Contains("plan.wang_yun.review_gate_security", loaded.World.Plans.Keys);
        Assert.Single(loaded.World.ScheduledEvents);
        Assert.Equal(210, loaded.World.ScheduledEvents[0].DueMinute);
        Assert.StartsWith("sha256:", loaded.ComputedContentHash, StringComparison.Ordinal);
        Assert.Equal(loaded.DeclaredContentHash, loaded.ComputedContentHash);
        Assert.NotEqual("pending-tooling", loaded.World.ContentHash);
    }

    [Fact]
    public void CanonicalJsonIgnoresObjectKeyOrderAndWhitespace()
    {
        using var first = JsonDocument.Parse("{\"b\":2,\"a\":[true,{\"x\":1}]}");
        using var second = JsonDocument.Parse("{ \"a\" : [ true, { \"x\" : 1 } ], \"b\" : 2 }");

        var firstBytes = CanonicalJson.Canonicalize(first.RootElement);
        var secondBytes = CanonicalJson.Canonicalize(second.RootElement);

        Assert.Equal(firstBytes, secondBytes);
    }

    [Fact]
    public void UnknownPersonLocationIsRejectedBeforeWorldConstruction()
    {
        var copy = CreateScenarioCopy();
        try
        {
            var actorsPath = Path.Combine(copy, "actors.json");
            var actors = File.ReadAllText(actorsPath)
                .Replace(
                    "\"location\": \"place.luoyang.general_in_chief_office\"",
                    "\"location\": \"place.missing\"",
                    StringComparison.Ordinal);
            File.WriteAllText(actorsPath, actors);

            var exception = Assert.Throws<ScenarioValidationException>(() => new ScenarioLoader().Load(copy));

            Assert.Contains(exception.Errors, error => error.Contains("SCN-REF-001", StringComparison.Ordinal));
            Assert.Contains(exception.Errors, error => error.Contains("place.missing", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public void ModifiedContentIsRejectedWhenDeclaredHashIsStale()
    {
        var copy = CreateScenarioCopy();
        try
        {
            var worldPath = Path.Combine(copy, "world.json");
            var world = File.ReadAllText(worldPath)
                .Replace("\"name\": \"雒阳\"", "\"name\": \"雒阳测试值\"", StringComparison.Ordinal);
            File.WriteAllText(worldPath, world);

            var exception = Assert.Throws<ScenarioValidationException>(() => new ScenarioLoader().Load(copy));

            Assert.Contains(exception.Errors, error => error.Contains("SCN-HASH-001", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public void UnsupportedRandomAlgorithmIsRejectedBeforeWorldConstruction()
    {
        var copy = CreateScenarioCopy();
        try
        {
            var manifestPath = Path.Combine(copy, "manifest.json");
            var manifest = File.ReadAllText(manifestPath)
                .Replace("\"xoshiro256ss.v1\"", "\"system-random\"", StringComparison.Ordinal);
            File.WriteAllText(manifestPath, manifest);

            var exception = Assert.Throws<ScenarioValidationException>(() => new ScenarioLoader().Load(copy));

            Assert.Contains(exception.Errors, error => error.Contains("SCN-RNG-001", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private static string CreateScenarioCopy()
    {
        var destination = Path.Combine(Path.GetTempPath(), $"latehan-scenario-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (var source in Directory.GetFiles(RepositoryFixture.ScenarioDirectory, "*.json"))
        {
            File.Copy(source, Path.Combine(destination, Path.GetFileName(source)));
        }

        return destination;
    }
}
