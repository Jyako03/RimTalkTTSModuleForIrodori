using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using RimTalk.TTS.UI;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.Patch
{
    internal static class IrodoriVoiceLabGenerationContext
    {
        private static readonly AsyncLocal<int> Depth = new AsyncLocal<int>();
        public static bool Active => Depth.Value > 0;
        public static void Enter() => Depth.Value = Depth.Value + 1;
        public static void Exit() => Depth.Value = Math.Max(0, Depth.Value - 1);
    }

    /// <summary>
    /// Adds the dedicated generation-settings entry point directly to Voice Lab.
    /// The Reference Pack button occupies the far-right title slot, so this button sits immediately left of it.
    /// </summary>
    [HarmonyPatch(typeof(IrodoriVoiceLabWindow), nameof(IrodoriVoiceLabWindow.DoWindowContents))]
    public static class IrodoriVoiceLabGenerationSettingsButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IrodoriVoiceLabWindow __instance, Rect inRect)
        {
            try
            {
                var settingsField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_settings");
                var settings = settingsField?.GetValue(__instance) as TTSSettings;
                if (settings?.Irodori == null || settings.Supplier != TTSSettings.TTSSupplier.Irodori)
                    return;

                if (settings.Irodori.VoiceLab == null)
                    settings.Irodori.VoiceLab = new IrodoriVoiceLabSettings();

                Rect rect = new Rect(inRect.xMax - 340f, inRect.y, 150f, 30f);
                string label = "RimTalk.TTS.VoiceLab.Settings.Button".Translate(settings.Irodori.VoiceLab.NumSteps).ToString();
                if (Widgets.ButtonText(rect, label))
                    Find.WindowStack.Add(new IrodoriVoiceLabSettingsWindow(settings));
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/VoiceLab] Could not draw Lab Settings button: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reference Pack generation is part of Voice Lab, so expose the same independent settings there too.
    /// </summary>
    [HarmonyPatch(typeof(IrodoriReferencePackWindow), nameof(IrodoriReferencePackWindow.DoWindowContents))]
    public static class IrodoriReferencePackGenerationSettingsButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IrodoriReferencePackWindow __instance, Rect inRect)
        {
            try
            {
                var settingsField = AccessTools.Field(typeof(IrodoriReferencePackWindow), "_settings");
                var settings = settingsField?.GetValue(__instance) as TTSSettings;
                if (settings?.Irodori == null) return;
                if (settings.Irodori.VoiceLab == null)
                    settings.Irodori.VoiceLab = new IrodoriVoiceLabSettings();

                Rect rect = new Rect(inRect.xMax - 150f, inRect.y, 150f, 30f);
                string label = "RimTalk.TTS.VoiceLab.Settings.Button".Translate(settings.Irodori.VoiceLab.NumSteps).ToString();
                if (Widgets.ButtonText(rect, label))
                    Find.WindowStack.Add(new IrodoriVoiceLabSettingsWindow(settings));
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/ReferencePack] Could not draw Lab Settings button: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// BuildVoiceDesignJson is synchronous and private. Replacing only this JSON builder keeps the existing
    /// upload/HTTP/error path intact while making Voice Design completely independent from gameplay settings.
    /// </summary>
    [HarmonyPatch]
    public static class IrodoriVoiceDesignDedicatedSettingsPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(IrodoriClient), "BuildVoiceDesignJson");
        }

        [HarmonyPrefix]
        public static bool Prefix(IrodoriClient __instance, object[] __args, ref string __result)
        {
            if (__instance == null || __args == null || __args.Length < 3)
                return true;

            var settings = IrodoriVoiceLabJsonBuilder.GetSettings(__instance);
            if (settings == null) return true;

            var request = __args[0] as TTSRequest;
            string caption = __args[1] as string ?? string.Empty;
            int seed = __args[2] is int value ? value : -1;
            if (request == null) return true;

            __result = IrodoriVoiceLabJsonBuilder.BuildVoiceDesignJson(settings, request, caption, seed);
            return false;
        }
    }

    /// <summary>
    /// Mark only Reference Pack clone generation as Voice-Lab work. AsyncLocal flows into the Task.Run created
    /// by BeginGenerateReference, then the caller thread is immediately restored in the finalizer.
    /// </summary>
    [HarmonyPatch]
    public static class IrodoriReferencePackGenerationContextPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(IrodoriReferencePackWindow), "BeginGenerateReference");
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            IrodoriVoiceLabGenerationContext.Enter();
        }

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            IrodoriVoiceLabGenerationContext.Exit();
            return __exception;
        }
    }

    /// <summary>
    /// Reference Pack clips are generated from a registry anchor through GenerateVoiceRegistryPreviewAsync.
    /// Only while the Reference Pack AsyncLocal context is active, replace the normal gameplay JSON with the
    /// dedicated Voice Lab profile. BIO preview and gameplay synthesis keep the ordinary settings path.
    /// </summary>
    [HarmonyPatch]
    public static class IrodoriReferencePackDedicatedSettingsPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(IrodoriClient), "BuildSpeechJson");
        }

        [HarmonyPrefix]
        public static bool Prefix(IrodoriClient __instance, object[] __args, ref string __result)
        {
            if (!IrodoriVoiceLabGenerationContext.Active || __instance == null || __args == null || __args.Length < 6)
                return true;

            var settings = IrodoriVoiceLabJsonBuilder.GetSettings(__instance);
            var request = __args[0] as TTSRequest;
            string input = __args[1] as string ?? string.Empty;
            string caption = __args[2] as string ?? string.Empty;
            var voiceConfig = __args[3] as IrodoriVoiceConfig;
            string format = __args[4] as string ?? "wav";
            bool useSse = __args[5] is bool b && b;

            if (settings == null || request == null)
                return true;

            // The Reference Pack builder only accepts a normal registry anchor. If that invariant ever changes,
            // fall back to the original builder rather than silently dropping direct/no-ref semantics.
            if (voiceConfig != null && voiceConfig.Mode != IrodoriVoiceConfig.ReferenceMode.RegistryVoice)
                return true;

            __result = IrodoriVoiceLabJsonBuilder.BuildReferencePackJson(
                settings, request, input, caption, voiceConfig, format, useSse);
            return false;
        }
    }

    internal static class IrodoriVoiceLabJsonBuilder
    {
        private static readonly FieldInfo SettingsField = AccessTools.Field(typeof(IrodoriClient), "_settings");

        public static IrodoriSettings GetSettings(IrodoriClient client)
        {
            return SettingsField?.GetValue(client) as IrodoriSettings;
        }

        public static string BuildVoiceDesignJson(IrodoriSettings settings, TTSRequest request, string caption, int seed)
        {
            IrodoriVoiceLabSettings lab = GetLab(settings);
            var root = new List<string>
            {
                Pair("model", string.IsNullOrWhiteSpace(request.Model) ? "irodori-tts" : request.Model),
                Pair("input", request.Input ?? string.Empty),
                Pair("voice", "none"),
                Pair("response_format", "wav"),
                Pair("speed", lab.Speed)
            };

            var opts = BuildLabOptions(lab, caption, false);
            opts.Insert(0, Pair("no_ref", true));
            if (seed >= 0) opts.Add(Pair("seed", seed));
            opts.Add(Pair("chunking_enabled", false));
            AddIf(opts, "lora_adapter", settings.GlobalLoraAdapter);

            root.Add("\"irodori\":{" + string.Join(",", opts) + "}");
            return "{" + string.Join(",", root) + "}";
        }

        public static string BuildReferencePackJson(
            IrodoriSettings settings,
            TTSRequest request,
            string input,
            string caption,
            IrodoriVoiceConfig voiceConfig,
            string format,
            bool useSse)
        {
            IrodoriVoiceLabSettings lab = GetLab(settings);
            var root = new List<string>
            {
                Pair("model", string.IsNullOrWhiteSpace(request.Model) ? "irodori-tts" : request.Model),
                Pair("input", input ?? string.Empty),
                Pair("response_format", string.IsNullOrWhiteSpace(format) ? "wav" : format),
                Pair("speed", lab.Speed),
                Pair("voice", request.Voice ?? string.Empty)
            };
            if (useSse) root.Add(Pair("stream_format", "sse"));

            var opts = BuildLabOptions(lab, caption, true);
            opts.Add(Pair("chunking_enabled", false));
            string lora = !string.IsNullOrWhiteSpace(voiceConfig?.LoraAdapter)
                ? voiceConfig.LoraAdapter
                : settings.GlobalLoraAdapter;
            AddIf(opts, "lora_adapter", lora);

            // Voice Lab intentionally does not merge AdvancedOptionsJson. Exploration should be reproducible and
            // isolated from experimental gameplay overrides such as multi-candidate/batch decode options.
            root.Add("\"irodori\":{" + string.Join(",", opts) + "}");
            return "{" + string.Join(",", root) + "}";
        }

        private static List<string> BuildLabOptions(IrodoriVoiceLabSettings lab, string caption, bool includeMaxRefSeconds)
        {
            var opts = new List<string>();
            if (!string.IsNullOrWhiteSpace(caption)) opts.Add(Pair("caption", caption.Trim()));
            opts.Add(Pair("num_steps", lab.NumSteps));
            opts.Add(Pair("t_schedule_mode", lab.TScheduleMode));
            opts.Add(Pair("sway_coeff", lab.SwayCoeff));
            opts.Add(Pair("duration_scale", lab.DurationScale));
            opts.Add(Pair("cfg_scale_text", lab.CfgScaleText));
            opts.Add(Pair("cfg_scale_speaker", lab.CfgScaleSpeaker));
            if (lab.CfgScaleCaption > 0f) opts.Add(Pair("cfg_scale_caption", lab.CfgScaleCaption));
            if (!string.IsNullOrWhiteSpace(lab.CfgGuidanceMode))
                opts.Add(Pair("cfg_guidance_mode", lab.CfgGuidanceMode));
            if (includeMaxRefSeconds && lab.MaxRefSeconds > 0f)
                opts.Add(Pair("max_ref_seconds", lab.MaxRefSeconds));
            return opts;
        }

        private static IrodoriVoiceLabSettings GetLab(IrodoriSettings settings)
        {
            if (settings.VoiceLab == null)
                settings.VoiceLab = new IrodoriVoiceLabSettings();
            settings.VoiceLab.Normalize();
            return settings.VoiceLab;
        }

        private static string Pair(string key, string value) => Quote(key) + ":" + Quote(value ?? string.Empty);
        private static string Pair(string key, bool value) => Quote(key) + ":" + (value ? "true" : "false");
        private static string Pair(string key, int value) => Quote(key) + ":" + value.ToString(CultureInfo.InvariantCulture);
        private static string Pair(string key, float value) => Quote(key) + ":" + value.ToString("R", CultureInfo.InvariantCulture);

        private static void AddIf(List<string> dst, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) dst.Add(Pair(key, value.Trim()));
        }

        private static string Quote(string value)
        {
            if (value == null) value = string.Empty;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }
    }
}
