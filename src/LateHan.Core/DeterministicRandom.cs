using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace LateHan.Core;

public static class RandomMetadata
{
    public const string Xoshiro256StarStarV1 = "xoshiro256ss.v1";
    public const string Sha256LittleEndianV1 = "sha256-le.v1";
}

public sealed class RandomStreamState
{
    public RandomStreamState(string key, ulong state0, ulong state1, ulong state2, ulong state3, ulong drawCount = 0)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Random stream key cannot be empty.", nameof(key));
        }

        if ((state0 | state1 | state2 | state3) == 0)
        {
            throw new ArgumentException("A xoshiro256** state cannot contain four zero words.", nameof(state0));
        }

        Key = key;
        State0 = state0;
        State1 = state1;
        State2 = state2;
        State3 = state3;
        DrawCount = drawCount;
    }

    public string Key { get; }

    public ulong State0 { get; internal set; }

    public ulong State1 { get; internal set; }

    public ulong State2 { get; internal set; }

    public ulong State3 { get; internal set; }

    public ulong DrawCount { get; internal set; }

    public RandomStreamState Copy() => new(Key, State0, State1, State2, State3, DrawCount);
}

public sealed class RandomStreamRegistry
{
    private readonly SortedDictionary<string, RandomStreamState> _streams;

    public RandomStreamRegistry(
        string version,
        string rootSeedHex,
        string derivation,
        IEnumerable<RandomStreamState>? streams = null)
    {
        if (!string.Equals(version, RandomMetadata.Xoshiro256StarStarV1, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported random algorithm '{version}'.", nameof(version));
        }

        if (!string.Equals(derivation, RandomMetadata.Sha256LittleEndianV1, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported random stream derivation '{derivation}'.", nameof(derivation));
        }

        if (!ulong.TryParse(rootSeedHex, System.Globalization.NumberStyles.AllowHexSpecifier, null, out _))
        {
            throw new ArgumentException("Root seed must contain at most 16 hexadecimal digits.", nameof(rootSeedHex));
        }

        Version = version;
        RootSeedHex = rootSeedHex.ToUpperInvariant().PadLeft(16, '0');
        Derivation = derivation;
        _streams = new SortedDictionary<string, RandomStreamState>(StringComparer.Ordinal);
        foreach (var stream in streams ?? [])
        {
            _streams.Add(stream.Key, stream.Copy());
        }
    }

    public string Version { get; }

    public string RootSeedHex { get; }

    public string Derivation { get; }

    public IReadOnlyDictionary<string, RandomStreamState> Streams =>
        new ReadOnlyDictionary<string, RandomStreamState>(_streams);

    public ulong NextUInt64(string domain, string stableEntityId)
    {
        var stream = GetOrCreate(domain, stableEntityId);
        return Next(stream);
    }

    public IReadOnlyList<ulong> PreviewUInt64(string domain, string stableEntityId, int count)
    {
        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Preview count must be between 1 and 100.");
        }

        var key = BuildKey(domain, stableEntityId);
        var preview = _streams.TryGetValue(key, out var existing)
            ? existing.Copy()
            : Derive(domain, stableEntityId, key);
        var values = new ulong[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = Next(preview);
        }

        return values;
    }

    public static ulong Next(RandomStreamState state)
    {
        var result = RotateLeft(state.State1 * 5, 7) * 9;
        var temporary = state.State1 << 17;

        state.State2 ^= state.State0;
        state.State3 ^= state.State1;
        state.State1 ^= state.State2;
        state.State0 ^= state.State3;
        state.State2 ^= temporary;
        state.State3 = RotateLeft(state.State3, 45);
        state.DrawCount++;
        return result;
    }

    private RandomStreamState GetOrCreate(string domain, string stableEntityId)
    {
        var key = BuildKey(domain, stableEntityId);
        if (!_streams.TryGetValue(key, out var stream))
        {
            stream = Derive(domain, stableEntityId, key);
            _streams.Add(key, stream);
        }

        return stream;
    }

    private RandomStreamState Derive(string domain, string stableEntityId, string key)
    {
        var material = $"{Version}\0{RootSeedHex}\0{domain}\0{stableEntityId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new RandomStreamState(
            key,
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(24, 8)));
    }

    private static string BuildKey(string domain, string stableEntityId)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Contains('\0') || domain.Contains(':'))
        {
            throw new ArgumentException("Random domain cannot be empty or contain NUL or colon.", nameof(domain));
        }

        if (string.IsNullOrWhiteSpace(stableEntityId) || stableEntityId.Contains('\0') || stableEntityId.Contains(':'))
        {
            throw new ArgumentException("Stable entity ID cannot be empty or contain NUL or colon.", nameof(stableEntityId));
        }

        return $"{domain}:{stableEntityId}";
    }

    private static ulong RotateLeft(ulong value, int count) =>
        (value << count) | (value >> (64 - count));
}
