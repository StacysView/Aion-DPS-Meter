using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Writes captured segments as JSON lines and reads them back - the calibration fixture format.
/// One recorded fight from a real Aion 2 session is what turns opcodes.json from a template into
/// a working protocol, and the same file then becomes a regression test that replays without the
/// game. Payloads are base64; nothing but the game-server stream is ever in here, and the markers
/// its owner dropped while playing (a word said into the microphone, see <see cref="Writer.WriteMarker"/>).
/// </summary>
public static class SegmentRecording
{
    private sealed record Line(DateTime T, string From, string To, uint Seq, bool Server, string Data);

    private sealed record MarkerLine(DateTime T, string Marker);

    private sealed record NoteLine(DateTime T, string Note);

    /// <summary>A word its owner said while the recording ran ("faux", "orbe"...), at the time they began to say it.</summary>
    public readonly record struct Marker(DateTime At, string Word);

    public sealed class Writer : IDisposable
    {
        private readonly StreamWriter _out;
        private readonly object _gate = new();

        /// <summary>A new file, or with <paramref name="append"/> the end of an existing one: a capture
        /// restarted while recording goes on in the same file.</summary>
        public Writer(string path, bool append = false)
        {
            _out = new StreamWriter(path, append) { AutoFlush = true };
        }

        public int Count { get; private set; }

        public void Write(TcpSegment segment)
        {
            string json = JsonSerializer.Serialize(new Line(segment.Timestamp, segment.Source, segment.Destination, segment.Sequence, segment.FromServer, Convert.ToBase64String(segment.Payload.Span)));
            lock (_gate)
            {
                _out.WriteLine(json);
                Count++;
            }
        }

        /// <summary>A marker between the segments (Stacy, 2026-11-10: "faux" or "orbe" said in Ludra, to find the mechanic in the
        /// capture afterwards). <see cref="Read"/> steps over it, <see cref="ReadMarkers"/> lists them.</summary>
        public void WriteMarker(DateTime at, string word)
        {
            string json = JsonSerializer.Serialize(new MarkerLine(at, word));
            lock (_gate)
            {
                _out.WriteLine(json);
            }
        }

        /// <summary>A note for whoever reads the capture (the microphone's language, a word heard but not taken). <see cref="Read"/>
        /// and <see cref="ReadMarkers"/> step over it.</summary>
        public void WriteNote(DateTime at, string note)
        {
            string json = JsonSerializer.Serialize(new NoteLine(at, note));
            lock (_gate)
            {
                _out.WriteLine(json);
            }
        }

        public void Dispose() => _out.Dispose();
    }

    public static IEnumerable<TcpSegment> Read(string path)
    {
        // Shared read/write: a recording that is still being written (the recorder keeps it open)
        // can be replayed while it grows.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            Line? parsed = JsonSerializer.Deserialize<Line>(line);
            if (parsed?.Data is null)
            {
                continue; // a marker line
            }

            yield return new TcpSegment(parsed.T, parsed.From, parsed.To, parsed.Seq, Convert.FromBase64String(parsed.Data), parsed.Server);
        }
    }

    /// <summary>The markers of a recording, in file order.</summary>
    public static IReadOnlyList<Marker> ReadMarkers(string path)
    {
        var markers = new List<Marker>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is string line)
        {
            if (!line.Contains("\"Marker\"", StringComparison.Ordinal))
            {
                continue;
            }

            if (JsonSerializer.Deserialize<MarkerLine>(line) is { Marker: not null } parsed)
            {
                markers.Add(new Marker(parsed.T, parsed.Marker));
            }
        }

        return markers;
    }

    /// <summary>The notes of a recording, in file order.</summary>
    public static IReadOnlyList<(DateTime At, string Note)> ReadNotes(string path)
    {
        var notes = new List<(DateTime, string)>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is string line)
        {
            if (line.Contains("\"Note\"", StringComparison.Ordinal) && JsonSerializer.Deserialize<NoteLine>(line) is { Note: not null } parsed)
            {
                notes.Add((parsed.T, parsed.Note));
            }
        }

        return notes;
    }
}
