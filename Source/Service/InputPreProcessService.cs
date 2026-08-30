using System.Text;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using Verse;

namespace RimTalk.TTS.Service
{
    /// <summary>
    /// Translation service using TTS module's own LLM API configuration
    /// </summary>
    public static class InputPreProcessService
    {
        /// <summary>
        /// Translate text to target language using configured LLM API
        /// </summary>
        public static async Task<PreProcessResult> PreProcessAsync(string text, string targetLanguage, TTSSettings settings)
        {
            if (settings == null)
            {
                Log.Warning("[RimTalk.TTS] preprocess settings is null");
                return null;
            }

            try
            {
                // Get TTS processing prompt from settings or use default
                string promptTemplate = TTSConstant.GetTTSProcessingPrompt(settings);

                if (string.IsNullOrWhiteSpace(promptTemplate))
                {
                    Log.Warning("[RimTalk.TTS] preprocess prompt is empty");
                    return BuildUnifiedLocalFallback(text, settings, "preprocess prompt is empty");
                }

                // Build translation prompt
                string prompt = promptTemplate
                    .Replace("{language}", targetLanguage ?? string.Empty);

                // QueryAsync deliberately returns (null, false) for configuration, HTTP, or
                // response-parsing failures. Never dereference response before checking both values.
                var (response, success) = await InputPreProcessClient.QueryAsync(prompt, text, settings);
                if (!success || response == null)
                {
                    Log.Warning("[RimTalk.TTS] Preprocess API failed or returned no structured response");
                    return BuildUnifiedLocalFallback(text, settings, "legacy preprocess API unavailable");
                }

                response.Text = CleanText(response.Text);

                if (!string.IsNullOrEmpty(response.Text))
                    return response;

                Log.Warning("[RimTalk.TTS] Empty response from preprocess API");
                return BuildUnifiedLocalFallback(text, settings, "legacy preprocess returned empty text");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimTalk.TTS] preprocess failed - {ex.Message}");
                return BuildUnifiedLocalFallback(text, settings, "legacy preprocess threw an exception");
            }
        }

        /// <summary>
        /// Final safety net for Irodori Unified Fast Path.
        /// If the model misses [[RTTTS:...]] and the optional legacy preprocessing LLM is also
        /// unavailable, keep the dialogue audible by using deterministic local sanitization.
        /// No delivery caption can be recovered in this path, but inline Irodori control emojis
        /// remain in the TTS text.
        /// </summary>
        private static PreProcessResult BuildUnifiedLocalFallback(string text, TTSSettings settings, string reason)
        {
            if (!UnifiedTtsPayloadStore.IsEnabled(settings))
                return null;

            string localText = UnifiedTtsPayloadStore.SanitizeForTts(text, settings);
            if (string.IsNullOrWhiteSpace(localText))
                return null;

            Log.Warning($"[RimTalk.TTS] {reason}; using local Irodori text fallback without delivery caption.");
            return new PreProcessResult
            {
                Text = localText,
                Emotion = string.Empty
            };
        }

        private static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = System.Text.RegularExpressions.Regex.Replace(
                        System.Text.RegularExpressions.Regex.Replace(
                            text.Normalize(NormalizationForm.FormKC), @"\([^)]*\)", ""
                        )
                        , @"\s+", " "
                    ).Trim();

            if (TTSConfig.CurrentSupplier == TTSSettings.TTSSupplier.FishAudio)
            {
                text = text.Replace("[","(").Replace("]",")");
            }

            return text;
        }
    }
}
