using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LateHan.Core;

public sealed class WorldEventFingerprint : IDisposable
{
    private const int StackBufferSize = 256;
    private static readonly byte[] Separator = [0];
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _completed;

    public void Append(WorldEvent worldEvent)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        AppendValue(worldEvent.Sequence.ToString(CultureInfo.InvariantCulture));
        AppendValue(worldEvent.Id);
        AppendValue(worldEvent.Type);
        AppendValue(worldEvent.Minute.ToString(CultureInfo.InvariantCulture));
        AppendValue(worldEvent.LocationId ?? string.Empty);
        foreach (var subject in worldEvent.SubjectIds)
        {
            AppendValue(subject);
        }

        foreach (var cause in worldEvent.CauseIds)
        {
            AppendValue(cause);
        }

        foreach (var detail in worldEvent.Details.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendValue(detail.Key);
            AppendValue(detail.Value);
        }
    }

    public string Complete()
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        _completed = true;
        return $"sha256:{Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    public static string Compute(IEnumerable<WorldEvent> events)
    {
        using var fingerprint = new WorldEventFingerprint();
        foreach (var worldEvent in events)
        {
            fingerprint.Append(worldEvent);
        }

        return fingerprint.Complete();
    }

    public void Dispose()
    {
        _completed = true;
        _hash.Dispose();
    }

    private void AppendValue(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount <= StackBufferSize)
        {
            Span<byte> buffer = stackalloc byte[StackBufferSize];
            var written = Encoding.UTF8.GetBytes(value, buffer);
            _hash.AppendData(buffer[..written]);
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = Encoding.UTF8.GetBytes(value, buffer);
                _hash.AppendData(buffer.AsSpan(0, written));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        _hash.AppendData(Separator);
    }
}
