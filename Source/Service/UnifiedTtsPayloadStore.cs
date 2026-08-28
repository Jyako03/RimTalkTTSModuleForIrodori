using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimTalk.Data;
using RimTalk.TTS.Data;
using Verse;

namespace RimTalk.TTS.Service
{
    /// <summary>
    /// Fast-path bridge between RimTalk's first LLM response and the TTS request.
    ///
    /// Canonical machine envelope (v4):
    ///   [[RTTTS:穏やかで柔らかく、少し安堵して]]実際に表示する台詞
    ///
    /// Older v2/v3 envelopes (⟦RTTTS:...⟧) and several common bracket substitutions are
    /// accepted for backward compatibility. The parser is intentionally tolerant because LLMs
    /// occasionally substitute visually similar Unicode brackets even when explicitly instructed
    /// not to do so.
    ///
    /// The envelope is stripped before the response enters RimTalk's visible/history queue and the
    /// delivery caption is kept here by TalkResponse.Id until TTSService consumes it. RimTalk itself
    /// therefore remains unmodified and the second preprocessing LLM request can be skipped.
    /// </summary>
    public static class UnifiedTtsPayloadStore
    {
        public sealed class Payload
        {
            public string Text;
            public string Emotion;
        }

        private const string CanonicalPrefix = "[[RTTTS:";
        private const string CanonicalSuffix = "]]";
        private const string LegacyPrefix = "⟦RTTTS:";
        private const int MaxReasonableCaptionChars = 240;

        private static readonly ConcurrentDictionary<Guid, Payload> Payloads =
            new ConcurrentDictionary<Guid, Payload>();

        // Prefixes accepted only at the beginning (ignoring leading whitespace). Keeping this list
        // explicit avoids accidentally treating ordinary dialogue containing "RTTTS:" as metadata.
        private static readonly string[] AcceptedPrefixes =
        {
            CanonicalPrefix,
            LegacyPrefix,
            "［［RTTTS:",
            "【RTTTS:",
            "〔RTTTS:",
            "[RTTTS:"
        };

        // Ordered longest/most-specific first. The first matching terminator wins.
        private static readonly string[] AcceptedSuffixes =
        {
            CanonicalSuffix,
            "］］",
            "⟧",
            "］",
            "】",
            "〕",
            "]"
        };

        public static bool IsEnabled(TTSSettings settings)
        {
            return settings != null
                && settings.EnableTTS
                && settings.isOnButton
                && settings.Supplier == TTSSettings.TTSSupplier.Irodori
                && settings.Irodori != null
                && settings.Irodori.UnifiedTtsEnabled;
        }

        /// <summary>
        /// Instruction injected into RimTalk's already-built prompt. ASCII delimiters are used on
        /// purpose: they are materially less likely than uncommon Unicode brackets to be substituted
        /// by the model/tokenizer. Source-side recovery still treats the model output as untrusted.
        /// </summary>
        public static string BuildPromptInstruction(TTSSettings settings)
        {
            var cfg = settings?.Irodori;
            string extra = cfg?.UnifiedTtsExtraInstruction;

            string instruction = @"
[RIMTALK TTS FAST PATH — MACHINE ENVELOPE]
This requirement supplements the existing dialogue JSON format and applies to EVERY generated dialogue object.

For the value of each JSON ""text"" field:
1. The FIRST characters MUST be exactly these ASCII characters: [[RTTTS:
2. Close the metadata with exactly these two ASCII characters: ]]
3. Required format: [[RTTTS:<delivery-caption>]]<dialogue>
4. Use ASCII '[' and ']' for this envelope ONLY. NEVER replace them with Unicode lookalikes such as ⟦ ⟧ ［ ］ 【 】 〔 〕.
5. <delivery-caption> is one short single-line natural-language instruction for Irodori-TTS describing ONLY audible delivery: emotion, restraint/intensity, softness/harshness, pace, hesitation, confidence, whisper/shout tendency, breathing quality, or similar vocal style.
6. Keep <delivery-caption> concise (preferably under 120 characters), with no brackets, quotation marks, line breaks, stage directions, or spoken dialogue inside it.
7. Prefer a nuanced Japanese caption when the dialogue is Japanese. Example: [[RTTTS:嬉しさを抑えきれず、少し早口で明るく]]やっと終わった！
8. For a genuinely neutral line, use an empty caption: [[RTTTS:]]普通に話す台詞。
9. Immediately after ]], write the actual RimTalk dialogue exactly once. The envelope is metadata, not spoken dialogue. Do NOT mention, explain, quote, or react to it.
10. Keep every other required JSON field (name / act / target etc.) exactly as required by the existing RimTalk instructions.
11. Do not add TTS tags, SSML, or a second copy of the dialogue. Stage directions that are required by the existing RimTalk style may remain in the dialogue; RimTalk TTS can remove them locally from spoken input.
";

