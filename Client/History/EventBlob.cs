using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>Compact JSON + gzip for a raw DamageEvent list plus its object-id-&gt;name map -
/// originally FightStore's own private nested type (one blob per row in its SQLite table), now
/// shared with SessionFile's own file-based save/load (Main.MenuApp.SaveSession/LoadSession),
/// which needs the exact same round-trip but to a standalone file instead of a database column.
/// Ticks+Kind rather than ISO strings: the Chat.log timestamps are local, kind-unspecified values
/// and must come back exactly so.</summary>
public static class EventBlob
{
    // Id and Tk came later: the skills window finds a skill's icon by its id. Kd (the hit kind: parry, perfect, double) and Sm (struck by a
    // summon) came later still, so a fight opened from the history can tell how many hits a boss parried. Blobs written before read back
    // with 0 / false.
    private sealed record Dto(long T, int K, int S, int G, long A, bool H, string? Sk, bool C, int Id = 0, bool Tk = false, int Kd = 0, bool Sm = false);

    private sealed record Payload(List<Dto> Events, Dictionary<string, string> Names);

    public static byte[] Pack(IReadOnlyList<DamageEvent> events, IReadOnlyDictionary<int, string> names)
    {
        var payload = new Payload(
            events.Select(e => new Dto(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical, e.SkillId, e.IsTick, (int)e.Kind, e.FromSummon)).ToList(),
            names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, payload);
        }

        return output.ToArray();
    }

    public static (List<DamageEvent> Events, Dictionary<int, string> Names) Unpack(byte[] blob)
    {
        using var input = new MemoryStream(blob);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        Payload payload = JsonSerializer.Deserialize<Payload>(gzip) ?? new Payload(new List<Dto>(), new Dictionary<string, string>());
        var events = payload.Events
            .Select(d => new DamageEvent(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, d.A, d.H, d.Sk, d.C, d.Tk, d.Id, (HitKind)d.Kd, d.Sm))
            .ToList();
        var names = payload.Names.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        return (events, names);
    }
}
