using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimTalk.TTS.Data;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.Patch
{
    /// <summary>
    /// Adds Voice Model button to RimTalk's Persona Editor Window
    /// This patch hooks into the PersonaEditorWindow to add a voice model selection button
    /// </summary>
    [HarmonyPatch]
    public static class PersonaEditorPatch
    {

        /// <summary>
        /// Target the DoWindowContents method of RimTalk's PersonaEditorWindow
        /// </summary>
        static System.Reflection.MethodBase TargetMethod()
        {
            try
            {
                var personaEditorType = AccessTools.TypeByName("RimTalk.UI.PersonaEditorWindow");
                if (personaEditorType == null)
                {
                    Log.Warning("[RimTalk.TTS] PersonaEditorWindow type not found. Skipping patch.");
                    return null;
                }

                var method = AccessTools.Method(personaEditorType, "DoWindowContents");
                if (method == null)
                {
                    Log.Warning("[RimTalk.TTS] PersonaEditorWindow.DoWindowContents method not found. Skipping patch.");
                    return null;
                }

                Log.Message("[RimTalk.TTS] Successfully found PersonaEditorWindow.DoWindowContents method");
                return method;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS] Failed to find PersonaEditorWindow: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Only apply patch if the target method is found
        /// </summary>
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        /// <summary>
        /// Postfix to add voice model button in the persona editor
        /// </summary>
        static void Postfix(object __instance, Rect inRect)
        {
            try
            {
                if (!TTSConfig.IsEnabled)
                    return;

                // Get the pawn from the PersonaEditorWindow instance
                var instanceType = __instance.GetType();
                var pawnField = AccessTools.Field(instanceType, "pawn");
                if (pawnField == null)
                {
                    // Try alternative field names
                    pawnField = AccessTools.Field(instanceType, "_pawn");
                }

                if (pawnField == null)
                    return;

                var pawn = pawnField.GetValue(__instance) as Pawn;

                // Draw voice model button
                DrawVoiceModelButton(inRect, pawn);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS] PersonaEditorPatch.Postfix error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void DrawVoiceModelButton(Rect inRect, Pawn pawn)
        {
            // RimTalk 1.3.x redesigned PersonaEditorWindow into Personality / Memories tabs.
            // The old bottom-fixed position now overlaps the Personality tab's chattiness controls.
            // Keep the addon entry point in the window title row instead, which is stable across tabs
            // and does not depend on the internal content layout.
            const float buttonWidth = 120f;
            const float buttonHeight = 28f;

            Rect buttonRect = new Rect(
                inRect.xMax - buttonWidth,
                inRect.y,
                buttonWidth,
                buttonHeight
            );

            TooltipHandler.TipRegion(buttonRect, "RimTalk.TTS.VoiceModelTooltip".Translate());

            if (Widgets.ButtonText(buttonRect, "RimTalk.PersonaEditor.VoiceModel".Translate()))
            {
                Find.WindowStack.Add(new UI.VoiceSelectionWindow(pawn));
            }
        }
    }
}
