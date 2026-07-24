using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using LateHan.Core;
using Microsoft.Data.Sqlite;

namespace LateHan.Persistence;

public sealed record EventArchiveCheckpoint(
    long EventSequence,
    string EventId,
    string EventFingerprint,
    byte[] SnapshotPayload);

public sealed record EventArchiveRestore(
    EventArchiveCheckpoint Checkpoint,
    IReadOnlyList<WorldEvent> EventsAfterCheckpoint);

public sealed record CausalEvent(WorldEvent Event, int Depth);

public sealed record EventArchiveAudit(long EventCount, long LastSequence, string EventFingerprint);

public sealed class WorldEventArchive : IDisposable
{
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private readonly SqliteConnection _connection;
    private readonly string _path;
    private bool _disposed;

    public WorldEventArchive(string path)
    {
        _path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _connection.Open();
        ConfigureConnection();
        EnsureSchema();
    }

    public long EventCount => ExecuteInt64("SELECT COUNT(*) FROM events;");

    public long LastSequence => ExecuteInt64("SELECT COALESCE(MAX(sequence), 0) FROM events;");

    public long CheckpointCount => ExecuteInt64("SELECT COUNT(*) FROM checkpoints;");

    public void Append(IReadOnlyList<WorldEvent> events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (events.Count == 0)
        {
            return;
        }

        var expectedSequence = LastSequence + 1;
        using var transaction = _connection.BeginTransaction();
        using var insertEvent = CreateInsertEventCommand(transaction);
        using var insertCause = CreateInsertCauseCommand(transaction);
        foreach (var worldEvent in events)
        {
            if (worldEvent.Sequence != expectedSequence)
            {
                throw new InvalidDataException(
                    $"Event sequence '{worldEvent.Sequence}' must follow archive sequence '{expectedSequence - 1}'.");
            }

            BindEvent(insertEvent, worldEvent);
            insertEvent.ExecuteNonQuery();
            for (var index = 0; index < worldEvent.CauseIds.Count; index++)
            {
                insertCause.Parameters["$event_sequence"].Value = worldEvent.Sequence;
                insertCause.Parameters["$ordinal"].Value = index;
                insertCause.Parameters["$cause_id"].Value = worldEvent.CauseIds[index];
                insertCause.ExecuteNonQuery();
            }

            expectedSequence++;
        }

        transaction.Commit();
    }

    public WorldEvent? Find(string eventId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, id, type, minute, location_id, subjects_json, causes_json, details_json
            FROM events
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", eventId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEvent(reader) : null;
    }

