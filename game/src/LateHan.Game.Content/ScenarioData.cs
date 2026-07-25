using System.Text.Json;
using System.Text.Json.Serialization;
using LateHan.Game.Domain;

namespace LateHan.Game.Content;

public sealed record ScenarioSourceData(
    string Id,
    string Title,
    string Url,
    string VerificationStatus,
    string Limitations);

public sealed record FieldOriginData(
    string Kind,
    IReadOnlyList<string> AppliesTo,
    IReadOnlyList<string> SourceIds,
    string Confidence,
    string Dispute,
    string GameplayAssumption);

public sealed record EntityAuditData(string EntityId, IReadOnlyList<FieldOriginData> Origins);

public sealed record ScenarioDataDocument(
    int SchemaVersion,
    string ContentVersion,
    string Id,
    string Name,
    string Description,
    GameDate StartDate,
    string StartSettlementId,
    string StartUrbanLocationId,
    IReadOnlyList<Settlement> Settlements,
    IReadOnlyList<Road> Roads,
    IReadOnlyList<Character> Characters,
    IReadOnlyList<PlayerBackground> Backgrounds,
    IReadOnlyList<ConversationTopic> Topics,
    IReadOnlyList<SettlementConditionSeed> SettlementConditions,
    IReadOnlyList<RoadConditionSeed> RoadConditions,
    IReadOnlyList<ScenarioSourceData> Sources,
    IReadOnlyList<EntityAuditData> Audit);

public sealed class ScenarioDataException(IReadOnlyList<string> errors)
    : Exception($"场景数据校验失败：{Environment.NewLine}{string.Join(Environment.NewLine, errors)}")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class ScenarioJsonLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new GameDateJsonConverter(), new JsonStringEnumConverter() },
    };

    public static GameScenario Load(string path)
    {
        var document = ReadDocument(path);
        return CreateScenario(document);
    }

    public static GameScenario Load(Stream stream, string sourceName = "场景数据")
    {
        var document = ReadDocument(stream, sourceName);
        return CreateScenario(document);
    }

    public static ScenarioDataDocument ReadDocument(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return ReadDocument(stream, path);
        }
        catch (ScenarioDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ScenarioDataException([$"SCN-IO-001 无法读取场景数据 {path}：{exception.Message}"]);
        }
    }

    public static ScenarioDataDocument ReadDocument(Stream stream, string sourceName = "场景数据")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ScenarioDataDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ScenarioDataDocument>(stream, Options)
                ?? throw new ScenarioDataException([$"SCN-DOC-001 {sourceName} 没有可读取的根对象。"]);
        }
        catch (JsonException exception)
        {
            throw new ScenarioDataException([$"SCN-JSON-001 {sourceName} JSON 无效：{exception.Message}"]);
        }

        ScenarioDataValidator.Validate(document);
        return document;
    }

    private static GameScenario CreateScenario(ScenarioDataDocument document) => new(
        document.Id,
        document.Name,
        document.Description,
        document.StartDate,
        document.StartSettlementId,
        document.StartUrbanLocationId,
        new WorldMap(document.Settlements, document.Roads),
        document.Characters,
        document.Backgrounds,
        document.Topics,
        document.SettlementConditions,
        document.RoadConditions);
}

internal sealed class GameDateJsonConverter : JsonConverter<GameDate>
{
    public override GameDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            return new GameDate(
                root.GetProperty("year").GetInt32(),
                root.GetProperty("month").GetInt32(),
                root.GetProperty("day").GetInt32());
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new JsonException("日期必须包含有效的 year、month 和 day。", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, GameDate value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("year", value.Year);
        writer.WriteNumber("month", value.Month);
        writer.WriteNumber("day", value.Day);
        writer.WriteEndObject();
    }
}

public static class ScenarioDataValidator
{
    private static readonly HashSet<string> OriginKinds =
    [
        "historical_claim",
        "bounded_inference",
        "gameplay_assumption",
        "simulation_seed",
    ];
    private static readonly string[] SettlementFields = ["name", "type", "regionName", "coordinate", "description", "initialCondition"];
    private static readonly string[] CharacterFields = ["name", "courtesyName", "gender", "roles", "identity", "abilities", "traits", "motivations", "initialLocation", "affiliation"];

