using HarmonyLib;
using RimTalk.Data;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Legacy compatibility only. New Fast Path output asks the LLM to emit Irodori acting emojis
    /// directly. If a model still emits a recognizable prose stage direction, convert it here for
    /// TTS while preserving the established sanitizer as the fail-open fallback.
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

                // Direct Irodori emojis already survive SanitizeForTts unchanged, so only rebuild
                // the text when a legacy prose stage direction was actually converted.
                string source = text;
                if (UnifiedTtsPayloadStore.TryStripEnvelopeForDisplay(source, out string clean))
                    source = clean;

                string transformed = IrodoriStageDirectionMapper.Transform(
                    source,
                    stripUnmapped: true,
                    out int convertedCount);

                if (convertedCount <= 0)
                    return;

                __result = transformed;

                if (cfg.UnifiedTtsDebugLogging)
                {
                    Log.Message($"[RimTalk.TTS/Irodori] Legacy Stage Direction converted to acting emoji: {convertedCount}; TTS='{transformed}'");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/Irodori] Legacy Stage Direction conversion failed; using normal sanitized text. {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Prefer Irodori's native inline emoji controls instead of asking the LLM for prose stage
    /// directions and trying to classify them afterwards. Whole-line delivery remains in RTTTS
    /// caption; direct emojis are only for events/styles that need a precise position in the line.
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
12. IRODORI INLINE ACTING CONTROL: For a genuinely audible event or a delivery change that must happen at a PRECISE position inside the spoken line, you MAY insert an Irodori control emoji DIRECTLY into <dialogue>. Use at most one or two per dialogue line, and never force them into every line.
13. Use ONLY these control emojis when appropriate: 🤭 laughter/giggle, 😮‍💨 sigh/exhale, 🤧 cough/sneeze/sniffle, 😭 crying/sobbing, 😮 gasp, 🌬️ heavy/breathless breathing, 😱 scream/shout, 🥱 yawn, 😒 tongue-click/disdain, 🥵 groan/moan, 🎵 humming, ⏸️ deliberate pause, 👂 whisper, ⏩ fast speech, 🐢 slow speech, 😰 nervous/panicked, 🥺 trembling/timid, 🫣 shy/embarrassed, 🙄 exasperated, 😏 teasing, 🫶 gentle/tender, 😪 sleepy, 😠 angry, 😲 surprised, 😖 pained, 😟 worried, 😆 joyful, 😊 cheerful, 😎 confident/proud, 🙏 pleading, 🥴 drunk, 😌 relieved, 🤔 questioning/thoughtful, 💪 effortful/strong, 💥 forceful, 📖 narration/monologue.
14. These emojis are MACHINE CONTROLS: RimTalk TTS removes them from visible dialogue/history but preserves them in the text sent to Irodori. Place the emoji exactly where its effect should occur. Example: あははっ🤭、本気で言ってるの？…😮‍💨まあ、君らしいけどね。
15. DO NOT write audible acting as prose stage directions such as （ため息）, (laughs), [whispers], *sigh*, etc. Never output both a prose stage direction and an emoji for the same event. Prefer the direct emoji control instead.
16. Avoid parenthesized/bracketed physical-action narration inside the spoken text. Non-audible physical actions should normally be handled by the existing RimTalk interaction/context fields or omitted from the spoken line, not inserted mid-sentence in parentheses. Whole-line emotion, pace, intensity, and voice style still belong in <delivery-caption>.
";
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/Irodori] Could not append direct acting-emoji prompt instruction: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// CaptureAndStrip caches the TTS payload before this postfix runs. We can therefore remove the
    /// direct Irodori control emojis from RimTalk's visible/history TalkResponse without removing
    /// them from the TTS-only payload. Recognized old-style stage directions are also hidden as a
    /// backward-compatible fallback.
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

                string cleanDisplay = IrodoriStageDirectionMapper.StripActingControlsForDisplay(
                    response.Text,
                    out int strippedCount);

                if (strippedCount <= 0)
                    return;

                response.Text = cleanDisplay;

                if (cfg.UnifiedTtsDebugLogging)
                    Log.Message($"[RimTalk.TTS/Irodori] Hidden {strippedCount} Irodori acting control(s) from RimTalk display/history.");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/Irodori] Acting-control display cleanup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// RimTalk records the raw API response before QueueIncomingResponse performs normal Fast Path
    /// capture. Remove direct acting controls from ApiHistory as well so they remain TTS-only data.
    /// This patch is intentionally independent of the existing RTTTS-envelope ApiHistory patch;
    /// removing the envelope and removing acting controls are commutative operations.
    /// </summary>
    [HarmonyPatch(typeof(ApiHistory), nameof(ApiHistory.AddResponse))]
    public static class IrodoriActingControlApiHistoryPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string response)
        {
            try
            {
                var settings = TTSConfig.Settings;
                var cfg = settings?.Irodori;
                if (!UnifiedTtsPayloadStore.IsEnabled(settings) ||
                    cfg == null ||
                    !cfg.UnifiedTtsStripStageDirections ||
                    string.IsNullOrWhiteSpace(response))
                {
                    return;
                }

                response = IrodoriStageDirectionMapper.StripActingControlsForDisplay(
                    response,
                    out _);
            }
            catch
            {
                // ApiHistory cleanup must never interfere with RimTalk response handling.
            }
        }
    }
}
