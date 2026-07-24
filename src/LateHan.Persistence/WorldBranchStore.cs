using System.Text.Json;
using System.Text.Json.Serialization;
using LateHan.Core;

namespace LateHan.Persistence;

public sealed class WorldBranchStore : IDisposable
{
    public const string SchemaVersion = "1.0";

    private const string DescriptorFileName = "branch.json";
    private const string TailArchiveFileName = "events.db";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly WorldEventArchive _baseArchive;
    private readonly WorldEventArchive _tailArchive;
    private readonly EventArchiveCheckpoint _baseCheckpoint;
    private readonly WorldSnapshotStore _snapshotStore = new();
    private bool _disposed;

    private WorldBranchStore(
        string directory,
        WorldBranchDescriptor descriptor,
        WorldEventArchive baseArchive,
        WorldEventArchive tailArchive,
        EventArchiveCheckpoint baseCheckpoint)
    {
        Directory = directory;
        Descriptor = descriptor;
        _baseArchive = baseArchive;
        _tailArchive = tailArchive;
        _baseCheckpoint = baseCheckpoint;
    }

    public string Directory { get; }

    public WorldBranchDescriptor Descriptor { get; }

    public WorldEventArchive TailArchive => _tailArchive;

    public static WorldBranchStore Create(string baseArchivePath, string directory, string branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            throw new ArgumentException("Branch ID cannot be empty.", nameof(branchId));
        }

        var fullDirectory = Path.GetFullPath(directory);
        System.IO.Directory.CreateDirectory(fullDirectory);
        var descriptorPath = Path.Combine(fullDirectory, DescriptorFileName);
        if (File.Exists(descriptorPath))
        {
            throw new InvalidOperationException($"Branch descriptor '{descriptorPath}' already exists.");
        }

        using var baseArchive = WorldEventArchive.OpenReadOnly(baseArchivePath);
        var checkpoint = baseArchive.LatestCheckpoint
            ?? throw new InvalidDataException("A branch base archive must have a checkpoint.");
        if (checkpoint.EventSequence != baseArchive.LastSequence)
        {
            throw new InvalidDataException("A branch base archive must be checkpointed at its latest event.");
        }

        var descriptor = new WorldBranchDescriptor(
            SchemaVersion,
            branchId,
            Path.GetFullPath(baseArchivePath),
            checkpoint.EventSequence,
            checkpoint.EventFingerprint,
            ComputeHash(checkpoint.SnapshotPayload),
            new FileInfo(baseArchivePath).Length,
            File.GetLastWriteTimeUtc(baseArchivePath).Ticks);
        var temporaryPath = $"{descriptorPath}.tmp";
        File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(descriptor, JsonOptions));
        File.Move(temporaryPath, descriptorPath, overwrite: false);
        using (var tailArchive = new WorldEventArchive(
                   Path.Combine(fullDirectory, TailArchiveFileName),
                   checkpoint.EventSequence))
        {
        }

        return Open(fullDirectory);
    }

    public static WorldBranchStore Open(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var descriptorPath = Path.Combine(fullDirectory, DescriptorFileName);
        var descriptor = JsonSerializer.Deserialize<WorldBranchDescriptor>(
            File.ReadAllBytes(descriptorPath), JsonOptions)
            ?? throw new InvalidDataException($"Branch descriptor '{descriptorPath}' is empty.");
        if (!string.Equals(descriptor.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported branch schema '{descriptor.SchemaVersion}'.");
        }

        var baseArchive = WorldEventArchive.OpenReadOnly(descriptor.BaseArchivePath);
        try
        {
            var checkpoint = baseArchive.LatestCheckpoint
                ?? throw new InvalidDataException("Branch base checkpoint is missing.");
            if (baseArchive.LastSequence != descriptor.BaseEventSequence ||
                checkpoint.EventSequence != descriptor.BaseEventSequence ||
                !string.Equals(
                    checkpoint.EventFingerprint,
                    descriptor.BaseEventFingerprint,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    ComputeHash(checkpoint.SnapshotPayload),
                    descriptor.BaseSnapshotHash,
                    StringComparison.Ordinal) ||
                new FileInfo(descriptor.BaseArchivePath).Length != descriptor.BaseArchiveLength ||
                File.GetLastWriteTimeUtc(descriptor.BaseArchivePath).Ticks != descriptor.BaseArchiveLastWriteUtcTicks)
            {
                throw new InvalidDataException(
                    $"Branch '{descriptor.BranchId}' base archive changed after branch creation.");
            }

            var tailArchive = new WorldEventArchive(
                Path.Combine(fullDirectory, TailArchiveFileName),
                descriptor.BaseEventSequence);
            return new WorldBranchStore(fullDirectory, descriptor, baseArchive, tailArchive, checkpoint);
        }
        catch
        {
            baseArchive.Dispose();
            throw;
        }
    }

    public WorldState Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var checkpoint = _tailArchive.LatestCheckpoint ?? _baseCheckpoint;
        return _snapshotStore.Load(checkpoint.SnapshotPayload);
    }

    public EventArchiveCheckpoint Save(WorldState world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var lastSequence = world.EventSequenceCursor - 1;
        if (lastSequence <= _tailArchive.LastSequence)
        {
            throw new InvalidDataException(
                $"Branch '{Descriptor.BranchId}' has no new events after '{_tailArchive.LastSequence}'.");
        }

        var newEvents = world.Events
            .Where(worldEvent => worldEvent.Sequence > _tailArchive.LastSequence)
            .OrderBy(worldEvent => worldEvent.Sequence)
            .ToArray();
        _tailArchive.Append(newEvents);
        return _tailArchive.CreateCheckpoint(
            lastSequence,
            world.ComputeEventFingerprint(),
            _snapshotStore.Serialize(world));
    }

    public WorldEvent? Find(string eventId) => _tailArchive.Find(eventId) ?? _baseArchive.Find(eventId);

    public IReadOnlyList<CausalEvent> Why(string eventId, int maximumDepth = 8, int maximumEvents = 256)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEvents, 1);
        var results = new List<CausalEvent>();
        var pending = new Queue<(string EventId, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Enqueue((eventId, 0));
        while (pending.Count > 0 && results.Count < maximumEvents)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.EventId))
            {
                continue;
            }

            var worldEvent = Find(current.EventId);
            if (worldEvent is null)
            {
                continue;
            }

            results.Add(new CausalEvent(worldEvent, current.Depth));
            if (current.Depth >= maximumDepth)
            {
                continue;
            }

            foreach (var causeId in worldEvent.CauseIds)
            {
                pending.Enqueue((causeId, current.Depth + 1));
            }
        }

        return results;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tailArchive.Dispose();
        _baseArchive.Dispose();
    }

    private static string ComputeHash(ReadOnlySpan<byte> value) =>
        $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value)).ToLowerInvariant()}";
}

public sealed record WorldBranchDescriptor(
    string SchemaVersion,
    string BranchId,
    string BaseArchivePath,
    long BaseEventSequence,
    string BaseEventFingerprint,
    string BaseSnapshotHash,
    long BaseArchiveLength,
    long BaseArchiveLastWriteUtcTicks);
