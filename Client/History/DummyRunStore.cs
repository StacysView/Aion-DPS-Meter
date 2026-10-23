using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;
using Microsoft.Data.Sqlite;

namespace AionDPS.History;

/// <summary>One stored run as the list shows it: the summary columns, no events.</summary>
public sealed record DummyRunRow(
    long Id, int Number, string Label, string Note, DateTime StartedAt, TimeSpan Planned, TimeSpan Length, bool Complete,
    string TargetName, long TotalDamage, bool LevelsConfirmed, DateTime SavedAt);

/// <summary>
/// The dummy test's runs: one SQLite file (dummy-runs.db) under %AppData%, apart from the fight history. Summary columns are real columns so the list
/// needs no blob; the events, skill uses, effects, names, levels and stat table travel as one gzip'd JSON blob per run. EventBlob is not used for
/// the events: it does not keep the hit kind (crit, double, perfect, parry), which the plain-hit statistics need.
/// All times keep their ticks and kind, so a run reads back exactly as it was stored. Only the UI thread uses a store.
/// </summary>
public sealed class DummyRunStore : IDisposable
{
    /// <summary>The runs kept; <see cref="Prune"/> drops the oldest beyond it.</summary>
    public const int Keep = 200;

    private readonly SqliteConnection _connection;

    public static string DefaultPath => Path.Combine(AppDataFolder.Path, "dummy-runs.db");

