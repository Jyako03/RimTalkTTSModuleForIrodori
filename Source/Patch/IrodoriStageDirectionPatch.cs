using HarmonyLib;
using RimTalk.Data;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Backward-compatibility path only.
    ///
    /// Irodori-aware RimTalk presets are expected to emit supported inline control emojis directly.
    /// If an older preset or a model deviation still emits a recognizable prose stage direction,
    /// convert it locally for TTS without making the Mod another source of generation rules.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.SanitizeForTts))]
    public static class IrodoriLegacyStageDirectionSanitizePatch
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

                // Direct Irodori control emojis survive SanitizeForTts unchanged.
                // Rebuild the TTS string only when a legacy prose cue was actually recognized.
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
    /// CaptureAndStrip caches the TTS payload before this postfix runs. Remove only Mod-recognized
    /// Irodori machine controls from RimTalk's visible/history TalkResponse while leaving the cached
    /// TTS payload untouched. Recognized legacy prose cues are hidden as a compatibility fallback.
    ///
    /// Important: this patch intentionally does NOT tell the LLM which emojis to generate.
    /// The active RimTalk prompt/preset is the single source of generation policy.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.CaptureAndStrip))]
    public static class IrodoriActingControlDisplayPatch
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
    /// capture. Remove recognized TTS-only controls from ApiHistory as well so machine annotations
    /// do not become future dialogue/style examples.
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
                // History cleanup must never interfere with RimTalk response handling.
            }
        }
    }
}
