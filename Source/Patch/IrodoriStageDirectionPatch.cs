using HarmonyLib;
using RimTalk.Data;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Enrich the TTS-only fast-path text with Irodori's documented emoji annotations.
    /// SanitizeForTts receives the original display dialogue as its first argument, even though its
    /// normal result strips stage directions. The postfix can therefore rebuild the sanitized text
    /// from that original argument and preserve recognized audible acting cues as Irodori emojis.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.SanitizeForTts))]
    public static class IrodoriStageDirectionSanitizePatch
    {
        [HarmonyPostfix]
        public static void Postfix(string text, TTSSettings settings, ref string __result)
        {
            try
            {
                var cfg = settings?.Irodori;
                if (settings == null ||
                    settings.Supplier != TTSSettings.TTSSupplier.Irodori ||
                    cfg == null ||
                    !cfg.UnifiedTtsEnabled ||
                    !cfg.UnifiedTtsStripStageDirections ||
                    string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                // Avoid reintroducing a malformed/legacy RTTTS envelope if SanitizeForTts was called
                // from the fast-path fallback with raw model output rather than displayText.
                string source = text;
                if (UnifiedTtsPayloadStore.TryStripEnvelopeForDisplay(source, out string clean))
                    source = clean;

                string transformed = IrodoriStageDirectionMapper.Transform(
                    source,
                    stripUnmapped: true,
                    out int convertedCount);

                if (convertedCount <= 0)
                    return; // Keep the original sanitizer result exactly as before.

                __result = transformed;

                if (cfg.UnifiedTtsDebugLogging)
                {
                    Log.Message($"[RimTalk.TTS/Irodori] Stage Direction acting cues converted: {convertedCount}; TTS='{transformed}'");
                }
            }
            catch (System.Exception ex)
            {
                // Deliberately fail open: Fast Path keeps its old sanitized output on any mapper error.
                Log.Warning($"[RimTalk.TTS/Irodori] Stage Direction conversion failed; using normal sanitized text. {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Teach the first RimTalk LLM that it may emit a small set of local acting cues when a precise
    /// non-verbal vocal event is genuinely useful. Whole-line emotion/style still belongs in RTTTS
    /// delivery-caption; these inline cues are only for timing-sensitive events.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.BuildPromptInstruction))]
    public static class IrodoriStageDirectionPromptPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TTSSettings settings, ref string __result)
        {
            try
            {
                var cfg = settings?.Irodori;
                if (settings == null ||
                    settings.Supplier != TTSSettings.TTSSupplier.Irodori ||
                    cfg == null ||
                    !cfg.UnifiedTtsEnabled ||
                    !cfg.UnifiedTtsStripStageDirections ||
                    string.IsNullOrEmpty(__result))
                {
                    return;
                }

                __result += @"
12. OPTIONAL LOCAL ACTING CUES: Only when a genuine audible/non-verbal vocal event occurs at a precise point in the dialogue, you MAY insert a short Japanese-parenthesized stage direction at that exact position. Good examples: （ため息）, （息をのむ）, （くすくす笑う）, （咳払い）, （すすり泣く）, （囁く）, （あくび）, （舌打ち）, （一拍置く）. RimTalk TTS hides recognized cues from display/history and converts them locally into Irodori emoji annotations. Use at most one or two per dialogue line, never force a cue into every line, and never output the emoji annotation yourself. Whole-line emotion, pace, intensity, and voice style still belong in <delivery-caption>.
";
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/Irodori] Could not append Stage Direction prompt instruction: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// After CaptureAndStrip has already cached the TTS payload, remove only recognized acting
    /// directions from the TalkResponse that RimTalk will display/store. Unknown parentheticals are
    /// deliberately preserved so ordinary dialogue content is never silently discarded.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.CaptureAndStrip))]
    public static class IrodoriStageDirectionDisplayPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TalkResponse response, TTSSettings settings, bool __result)
        {
            try
            {
                var cfg = settings?.Irodori;
                if (!__result ||
                    response == null ||
                    settings == null ||
                    settings.Supplier != TTSSettings.TTSSupplier.Irodori ||
                    cfg == null ||
                    !cfg.UnifiedTtsEnabled ||
                    !cfg.UnifiedTtsStripStageDirections ||
                    string.IsNullOrWhiteSpace(response.Text))
                {
                    return;
                }

                string cleanDisplay = IrodoriStageDirectionMapper.StripRecognizedForDisplay(
                    response.Text,
                    out int strippedCount);

                if (strippedCount <= 0)
                    return;

                response.Text = cleanDisplay;

                if (cfg.UnifiedTtsDebugLogging)
                    Log.Message($"[RimTalk.TTS/Irodori] Hidden {strippedCount} converted Stage Direction cue(s) from RimTalk display/history.");
            }
            catch (System.Exception ex)
            {
                // Display cleanup is optional. Never jeopardize normal RimTalk response handling.
                Log.Warning($"[RimTalk.TTS/Irodori] Stage Direction display cleanup failed: {ex.Message}");
            }
        }
    }
}
