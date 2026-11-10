using System.Speech.Recognition;

namespace AionDPS.Ui;

/// <summary>
/// Words said into the microphone while a capture is recorded become markers in the file (Stacy, 2026-11-10, in Ludra: "faux" when
/// Ketu's scythes come, "orbe" for the orbs; the exact moments are found in the capture afterwards). The Windows speech recognizer
/// of this PC listens for these few words; the rest of what is said goes to a dictation grammar, which only counts when it heard
/// the word alone, so "c'est faux" in a sentence is no marker. Nothing is kept but the word and its time, and nothing leaves the PC.
/// The French words work with an English recognizer too (2026-11-10 test: only "orbe" was heard, close to "orb", "faux" and "top" were
/// not in the English list): each list holds the French words, their homophones the dictation writes ("faut", "taupe"), and the
/// spellings an English recognizer says like them ("foe").
/// </summary>
public sealed class VoiceMarkers : IDisposable
{
    /// <summary>The words by recognizer language, and the marker each one writes (the same markers whatever the language).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Words = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["fr"] = new Dictionary<string, string>
        {
            ["faux"] = "faux", ["faut"] = "faux", ["fau"] = "faux",
            ["orbe"] = "orbe", ["orbes"] = "orbe", ["orb"] = "orbe",
            ["adds"] = "adds", ["ads"] = "adds", ["phase"] = "adds",
            ["top"] = "top", ["taupe"] = "top",
        },
        ["en"] = new Dictionary<string, string>
        {
            ["faux"] = "faux", ["foe"] = "faux", ["scythe"] = "faux",
            ["orb"] = "orbe", ["orbs"] = "orbe",
            ["adds"] = "adds", ["ads"] = "adds", ["phase"] = "adds",
            ["top"] = "top", ["mark"] = "top",
        },
    };

    /// <summary>Below this the recognizer was guessing (a word said alone often comes back around 0.5).</summary>
    public const float MinConfidence = 0.4f;

    /// <summary>A word the recognizer turned down (said alone, it often is) still counts when it was this sure of it.</summary>
    public const float MinRejectedConfidence = 0.5f;

    private const string MarkerGrammar = "markers";

    private SpeechRecognitionEngine? _engine;
    private IReadOnlyDictionary<string, string> _words = Words["fr"];

    /// <summary>A word was heard: its marker, and when it began to be said. Raised on a recognizer thread.</summary>
    public event Action<string, DateTime>? Heard;

    /// <summary>One of the words heard but not taken (too unsure), with how sure the recognizer was: written into the capture so the
    /// next one tells why a word was missed. Only the listed words, never what else was said. Raised on a recognizer thread.</summary>
    public event Action<string, DateTime>? Missed;

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
            engine.SpeechRecognitionRejected += OnRejected;
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

    /// <summary>The marker for what the recognizer heard, or null: the word alone, from the words' grammar when the recognizer is sure
    /// enough, or as the whole of what the dictation heard ("Faut."); a sentence that holds the word ("c'est faux") is no marker.</summary>
    public static string? MarkerFor(IReadOnlyDictionary<string, string> words, string heard, bool fromWords, float confidence)
    {
        string text = heard.Trim().TrimEnd('.', ',', '!', '?', '…').Trim().ToLowerInvariant();
        return words.TryGetValue(text, out string? marker) && (!fromWords || confidence >= MinConfidence) ? marker : null;
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        bool fromWords = e.Result.Grammar?.Name == MarkerGrammar;
        if (MarkerFor(_words, e.Result.Text, fromWords, e.Result.Confidence) is string marker)
        {
            Heard?.Invoke(marker, StartOf(e.Result));
        }
        else if (fromWords)
        {
            Missed?.Invoke($"{e.Result.Text} {e.Result.Confidence:0.00}", StartOf(e.Result));
        }
    }

    /// <summary>Turned down by the recognizer: its best guess among the words still counts when it was sure enough of it.</summary>
    private void OnRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        RecognizedPhrase? best = e.Result.Alternates.Where(a => a.Grammar?.Name == MarkerGrammar).MaxBy(a => a.Confidence);
        if (best is null)
        {
            return;
        }

        if (best.Confidence >= MinRejectedConfidence && MarkerFor(_words, best.Text, false, best.Confidence) is string marker)
        {
            Heard?.Invoke(marker, StartOf(e.Result));
        }
        else
        {
            Missed?.Invoke($"{best.Text} {best.Confidence:0.00} (rejected)", StartOf(e.Result));
        }
    }

    /// <summary>When the word began: the audio's start, or now less its length if that start looks like the session's rather than the word's.</summary>
    private static DateTime StartOf(RecognitionResult result)
    {
        DateTime now = DateTime.Now;
        return result.Audio is { } audio
            ? (now - audio.StartTime is var ago && ago >= TimeSpan.Zero && ago < TimeSpan.FromSeconds(10) ? audio.StartTime : now - audio.Duration)
            : now;
    }

    public void Dispose()
    {
        var engine = Interlocked.Exchange(ref _engine, null);
        if (engine is null)
        {
            return;
        }

        engine.SpeechRecognized -= OnRecognized;
        engine.SpeechRecognitionRejected -= OnRejected;
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
