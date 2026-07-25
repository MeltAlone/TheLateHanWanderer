using System.Text.Json;
using System.Text.Json.Nodes;
using LateHan.Game.Content;

namespace LateHan.Game.Content.Tests;

public sealed class DemoScenarioTests
{
    [Fact]
    public void DefaultJsonHasTheAgreedStrategicScale()
    {
        var scenario = DemoScenarioFactory.Create();

        Assert.Equal(8, scenario.Map.Settlements.Count);
        Assert.Equal(24, scenario.Characters.Count);
        Assert.Equal((189, 8, 18), (scenario.StartDate.Year, scenario.StartDate.Month, scenario.StartDate.Day));
        Assert.Equal(3, scenario.Backgrounds.Count);
        Assert.Equal(4, scenario.Topics.Count);
        Assert.Equal(scenario.Map.Settlements.Count, scenario.SettlementConditions.Count);
        Assert.Equal(scenario.Map.Roads.Count, scenario.RoadConditions.Count);
        Assert.All(scenario.Map.Settlements, settlement => Assert.NotEmpty(settlement.UrbanLocations));
    }

    [Fact]
    public void DefaultJsonIsCopiedBesideTheContentAssembly()
    {
        var path = DemoScenarioFactory.ResolveScenarioPath();

        Assert.True(File.Exists(path), $"场景文件未复制到输出目录：{path}");
        Assert.EndsWith(Path.Combine("Data", DemoScenarioFactory.DefaultScenarioFileName), path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EverySensitiveEntityHasCompleteAuditMetadata()
    {
        var document = ScenarioJsonLoader.ReadDocument(DemoScenarioFactory.ResolveScenarioPath());
        var expectedIds = new[] { document.Id }
            .Concat(document.Settlements.Select(item => item.Id))
            .Concat(document.Settlements.SelectMany(item => item.UrbanLocations).Select(item => item.Id))
            .Concat(document.Roads.Select(item => item.Id))
            .Concat(document.Characters.Select(item => item.Id))
            .Concat(document.Backgrounds.Select(item => item.Id))
            .Concat(document.Topics.Select(item => item.Id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedIds, document.Audit.Select(item => item.EntityId).Order(StringComparer.Ordinal));
        Assert.All(document.Audit.SelectMany(item => item.Origins), origin =>
        {
            Assert.NotEmpty(origin.AppliesTo);
            Assert.NotEmpty(origin.SourceIds);
            Assert.Contains(origin.Confidence, new[] { "A", "B", "C", "D" });
            Assert.False(string.IsNullOrWhiteSpace(origin.Dispute));
            Assert.False(string.IsNullOrWhiteSpace(origin.GameplayAssumption));
        });
    }

    [Fact]
    public void ExplicitJsonCanChangePlayerFacingContentWithoutRecompiling()
    {
        var root = ReadDefaultJson();
        root["name"] = "可替换的京畿测试场景";

        WithScenarioFile(root, path =>
        {
            var scenario = DemoScenarioFactory.Create(path);

            Assert.Equal("可替换的京畿测试场景", scenario.Name);
        });
    }

    [Fact]
    public void DuplicateIdIsRejectedWithStableErrorCode()
    {
        var root = ReadDefaultJson();
        var settlements = root["settlements"]!.AsArray();
        settlements.Add(settlements[0]!.DeepClone());

        AssertInvalid(root, "SCN-ID-001");
    }

    [Fact]
    public void UnknownRoadEndpointIsRejected()
    {
        var root = ReadDefaultJson();
        root["roads"]![0]!["fromSettlementId"] = "settlement.unknown";

        AssertInvalid(root, "SCN-REF-003");
    }

    [Fact]
    public void UnknownCharacterLocationIsRejected()
    {
        var root = ReadDefaultJson();
        root["characters"]![0]!["urbanLocationId"] = "luoyang.unknown";

        AssertInvalid(root, "SCN-REF-004");
    }

    [Fact]
    public void DisconnectedRoadGraphIsRejected()
    {
        var root = ReadDefaultJson();
        RemoveById(root["roads"]!.AsArray(), "road.mengjin.henei", "id");
        RemoveById(root["roadConditions"]!.AsArray(), "road.mengjin.henei", "roadId");

        AssertInvalid(root, "SCN-GRAPH-002");
    }

    [Fact]
    public void MissingChinesePlayerTextIsRejected()
    {
        var root = ReadDefaultJson();
        root["name"] = "Central Plains";

        AssertInvalid(root, "SCN-L10N-001");
    }

    [Fact]
    public void MissingAuditFieldCoverageIsRejected()
    {
        var root = ReadDefaultJson();
        var scenarioAudit = FindById(root["audit"]!.AsArray(), "scenario.189.central_plains", "entityId");
        scenarioAudit["origins"]![0]!["appliesTo"] = new JsonArray("name", "description", "startDate");

        AssertInvalid(root, "SCN-AUDIT-002");
    }

    [Fact]
    public void UnknownSourceIdIsRejected()
    {
        var root = ReadDefaultJson();
        var scenarioAudit = FindById(root["audit"]!.AsArray(), "scenario.189.central_plains", "entityId");
        scenarioAudit["origins"]![0]!["sourceIds"] = new JsonArray("source.unknown");

        AssertInvalid(root, "SCN-AUDIT-003");
    }

    [Fact]
    public void CharacterAbilityOutsideRangeIsRejected()
    {
        var root = ReadDefaultJson();
        root["characters"]![0]!["abilities"]!["strategy"] = 101;

        AssertInvalid(root, "SCN-RANGE-002");
    }

    [Fact]
    public void SettlementConditionOutsideRangeIsRejected()
    {
        var root = ReadDefaultJson();
        root["settlementConditions"]![0]!["grainPrice"] = -1;

        AssertInvalid(root, "SCN-RANGE-003");
    }

    [Fact]
    public void BackgroundAbilityOutsideRangeIsRejected()
    {
        var root = ReadDefaultJson();
        root["backgrounds"]![0]!["startingAbilities"]!["learning"] = 0;

        AssertInvalid(root, "SCN-RANGE-005");
    }

    [Fact]
    public void InvalidJsonIsWrappedAsScenarioError()
    {
        using var stream = new MemoryStream("{"u8.ToArray());

        var exception = Assert.Throws<ScenarioDataException>(() => ScenarioJsonLoader.Load(stream, "损坏测试"));

        Assert.Contains(exception.Errors, error => error.StartsWith("SCN-JSON-001", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidCalendarDateIsRejectedDuringJsonLoading()
    {
        var root = ReadDefaultJson();
        root["startDate"]!["month"] = 13;

        AssertInvalid(root, "SCN-JSON-001");
    }

    private static JsonObject ReadDefaultJson() =>
        JsonNode.Parse(File.ReadAllText(DemoScenarioFactory.ResolveScenarioPath()))!.AsObject();

    private static void AssertInvalid(JsonObject root, string errorCode) => WithScenarioFile(root, path =>
    {
        var exception = Assert.Throws<ScenarioDataException>(() => ScenarioJsonLoader.Load(path));
        Assert.Contains(exception.Errors, error => error.StartsWith(errorCode, StringComparison.Ordinal));
    });

    private static void WithScenarioFile(JsonObject root, Action<string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-content-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "scenario.json");
        try
        {
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            assertion(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonObject FindById(JsonArray items, string id, string propertyName) =>
        items.Select(item => item!.AsObject()).Single(item => item[propertyName]!.GetValue<string>() == id);

    private static void RemoveById(JsonArray items, string id, string propertyName) =>
        items.Remove(FindById(items, id, propertyName));
}