    public static void Validate(ScenarioDataDocument document)
    {
        var errors = new List<string>();
        if (document.SchemaVersion != 1)
        {
            errors.Add($"SCN-VERSION-001 不支持 schemaVersion {document.SchemaVersion}，当前只接受 1。");
        }
        if (string.IsNullOrWhiteSpace(document.ContentVersion) ||
            !Version.TryParse(document.ContentVersion, out var contentVersion) ||
            contentVersion.Build < 0)
        {
            errors.Add("SCN-VERSION-002 contentVersion 必须使用主版本.次版本.修订号格式。");
        }

        RequireId(document.Id, "scenario", errors);
        RequireChinese(document.Name, "scenario.name", errors);
        RequireChinese(document.Description, "scenario.description", errors);
        RequireUnique(document.Settlements.Select(item => item.Id), "settlement", errors);
        RequireUnique(document.Settlements.SelectMany(item => item.UrbanLocations).Select(item => item.Id), "urbanLocation", errors);
        RequireUnique(document.Roads.Select(item => item.Id), "road", errors);
        RequireUnique(document.Characters.Select(item => item.Id), "character", errors);
        RequireUnique(document.Backgrounds.Select(item => item.Id), "background", errors);
        RequireUnique(document.Topics.Select(item => item.Id), "topic", errors);
        RequireUnique(document.Sources.Select(item => item.Id), "source", errors);
        RequireUnique(document.Audit.Select(item => item.EntityId), "audit", errors);

        var settlements = ToUniqueDictionary(document.Settlements, item => item.Id);
        var roads = ToUniqueDictionary(document.Roads, item => item.Id);
        var sourceIds = document.Sources.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var audits = ToUniqueDictionary(document.Audit, item => item.EntityId);
        foreach (var source in document.Sources)
        {
            RequireId(source.Id, "source", errors);
            RequireChinese(source.Title, $"{source.Id}.title", errors);
            RequireChinese(source.Limitations, $"{source.Id}.limitations", errors);
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme is not ("http" or "https") ||
                string.IsNullOrWhiteSpace(source.VerificationStatus))
            {
                errors.Add($"SCN-SOURCE-001 来源 {source.Id} 的 URL 或核验状态无效。");
            }
        }
        if (!settlements.TryGetValue(document.StartSettlementId, out var startSettlement))
        {
            errors.Add($"SCN-REF-001 开局地点 {document.StartSettlementId} 不存在。");
        }
        else if (!startSettlement.UrbanLocations.Any(item => item.Id == document.StartUrbanLocationId))
        {
            errors.Add($"SCN-REF-002 开局城内地点 {document.StartUrbanLocationId} 不属于 {document.StartSettlementId}。");
        }

        foreach (var settlement in document.Settlements)
        {
            RequireId(settlement.Id, "settlement", errors);
            RequireChinese(settlement.Name, $"{settlement.Id}.name", errors);
            RequireChinese(settlement.RegionName, $"{settlement.Id}.regionName", errors);
            RequireChinese(settlement.Description, $"{settlement.Id}.description", errors);
            RequireUnique(settlement.UrbanLocations.Select(item => item.Id), $"{settlement.Id}.urbanLocation", errors);
            foreach (var location in settlement.UrbanLocations)
            {
                RequireId(location.Id, "urbanLocation", errors);
                RequireChinese(location.Name, $"{location.Id}.name", errors);
                RequireChinese(location.Description, $"{location.Id}.description", errors);
                ValidateAudit(location.Id, ["name", "type", "description"], audits, sourceIds, errors);
            }

            ValidateAudit(settlement.Id, SettlementFields, audits, sourceIds, errors);
        }

        foreach (var road in document.Roads)
        {
            RequireId(road.Id, "road", errors);
            if (!settlements.ContainsKey(road.FromSettlementId) || !settlements.ContainsKey(road.ToSettlementId))
            {
                errors.Add($"SCN-REF-003 道路 {road.Id} 引用了未知城邑。");
            }
            if (road.FromSettlementId == road.ToSettlementId || road.TravelDays is < 1 or > 30)
            {
                errors.Add($"SCN-RANGE-001 道路 {road.Id} 的端点或行程天数无效。");
            }
            RequireChinese(road.Description, $"{road.Id}.description", errors);
            ValidateAudit(road.Id, ["endpoints", "travelDays", "description", "initialRisk"], audits, sourceIds, errors);
        }

        foreach (var character in document.Characters)
        {
            RequireId(character.Id, "character", errors);
            RequireChinese(character.Name, $"{character.Id}.name", errors);
            RequireChinese(character.Identity, $"{character.Id}.identity", errors);
            if (!settlements.TryGetValue(character.SettlementId, out var settlement) || !settlement.UrbanLocations.Any(item => item.Id == character.UrbanLocationId))
            {
                errors.Add($"SCN-REF-004 人物 {character.Id} 的初始位置无效。");
            }
            if (AbilityValues(character.Abilities).Any(value => value is < 1 or > 100))
            {
                errors.Add($"SCN-RANGE-002 人物 {character.Id} 的能力值必须在 1..100。");
            }
            ValidateAudit(character.Id, CharacterFields, audits, sourceIds, errors);
        }

        foreach (var background in document.Backgrounds)
        {
            RequireId(background.Id, "background", errors);
            RequireChinese(background.Name, $"{background.Id}.name", errors);
            RequireChinese(background.Description, $"{background.Id}.description", errors);
            if (background.StartingMoney < 0 || AbilityValues(background.StartingAbilities).Any(value => value is < 1 or > 100))
            {
                errors.Add($"SCN-RANGE-005 身份 {background.Id} 的金钱或能力初态无效。");
            }
            ValidateAudit(background.Id, ["name", "description", "identity", "startingMoney", "startingAbilities", "startingTraits"], audits, sourceIds, errors);
        }
        foreach (var topic in document.Topics)
        {
            RequireId(topic.Id, "topic", errors);
            RequireChinese(topic.Title, $"{topic.Id}.title", errors);
            RequireChinese(topic.Summary, $"{topic.Id}.summary", errors);
            ValidateAudit(topic.Id, ["title", "summary"], audits, sourceIds, errors);
        }

