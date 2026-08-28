using System;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.UI
{
    public sealed class VoiceDeleteConfirmWindow : Window
    {
        private readonly string _displayName;
        private readonly string _voiceId;
        private readonly Action _onConfirm;

        public VoiceDeleteConfirmWindow(string displayName, string voiceId, Action onConfirm)
        {
            _displayName = string.IsNullOrWhiteSpace(displayName) ? voiceId : displayName;
            _voiceId = voiceId ?? "";
            _onConfirm = onConfirm;
            doCloseX = true;
            draggable = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(520f, 240f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "RimTalk.TTS.VoiceManage.DeleteTitle".Translate().ToString());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inRect.x, inRect.y + 40f, inRect.width, 82f),
                "RimTalk.TTS.VoiceManage.DeleteConfirm".Translate(_displayName, _voiceId).ToString());

            float bw = 120f;
            float by = inRect.yMax - 42f;
            GUI.color = new Color(1f, 0.55f, 0.55f);
            bool delete = Widgets.ButtonText(new Rect(inRect.center.x - bw - 5f, by, bw, 32f),
                "RimTalk.TTS.VoiceManage.Delete".Translate().ToString());
            GUI.color = Color.white;
            if (delete)
            {
                _onConfirm?.Invoke();
                Close();
            }
            if (Widgets.ButtonText(new Rect(inRect.center.x + 5f, by, bw, 32f),
                "RimTalk.TTS.Cancel".Translate().ToString()))
                Close();
        }
    }
}