            if (!string.IsNullOrWhiteSpace(extra))
                instruction += "\nAdditional fast-path instruction:\n" + extra.Trim() + "\n";

            return instruction;
        }

        /// <summary>
        /// Parse and remove the machine marker from a RimTalk response, preserving the delivery
        /// caption for TTS. Marker-like text is never allowed to remain in visible RimTalk text.
        /// </summary>
        public static bool CaptureAndStrip(TalkResponse response, TTSSettings settings)
        {
            if (response == null || !IsEnabled(settings)) return false;

            bool recognized = TryParseEnvelope(response.Text, out string emotion, out string displayText, out bool recovered);
            if (!recognized)
            {
                if (settings.Irodori?.UnifiedTtsDebugLogging == true)
                    Log.Warning("[RimTalk.TTS] Unified fast path marker missing; legacy fallback may be used.");
                return false;
            }

            // Even a malformed-but-recognized envelope must never leak into normal RimTalk display/history.
            response.Text = displayText ?? string.Empty;

            if (response.Id == Guid.Empty)
            {
                if (settings.Irodori?.UnifiedTtsDebugLogging == true)
                    Log.Warning("[RimTalk.TTS] Unified payload parsed before TalkResponse.Id was assigned; payload not cached.");
                return false;
            }

            // A recognized malformed envelope may have no safely recoverable caption. Empty caption is
            // preferable to exposing metadata or forcing a second LLM call for an otherwise usable line.
            Payloads[response.Id] = new Payload
            {
                Text = SanitizeForTts(displayText, settings),
                Emotion = emotion?.Trim() ?? string.Empty
            };

            if (settings.Irodori?.UnifiedTtsDebugLogging == true)
            {
                string mode = recovered ? "recovered" : "canonical/compatible";
                Log.Message($"[RimTalk.TTS] Unified payload captured ({mode}): id={response.Id}, emotion='{emotion}'");
            }

            return true;
        }

        public static bool TryTake(Guid dialogueId, out Payload payload)
        {
            return Payloads.TryRemove(dialogueId, out payload);
        }

        /// <summary>
        /// Non-destructive lookup used by the Voice Lab recent-dialogue cache before TTSService
        /// consumes the fast-path payload.
        /// </summary>
        public static bool TryPeek(Guid dialogueId, out Payload payload)
        {
            if (dialogueId == Guid.Empty)
            {
                payload = null;
                return false;
            }
            return Payloads.TryGetValue(dialogueId, out payload);
        }

        public static void Remove(Guid dialogueId)
        {
            if (dialogueId != Guid.Empty)
                Payloads.TryRemove(dialogueId, out _);
        }

        public static void Clear()
        {
            Payloads.Clear();
        }

        /// <summary>
        /// Used by the ApiHistory patch. If the text begins with any recognized RTTTS marker,
        /// return a cleaned display string even when the closing bracket was malformed.
        /// </summary>
        public static bool TryStripEnvelopeForDisplay(string raw, out string displayText)
        {
            if (TryParseEnvelope(raw, out _, out displayText, out _)) return true;
            displayText = raw;
            return false;
        }

        public static bool TryParseEnvelope(string raw, out string emotion, out string text)
        {
            return TryParseEnvelope(raw, out emotion, out text, out _);
        }

        /// <summary>
        /// Tolerant parser for model-generated machine envelopes.
        /// Returns true when an RTTTS prefix is recognized, even if recovery/fail-safe handling was
        /// required. This guarantees that machine metadata cannot leak into normal display/history.
        /// </summary>
        private static bool TryParseEnvelope(string raw, out string emotion, out string text, out bool recovered)
        {
            emotion = string.Empty;
            text = raw ?? string.Empty;
            recovered = false;
            if (string.IsNullOrEmpty(raw)) return false;

            int start = 0;
            while (start < raw.Length && char.IsWhiteSpace(raw[start])) start++;

            string prefix = null;
            foreach (string candidate in AcceptedPrefixes)
            {
                if (StartsWithOrdinalIgnoreCase(raw, start, candidate))
                {
                    prefix = candidate;
                    break;
                }
            }

            if (prefix == null) return false;

            int captionStart = start + prefix.Length;
            int searchLimit = Math.Min(raw.Length, captionStart + MaxReasonableCaptionChars + 8);

            // First accept the canonical/compatible closing delimiters. This includes the exact bad
            // sample that motivated v4: legacy ⟦RTTTS: opener closed with full-width ］.
            if (TryFindSuffix(raw, captionStart, searchLimit, out int suffixIndex, out int suffixLength, out string suffix))
            {
                emotion = raw.Substring(captionStart, suffixIndex - captionStart).Trim();
                text = raw.Substring(suffixIndex + suffixLength).Trim();
                recovered = !(prefix == CanonicalPrefix && suffix == CanonicalSuffix);
                return true;
            }

            // Recovery 1: a newline accidentally replaced the closer. Caption is explicitly required
            // to be one line, so the first newline is a safe boundary.
            int newlineIndex = IndexOfNewline(raw, captionStart, searchLimit);
            if (newlineIndex >= 0)
            {
                emotion = raw.Substring(captionStart, newlineIndex - captionStart).Trim();
                text = raw.Substring(SkipNewline(raw, newlineIndex)).Trim();
                recovered = true;
                return true;
            }

            // Recovery 2: common RimTalk stage-direction/dialogue starters. This preserves the example
            // form "...］(stage direction)..." even if the closing bracket itself was omitted entirely.
            int likelyDialogueStart = FindLikelyDialogueStart(raw, captionStart, searchLimit);
            if (likelyDialogueStart > captionStart)
            {
                emotion = raw.Substring(captionStart, likelyDialogueStart - captionStart).Trim();
                text = raw.Substring(likelyDialogueStart).Trim();
                recovered = true;
                return true;
            }

            // Final fail-safe: an RTTTS prefix was definitely emitted, but its boundary cannot be
            // determined safely. Suppress the marker-leading line rather than ever showing/speaking
            // machine metadata. If a later line exists, preserve that later text as the dialogue.
            int anyNewline = IndexOfNewline(raw, captionStart, raw.Length);
            if (anyNewline >= 0)
                text = raw.Substring(SkipNewline(raw, anyNewline)).Trim();
            else
                text = string.Empty;

            emotion = string.Empty;
            recovered = true;
            Log.Warning("[RimTalk.TTS] Malformed RTTTS envelope could not be safely separated; machine metadata was suppressed.");
            return true;
        }

        private static bool StartsWithOrdinalIgnoreCase(string value, int start, string prefix)
        {
            if (value == null || prefix == null || start < 0 || start + prefix.Length > value.Length)
                return false;
            return string.Compare(value, start, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool TryFindSuffix(
            string raw,
            int captionStart,
            int searchLimit,
            out int suffixIndex,
            out int suffixLength,
            out string matchedSuffix)
        {
            suffixIndex = -1;
            suffixLength = 0;
            matchedSuffix = null;

            int bestIndex = int.MaxValue;
            string bestSuffix = null;
            foreach (string suffix in AcceptedSuffixes)
            {
                int index = raw.IndexOf(suffix, captionStart, Math.Max(0, searchLimit - captionStart), StringComparison.Ordinal);
                if (index >= 0 && index < bestIndex)
                {
                    bestIndex = index;
                    bestSuffix = suffix;
                }
                else if (index >= 0 && index == bestIndex && bestSuffix != null && suffix.Length > bestSuffix.Length)
                {
                    bestSuffix = suffix;
                }
            }

            if (bestSuffix == null) return false;
            suffixIndex = bestIndex;
            suffixLength = bestSuffix.Length;
            matchedSuffix = bestSuffix;
            return true;
        }

        private static int IndexOfNewline(string raw, int start, int limit)
        {
            int end = Math.Min(raw.Length, Math.Max(start, limit));
            for (int i = start; i < end; i++)
            {
                if (raw[i] == '\r' || raw[i] == '\n') return i;
            }
            return -1;
        }

        private static int SkipNewline(string raw, int index)
        {
            int pos = index;
            if (pos < raw.Length && raw[pos] == '\r') pos++;
            if (pos < raw.Length && raw[pos] == '\n') pos++;
            return pos;
        }

        private static int FindLikelyDialogueStart(string raw, int captionStart, int searchLimit)
        {
            // Stage directions and Japanese dialogue punctuation are much more plausible dialogue
            // starters than delivery-caption content. Only use this heuristic after all closers failed.
            var candidates = new List<int>();
            AddIndex(candidates, raw, "(", captionStart, searchLimit);
            AddIndex(candidates, raw, "（", captionStart, searchLimit);
            AddIndex(candidates, raw, "「", captionStart, searchLimit);
            AddIndex(candidates, raw, "『", captionStart, searchLimit);
            AddIndex(candidates, raw, "\"", captionStart, searchLimit);
            AddIndex(candidates, raw, "……", captionStart, searchLimit);
            AddIndex(candidates, raw, "…", captionStart, searchLimit);

            int best = int.MaxValue;
            foreach (int index in candidates)
            {
                // Require at least a small caption before switching to heuristic mode.
                if (index >= captionStart + 2 && index < best)
                    best = index;
            }
            return best == int.MaxValue ? -1 : best;
        }

        private static void AddIndex(List<int> values, string raw, string token, int start, int limit)
        {
            int count = Math.Max(0, Math.Min(raw.Length, limit) - start);
            if (count <= 0) return;
            int index = raw.IndexOf(token, start, count, StringComparison.Ordinal);
            if (index >= 0) values.Add(index);
        }

        /// <summary>
        /// Deterministic local cleanup replacing the old LLM's most important non-translation
        /// cleanup behavior. It only affects TTS input, never RimTalk's displayed line.
        /// </summary>
        public static string SanitizeForTts(string text, TTSSettings settings)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

            var cfg = settings?.Irodori;
            string result = text;

            if (cfg?.UnifiedTtsStripStageDirections == true)
            {
                result = Regex.Replace(result, @"\([^()]*\)", " ");
                result = Regex.Replace(result, @"（[^（）]*）", " ");
                result = Regex.Replace(result, @"\[[^\[\]]*\]", " ");
                result = Regex.Replace(result, @"【[^【】]*】", " ");
                result = Regex.Replace(result, @"\*[^*]*\*", " ");
            }

            // Last line of defense: even if malformed metadata somehow reaches this method, remove
            // any recognized RTTTS envelope before it can be sent to Irodori as spoken text.
            if (TryParseEnvelope(result, out _, out string safeText, out _))
                result = safeText;

            result = Regex.Replace(result.Normalize(System.Text.NormalizationForm.FormKC), @"\s+", " ").Trim();
            return result;
        }
    }
}
