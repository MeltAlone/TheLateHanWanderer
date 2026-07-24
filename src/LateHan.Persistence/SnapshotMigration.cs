using System.Text.Json;

namespace LateHan.Persistence;

public sealed record SnapshotMigrationDescriptor(
    string Id,
    string Component,
    string SourceVersion,
    string TargetVersion);

public interface IWorldSnapshotMigrator
{
    SnapshotMigrationDescriptor Descriptor { get; }

    byte[] Migrate(ReadOnlySpan<byte> sourceSnapshot);
}

public static class SnapshotMigratorRegistry
{
    private static readonly IReadOnlyList<IWorldSnapshotMigrator> Migrators = [];

    public static IReadOnlyList<SnapshotMigrationDescriptor> Available =>
        Migrators.Select(migrator => migrator.Descriptor).ToArray();

    public static SnapshotCompatibilityException Incompatible(
        string component,
        string actualVersion,
        string requiredVersion) =>
        new(
            component,
            actualVersion,
            requiredVersion,
            Available
                .Where(migrator => string.Equals(migrator.Component, component, StringComparison.Ordinal))
                .Select(migrator =>
                    $"{migrator.Id} ({migrator.SourceVersion} -> {migrator.TargetVersion})")
                .ToArray());

    internal static IWorldSnapshotMigrator Find(
        string component,
        string sourceVersion,
        string targetVersion) =>
        Migrators.FirstOrDefault(migrator =>
            string.Equals(migrator.Descriptor.Component, component, StringComparison.Ordinal) &&
            string.Equals(migrator.Descriptor.SourceVersion, sourceVersion, StringComparison.Ordinal) &&
            string.Equals(migrator.Descriptor.TargetVersion, targetVersion, StringComparison.Ordinal))
        ?? throw Incompatible(component, sourceVersion, targetVersion);
}

public sealed record SnapshotMigrationResult(
    string MigratorId,
    string SourcePath,
    string TargetPath);

public sealed class WorldSnapshotMigrationService
{
    private readonly WorldSnapshotStore _snapshotStore;

    public WorldSnapshotMigrationService(WorldSnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
    }

    public SnapshotMigrationResult Migrate(string sourcePath, string targetPath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Snapshot migration must write to a different target file.");
        }

        if (File.Exists(fullTargetPath))
        {
            throw new IOException($"Snapshot migration target already exists: '{fullTargetPath}'.");
        }

        var sourceBytes = File.ReadAllBytes(fullSourcePath);
        var compatibility = InspectCompatibility(sourceBytes);
        var migrator = SnapshotMigratorRegistry.Find(
            compatibility.Component,
            compatibility.ActualVersion,
            compatibility.RequiredVersion);
        var migrated = migrator.Migrate(sourceBytes);
        _ = _snapshotStore.Load(migrated);

        var directory = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{fullTargetPath}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, migrated);
            File.Move(temporaryPath, fullTargetPath);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }

        return new SnapshotMigrationResult(
            migrator.Descriptor.Id,
            fullSourcePath,
            fullTargetPath);
    }

    private SnapshotCompatibilityException InspectCompatibility(ReadOnlySpan<byte> sourceSnapshot)
    {
        try
        {
            using var document = JsonDocument.Parse(sourceSnapshot.ToArray());
            var root = document.RootElement;
            return FindMismatch(
                       root,
                       "snapshot_schema_version",
                       "schema",
                       WorldSnapshotStore.SchemaVersion) ??
                   FindMismatch(
                       root,
                       "engine_version",
                       "engine",
                       LateHan.Core.EngineMetadata.Version) ??
                   (_snapshotStore.RequiredRulesetVersion is null
                       ? null
                       : FindMismatch(
                           root,
                           "ruleset_version",
                           "ruleset",
                           _snapshotStore.RequiredRulesetVersion)) ??
                   FindMismatch(
                       root,
                       "rng_version",
                       "rng",
                       LateHan.Core.RandomMetadata.Xoshiro256StarStarV1) ??
                   throw new InvalidDataException("Snapshot is already compatible and does not require migration.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Snapshot migration source is not valid JSON.", exception);
        }
    }

    private static SnapshotCompatibilityException? FindMismatch(
        JsonElement root,
        string propertyName,
        string component,
        string requiredVersion)
    {
        var actualVersion = root.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
        return string.Equals(actualVersion, requiredVersion, StringComparison.Ordinal)
            ? null
            : SnapshotMigratorRegistry.Incompatible(component, actualVersion, requiredVersion);
    }
}
