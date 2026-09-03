using System.Collections.Generic;
using Verse;

namespace RimTalk.TTS.Data
{
    /// <summary>
    /// Irodori-TTS request-side configuration.
    /// Server/runtime options (device, precision, compile, cache lifecycle, etc.) remain server settings.
    /// AdvancedOptionsJson is merged into the request's `irodori` object last, so it can override typed defaults
    /// and can carry new Irodori options added by the server without a mod update.
    /// </summary>
    public class IrodoriSettings : IExposable
    {
        public string BaseUrl = "http://127.0.0.1:8088/v1";
        public string ResponseFormat = "wav";
        public bool UseSse = false;

        // High-value v4 inference controls exposed directly in the UI.
        public int NumSteps = 40;
        public string TScheduleMode = "linear";
        public float SwayCoeff = -1.0f;
        public float DurationScale = 1.0f;
        public float CfgScaleText = 3.0f;
        public float CfgScaleSpeaker = 5.0f;
        public float CfgScaleCaption = 0.0f; // 0 = omit and use server/checkpoint default
        public string CfgGuidanceMode = "independent";
        public int Seed = -1; // -1 = omit/random
        public float MaxRefSeconds = 120.0f;

        public bool ChunkingEnabled = true;
        public int ChunkMinChars = 80;
        public int FirstSentenceChunkMinChars = 0; // 0 = omit

        // Voice Lab uses an independent generation profile. Changing these values does not alter
        // normal gameplay synthesis settings above. Missing legacy saves receive the Voice Lab defaults.
        public IrodoriVoiceLabSettings VoiceLab = new IrodoriVoiceLabSettings();

        // Emotion/style bridge. Caption remains useful with Unified Fast Path, but automatic
        // emotion->emoji mapping is legacy behavior and defaults OFF because Irodori-aware RimTalk
        // presets now emit localized inline control emojis directly.
        public bool EmotionToCaption = true;
        public bool EmotionToEmoji = false;
        public string CaptionPrefix = "";
        public string GlobalLoraAdapter = "";

        // Optional fast path: ask RimTalk's FIRST LLM response to carry an Irodori delivery caption
        // in a compact machine envelope, then bypass the TTS preprocessing LLM call.
        // Successful path becomes: RimTalk LLM -> Irodori TTS (2 network/API requests instead of 3).
        public bool UnifiedTtsEnabled = false;

        // Legacy preprocessing is now a compatibility option rather than the recommended Fast Path
        // behavior. When disabled, a missing marker is handled locally without an extra LLM request.
        public bool UnifiedTtsFallbackToLegacy = false;
        public bool UnifiedTtsStripStageDirections = true;
        public bool UnifiedTtsDebugLogging = false;
        public string UnifiedTtsExtraInstruction = "";

        // Raw JSON object contents. Example: {"num_candidates":2,"decode_mode":"batch"}
        public string AdvancedOptionsJson = "{}";

        // Irodori-specific per-voice/reference configuration, keyed by VoiceModel.ModelId.
        public Dictionary<string, IrodoriVoiceConfig> VoiceConfigs = new Dictionary<string, IrodoriVoiceConfig>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref BaseUrl, "baseUrl", "http://127.0.0.1:8088/v1");
            Scribe_Values.Look(ref ResponseFormat, "responseFormat", "wav");
            Scribe_Values.Look(ref UseSse, "useSse", false);
            Scribe_Values.Look(ref NumSteps, "numSteps", 40);
            Scribe_Values.Look(ref TScheduleMode, "tScheduleMode", "linear");
            Scribe_Values.Look(ref SwayCoeff, "swayCoeff", -1.0f);
            Scribe_Values.Look(ref DurationScale, "durationScale", 1.0f);
            Scribe_Values.Look(ref CfgScaleText, "cfgScaleText", 3.0f);
            Scribe_Values.Look(ref CfgScaleSpeaker, "cfgScaleSpeaker", 5.0f);
            Scribe_Values.Look(ref CfgScaleCaption, "cfgScaleCaption", 0.0f);
            Scribe_Values.Look(ref CfgGuidanceMode, "cfgGuidanceMode", "independent");
            Scribe_Values.Look(ref Seed, "seed", -1);
            Scribe_Values.Look(ref MaxRefSeconds, "maxRefSeconds", 120.0f);
            Scribe_Values.Look(ref ChunkingEnabled, "chunkingEnabled", true);
            Scribe_Values.Look(ref ChunkMinChars, "chunkMinChars", 80);
            Scribe_Values.Look(ref FirstSentenceChunkMinChars, "firstSentenceChunkMinChars", 0);
            Scribe_Deep.Look(ref VoiceLab, "voiceLabSettings");
            Scribe_Values.Look(ref EmotionToCaption, "emotionToCaption", true);
            Scribe_Values.Look(ref EmotionToEmoji, "emotionToEmoji", false);
            Scribe_Values.Look(ref CaptionPrefix, "captionPrefix", "");
            Scribe_Values.Look(ref GlobalLoraAdapter, "globalLoraAdapter", "");
            Scribe_Values.Look(ref UnifiedTtsEnabled, "unifiedTtsEnabled", false);
            Scribe_Values.Look(ref UnifiedTtsFallbackToLegacy, "unifiedTtsFallbackToLegacy", false);
            Scribe_Values.Look(ref UnifiedTtsStripStageDirections, "unifiedTtsStripStageDirections", true);
            Scribe_Values.Look(ref UnifiedTtsDebugLogging, "unifiedTtsDebugLogging", false);
            Scribe_Values.Look(ref UnifiedTtsExtraInstruction, "unifiedTtsExtraInstruction", "");
            Scribe_Values.Look(ref AdvancedOptionsJson, "advancedOptionsJson", "{}");
            Scribe_Collections.Look(ref VoiceConfigs, "voiceConfigs", LookMode.Value, LookMode.Deep);

            if (VoiceLab == null)
                VoiceLab = new IrodoriVoiceLabSettings();
            VoiceLab.Normalize();

            if (VoiceConfigs == null)
                VoiceConfigs = new Dictionary<string, IrodoriVoiceConfig>();
        }

        private void EnsureVoiceConfigs()
        {
            if (VoiceConfigs == null)
                VoiceConfigs = new Dictionary<string, IrodoriVoiceConfig>();
        }

        public IrodoriVoiceConfig GetVoiceConfig(string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
                return null;
            EnsureVoiceConfigs();
            return VoiceConfigs.TryGetValue(voiceId, out var cfg) ? cfg : null;
        }

        public IrodoriVoiceConfig GetOrCreateVoiceConfig(string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
                return null;
            EnsureVoiceConfigs();
            if (!VoiceConfigs.TryGetValue(voiceId, out var cfg) || cfg == null)
            {
                cfg = new IrodoriVoiceConfig();
                VoiceConfigs[voiceId] = cfg;
            }
            return cfg;
        }
    }
}
