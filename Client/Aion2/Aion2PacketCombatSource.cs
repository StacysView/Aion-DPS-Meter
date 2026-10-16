using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Aion2;

/// <summary>
/// Aion 2 input: the UE5 client writes no Chat.log, so combat is read from the game's own network
/// traffic instead (Npcap capture of the TCP stream to the game server, then the game's frame and
/// opcode layout - the same approach AionFlex takes). Nothing is injected into or read from the
/// game process. This is the seam-side shell: it owns capture lifecycle and status reporting and
/// turns decoded frames into <see cref="DamageEvent"/>s once <see cref="Protocol.Aion2Protocol"/>
/// knows the opcodes. Until a real capture from an Aion 2 session has been used to calibrate that
/// layout, it reports exactly that and delivers nothing - never a guess.
/// </summary>
public sealed class Aion2PacketCombatSource : ICombatSource
{
    private readonly Capture.NpcapAvailability _npcap = Capture.NpcapAvailability.Detect();
    private readonly Protocol.Aion2Protocol _protocol;
    private readonly Capture.TcpReassembler _reassembler = new();
    private readonly Protocol.Aion2FrameDecoder _decoder;
    private readonly Aion2EntityDirectory _entities = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<DamageEvent> _pending = new();
    private readonly string? _adapterId;
    private Capture.NpcapCaptureService? _capture;
    private SourceStatus _status = new(SourceState.Idle, "");

    public Aion2PacketCombatSource(Protocol.Aion2Protocol protocol, string? adapterId = null, string? ownCharacterName = null, string? characterStorePath = null, string? petsFolder = null)
    {
        _adapterId = adapterId;
        _configuredName = string.IsNullOrWhiteSpace(ownCharacterName) ? null : ownCharacterName.Trim();
        _petsFolder = petsFolder;
        _entities.SetConfiguredLocalName(ownCharacterName);
        if (characterStorePath is not null)
        {
            // Data from the last login, so the Character view and the profile upload are never empty
            // after a mid-session start; every fresh record the game sends replaces and re-saves it.
            if (Aion2CharacterStore.Load(characterStorePath) is { } saved)
            {
                _entities.RestoreFrom(saved);
            }

            _entities.CharacterChanged += _ =>
            {
                if (_entities.ToSaved() is { } snapshot)
                {
                    Aion2CharacterStore.Save(characterStorePath, snapshot);
                }
            };
        }
        _protocol = protocol;
        _decoder = new Protocol.Aion2FrameDecoder(protocol, _entities, Pets);
        Pets.Changed += (_, _) => PetsDirty(settle: true);
        Pets.ListReplaced += () => PetsDirty(settle: false);
        _entities.CharacterChanged += _ => SyncPetCharacter();
        SyncPetCharacter();
    }

    /// <summary>The local player's pets, filled from the game's frames (login list, soul gains, level-ups).
    /// With a save folder, kept per character in pets-&lt;name&gt;.json: loaded as soon as the character is
    /// known and saved after every change.</summary>
    public Aion2PetCollection Pets { get; } = new();

    private readonly string? _configuredName;
    private readonly string? _petsFolder;
    private readonly object _petsGate = new();
    private bool _petsUnsaved;
    private bool _listPending;
    private DateTime _listPendingAt;

    [ThreadStatic]
    private static bool _petsRestoringHere;

    // A fresh login list has no owner yet: the game sends the pet list BEFORE the character record, so
    // on a login as another character the old name is still the current one. The list therefore waits,
    // unsaved. It is settled (belongs to the current name) when the first soul or level frame follows, or
    // after 30 s; if a different name is announced first, the live list is kept and saved under it.
    private void PetsDirty(bool settle)
    {
        if (_petsRestoringHere)
        {
            return;
        }

        lock (_petsGate)
        {
            _petsUnsaved = true;
            if (!settle)
            {
                _listPending = true;
                _listPendingAt = DateTime.UtcNow;
            }
            else
            {
                _listPending = false;
            }
        }

        SyncPetCharacter();
    }

    /// <summary>Learns the character the pets belong to (its own record, else the configured name), loads
    /// its saved file when the name is first known and saves whatever is waiting to be saved. Events are
    /// raised after the lock is released.</summary>
    private void SyncPetCharacter()
    {
        if (_petsFolder is null)
        {
            return;
        }

        string? name = _entities.LearnedLocalName ?? _configuredName;
        if (name is null)
        {
            return;
        }

        string? restoreFrom = null;
        lock (_petsGate)
        {
            string path = Aion2PetCollection.PathFor(_petsFolder, name);
            if (_listPending && DateTime.UtcNow - _listPendingAt > TimeSpan.FromSeconds(30))
            {
                _listPending = false;
            }

            if (!string.Equals(Pets.Character, name, StringComparison.Ordinal))
            {
                bool hadOther = Pets.Character is not null;
                Pets.Character = name;
                if (_listPending || !hadOther && _petsUnsaved)
                {
                    // A list just received (or data received before any name): it is this character's.
                    _listPending = false;
                    _petsUnsaved = true;
                }
                else
                {
                    restoreFrom = path;
                    _petsUnsaved = false;
                }
            }

            if (restoreFrom is null && _petsUnsaved && !_listPending)
            {
                Pets.Save(path);
                _petsUnsaved = false;
            }
        }

        if (restoreFrom is not null)
        {
            _petsRestoringHere = true;
            try
            {
                Pets.Restore(Aion2PetCollection.Load(restoreFrom));
            }
            finally
            {
                _petsRestoringHere = false;
            }
        }
    }

    public SourceCapabilities Capabilities => SourceCapabilities.ExactIds | SourceCapabilities.Kills | SourceCapabilities.Defense;

