using LateHan.Game.Content;

namespace LateHan.Game.Content.Tests;

public sealed class DemoScenarioTests
{
    [Fact]
    public void DemoHasTheAgreedStrategicScale()
    {
        var scenario = DemoScenarioFactory.Create();

        Assert.Equal(8, scenario.Map.Settlements.Count);
        Assert.Equal(3, scenario.Backgrounds.Count);
        Assert.Equal(4, scenario.Topics.Count);
        Assert.InRange(scenario.Characters.Count, 20, 30);
        Assert.Equal(scenario.Map.Settlements.Count, scenario.SettlementConditions.Count);
        Assert.Equal(scenario.Map.Roads.Count, scenario.RoadConditions.Count);
        Assert.All(scenario.Map.Settlements, settlement => Assert.NotEmpty(settlement.UrbanLocations));
    }

    [Fact]
    public void RoadsAndCharactersReferenceKnownPlaces()
    {
        var scenario = DemoScenarioFactory.Create();
        var settlementIds = scenario.Map.Settlements.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        Assert.All(scenario.Map.Roads, road =>
        {
            Assert.Contains(road.FromSettlementId, settlementIds);
            Assert.Contains(road.ToSettlementId, settlementIds);
        });
        Assert.All(scenario.Characters, character =>
        {
            var settlement = scenario.Map.GetSettlement(character.SettlementId);
            Assert.Contains(settlement.UrbanLocations, item => item.Id == character.UrbanLocationId);
        });
    }

    [Fact]
    public void AllPlayerFacingContentIsNamed()
    {
        var scenario = DemoScenarioFactory.Create();

        Assert.All(scenario.Map.Settlements, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
        });
        Assert.All(scenario.Characters, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
        Assert.All(scenario.Backgrounds, item => Assert.False(string.IsNullOrWhiteSpace(item.Description)));
        Assert.All(scenario.Topics, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Summary));
        });
    }
}
