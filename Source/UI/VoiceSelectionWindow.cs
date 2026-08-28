using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using RimTalk.TTS.Data;
using System.Threading.Tasks;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;

namespace RimTalk.TTS.UI
{
    /// <summary>
    /// Voice model selection window for individual pawns
    /// </summary>
    public class VoiceSelectionWindow : Window
    {
        private readonly Pawn _pawn;
        private string _selectedVoiceId;
        private string _customLanguage;
        private string _previewSampleText;
        private Vector2 _scrollPos = Vector2.zero;
        private readonly TTSSettings _settings;
        private readonly List<VoiceModel> _voiceModels;

        // BIO preview requests are asynchronous. Only the most recently clicked voice may play;
        // older HTTP results are discarded when the user clicks another Preview button.
        private readonly object _previewLock = new object();
        private int _previewRequestVersion = 0;
        private int _pendingPreviewVersion = 0;
        private byte[] _pendingPreviewAudio = null;
        private string _pendingPreviewError = null;
        private string _previewGeneratingVoiceId = null;

        static VoiceSelectionWindow()
        {
        }

        public VoiceSelectionWindow(Pawn pawn)
        {
            _pawn = pawn;
            
            // Load settings once
            var modInstance = LoadedModManager.GetMod(typeof(TTSMod)) as TTSMod;
            if (modInstance != null)
            {
                _settings = modInstance.GetSettings<TTSSettings>();
                _voiceModels = _settings != null ? (_settings.GetSupplierVoiceModels(_settings.Supplier) ?? new List<VoiceModel>()) : new List<VoiceModel>();
            }
            else
            {
                _settings = null;
                _voiceModels = new List<VoiceModel>();
            }
            
            _selectedVoiceId = GetCurrentVoiceModel();
            _customLanguage = GetCurrentLanguage();
            _previewSampleText = GetCurrentPreviewSampleText();

            doCloseX = true;
            draggable = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(560f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            ApplyVoicePreviewResult();

            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
            Widgets.Label(titleRect, "RimTalk.TTS.VoiceSelection".Translate(_pawn.LabelShort));

            Text.Font = GameFont.Small;
            Rect instructRect = new Rect(inRect.x, titleRect.yMax + 5f, inRect.width, 30f);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(instructRect, "RimTalk.TTS.VoiceSelectionDesc".Translate());
            GUI.color = Color.white;

            // Voice model list
            float listTop = instructRect.yMax + 10f;
            float reservedBottom = (_settings != null && _settings.Supplier == TTSSettings.TTSSupplier.Irodori) ? 215f : 120f;
            float listHeight = inRect.height - listTop - reservedBottom; // Extra room for Irodori preview sample editor
            Rect listOutRect = new Rect(inRect.x, listTop, inRect.width, listHeight);

            // Calculate content height
            int itemCount = 3 + _voiceModels.Count; // "None" + "Default" + "Rule-based" + custom models
            float contentHeight = itemCount * 40f;
            Rect listViewRect = new Rect(0f, 0f, listOutRect.width - 20f, contentHeight);

            Widgets.BeginScrollView(listOutRect, ref _scrollPos, listViewRect);

            float y = 0f;

            // Option: None (disable TTS for this pawn)
            DrawVoiceOption(ref y, listViewRect.width, VoiceModel.NONE_MODEL_ID, 
                "RimTalk.TTS.VoiceNone".Translate(), 
                "RimTalk.TTS.VoiceNoneDesc".Translate());

            // Option: Default (use default voice model from settings)
            DrawVoiceOption(ref y, listViewRect.width, VoiceModel.DEFAULT_MODEL_ID, 
                "RimTalk.TTS.VoiceDefault".Translate(), 
                "RimTalk.TTS.VoiceDefaultDesc".Translate());

            // Option: Rule-based (determine voice by rules)
            DrawVoiceOption(ref y, listViewRect.width, VoiceModel.RULE_BASED_MODEL_ID, 
                "RimTalk.TTS.VoiceRuleBased".Translate(), 
                "RimTalk.TTS.VoiceRuleBasedDesc".Translate());

            // Custom voice models - with validation
            if (_voiceModels != null && _voiceModels.Count > 0)
            {
                foreach (var model in _voiceModels)
                {
                    if (model != null && !string.IsNullOrEmpty(model.ModelId))
                    {
                        string displayName = !string.IsNullOrEmpty(model.ModelName) ? model.ModelName : model.ModelId;
                        string description = $"ID: {model.ModelId}";
                        
                        DrawVoiceOption(ref y, listViewRect.width, model.ModelId, displayName, description);
                    }
                }
            }
            else
            {
                // Show a message if no custom models are configured
                Rect noModelsRect = new Rect(10f, y, listViewRect.width - 20f, 60f);
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(noModelsRect, "RimTalk.Settings.TTS.NoCustomModels".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                y += 65f;
            }

            Widgets.EndScrollView();

            // Language section
            float languageSectionY = listOutRect.yMax + 10f;
            Rect languageLabelRect = new Rect(inRect.x, languageSectionY, inRect.width, 22f);
            Widgets.Label(languageLabelRect, "RimTalk.TTS.CustomLanguage".Translate());
            
            Rect languageInputRect = new Rect(inRect.x, languageLabelRect.yMax + 2f, inRect.width, 24f);
            _customLanguage = Widgets.TextField(languageInputRect, _customLanguage ?? "");
            
            // Language hint
            Rect languageHintRect = new Rect(inRect.x, languageInputRect.yMax + 2f, inRect.width, 18f);
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            Text.Font = GameFont.Tiny;
            string globalLang = _settings?.TTSTranslationLanguage ?? "";
            string hintText = string.IsNullOrEmpty(globalLang) 
                ? "RimTalk.TTS.CustomLanguageHintNoGlobal".Translate()
                : "RimTalk.TTS.CustomLanguageHint".Translate(globalLang);
            Widgets.Label(languageHintRect, hintText);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // Irodori BIO audition sample text. The live field is used immediately by Preview;
            // Save persists it globally in RimTalk TTS settings. Other suppliers keep the old layout.
            float buttonY = languageHintRect.yMax + 10f;
            if (_settings != null && _settings.Supplier == TTSSettings.TTSSupplier.Irodori)
            {
                float previewSampleY = languageHintRect.yMax + 8f;
                Rect previewSampleLabelRect = new Rect(inRect.x, previewSampleY, inRect.width, 22f);
                Widgets.Label(previewSampleLabelRect, "RimTalk.TTS.VoicePreview.SampleLabel".Translate());

                Rect previewSampleInputRect = new Rect(inRect.x, previewSampleLabelRect.yMax + 2f, inRect.width, 52f);
                _previewSampleText = Widgets.TextArea(previewSampleInputRect, _previewSampleText ?? "");

                Rect previewSampleHintRect = new Rect(inRect.x, previewSampleInputRect.yMax + 2f, inRect.width, 18f);
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(previewSampleHintRect, "RimTalk.TTS.VoicePreview.SampleHint".Translate());
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                buttonY = previewSampleHintRect.yMax + 10f;
            }

            // Buttons
            float buttonWidth = 100f;
            float buttonHeight = 30f;
            float spacing = 10f;

            if (_settings != null && _settings.Supplier == TTSSettings.TTSSupplier.Irodori)
            {
                float labWidth = 140f;
                float totalWidth = buttonWidth * 2f + labWidth + spacing * 2f;
                float startX = inRect.center.x - totalWidth / 2f;
                Rect saveButton = new Rect(startX, buttonY, buttonWidth, buttonHeight);
                Rect labButton = new Rect(saveButton.xMax + spacing, buttonY, labWidth, buttonHeight);
                Rect cancelButton = new Rect(labButton.xMax + spacing, buttonY, buttonWidth, buttonHeight);

                if (Widgets.ButtonText(saveButton, "RimTalk.TTS.Save".Translate()))
                {
                    SaveVoiceModel(_selectedVoiceId);
                    SaveLanguage(_customLanguage);
                    SavePreviewSampleText(_previewSampleText);
                    Messages.Message("RimTalk.TTS.VoiceUpdated".Translate(_pawn.LabelShort),
                        MessageTypeDefOf.TaskCompletion, false);
                    Close();
                }

                if (Widgets.ButtonText(labButton, "RimTalk.TTS.VoiceLab.Open".Translate()))
                {
                    Find.WindowStack.Add(new IrodoriVoiceLabWindow(_pawn, _settings, voiceId =>
                    {
                        if (!string.IsNullOrWhiteSpace(voiceId)) _selectedVoiceId = voiceId;
                    }));
                }

                if (Widgets.ButtonText(cancelButton, "RimTalk.TTS.Cancel".Translate()))
                    Close();
            }
            else
            {
                Rect saveButton = new Rect(inRect.center.x - buttonWidth - spacing / 2f, buttonY, buttonWidth, buttonHeight);
                Rect cancelButton = new Rect(inRect.center.x + spacing / 2f, buttonY, buttonWidth, buttonHeight);

                if (Widgets.ButtonText(saveButton, "RimTalk.TTS.Save".Translate()))
                {
                    SaveVoiceModel(_selectedVoiceId);
                    SaveLanguage(_customLanguage);
                    SavePreviewSampleText(_previewSampleText);
                    Messages.Message("RimTalk.TTS.VoiceUpdated".Translate(_pawn.LabelShort),
                        MessageTypeDefOf.TaskCompletion, false);
                    Close();
                }

                if (Widgets.ButtonText(cancelButton, "RimTalk.TTS.Cancel".Translate()))
                    Close();
            }
        }

        private void DrawVoiceOption(ref float y, float width, string voiceId, string label, string description)
        {
            Rect optionRect = new Rect(0f, y, width, 35f);

            bool isSelected = _selectedVoiceId == voiceId;
            bool canPreview = CanPreviewVoice(voiceId);
            float previewArea = canPreview ? 58f : 0f;

            if (isSelected)
            {
                Widgets.DrawBoxSolid(optionRect, new Color(0.3f, 0.5f, 0.3f, 0.5f));
            }
            else
            {
                Widgets.DrawBoxSolid(optionRect, new Color(0.2f, 0.2f, 0.2f, 0.3f));
            }

            Widgets.DrawHighlightIfMouseover(optionRect);

            // Radio button
            Rect radioRect = new Rect(optionRect.x + 5f, optionRect.y + 7f, 20f, 20f);
            bool wasSelected = isSelected;
            Widgets.Checkbox(radioRect.position, ref isSelected, 20f, false, true);

            if (isSelected && !wasSelected)
            {
                _selectedVoiceId = voiceId;
            }

            // Label / ID leave room for the Irodori audition button.
            Rect labelRect = new Rect(radioRect.xMax + 10f, optionRect.y + 2f,
                Math.Max(40f, width - 40f - previewArea), 18f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);

            Rect descRect = new Rect(labelRect.x, labelRect.yMax, labelRect.width, 15f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(descRect, description);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            if (canPreview)
            {
                Rect previewRect = new Rect(optionRect.xMax - 50f, optionRect.y + 4f, 44f, 27f);
                // Keep this deliberately language-neutral and compact. Missing translations must never
                // expand the button into a raw localization key again.
                string previewLabel = _previewGeneratingVoiceId == voiceId ? "…" : "▶";
                if (Widgets.ButtonText(previewRect, previewLabel))
                    BeginVoicePreview(voiceId);
            }

            // Keep the row click target away from the preview button so auditioning a voice does
            // not implicitly change the pending BIO assignment.
            Rect rowClickRect = optionRect;
            if (canPreview) rowClickRect.width = Math.Max(1f, rowClickRect.width - 58f);
            if (Widgets.ButtonInvisible(rowClickRect))
            {
                _selectedVoiceId = voiceId;
            }

            y += 40f;
        }

        private bool CanPreviewVoice(string voiceId)
        {
            if (_settings == null || _settings.Supplier != TTSSettings.TTSSupplier.Irodori || _settings.Irodori == null)
                return false;
            if (string.IsNullOrWhiteSpace(voiceId) || voiceId == VoiceModel.NONE_MODEL_ID ||
                voiceId == VoiceModel.DEFAULT_MODEL_ID || voiceId == VoiceModel.RULE_BASED_MODEL_ID)
                return false;
            return _voiceModels != null && _voiceModels.Exists(m => m != null && m.ModelId == voiceId);
        }

        private void BeginVoicePreview(string voiceId)
        {
            if (!CanPreviewVoice(voiceId)) return;

            // Clicking another BIO preview immediately stops an already-audible preview. The new
            // voice begins when its short synthesis request returns.
            AudioPlaybackService.StopPreviewAudio();

            int requestVersion;
            lock (_previewLock)
            {
                _previewRequestVersion++;
                requestVersion = _previewRequestVersion;
                _pendingPreviewVersion = 0;
                _pendingPreviewAudio = null;
                _pendingPreviewError = null;
            }
            _previewGeneratingVoiceId = voiceId;

            var request = new TTSRequest
            {
                ApiKey = _settings.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori),
                Model = _settings.GetSupplierModel(TTSSettings.TTSSupplier.Irodori),
                Input = GetEffectivePreviewSampleText(),
                Voice = voiceId,
                Speed = _settings.GetSupplierSpeed(TTSSettings.TTSSupplier.Irodori),
                Volume = _settings.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori),
                Emotion = ""
            };
            var client = new IrodoriClient(_settings.Irodori);

            Task.Run(async () =>
            {
                byte[] audio = null;
                string error = null;
                try
                {
                    audio = await client.GenerateVoiceRegistryPreviewAsync(request);
                    if (audio == null || audio.Length == 0)
                        error = "RimTalk.TTS.VoicePreview.Failed".Translate().ToString();
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                    Log.Error($"[RimTalk.TTS/BioPreview] Preview generation exception for '{voiceId}': {ex}");
                }

                lock (_previewLock)
                {
                    // A newer click wins; do not let a slower old HTTP result suddenly play later.
                    if (requestVersion != _previewRequestVersion) return;
                    _pendingPreviewVersion = requestVersion;
                    _pendingPreviewAudio = audio;
                    _pendingPreviewError = error;
                }
            });
        }

        private void ApplyVoicePreviewResult()
        {
            byte[] audio = null;
            string error = null;
            int version = 0;
            lock (_previewLock)
            {
                if (_pendingPreviewVersion == 0) return;
                version = _pendingPreviewVersion;
                audio = _pendingPreviewAudio;
                error = _pendingPreviewError;
                _pendingPreviewVersion = 0;
                _pendingPreviewAudio = null;
                _pendingPreviewError = null;
            }

            if (version != _previewRequestVersion) return;
            _previewGeneratingVoiceId = null;

            if (!string.IsNullOrWhiteSpace(error))
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                return;
            }

            AudioPlaybackService.PlayPreviewAudio(audio,
                _settings?.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori) ?? 1.0f);
        }

