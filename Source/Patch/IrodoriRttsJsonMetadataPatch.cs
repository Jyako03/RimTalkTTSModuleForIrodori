using System;
using System.Collections.Generic;
using HarmonyLib;
using RimTalk.Data;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.Util;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// RimTalk's DataContract TalkResponse has no "rtts" member, so its serializer discards
    /// unknown JSON fields. Intercept only TalkResponse JSON after RimTalk's own sanitizer,
    /// before deserialization, and adapt the new sibling metadata property to the existing
    /// in-memory Fast Path envelope. The LLM's on-wire "text" remains free of metadata.
    ///
    /// Existing ApiHistory/QueueIncomingResponse patches already remove the temporary envelope
    /// from display/history and cache its caption for Irodori. Old envelope-only presets are
    /// left unchanged. No modifications to RimTalk.dll are required.
    /// </summary>
    [HarmonyPatch(typeof(JsonUtil), nameof(JsonUtil.Sanitize), new Type[]
    {
        typeof(string),
        typeof(Type)
    })]
    public static class IrodoriRttsJsonMetadataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Type targetType, ref string __result)
        {
            try
            {
                if (targetType != typeof(TalkResponse) ||
                    !UnifiedTtsPayloadStore.IsEnabled(TTSConfig.Settings) ||
                    string.IsNullOrWhiteSpace(__result))
                    return;

                if (!(JsonUtil.ParseJsonValue(__result, out _) is Dictionary<string, object> obj))
                    return;

                // An absent rtts property means a legacy preset or incomplete output.
                // Preserve the original JSON so the existing fallback path can handle it.
                if (!obj.TryGetValue("rtts", out object rawCaption) || !(rawCaption is string caption))
                    return;

                if (!obj.TryGetValue("text", out object rawText) || !(rawText is string text))
                    return;

                // Normalize untrusted model metadata before constructing the internal marker.
                // Bracket characters and newlines cannot appear inside the envelope delimiter.
                caption = caption.Replace('\r', ' ').Replace('\n', ' ')
                                 .Replace('[', ' ').Replace(']', ' ')
                                 .Replace('［', ' ').Replace('］', ' ')
                                 .Trim();
                if (caption.Length > 200)
                    caption = caption.Substring(0, 200).TrimEnd();

                // If an outdated prompt emits BOTH formats, prefer explicit rtts metadata and
                // avoid creating nested envelopes.
                if (UnifiedTtsPayloadStore.TryStripEnvelopeForDisplay(text, out string cleanText))
                    text = cleanText;

                obj["text"] = "[[RTTTS:" + caption + "]]" + (text ?? string.Empty).TrimStart();
                obj.Remove("rtts"); // No matching DataMember on RimTalk's TalkResponse.
                __result = JsonUtil.SerializeJsonValue(obj);
            }
            catch (Exception ex)
            {
                // Never break RimTalk dialogue deserialization for optional TTS metadata.
                Log.Warning("[RimTalk.TTS] Could not read rtts JSON metadata: " + ex.Message);
            }
        }
    }
}
