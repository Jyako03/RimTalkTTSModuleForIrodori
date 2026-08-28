using Verse;
using RimWorld;

namespace RimTalk.TTS.Data
{
    /// <summary>
    /// GameComponent to hook PawnVoiceManager.ExposeData into the save/load cycle per game.
    /// </summary>
    public class PawnVoiceGameComponent : GameComponent
    {
        public PawnVoiceGameComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            PawnVoiceManager.ExposeData();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            int repaired = PawnVoiceManager.RepairInvalidVoiceAssignments();
            if (repaired <= 0) return;

            Log.Warning($"[RimTalk.TTS/VoiceManage] Repaired {repaired} pawn voice assignment(s) that referenced Voice Profiles no longer present in global settings. They now use Default.");
            Messages.Message(
                "RimTalk.TTS.VoiceManage.RepairedAssignments".Translate(repaired).ToString(),
                MessageTypeDefOf.TaskCompletion,
                false);
        }
    }
}