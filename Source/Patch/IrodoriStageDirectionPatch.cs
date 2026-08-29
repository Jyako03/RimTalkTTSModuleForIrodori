using HarmonyLib;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Preserves the stable UnifiedTtsPayloadStore implementation while enriching its TTS-only
    /// sanitized text with Irodori's documented emoji annotations.
    ///
    /// SanitizeForTts already receives the original display dialogue as its first argument. Its
    /// normal implementation strips stage directions. This postfix can therefore rebuild the
    /// TTS-only text from that original argument and replace recognized directions with emojis,
    /// while leaving RimTalk's visible/history text untouched.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.SanitizeForTts))]
    public static class IrodoriStageDirectionPatch
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
                // from the fast-path fallback with the raw model output rather than displayText.
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
                // This feature is deliberately fail-open: if the mapper ever fails, retain the
                // original SanitizeForTts result so Fast Path behavior is unchanged.
                Log.Warning($"[RimTalk.TTS/Irodori] Stage Direction conversion failed; using normal sanitized text. {ex.Message}");
            }
        }
    }
}