    public DummyRunStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connection = new SqliteConnection($"Data Source={path}");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("""
            CREATE TABLE IF NOT EXISTS dummy_runs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                number INTEGER NOT NULL,
                label TEXT NOT NULL,
                note TEXT NOT NULL,
                started_at TEXT NOT NULL,
                planned_ms INTEGER NOT NULL,
                length_ms INTEGER NOT NULL,
                complete INTEGER NOT NULL,
                target_name TEXT NOT NULL,
                total_damage INTEGER NOT NULL,
                levels_confirmed INTEGER NOT NULL,
                saved_at TEXT NOT NULL,
                data_gz BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS dummy_runs_started_at_idx ON dummy_runs(started_at);
            """);
    }

    // The blob. Times are Ticks + Kind like EventBlob; names that came later get a default so an older blob still reads.
    private sealed record EventDto(long T, int K, int S, int G, long A, bool H, string? Sk, bool C, bool Tk, int Id, int Kd);

    private sealed record UseDto(long T, int K, int Id, bool Ok, int Rc);

    private sealed record EffectDto(long T, int K, int Id, bool On);

    private sealed record StatsDto(long T, int K, Dictionary<int, int> V);

    private sealed record Payload(
        long StartTicks, int StartKind, int PlayerId, List<EventDto> Events, List<UseDto> Uses, List<EffectDto> Effects,
        Dictionary<int, string> Names, List<SkillLevel>? Levels, long? LevelsReadTicks, int LevelsReadKind, StatsDto? Stats);

    private static byte[] Pack(DummyRun run)
    {
        var payload = new Payload(
            run.Start.Ticks, (int)run.Start.Kind, run.PlayerId,
            run.Events.Select(e => new EventDto(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical, e.IsTick, e.SkillId, (int)e.Kind)).ToList(),
            run.Uses.Select(u => new UseDto(u.At.Ticks, (int)u.At.Kind, u.SkillId, u.Accepted, u.RejectCode)).ToList(),
            run.Effects.Select(e => new EffectDto(e.At.Ticks, (int)e.At.Kind, e.EffectId, e.On)).ToList(),
            new Dictionary<int, string>(run.Names),
            run.Levels,
            run.LevelsReadAt?.Ticks, run.LevelsReadAt is { } read ? (int)read.Kind : 0,
            run.Stats is { } stats ? new StatsDto(stats.At.Ticks, (int)stats.At.Kind, new Dictionary<int, int>(stats.Values)) : null);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, payload);
        }

        return output.ToArray();
    }

    private static void Unpack(byte[] blob, DummyRun run)
    {
        using var input = new MemoryStream(blob);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        Payload? payload = JsonSerializer.Deserialize<Payload>(gzip);
        if (payload is null)
        {
            return;
        }

        run.Start = new DateTime(payload.StartTicks, (DateTimeKind)payload.StartKind);
        run.PlayerId = payload.PlayerId;
        run.Events = payload.Events
            .Select(d => new DamageEvent(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, d.A, d.H, d.Sk, d.C, d.Tk, d.Id, (HitKind)d.Kd))
            .ToList();
        run.Uses = payload.Uses.Select(d => new SkillUse(new DateTime(d.T, (DateTimeKind)d.K), d.Id, d.Ok, d.Rc)).ToList();
        run.Effects = payload.Effects.Select(d => new EffectChange(new DateTime(d.T, (DateTimeKind)d.K), d.Id, d.On)).ToList();
        run.Names = payload.Names;
        run.Levels = payload.Levels;
        run.LevelsReadAt = payload.LevelsReadTicks is long ticks ? new DateTime(ticks, (DateTimeKind)payload.LevelsReadKind) : null;
        run.Stats = payload.Stats is { } s ? new StatSnapshot(new DateTime(s.T, (DateTimeKind)s.K), s.V) : null;
    }

    /// <summary>Files a run (its <see cref="DummyRun.Id"/> is set) and returns the new id. The total damage column is the run's window damage.</summary>
    public long Insert(DummyRun run)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dummy_runs (number, label, note, started_at, planned_ms, length_ms, complete, target_name, total_damage, levels_confirmed, saved_at, data_gz)
            VALUES ($number, $label, $note, $started, $planned, $length, $complete, $target, $total, $confirmed, $saved, $data);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$number", run.Number);
        cmd.Parameters.AddWithValue("$label", run.Label);
        cmd.Parameters.AddWithValue("$note", run.Note);
        cmd.Parameters.AddWithValue("$started", run.Start.ToString("o"));
        cmd.Parameters.AddWithValue("$planned", (long)run.Planned.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$length", (long)run.Length.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$complete", run.Complete ? 1 : 0);
        cmd.Parameters.AddWithValue("$target", run.TargetName);
        cmd.Parameters.AddWithValue("$total", DummyAnalysis.Summarize(run).TotalDamage);
        cmd.Parameters.AddWithValue("$confirmed", run.LevelsConfirmed ? 1 : 0);
        cmd.Parameters.AddWithValue("$saved", (run.SavedAt == default ? DateTime.Now : run.SavedAt).ToString("o"));
        cmd.Parameters.AddWithValue("$data", Pack(run));
        long id = (long)cmd.ExecuteScalar()!;
        run.Id = id;
        return id;
    }

    /// <summary>Newest first (by start, then by id).</summary>
    public List<DummyRunRow> Query(int limit = 500)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, number, label, note, started_at, planned_ms, length_ms, complete, target_name, total_damage, levels_confirmed, saved_at
            FROM dummy_runs ORDER BY started_at DESC, id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var rows = new List<DummyRunRow>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadRow(reader));
        }

        return rows;
    }

    /// <summary>The whole run, events included; null for an id that is gone or a blob that cannot be read.</summary>
    public DummyRun? Load(long id)
    {
        DummyRunRow row;
        byte[] blob;
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, number, label, note, started_at, planned_ms, length_ms, complete, target_name, total_damage, levels_confirmed, saved_at, data_gz
                FROM dummy_runs WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            row = ReadRow(reader);
            blob = (byte[])reader["data_gz"];
        }

        var run = new DummyRun
        {
            Id = row.Id,
            Number = row.Number,
            Label = row.Label,
            Note = row.Note,
            Start = row.StartedAt,
            Planned = row.Planned,
            Length = row.Length,
            TargetName = row.TargetName,
            LevelsConfirmed = row.LevelsConfirmed,
            SavedAt = row.SavedAt,
        };
        try
        {
            Unpack(blob, run);
        }
        catch (JsonException)
        {
            // A damaged blob is no run: better none than one with no hits that looks like a zero.
            return null;
        }

        return run;
    }

    /// <summary>The number the next run gets: one past the highest stored, so the list keeps counting across sessions.</summary>
    public int NextNumber()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(number), 0) FROM dummy_runs;";
        return (int)(long)cmd.ExecuteScalar()! + 1;
    }

    public void UpdateNote(long id, string note) => Execute("UPDATE dummy_runs SET note = $note WHERE id = $id;", ("$note", note), ("$id", id));

    public void UpdateLabel(long id, string label) => Execute("UPDATE dummy_runs SET label = $label WHERE id = $id;", ("$label", label), ("$id", id));

    public void Delete(long id) => Execute("DELETE FROM dummy_runs WHERE id = $id;", ("$id", id));

    /// <summary>Drops everything past the newest <paramref name="keep"/> runs. Returns how many were removed.</summary>
    public int Prune(int keep = Keep) => Execute(
        "DELETE FROM dummy_runs WHERE id NOT IN (SELECT id FROM dummy_runs ORDER BY started_at DESC, id DESC LIMIT $keep);",
        ("$keep", keep));

    public long Count()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dummy_runs;";
        return (long)cmd.ExecuteScalar()!;
    }

    public void Dispose() => _connection.Dispose();

    private static DummyRunRow ReadRow(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        (int)reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
        TimeSpan.FromMilliseconds(reader.GetInt64(5)),
        TimeSpan.FromMilliseconds(reader.GetInt64(6)),
        reader.GetInt64(7) != 0,
        reader.GetString(8),
        reader.GetInt64(9),
        reader.GetInt64(10) != 0,
        DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind));

    private int Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        return cmd.ExecuteNonQuery();
    }
}
