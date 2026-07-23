using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LateHan.Scenarios;

public static class CanonicalJson
{
    public static string ComputeScenarioHash(string scenarioDirectory, IEnumerable<string> componentNames)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = componentNames
            .Append("manifest.json")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var fileName in files)
        {
            var path = Path.Combine(scenarioDirectory, fileName);
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var canonical = Canonicalize(document.RootElement, fileName == "manifest.json");
            hash.AppendData(Encoding.UTF8.GetBytes(fileName));
            hash.AppendData([0]);
            hash.AppendData(canonical);
            hash.AppendData([0]);
        }

        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    public static byte[] Canonicalize(JsonElement root, bool omitRootContentHash = false)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });
        WriteElement(writer, root, omitRootContentHash, isRoot: true);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, bool omitRootContentHash, bool isRoot)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (isRoot && omitRootContentHash && property.NameEquals("content_hash"))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value, omitRootContentHash: false, isRoot: false);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item, omitRootContentHash: false, isRoot: false);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    writer.WriteNumberValue(integer);
                }
                else if (element.TryGetDecimal(out var decimalValue))
                {
                    writer.WriteRawValue(decimalValue.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    throw new ScenarioValidationException(["SCN-NUM-005 Floating-point scenario numbers are not supported."]);
                }

                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new ScenarioValidationException([$"SCN-JSON-002 Unsupported JSON token '{element.ValueKind}'."]);
        }
    }
}