    public IReadOnlyList<WorldEvent> ReadAfter(long sequence, int maximumCount = 25_000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, id, type, minute, location_id, subjects_json, causes_json, details_json
            FROM events
            WHERE sequence > $sequence
            ORDER BY sequence
            LIMIT $maximum_count;
            """;
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$maximum_count", maximumCount);
        return ReadEventList(command);
    }

    public IReadOnlyList<CausalEvent> Why(string eventId, int maximumDepth = 8, int maximumEvents = 256)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEvents, 1);
        using var command = _connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE causal(event_id, depth, path) AS (
                SELECT $event_id, 0, '|' || $event_id || '|'
                UNION ALL
                SELECT causes.cause_id,
                       causal.depth + 1,
                       causal.path || causes.cause_id || '|'
                FROM causal
                JOIN events current_event ON current_event.id = causal.event_id
                JOIN event_causes causes ON causes.event_sequence = current_event.sequence
                WHERE causal.depth < $maximum_depth
                  AND instr(causal.path, '|' || causes.cause_id || '|') = 0
            ), depths AS (
                SELECT event_id, MIN(depth) AS depth
                FROM causal
                GROUP BY event_id
            )
            SELECT events.sequence,
                   events.id,
                   events.type,
                   events.minute,
                   events.location_id,
                   events.subjects_json,
                   events.causes_json,
                   events.details_json,
                   depths.depth
            FROM depths
            JOIN events ON events.id = depths.event_id
            ORDER BY depths.depth, events.sequence DESC
            LIMIT $maximum_events;
            """;
        command.Parameters.AddWithValue("$event_id", eventId);
        command.Parameters.AddWithValue("$maximum_depth", maximumDepth);
        command.Parameters.AddWithValue("$maximum_events", maximumEvents);
        var results = new List<CausalEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CausalEvent(
                ReadEvent(reader),
                reader.GetInt32(8)));
        }

        return results;
    }

    public EventArchiveCheckpoint CreateCheckpoint(
        long eventSequence,
        string eventFingerprint,
        ReadOnlySpan<byte> snapshotPayload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (eventSequence <= 0 || eventSequence > LastSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(eventSequence));
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO checkpoints(event_sequence, event_id, event_fingerprint, snapshot_payload)
            SELECT sequence, id, $event_fingerprint, $snapshot_payload
            FROM events
            WHERE sequence = $event_sequence;
            """;
        command.Parameters.AddWithValue("$event_sequence", eventSequence);
        command.Parameters.AddWithValue("$event_fingerprint", eventFingerprint);
        command.Parameters.Add("$snapshot_payload", SqliteType.Blob).Value = snapshotPayload.ToArray();
        try
        {
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidDataException($"Cannot checkpoint missing event sequence '{eventSequence}'.");
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidDataException(
                $"Checkpoint for event sequence '{eventSequence}' already exists.",
                exception);
        }

        return LoadLatestCheckpoint()!;
    }

    public EventArchiveRestore? RestoreLatest(int maximumTailEvents = 25_000)
    {
        var checkpoint = LoadLatestCheckpoint();
        if (checkpoint is null)
        {
            return null;
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events WHERE sequence > $sequence;";
        command.Parameters.AddWithValue("$sequence", checkpoint.EventSequence);
        var tailCount = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (tailCount > maximumTailEvents)
        {
            throw new InvalidDataException(
                $"Archive tail contains '{tailCount}' events, exceeding restore limit '{maximumTailEvents}'.");
        }

        return new EventArchiveRestore(checkpoint, ReadAfter(checkpoint.EventSequence, maximumTailEvents));
    }

    public EventArchiveAudit Audit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var fingerprint = new WorldEventFingerprint();
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, id, type, minute, location_id, subjects_json, causes_json, details_json
            FROM events
            ORDER BY sequence;
            """;
        long count = 0;
        long lastSequence = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var worldEvent = ReadEvent(reader);
            if (worldEvent.Sequence != lastSequence + 1)
            {
                throw new InvalidDataException(
                    $"Archive sequence jumps from '{lastSequence}' to '{worldEvent.Sequence}'.");
            }

            fingerprint.Append(worldEvent);
            lastSequence = worldEvent.Sequence;
            count++;
        }

        return new EventArchiveAudit(count, lastSequence, fingerprint.Complete());
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA optimize;";
        command.ExecuteNonQuery();
    }

    public long CreateCompressedBackup(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Flush();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var sqliteBackupPath = $"{fullPath}.{Guid.NewGuid():N}.sqlite.tmp";
        var compressedTemporaryPath = $"{fullPath}.tmp";
        try
        {
            using (var backupConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sqliteBackupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString()))
            {
                backupConnection.Open();
                _connection.BackupDatabase(backupConnection);
            }

            using var source = File.OpenRead(sqliteBackupPath);
            using var destination = File.Create(compressedTemporaryPath);
            using var compressed = new GZipStream(destination, CompressionLevel.SmallestSize);
            source.CopyTo(compressed);
        }
        catch
        {
            if (File.Exists(compressedTemporaryPath))
            {
                File.Delete(compressedTemporaryPath);
            }

            throw;
        }
        finally
        {
            if (File.Exists(sqliteBackupPath))
            {
                File.Delete(sqliteBackupPath);
            }
        }

        File.Move(compressedTemporaryPath, fullPath, overwrite: true);
        return new FileInfo(fullPath).Length;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
    }

    private void ConfigureConnection()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA foreign_keys = ON;
            PRAGMA cache_size = -65536;
            """;
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS archive_metadata(
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS events(
                sequence INTEGER PRIMARY KEY,
                id TEXT NOT NULL UNIQUE,
                type TEXT NOT NULL,
                minute INTEGER NOT NULL,
                location_id TEXT NULL,
                subjects_json BLOB NOT NULL,
                causes_json BLOB NOT NULL,
                details_json BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS event_causes(
                event_sequence INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                cause_id TEXT NOT NULL,
                PRIMARY KEY(event_sequence, ordinal),
                FOREIGN KEY(event_sequence) REFERENCES events(sequence)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS ix_event_causes_cause_id ON event_causes(cause_id);
            CREATE TABLE IF NOT EXISTS checkpoints(
                event_sequence INTEGER PRIMARY KEY,
                event_id TEXT NOT NULL,
                event_fingerprint TEXT NOT NULL,
                snapshot_payload BLOB NOT NULL,
                FOREIGN KEY(event_sequence) REFERENCES events(sequence)
            ) WITHOUT ROWID;
            INSERT OR IGNORE INTO archive_metadata(key, value) VALUES('schema_version', $schema_version);
            INSERT OR IGNORE INTO archive_metadata(key, value) VALUES('engine_version', $engine_version);
            """;
        command.Parameters.AddWithValue("$schema_version", SchemaVersion);
        command.Parameters.AddWithValue("$engine_version", EngineMetadata.Version);
        command.ExecuteNonQuery();

        var schemaVersion = ReadMetadata("schema_version");
        if (!string.Equals(schemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported event archive schema '{schemaVersion}'.");
        }

        var engineVersion = ReadMetadata("engine_version");
        if (!string.Equals(engineVersion, EngineMetadata.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Event archive engine version '{engineVersion}' is incompatible with '{EngineMetadata.Version}'.");
        }
    }

    private EventArchiveCheckpoint? LoadLatestCheckpoint()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT event_sequence, event_id, event_fingerprint, snapshot_payload
            FROM checkpoints
            ORDER BY event_sequence DESC
            LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new EventArchiveCheckpoint(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                (byte[])reader.GetValue(3))
            : null;
    }

    private IReadOnlyList<WorldEvent> ReadEventList(SqliteCommand command)
    {
        var events = new List<WorldEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    private static WorldEvent ReadEvent(SqliteDataReader reader)
    {
        var subjects = JsonSerializer.Deserialize<string[]>(reader.GetFieldValue<byte[]>(5), JsonOptions) ?? [];
        var causes = JsonSerializer.Deserialize<string[]>(reader.GetFieldValue<byte[]>(6), JsonOptions) ?? [];
        var details = JsonSerializer.Deserialize<Dictionary<string, string>>(
            reader.GetFieldValue<byte[]>(7), JsonOptions) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new WorldEvent(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            subjects,
            causes,
            new ReadOnlyDictionary<string, string>(details));
    }

    private SqliteCommand CreateInsertEventCommand(SqliteTransaction transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO events(sequence, id, type, minute, location_id, subjects_json, causes_json, details_json)
            VALUES($sequence, $id, $type, $minute, $location_id, $subjects_json, $causes_json, $details_json);
            """;
        command.Parameters.Add("$sequence", SqliteType.Integer);
        command.Parameters.Add("$id", SqliteType.Text);
        command.Parameters.Add("$type", SqliteType.Text);
        command.Parameters.Add("$minute", SqliteType.Integer);
        command.Parameters.Add("$location_id", SqliteType.Text);
        command.Parameters.Add("$subjects_json", SqliteType.Blob);
        command.Parameters.Add("$causes_json", SqliteType.Blob);
        command.Parameters.Add("$details_json", SqliteType.Blob);
        command.Prepare();
        return command;
    }

    private SqliteCommand CreateInsertCauseCommand(SqliteTransaction transaction)
    {
        var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO event_causes(event_sequence, ordinal, cause_id)
            VALUES($event_sequence, $ordinal, $cause_id);
            """;
        command.Parameters.Add("$event_sequence", SqliteType.Integer);
        command.Parameters.Add("$ordinal", SqliteType.Integer);
        command.Parameters.Add("$cause_id", SqliteType.Text);
        command.Prepare();
        return command;
    }

    private static void BindEvent(SqliteCommand command, WorldEvent worldEvent)
    {
        command.Parameters["$sequence"].Value = worldEvent.Sequence;
        command.Parameters["$id"].Value = worldEvent.Id;
        command.Parameters["$type"].Value = worldEvent.Type;
        command.Parameters["$minute"].Value = worldEvent.Minute;
        command.Parameters["$location_id"].Value = worldEvent.LocationId is null
            ? DBNull.Value
            : worldEvent.LocationId;
        command.Parameters["$subjects_json"].Value = JsonSerializer.SerializeToUtf8Bytes(
            worldEvent.SubjectIds, JsonOptions);
        command.Parameters["$causes_json"].Value = JsonSerializer.SerializeToUtf8Bytes(
            worldEvent.CauseIds, JsonOptions);
        command.Parameters["$details_json"].Value = JsonSerializer.SerializeToUtf8Bytes(
            worldEvent.Details, JsonOptions);
    }

    private long ExecuteInt64(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string ReadMetadata(string key)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM archive_metadata WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return (string?)command.ExecuteScalar()
            ?? throw new InvalidDataException($"Event archive metadata '{key}' is missing.");
    }
}
