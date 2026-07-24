using System.Text.Json.Nodes;
using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class WorldSnapshotMigrationTests
{
    private const string RequiredRulesetVersion = "latehan.rules.0.1-draft";

    [Fact]
    public void UnknownRulesetReportsRequiredVersionAndAvailableMigrators()
    {
        var store = new WorldSnapshotStore(RequiredRulesetVersion);
        var snapshot = JsonNode.Parse(store.Serialize(RepositoryFixture.CreateEngine().State))!.AsObject();
        snapshot["ruleset_version"] = "latehan.rules.9.0";

        var exception = Assert.Throws<SnapshotCompatibilityException>(() =>
            store.Load(System.Text.Encoding.UTF8.GetBytes(snapshot.ToJsonString())));

        Assert.Equal("ruleset", exception.Component);
        Assert.Equal("latehan.rules.9.0", exception.ActualVersion);
        Assert.Equal(RequiredRulesetVersion, exception.RequiredVersion);
        Assert.Empty(exception.AvailableMigrators);
        Assert.Contains("Available migrators: none", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationNeverOverwritesItsSourceOrAnExistingTarget()
    {
        var store = new WorldSnapshotStore();
        var service = new WorldSnapshotMigrationService(store);
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.json");
        var target = Path.Combine(directory, "target.json");
        try
        {
            store.Save(RepositoryFixture.CreateEngine().State, source);
            File.WriteAllText(target, "sentinel");

            Assert.Throws<InvalidDataException>(() => service.Migrate(source, source));
            Assert.Throws<IOException>(() => service.Migrate(source, target));
            Assert.Equal("sentinel", File.ReadAllText(target));
            Assert.NotEmpty(File.ReadAllBytes(source));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnsupportedMigrationLeavesSourceUntouchedAndCreatesNoTarget()
    {
        var store = new WorldSnapshotStore(RequiredRulesetVersion);
        var service = new WorldSnapshotMigrationService(store);
        var directory = Path.Combine(Path.GetTempPath(), $"latehan-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.json");
        var target = Path.Combine(directory, "target.json");
        try
        {
            var snapshot = JsonNode.Parse(store.Serialize(RepositoryFixture.CreateEngine().State))!.AsObject();
            snapshot["ruleset_version"] = "latehan.rules.9.0";
            File.WriteAllText(source, snapshot.ToJsonString());
            var originalBytes = File.ReadAllBytes(source);

            _ = Assert.Throws<SnapshotCompatibilityException>(() => service.Migrate(source, target));

            Assert.Equal(originalBytes, File.ReadAllBytes(source));
            Assert.False(File.Exists(target));
            Assert.False(File.Exists($"{target}.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingSchemaVersionIsNeverAssumedToBeCurrent()
    {
        var store = new WorldSnapshotStore(RequiredRulesetVersion);
        var snapshot = JsonNode.Parse(store.Serialize(RepositoryFixture.CreateEngine().State))!.AsObject();
        snapshot.Remove("snapshot_schema_version");

        var exception = Assert.Throws<SnapshotCompatibilityException>(() =>
            store.Load(System.Text.Encoding.UTF8.GetBytes(snapshot.ToJsonString())));

        Assert.Equal("schema", exception.Component);
        Assert.Equal(string.Empty, exception.ActualVersion);
        Assert.Equal(WorldSnapshotStore.SchemaVersion, exception.RequiredVersion);
    }
}
