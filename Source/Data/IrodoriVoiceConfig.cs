using System.Collections.Generic;
using Verse;

namespace RimTalk.TTS.Data
{
    /// <summary>
    /// Optional Irodori-only configuration attached to a RimTalk TTS VoiceModel ID.
    /// Registry mode is ideal for a remote/sub-PC Irodori server. DirectReferences expects paths visible to the server.
    /// </summary>
    public class IrodoriVoiceConfig : IExposable
    {
        public enum ReferenceMode
        {
            RegistryVoice,
            DirectReferences,
            NoReference
        }

        public ReferenceMode Mode = ReferenceMode.RegistryVoice;
        public string RefWav = "";
        public List<string> RefWavs = new List<string>();
        public string RefLatent = "";
        public List<string> RefLatents = new List<string>();
        public string RefEmbed = "";
        public string Caption = "";
        public string LoraAdapter = "";

        public void ExposeData()
        {
            Scribe_Values.Look(ref Mode, "mode", ReferenceMode.RegistryVoice);
            Scribe_Values.Look(ref RefWav, "refWav", "");
            Scribe_Collections.Look(ref RefWavs, "refWavs", LookMode.Value);
            Scribe_Values.Look(ref RefLatent, "refLatent", "");
            Scribe_Collections.Look(ref RefLatents, "refLatents", LookMode.Value);
            Scribe_Values.Look(ref RefEmbed, "refEmbed", "");
            Scribe_Values.Look(ref Caption, "caption", "");
            Scribe_Values.Look(ref LoraAdapter, "loraAdapter", "");

            if (RefWavs == null) RefWavs = new List<string>();
            if (RefLatents == null) RefLatents = new List<string>();
        }
    }
}
