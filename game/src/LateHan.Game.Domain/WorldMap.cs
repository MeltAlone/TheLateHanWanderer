namespace LateHan.Game.Domain;

public enum SettlementType
{
    Capital,
    CommanderySeat,
    CountySeat,
    Pass,
    Ferry,
}

public enum UrbanLocationType
{
    GovernmentOffice,
    Inn,
    Market,
    Residence,
    School,
    Barracks,
    Tavern,
    Temple,
}

public readonly record struct MapCoordinate(int X, int Y);

public sealed record UrbanLocation(
    string Id,
    string Name,
    UrbanLocationType Type,
    string Description);

public sealed record Settlement(
    string Id,
    string Name,
    SettlementType Type,
    string RegionName,
    MapCoordinate Coordinate,
    string Description,
    IReadOnlyList<UrbanLocation> UrbanLocations);

public sealed record Road(
    string Id,
    string FromSettlementId,
    string ToSettlementId,
    int TravelDays,
    string Description)
{
    public bool Connects(string settlementId) =>
        FromSettlementId == settlementId || ToSettlementId == settlementId;

    public string OtherEnd(string settlementId) => settlementId switch
    {
        var id when id == FromSettlementId => ToSettlementId,
        var id when id == ToSettlementId => FromSettlementId,
        _ => throw new InvalidOperationException($"地点 {settlementId} 不在道路 {Id} 上。"),
    };
}

public sealed record SettlementConditionSeed(
    string SettlementId,
    int Security,
    int GrainPrice,
    int Prosperity,
    int GovernmentControl);

public sealed record RoadConditionSeed(string RoadId, int Risk);

public sealed class WorldMap
{
    private readonly IReadOnlyDictionary<string, Settlement> settlements;

    public WorldMap(IReadOnlyList<Settlement> settlements, IReadOnlyList<Road> roads)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(roads);
        this.settlements = settlements.ToDictionary(item => item.Id, StringComparer.Ordinal);
        Settlements = settlements;
        Roads = roads;

        foreach (var road in roads)
        {
            if (!this.settlements.ContainsKey(road.FromSettlementId) || !this.settlements.ContainsKey(road.ToSettlementId))
            {
                throw new ArgumentException($"道路 {road.Id} 引用了不存在的地点。", nameof(roads));
            }

            if (road.TravelDays < 1)
            {
                throw new ArgumentException($"道路 {road.Id} 的行程必须至少为一日。", nameof(roads));
            }
        }
    }

    public IReadOnlyList<Settlement> Settlements { get; }

    public IReadOnlyList<Road> Roads { get; }

    public Settlement GetSettlement(string id) => settlements.TryGetValue(id, out var settlement)
        ? settlement
        : throw new KeyNotFoundException($"未知地点：{id}");

    public IReadOnlyList<(Settlement Destination, Road Road)> GetDestinations(string fromSettlementId) => Roads
        .Where(road => road.Connects(fromSettlementId))
        .Select(road => (GetSettlement(road.OtherEnd(fromSettlementId)), road))
        .OrderBy(item => item.Item2.TravelDays)
        .ThenBy(item => item.Item1.Name, StringComparer.Ordinal)
        .ToArray();
}
