using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.Prompt;
using RimTalk.Service;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Keep the Fast Path prompt responsible only for the RTTTS machine envelope and delivery
    /// caption. Dialogue-body policy (including Irodori inline controls and RP action narration)
    /// belongs to the active RimTalk preset and must not be duplicated by the Mod.
    /// </summary>
    [HarmonyPatch(typeof(UnifiedTtsPayloadStore), nameof(UnifiedTtsPayloadStore.BuildPromptInstruction))]
    public static class IrodoriFastPathPromptIsolationPatch
    {
        private const string LegacyBodyPolicy =
            "11. Do not add TTS tags, SSML, or a second copy of the dialogue. Stage directions that are required by the existing RimTalk style may remain in the dialogue; RimTalk TTS can remove them locally from spoken input.";

        private const string IsolatedBodyPolicy =
            "11. Do not add TTS tags, SSML, or a second copy of the dialogue. The dialogue body is governed by the active RimTalk preset; this Fast Path layer defines only the [[RTTTS:...]] envelope and delivery-caption metadata.";

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result))
                return;

            __result = __result.Replace(LegacyBodyPolicy, IsolatedBodyPolicy);
        }
    }

    /// <summary>
    /// Gemma/RP prompts often place strong output rules in the final User message. The full Fast Path
    /// machine-envelope contract is still injected into the initial prompt block by RimTalkPatches,
    /// but a tiny final reminder materially reduces occasional marker omission without duplicating
    /// any Irodori emoji or dialogue-body generation policy.
    /// </summary>
    [HarmonyPatch(typeof(PromptManager), nameof(PromptManager.BuildMessages))]
    [HarmonyPriority(Priority.Last)]
    public static class IrodoriFastPathFinalEnvelopeReminderPatch
    {
        private const string Reminder =
            "\n\n[RIMTALK TTS FAST PATH — FINAL MACHINE REMINDER]\n" +
            "For EVERY JSON text value, begin with the literal ASCII prefix [[RTTTS: and then write the delivery caption. " +
            "Use the form [[RTTTS:<delivery-caption>]]<dialogue> exactly once. " +
            "The active RimTalk preset alone governs the dialogue body and Irodori inline controls; " +
            "this reminder only requires the machine envelope and delivery-caption metadata.";

        [HarmonyPostfix]
        public static void Postfix(ref List<(Role role, string content)> __result)
        {
            try
            {
                var settings = TTSConfig.Settings;
                if (!UnifiedTtsPayloadStore.IsEnabled(settings) || __result == null || __result.Count == 0)
                    return;

                int index = __result.Count - 1;
                var current = __result[index];

                if (current.content != null && current.content.Contains("[RIMTALK TTS FAST PATH — FINAL MACHINE REMINDER]"))
                    return;

                __result[index] = (current.role, (current.content ?? string.Empty) + Reminder);

                if (settings.Irodori?.UnifiedTtsDebugLogging == true)
                    Log.Message("[RimTalk.TTS] Fast Path final envelope reminder injected.");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimTalk.TTS] Fast Path final envelope reminder failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Display/history cleanup for the direct-Irodori Fast Path.
    ///
    /// The active RimTalk preset now owns RP action narration such as （耳元へ顔を寄せる）.
    /// Therefore the Mod MUST NOT interpret parenthesized/bracketed prose semantically here.
    /// Only the known Irodori machine-control emojis are hidden from display/history.
    ///
    /// A small leading-empty-bracket fail-safe removes artifacts such as [] that can be left by
    /// older cleanup ordering. Non-empty brackets and all Japanese action narration are preserved.
    /// </summary>
    internal static class IrodoriFastPathDisplaySanitizer
    {
        private static readonly Regex LeadingEmptyBracketArtifactRegex = new Regex(
            @"^\s*(?:(?:\[\s*\]|［\s*］|【\s*】|〔\s*〕)\s*)+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Clean(string text, bool stripEnvelopeFirst, out int hiddenCount)
        {
            hiddenCount = 0;
            if (string.IsNullOrWhiteSpace(text))
                return text ?? string.Empty;

            string result = text;

            // ApiHistory sees the raw LLM text before QueueIncomingResponse captures the payload.
            // Strip RTTTS first so its inner [RTTTS:caption] can never be mistaken for bracket prose.
            if (stripEnvelopeFirst && UnifiedTtsPayloadStore.TryStripEnvelopeForDisplay(result, out string clean))
                result = clean;

            result = IrodoriStageDirectionMapper.StripControlEmojisForDisplay(
                result,
                out int emojiCount);
            hiddenCount += emojiCount;

            string withoutArtifact = LeadingEmptyBracketArtifactRegex.Replace(result, string.Empty);
            if (!string.Equals(withoutArtifact, result, System.StringComparison.Ordinal))
            {
                hiddenCount++;
                result = withoutArtifact;
            }

            result = Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
            return result;
        }
    }

    /// <summary>
    /// CaptureAndStrip caches the TTS payload before this postfix runs. At this point the RTTTS
    /// envelope has already been removed from TalkResponse.Text. Hide only direct Irodori machine
    /// controls from RimTalk display/history and preserve RP action narration verbatim.
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

                string original = response.Text;
                string cleanDisplay = IrodoriFastPathDisplaySanitizer.Clean(
                    original,
                    stripEnvelopeFirst: false,
                    out int hiddenCount);

                if (!string.Equals(cleanDisplay, original, System.StringComparison.Ordinal))
                    response.Text = cleanDisplay;

                if (hiddenCount > 0 && cfg.UnifiedTtsDebugLogging)
                    Log.Message($"[RimTalk.TTS/Irodori] Hidden {hiddenCount} TTS-only display control(s); RP action narration preserved.");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/Irodori] Acting-control display cleanup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// RimTalk records the raw API response before QueueIncomingResponse performs normal Fast Path
    /// capture. RimTalk 1.2 added an AddResponse overload with targetName; patch the seven-argument
    /// implementation explicitly so both the legacy wrapper and the target-aware path flow through
    /// one unambiguous Harmony target. Always strip RTTTS first, then hide only direct Irodori
    /// control emojis. This avoids interpreting the RTTTS caption or RP action narration as legacy
    /// Stage Directions.
    /// </summary>
    [HarmonyPatch(typeof(ApiHistory), nameof(ApiHistory.AddResponse), new System.Type[]
    {
        typeof(System.Guid),
        typeof(string),
        typeof(string),
        typeof(string),
        typeof(global::RimTalk.Client.Payload),
        typeof(int),
        typeof(string)
    })]
    public static class IrodoriActingControlApiHistoryPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
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

                response = IrodoriFastPathDisplaySanitizer.Clean(
                    response,
                    stripEnvelopeFirst: true,
                    out _);
            }
            catch
            {
                // History cleanup must never interfere with RimTalk response handling.
            }
        }
    }
}
