using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service.IrodoriService;
using RimTalk.TTS.UI;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Adds the Reference Pack entry point to the existing Voice Lab without replacing its proven
    /// single-reference workflow. The currently adopted/resolved Irodori registry voice becomes the
    /// cloning anchor for the builder.
    /// </summary>
    [HarmonyPatch(typeof(IrodoriVoiceLabWindow), nameof(IrodoriVoiceLabWindow.DoWindowContents))]
    public static class IrodoriVoiceLabReferencePackButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IrodoriVoiceLabWindow __instance, Rect inRect)
        {
            try
            {
                if (__instance == null) return;

                var pawnField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_pawn");
                var settingsField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_settings");
                var callbackField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_onVoiceAdopted");
                var profileField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_profileName");
                var sampleField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_sampleText");
                var deliveryField = AccessTools.Field(typeof(IrodoriVoiceLabWindow), "_deliveryHint");

                var pawn = pawnField?.GetValue(__instance) as Pawn;
                var settings = settingsField?.GetValue(__instance) as TTSSettings;
                if (pawn == null || settings == null || settings.Supplier != TTSSettings.TTSSupplier.Irodori || settings.Irodori == null)
                    return;

                string anchorId = PawnVoiceManager.GetVoiceModel(pawn);
                bool validAnchor = !string.IsNullOrWhiteSpace(anchorId) &&
                                   anchorId != VoiceModel.NONE_MODEL_ID &&
                                   anchorId != VoiceModel.DEFAULT_MODEL_ID &&
                                   anchorId != VoiceModel.RULE_BASED_MODEL_ID;

                IrodoriVoiceConfig anchorCfg = validAnchor ? settings.Irodori.GetVoiceConfig(anchorId) : null;
                bool alreadyPack = anchorCfg != null &&
                                   anchorCfg.Mode == IrodoriVoiceConfig.ReferenceMode.DirectReferences &&
                                   anchorCfg.ReferenceVoiceIds != null &&
                                   anchorCfg.ReferenceVoiceIds.Count > 0;
                bool registryAnchor = validAnchor && !alreadyPack &&
                                      (anchorCfg == null || anchorCfg.Mode == IrodoriVoiceConfig.ReferenceMode.RegistryVoice);

                Rect button = new Rect(inRect.xMax - 184f, inRect.y, 184f, 30f);
                GUI.enabled = registryAnchor;
                string label = alreadyPack
                    ? "RimTalk.TTS.ReferencePack.AlreadyActive".Translate().ToString()
                    : "RimTalk.TTS.ReferencePack.Open".Translate().ToString();

                if (Widgets.ButtonText(button, label))
                {
                    var callback = callbackField?.GetValue(__instance) as Action<string>;
                    string profile = profileField?.GetValue(__instance) as string;
                    string sample = sampleField?.GetValue(__instance) as string;
                    string delivery = deliveryField?.GetValue(__instance) as string;

                    Find.WindowStack.Add(new IrodoriReferencePackWindow(
                        pawn,
                        settings,
                        anchorId,
                        profile,
                        sample,
                        delivery,
                        callback));
                }
                GUI.enabled = true;

                if (!registryAnchor)
                {
                    string tip = alreadyPack
                        ? "RimTalk.TTS.ReferencePack.AlreadyActiveTip".Translate().ToString()
                        : "RimTalk.TTS.ReferencePack.NeedAnchorTip".Translate().ToString();
                    TooltipHandler.TipRegion(button, tip);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/ReferencePack] Voice Lab button patch failed: {ex.Message}");
                GUI.enabled = true;
            }
        }
    }

    /// <summary>
    /// Reference Pack backing clips are implementation details and must not appear as normal voice
    /// choices when the user presses "Sync server voices".
    /// </summary>
    [HarmonyPatch(typeof(IrodoriClient), nameof(IrodoriClient.ListVoicesAsync))]
    public static class IrodoriReferencePackHideInternalVoicesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Task<List<(string id, string name)>> __result)
        {
            if (__result == null) return;
            __result = FilterAsync(__result);
        }

        private static async Task<List<(string id, string name)>> FilterAsync(
            Task<List<(string id, string name)>> source)
        {
            var list = await source;
            if (list == null) return new List<(string id, string name)>();
            return list.Where(v => string.IsNullOrWhiteSpace(v.id) ||
                                   !v.id.StartsWith(IrodoriReferencePackService.InternalReferencePrefix,
                                       StringComparison.OrdinalIgnoreCase))
                       .ToList();
        }
    }

    /// <summary>
    /// Packs use local profile IDs, so the normal managed-delete call intentionally gets 404 for
    /// that logical ID (treated as already deleted). Before local cleanup removes the pack config,
    /// schedule deletion of every pack-owned backing registry voice.
    /// </summary>
    [HarmonyPatch]
    public static class IrodoriReferencePackManagedDeletePatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(VoiceSelectionWindow), "BeginManagedVoiceDelete");
        }

        [HarmonyPrefix]
        public static void Prefix(VoiceSelectionWindow __instance, string voiceId)
        {
            try
            {
                if (__instance == null || string.IsNullOrWhiteSpace(voiceId)) return;
                var settingsField = AccessTools.Field(typeof(VoiceSelectionWindow), "_settings");
                var settings = settingsField?.GetValue(__instance) as TTSSettings;
                var cfg = settings?.Irodori?.GetVoiceConfig(voiceId);
                var owned = cfg?.ReferenceVoiceIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .ToList();
                if (owned == null || owned.Count == 0) return;

                string baseUrl = settings.Irodori.BaseUrl;
                string apiKey = settings.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori);
                Task.Run(() => IrodoriReferencePackService.DeleteOwnedReferencesAsync(baseUrl, apiKey, owned));
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/ReferencePack] Could not schedule backing-reference cleanup: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Unlike disposable single Voice Lab registrations, deleting a pack removes multiple backing
    /// files. Require the normal confirmation dialog even though the logical ID starts with rttts_.
    /// </summary>
    [HarmonyPatch]
    public static class IrodoriReferencePackDeleteConfirmationPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(VoiceSelectionWindow), "RequiresManagedDeleteConfirmation");
        }

        [HarmonyPostfix]
        public static void Postfix(VoiceSelectionWindow __instance, string voiceId, ref bool __result)
        {
            try
            {
                if (__result || __instance == null || string.IsNullOrWhiteSpace(voiceId)) return;
                var settingsField = AccessTools.Field(typeof(VoiceSelectionWindow), "_settings");
                var settings = settingsField?.GetValue(__instance) as TTSSettings;
                var cfg = settings?.Irodori?.GetVoiceConfig(voiceId);
                if (cfg?.ReferenceVoiceIds != null && cfg.ReferenceVoiceIds.Count > 0)
                    __result = true;
            }
            catch
            {
                // Keep the original confirmation behavior on reflection/config failure.
            }
        }
    }
}
