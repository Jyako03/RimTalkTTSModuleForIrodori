using System;
using System.Globalization;
using RimTalk.TTS.Data;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.UI
{
    /// <summary>
    /// Edits the generation profile used only by Voice Lab and Reference Pack creation.
    /// Normal gameplay Irodori settings remain untouched.
    /// </summary>
    public sealed class IrodoriVoiceLabSettingsWindow : Window
    {
        private readonly TTSSettings _settings;
        private string _steps;
        private string _speed;
        private string _duration;
        private string _sway;
        private string _cfgText;
        private string _cfgSpeaker;
        private string _cfgCaption;
        private string _maxRefSeconds;
        private string _schedule;
        private string _guidance;
        private string _status = "";

        public IrodoriVoiceLabSettingsWindow(TTSSettings settings)
        {
            _settings = settings;
            doCloseX = true;
            draggable = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            RefreshBuffers(GetLabSettings());
        }

        public override Vector2 InitialSize => new Vector2(650f, 530f);

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 30f),
                "RimTalk.TTS.VoiceLab.Settings.Title".Translate().ToString());
            Text.Font = GameFont.Small;
            y += 36f;

            GUI.color = new Color(0.75f, 0.85f, 1f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 42f),
                "RimTalk.TTS.VoiceLab.Settings.Description".Translate().ToString());
            GUI.color = Color.white;
            y += 48f;

            float colGap = 18f;
            float colW = (inRect.width - colGap) / 2f;
            float left = inRect.x;
            float right = inRect.x + colW + colGap;

            DrawField(left, ref y, colW, "RimTalk.TTS.VoiceLab.Settings.Steps".Translate().ToString(), ref _steps, false);
            float yRight = y - 34f;
            DrawField(right, ref yRight, colW, "RimTalk.TTS.VoiceLab.Settings.Speed".Translate().ToString(), ref _speed, false);

            DrawField(left, ref y, colW, "RimTalk.TTS.VoiceLab.Settings.Duration".Translate().ToString(), ref _duration, false);
            yRight = y - 34f;
            DrawField(right, ref yRight, colW, "RimTalk.TTS.VoiceLab.Settings.Sway".Translate().ToString(), ref _sway, false);

            DrawField(left, ref y, colW, "RimTalk.TTS.VoiceLab.Settings.CfgText".Translate().ToString(), ref _cfgText, false);
            yRight = y - 34f;
            DrawField(right, ref yRight, colW, "RimTalk.TTS.VoiceLab.Settings.CfgSpeaker".Translate().ToString(), ref _cfgSpeaker, false);

            DrawField(left, ref y, colW, "RimTalk.TTS.VoiceLab.Settings.CfgCaption".Translate().ToString(), ref _cfgCaption, false);
            yRight = y - 34f;
            DrawField(right, ref yRight, colW, "RimTalk.TTS.VoiceLab.Settings.MaxRefSeconds".Translate().ToString(), ref _maxRefSeconds, false);

            DrawField(left, ref y, colW, "RimTalk.TTS.VoiceLab.Settings.Schedule".Translate().ToString(), ref _schedule, true);
            yRight = y - 34f;
            DrawField(right, ref yRight, colW, "RimTalk.TTS.VoiceLab.Settings.Guidance".Translate().ToString(), ref _guidance, true);

            y += 8f;
            GUI.color = new Color(0.68f, 0.68f, 0.68f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 62f),
                "RimTalk.TTS.VoiceLab.Settings.Hint".Translate().ToString());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            y += 68f;

            if (!string.IsNullOrWhiteSpace(_status))
            {
                GUI.color = _status.StartsWith("ERROR:", StringComparison.Ordinal) ? Color.red : new Color(0.75f, 1f, 0.75f);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), _status);
                GUI.color = Color.white;
            }

            float buttonY = inRect.yMax - 36f;
            if (Widgets.ButtonText(new Rect(inRect.x, buttonY, 150f, 32f),
                    "RimTalk.TTS.VoiceLab.Settings.Reset".Translate().ToString()))
            {
                var defaults = new IrodoriVoiceLabSettings();
                RefreshBuffers(defaults);
                _status = "";
            }

            float rightEdge = inRect.xMax;
            if (Widgets.ButtonText(new Rect(rightEdge - 270f, buttonY, 130f, 32f),
                    "RimTalk.TTS.VoiceLab.Settings.Save".Translate().ToString()))
            {
                if (TrySave()) Close();
            }
            if (Widgets.ButtonText(new Rect(rightEdge - 130f, buttonY, 130f, 32f),
                    "RimTalk.TTS.Cancel".Translate().ToString()))
            {
                Close();
            }
        }

        private static void DrawField(float x, ref float y, float width, string label, ref string value, bool wideText)
        {
            float labelW = wideText ? 128f : 142f;
            Widgets.Label(new Rect(x, y + 4f, labelW, 26f), label);
            value = Widgets.TextField(new Rect(x + labelW, y, Math.Max(60f, width - labelW), 28f), value ?? "");
            y += 34f;
        }

        private IrodoriVoiceLabSettings GetLabSettings()
        {
            if (_settings?.Irodori == null) return new IrodoriVoiceLabSettings();
            if (_settings.Irodori.VoiceLab == null)
                _settings.Irodori.VoiceLab = new IrodoriVoiceLabSettings();
            _settings.Irodori.VoiceLab.Normalize();
            return _settings.Irodori.VoiceLab;
        }

        private void RefreshBuffers(IrodoriVoiceLabSettings cfg)
        {
            cfg = cfg ?? new IrodoriVoiceLabSettings();
            _steps = cfg.NumSteps.ToString(CultureInfo.InvariantCulture);
            _speed = cfg.Speed.ToString("R", CultureInfo.InvariantCulture);
            _duration = cfg.DurationScale.ToString("R", CultureInfo.InvariantCulture);
            _sway = cfg.SwayCoeff.ToString("R", CultureInfo.InvariantCulture);
            _cfgText = cfg.CfgScaleText.ToString("R", CultureInfo.InvariantCulture);
            _cfgSpeaker = cfg.CfgScaleSpeaker.ToString("R", CultureInfo.InvariantCulture);
            _cfgCaption = cfg.CfgScaleCaption.ToString("R", CultureInfo.InvariantCulture);
            _maxRefSeconds = cfg.MaxRefSeconds.ToString("R", CultureInfo.InvariantCulture);
            _schedule = cfg.TScheduleMode ?? IrodoriVoiceLabSettings.DefaultTScheduleMode;
            _guidance = cfg.CfgGuidanceMode ?? IrodoriVoiceLabSettings.DefaultCfgGuidanceMode;
        }

        private bool TrySave()
        {
            if (!int.TryParse((_steps ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps) || steps < 1 || steps > 200)
                return Fail("Steps must be an integer from 1 to 200.");
            if (!TryFloat(_speed, out float speed) || speed <= 0f || speed > 4f)
                return Fail("Speed must be greater than 0 and at most 4.");
            if (!TryFloat(_duration, out float duration) || duration <= 0f || duration > 10f)
                return Fail("Duration scale must be greater than 0 and at most 10.");
            if (!TryFloat(_sway, out float sway) || sway < -20f || sway > 20f)
                return Fail("Sway coefficient must be between -20 and 20.");
            if (!TryFloat(_cfgText, out float cfgText) || cfgText < 0f || cfgText > 50f)
                return Fail("CFG Text must be between 0 and 50.");
            if (!TryFloat(_cfgSpeaker, out float cfgSpeaker) || cfgSpeaker < 0f || cfgSpeaker > 50f)
                return Fail("CFG Speaker must be between 0 and 50.");
            if (!TryFloat(_cfgCaption, out float cfgCaption) || cfgCaption < 0f || cfgCaption > 50f)
                return Fail("CFG Caption must be between 0 and 50.");
            if (!TryFloat(_maxRefSeconds, out float maxRef) || maxRef <= 0f || maxRef > 600f)
                return Fail("Max reference seconds must be greater than 0 and at most 600.");
            if (string.IsNullOrWhiteSpace(_schedule))
                return Fail("T schedule mode cannot be empty.");
            if (string.IsNullOrWhiteSpace(_guidance))
                return Fail("CFG guidance mode cannot be empty.");

            IrodoriVoiceLabSettings cfg = GetLabSettings();
            cfg.NumSteps = steps;
            cfg.Speed = speed;
            cfg.DurationScale = duration;
            cfg.SwayCoeff = sway;
            cfg.CfgScaleText = cfgText;
            cfg.CfgScaleSpeaker = cfgSpeaker;
            cfg.CfgScaleCaption = cfgCaption;
            cfg.MaxRefSeconds = maxRef;
            cfg.TScheduleMode = _schedule.Trim();
            cfg.CfgGuidanceMode = _guidance.Trim();
            cfg.Normalize();

            try
            {
                var mod = LoadedModManager.GetMod(typeof(TTSMod)) as TTSMod;
                mod?.WriteSettings();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/VoiceLab] Failed to persist Voice Lab settings: {ex.Message}");
            }

            _status = "RimTalk.TTS.VoiceLab.Settings.Saved".Translate().ToString();
            return true;
        }

        private bool Fail(string message)
        {
            _status = "ERROR: " + message;
            return false;
        }

        private static bool TryFloat(string text, out float value)
        {
            string raw = (text ?? "").Trim();
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return !float.IsNaN(value) && !float.IsInfinity(value);
            if (float.TryParse(raw, out value))
                return !float.IsNaN(value) && !float.IsInfinity(value);
            return false;
        }
    }
}