        private string GetEffectivePreviewSampleText()
        {
            string text = (_previewSampleText ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            string fallback = "RimTalk.TTS.VoicePreview.SampleText".Translate().ToString();
            // If a language file is stale/missing, never send the localization key itself to TTS.
            if (string.IsNullOrWhiteSpace(fallback) || fallback == "RimTalk.TTS.VoicePreview.SampleText")
                fallback = "こんにちは。声の確認です。今日もよろしくお願いします。";
            return fallback;
        }

        private string GetCurrentPreviewSampleText()
        {
            try
            {
                string saved = _settings?.IrodoriVoicePreviewSampleText ?? "";
                if (!string.IsNullOrWhiteSpace(saved))
                    return saved;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/BioPreview] Failed to read preview sample text: {ex.Message}");
            }
            return GetEffectivePreviewSampleText();
        }

        private void SavePreviewSampleText(string text)
        {
            try
            {
                if (_settings == null) return;
                _settings.IrodoriVoicePreviewSampleText = (text ?? "").Trim();

                // BIO is outside the normal Mod Settings window, so explicitly write the ModSettings
                // file here rather than waiting for a later settings-screen close.
                var modInstance = LoadedModManager.GetMod(typeof(TTSMod)) as TTSMod;
                modInstance?.WriteSettings();
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS/BioPreview] Failed to save preview sample text: {ex}");
            }
        }

        private string GetCurrentVoiceModel()
        {
            try
            {
                // Get raw voice model from PawnVoiceManager (without resolving tags)
                string voiceId = Data.PawnVoiceManager.GetRawVoiceModel(_pawn);
                
                // If empty, treat as DEFAULT_MODEL_ID for UI purposes
                if (string.IsNullOrEmpty(voiceId))
                {
                    return VoiceModel.DEFAULT_MODEL_ID;
                }
                
                return voiceId;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS] Failed to get current voice model: {ex.Message}");
            }
            return VoiceModel.DEFAULT_MODEL_ID;
        }

        private string GetCurrentLanguage()
        {
            try
            {
                // Get custom language from PawnVoiceManager (null/empty = use global)
                return Data.PawnVoiceManager.GetLanguage(_pawn) ?? "";
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS] Failed to get current language: {ex.Message}");
            }
            return "";
        }

        private void SaveVoiceModel(string voiceId)
        {
            try
            {
                // Save voice model directly to PawnVoiceManager
                Data.PawnVoiceManager.SetVoiceModel(_pawn, voiceId);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS] Failed to save voice model: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void SaveLanguage(string language)
        {
            try
            {
                // Save custom language to PawnVoiceManager (empty = use global)
                Data.PawnVoiceManager.SetLanguage(_pawn, language);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS] Failed to save language: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