    public IEntityDirectory Entities => _entities;

    public string CurrentZone => _decoder.CurrentZone;

    public bool InArena => false;

    public string? ServerFingerprint => _capture?.ServerEndpoint is string endpoint ? $"aion2:{endpoint}" : null;

    public event Action<string, string>? SkillUsed;

    /// <summary>Raised once when the stream alone reveals the local player's name (see
    /// <see cref="Aion2EntityDirectory.LearnedLocalName"/>), so the caller can remember it.</summary>
    public event Action<string>? LocalNameLearned;
    private string? _learnedReported;
    private long _eventsDecoded;
    private DateTime _lastStatusAt = DateTime.MinValue;
    public event Action<SourceStatus>? StatusChanged;

    // Aion 2's chat frames are not decoded yet (see ICombatSource.CommandReceived).
#pragma warning disable CS0067
    public event Action<string?, string, string>? CommandReceived;
#pragma warning restore CS0067

    public void Start()
    {
        if (!_npcap.IsInstalled)
        {
            Report(SourceState.Error, "Aion 2: Npcap is not installed - the meter reads the game's network traffic and needs the Npcap driver (npcap.com). Nothing is captured until it is.");
            return;
        }

        if (!_protocol.IsCalibrated)
        {
            Report(SourceState.Waiting, "Aion 2: the packet layout has not been calibrated for this game version yet - record a fight with \"AionDPS aion2-record\" and update assets/aion2/protocol/opcodes.json.");
            return;
        }

        _capture = new Capture.NpcapCaptureService(_protocol.ServerPorts, OnPayload, OnCaptureStatus, _adapterId);
        _capture.Start();
    }

    public void Stop()
    {
        _capture?.Dispose();
        _capture = null;
    }

    public CombatBatch Poll(bool paused)
    {
        ReportLiveCounters();
        SyncPetCharacter();

        if (_entities.LearnedLocalName is string learned && learned != _learnedReported)
        {
            _learnedReported = learned;
            LocalNameLearned?.Invoke(learned);
        }

        if (_pending.IsEmpty)
        {
            return CombatBatch.Empty;
        }

        var drained = new List<DamageEvent>();
        while (_pending.TryDequeue(out DamageEvent ev))
        {
            drained.Add(ev);
        }

        // Paused time is discarded, not deferred - same tape-recorder rule as the Chat.log source.
        return paused ? CombatBatch.Empty : CombatBatch.DamageOnly(drained);
    }

    public void Dispose()
    {
        StopRecording();
        Stop();
    }

    /// <summary>
    /// A live one-line health report for the status bar: once the game server has been seen, how
    /// much has arrived and how much of it decoded. "Capturing ... waiting" for minutes while frames
    /// stay at 0 points at capture/alignment; frames growing with events at 0 points at the layout.
    /// Throttled, since the status line repaints on every change.
    /// </summary>
    private void ReportLiveCounters()
    {
        if (_capture?.ServerEndpoint is not string endpoint || DateTime.UtcNow - _lastStatusAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastStatusAt = DateTime.UtcNow;
        long frames;
        int desyncs;
        lock (_reassembler)
        {
            frames = _reassembler.Frames;
            desyncs = _reassembler.Desyncs;
        }

        int errors = _capture.CallbackErrors;
        Report(SourceState.Connected, $"Aion 2: {endpoint} - {frames:N0} frames, {_eventsDecoded:N0} events, {desyncs:N0} resyncs"
            + (errors > 0 ? $", {errors:N0} errors ({_capture.LastCallbackError})" : ""));
    }

    /// <summary>Feeds one captured segment through reassembly and decoding - the live capture's
    /// callback, and what a recorded fixture is replayed through in the self-checks.</summary>
    private Capture.SegmentRecording.Writer? _recording;

    /// <summary>Writes every captured segment to a file (Settings' diagnostic recording) until
    /// <see cref="StopRecording"/>; the same format as "aion2-record", replayable by the tools. With
    /// <paramref name="append"/>, goes on at the end of the file (a restarted capture).</summary>
    public void StartRecording(string path, bool append = false)
    {
        StopRecording();
        _recording = new Capture.SegmentRecording.Writer(path, append);
    }

    public void StopRecording()
    {
        var recording = Interlocked.Exchange(ref _recording, null);
        recording?.Dispose();
    }

    public bool IsRecording => _recording is not null;

    public void Ingest(Capture.TcpSegment segment)
    {
        _recording?.Write(segment);
        // Only the server's stream carries combat; the client's small command packets are a
        // different vocabulary and would only risk a false frame.
        if (!segment.FromServer)
        {
            return;
        }

        lock (_reassembler)
        {
            foreach (ReadOnlyMemory<byte> frame in _reassembler.Push(segment, _protocol.FrameLayout, _protocol.SyncOpcodes))
            {
                foreach (DamageEvent ev in _decoder.Decode(frame.Span, segment.Timestamp))
                {
                    _pending.Enqueue(ev);
                    _eventsDecoded++;
                }
            }

            foreach ((string actor, string skill) in _decoder.DrainSkillUses())
            {
                SkillUsed?.Invoke(actor, skill);
            }
        }

        SyncPetCharacter();
    }

    private void OnPayload(Capture.TcpSegment segment) => Ingest(segment);

    private void OnCaptureStatus(SourceState state, string message) => Report(state, $"Aion 2: {message}");

    private void Report(SourceState state, string message)
    {
        if (_status.State == state && _status.Message == message)
        {
            return;
        }

        _status = new SourceStatus(state, message);
        StatusChanged?.Invoke(_status);
    }
}