        var conditionIds = document.SettlementConditions.Select(item => item.SettlementId).ToArray();
        if (conditionIds.Length != settlements.Count || conditionIds.Distinct(StringComparer.Ordinal).Count() != settlements.Count || conditionIds.Any(id => !settlements.ContainsKey(id)))
        {
            errors.Add("SCN-REF-005 城邑初态必须与城邑一一对应。");
        }
        if (document.SettlementConditions.SelectMany(item => new[] { item.Security, item.GrainPrice, item.Prosperity, item.GovernmentControl }).Any(value => value is < 0 or > 100))
        {
            errors.Add("SCN-RANGE-003 城邑初态数值必须在 0..100。");
        }
        var roadConditionIds = document.RoadConditions.Select(item => item.RoadId).ToArray();
        if (roadConditionIds.Length != roads.Count || roadConditionIds.Distinct(StringComparer.Ordinal).Count() != roads.Count || roadConditionIds.Any(id => !roads.ContainsKey(id)))
        {
            errors.Add("SCN-REF-006 道路风险初态必须与道路一一对应。");
        }
        if (document.RoadConditions.Any(item => item.Risk is < 0 or > 100))
        {
            errors.Add("SCN-RANGE-004 道路风险必须在 0..100。");
        }
        ValidateConnectivity(document.Settlements, document.Roads, errors);
        ValidateAudit(document.Id, ["name", "description", "startDate", "startLocation"], audits, sourceIds, errors);

        if (errors.Count > 0)
        {
            throw new ScenarioDataException(errors);
        }
    }

    private static void ValidateAudit(string id, IReadOnlyList<string> requiredFields, IReadOnlyDictionary<string, EntityAuditData> audits, IReadOnlySet<string> sourceIds, List<string> errors)
    {
        if (!audits.TryGetValue(id, out var audit) || audit.Origins.Count == 0)
        {
            errors.Add($"SCN-AUDIT-001 {id} 缺少来源审计记录。");
            return;
        }
        var covered = audit.Origins.SelectMany(item => item.AppliesTo).ToHashSet(StringComparer.Ordinal);
        foreach (var field in requiredFields.Where(field => !covered.Contains(field)))
        {
            errors.Add($"SCN-AUDIT-002 {id}.{field} 没有来源或玩法假设覆盖。");
        }
        foreach (var origin in audit.Origins)
        {
            if (!OriginKinds.Contains(origin.Kind) || origin.AppliesTo.Count == 0 ||
                origin.SourceIds.Count == 0 || origin.SourceIds.Any(id => !sourceIds.Contains(id)) ||
                origin.Confidence is not ("A" or "B" or "C" or "D") ||
                string.IsNullOrWhiteSpace(origin.Dispute) || string.IsNullOrWhiteSpace(origin.GameplayAssumption))
            {
                errors.Add($"SCN-AUDIT-003 {id} 的来源、置信度、争议或玩法假设说明无效。");
            }
        }
    }

    private static void ValidateConnectivity(IReadOnlyList<Settlement> settlements, IReadOnlyList<Road> roads, List<string> errors)
    {
        if (settlements.Count == 0)
        {
            errors.Add("SCN-GRAPH-001 场景至少需要一座城邑。");
            return;
        }
        var visited = new HashSet<string>(StringComparer.Ordinal) { settlements[0].Id };
        var queue = new Queue<string>();
        queue.Enqueue(settlements[0].Id);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in roads.Where(item => item.Connects(current)).Select(item => item.OtherEnd(current)).Where(visited.Add))
            {
                queue.Enqueue(next);
            }
        }
        if (visited.Count != settlements.Count)
        {
            errors.Add("SCN-GRAPH-002 城邑道路图不连通。");
        }
    }

    private static void RequireUnique(IEnumerable<string> ids, string kind, List<string> errors)
    {
        var duplicate = ids.GroupBy(item => item, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            errors.Add($"SCN-ID-001 {kind} 存在重复 ID：{duplicate.Key}。");
        }
    }

    private static void RequireId(string id, string kind, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add($"SCN-ID-002 {kind} 的 ID 不得为空。");
        }
    }

    private static void RequireChinese(string text, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Any(character => character is >= '\u3400' and <= '\u9fff'))
        {
            errors.Add($"SCN-L10N-001 {path} 缺少中文玩家文案。");
        }
    }

    private static IReadOnlyDictionary<string, T> ToUniqueDictionary<T>(
        IEnumerable<T> items,
        Func<T, string> keySelector) =>
        items.Where(item => !string.IsNullOrWhiteSpace(keySelector(item)))
            .GroupBy(keySelector, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static IEnumerable<int> AbilityValues(Abilities value) =>
        [value.Command, value.Martial, value.Strategy, value.Administration, value.Diplomacy, value.Learning];
}
