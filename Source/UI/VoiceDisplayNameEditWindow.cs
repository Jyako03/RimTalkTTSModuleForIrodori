using System;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.UI
{
    public sealed class VoiceDisplayNameEditWindow : Window
    {
        private readonly string _voiceId;
        private readonly Action<string> _onSave;
        private string _displayName;

        public VoiceDisplayNameEditWindow(string voiceId, string currentName, Action<string> onSave)
        {
            _voiceId = voiceId ?? "";
            _displayName = string.IsNullOrWhiteSpace(currentName) ? _voiceId : currentName;
            _onSave = onSave;
            doCloseX = true;
            draggable = true;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override Vector2 InitialSize => new Vector2(500f, 205f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "RimTalk.TTS.VoiceManage.RenameTitle".Translate().ToString());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 38f, inRect.width, 22f),
                "RimTalk.TTS.VoiceManage.RenameId".Translate(_voiceId).ToString());
            _displayName = Widgets.TextField(new Rect(inRect.x, inRect.y + 66f, inRect.width, 30f), _displayName ?? "");

            float bw = 110f;
            float by = inRect.yMax - 40f;
            if (Widgets.ButtonText(new Rect(inRect.center.x - bw - 5f, by, bw, 32f),
                "RimTalk.TTS.Save".Translate().ToString()))
            {
                string value = (_displayName ?? "").Trim();
                _onSave?.Invoke(string.IsNullOrWhiteSpace(value) ? _voiceId : value);
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.center.x + 5f, by, bw, 32f),
                "RimTalk.TTS.Cancel".Translate().ToString()))
                Close();
        }
    }
}
