using System.Text.Json;
using System.Text.Json.Serialization;
using LateHan.Game.Domain;
using LateHan.Game.Simulation;

namespace LateHan.Game.Persistence;

public interface IGameSaveStore
{
    void Save(string path, GameSnapshot snapshot);

    GameSnapshot Load(string path);
}

public sealed class JsonGameSaveStore : IGameSaveStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
            new GameDateJsonConverter(),
        },
    };

    public void Save(string path, GameSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("存档路径没有有效目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, Options));
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public GameSnapshot Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var snapshot = JsonSerializer.Deserialize<GameSnapshot>(File.ReadAllText(path), Options);
        return snapshot ?? throw new InvalidDataException("存档内容为空或无法解析。");
    }
}

internal sealed class GameDateJsonConverter : JsonConverter<GameDate>
{
    public override GameDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("日期必须是对象。 ");
        }

        var year = 0;
        var month = 0;
        var day = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "year":
                    year = reader.GetInt32();
                    break;
                case "month":
                    month = reader.GetInt32();
                    break;
                case "day":
                    day = reader.GetInt32();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new GameDate(year, month, day);
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
