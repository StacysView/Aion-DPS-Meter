using System.Speech.Recognition;

namespace AionDPS.Ui;

/// <summary>
/// Words said into the microphone while a capture is recorded become markers in the file (Stacy, 2026-11-10, in Ludra: "faux" when
/// Ketu's scythes come, "orbe" for the orbs; the exact moments are found in the capture afterwards). The Windows speech recognizer
/// of this PC listens for these few words; the rest of what is said goes to a dictation grammar, which only counts when it heard
/// the word alone, so "c'est faux" in a sentence is no marker. Nothing is kept but the word and its time, and nothing leaves the PC.
/// </summary>
public sealed class VoiceMarkers : IDisposable
{
    /// <summary>The words by recognizer language, and the marker each one writes (the same markers whatever the language).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Words = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["fr"] = new Dictionary<string, string> { ["faux"] = "faux", ["orbe"] = "orbe", ["orbes"] = "orbe", ["adds"] = "adds", ["phase"] = "adds", ["top"] = "top" },
        ["en"] = new Dictionary<string, string> { ["scythe"] = "faux", ["scythes"] = "faux", ["orb"] = "orbe", ["orbs"] = "orbe", ["adds"] = "adds", ["phase"] = "adds", ["mark"] = "top" },
    };

    /// <summary>Below this the recognizer was guessing.</summary>
    public const float MinConfidence = 0.6f;

    private const string MarkerGrammar = "markers";

    private SpeechRecognitionEngine? _engine;
    private IReadOnlyDictionary<string, string> _words = Words["fr"];

    /// <summary>A word was heard: its marker, and when it began to be said. Raised on a recognizer thread.</summary>
    public event Action<string, DateTime>? Heard;

    /// <summary>The recognizer's language once started ("fr-FR").</summary>
    public string? Language { get; private set; }

    /// <summary>The words listened for once started.</summary>
    public IReadOnlyCollection<string> Listened => _words.Keys.ToList();

    /// <summary>Starts listening on the default microphone. False, with the reason, when this PC cannot: no speech recognizer
    /// installed (Windows settings, Language, speech), no microphone, microphone access off for desktop apps.</summary>
    public bool Start(out string? error)
    {
        try
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            RecognizerInfo? info = installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "fr")
                ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en") ?? installed.FirstOrDefault();
            if (info is null)
            {
                error = "no speech recognizer installed";
                return false;
            }

            _words = Words.GetValueOrDefault(info.Culture.TwoLetterISOLanguageName) ?? Words["en"];
            var engine = new SpeechRecognitionEngine(info);
            _engine = engine;
            engine.LoadGrammar(new Grammar(new GrammarBuilder(new Choices(_words.Keys.ToArray())) { Culture = info.Culture }) { Name = MarkerGrammar });
            try
            {
                engine.LoadGrammar(new DictationGrammar { Name = "dictation" });
            }
            catch (Exception)
            {
                // not every recognizer has dictation: the words alone still work, with more false markers
            }

            engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += OnRecognized;
            engine.RecognizeAsync(RecognizeMode.Multiple);
            Language = info.Culture.Name;
            error = null;
            return true;
        }
        catch (Exception e)
        {
            // the speech engine is COM underneath and fails in many ways (no device, access refused): never a reason for the meter to stop
            error = e.Message;
            Dispose();
            return false;
        }
    }

    /// <summary>The word alone counts, from the words' grammar when the recognizer is sure enough, or as the whole of what the
    /// dictation heard ("Faux."): a sentence that holds the word ("c'est faux") is no marker.</summary>
    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        string text = e.Result.Text.Trim().TrimEnd('.', ',', '!', '?', '…').Trim().ToLowerInvariant();
        bool fromWords = e.Result.Grammar?.Name == MarkerGrammar;
        if (!_words.TryGetValue(text, out string? marker) || (fromWords && e.Result.Confidence < MinConfidence))
        {
            return;
        }

        // when the word began: the audio's start, or now less its length if that start looks like the session's rather than the word's
        DateTime now = DateTime.Now;
        DateTime at = e.Result.Audio is { } audio
            ? (now - audio.StartTime is var ago && ago >= TimeSpan.Zero && ago < TimeSpan.FromSeconds(10) ? audio.StartTime : now - audio.Duration)
            : now;
        Heard?.Invoke(marker, at);
    }

    public void Dispose()
    {
        var engine = Interlocked.Exchange(ref _engine, null);
        if (engine is null)
        {
            return;
        }

        engine.SpeechRecognized -= OnRecognized;
        try
        {
            engine.RecognizeAsyncCancel();
        }
        catch (InvalidOperationException)
        {
        }

        engine.Dispose();
    }
}
