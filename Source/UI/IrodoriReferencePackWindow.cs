using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.UI
{
    /// <summary>
    /// Builds a true Irodori multi-reference pack from one already-adopted registry voice.
    /// The source voice is used only as the cloning anchor. Each generated clip is uploaded as a
    /// private backing registry voice, its server-side ref_wav path is resolved, and the final local
    /// Voice Profile uses DirectReferences/ref_wavs. The source/anchor voice is never owned or deleted
    /// by the pack, so the finished pack is independent after its backing clips have been generated.
    /// </summary>
    public sealed class IrodoriReferencePackWindow : Window
    {
        private sealed class PackReference
        {
            public string VoiceId;
            public string ServerPath;
            public string Text;
            public byte[] Audio;
        }

        private sealed class PendingReference
        {
            public PackReference Reference;
            public string Error;
        }

        private const int MinimumReferences = 2;
        private const int MaximumReferences = 8;

        private readonly Pawn _pawn;
        private readonly TTSSettings _settings;
        private readonly string _anchorVoiceId;
        private readonly Action<string> _onPackAdopted;
        private readonly object _asyncLock = new object();
        private readonly List<PackReference> _references = new List<PackReference>();

        private string _profileName;
        private string _sampleText;
        private string _deliveryHint;
        private string _status = "";
        private Vector2 _scroll = Vector2.zero;
        private volatile bool _isBusy;
        private PendingReference _pendingReference;
        private bool _saved;

        public IrodoriReferencePackWindow(
            Pawn pawn,
            TTSSettings settings,
            string anchorVoiceId,
            string initialProfileName,
            string initialSampleText,
            string initialDeliveryHint,
            Action<string> onPackAdopted = null)
        {
            _pawn = pawn;
            _settings = settings;
            _anchorVoiceId = anchorVoiceId ?? "";
            _onPackAdopted = onPackAdopted;

            string pawnName = pawn?.LabelShort ?? "Pawn";
            _profileName = string.IsNullOrWhiteSpace(initialProfileName)
                ? pawnName + " Reference Pack"
                : initialProfileName.Trim() + " Pack";
            _sampleText = string.IsNullOrWhiteSpace(initialSampleText)
                ? "こんにちは。これは声の参照パックを作るためのサンプルです。"
                : initialSampleText;
            _deliveryHint = initialDeliveryHint ?? "";

            doCloseX = true;
            draggable = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
        }

        public override Vector2 InitialSize => new Vector2(760f, 650f);

        public override void DoWindowContents(Rect inRect)
        {
            ApplyPendingReference();
            doCloseX = !_isBusy;

            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 30f),
                "RimTalk.TTS.ReferencePack.Title".Translate(_pawn?.LabelShort ?? "Pawn").ToString());
            Text.Font = GameFont.Small;
            y += 34f;

            GUI.color = new Color(0.75f, 0.85f, 1f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 46f),
                "RimTalk.TTS.ReferencePack.Description".Translate(MinimumReferences, MaximumReferences).ToString());
            GUI.color = Color.white;
            y += 50f;

            Widgets.Label(new Rect(inRect.x, y, 110f, 24f),
                "RimTalk.TTS.ReferencePack.Anchor".Translate().ToString());
            Widgets.Label(new Rect(inRect.x + 112f, y, inRect.width - 112f, 24f), _anchorVoiceId);
            y += 28f;

            Widgets.Label(new Rect(inRect.x, y, 110f, 24f),
                "RimTalk.TTS.ReferencePack.ProfileName".Translate().ToString());
            _profileName = Widgets.TextField(new Rect(inRect.x + 112f, y, 310f, 26f), _profileName ?? "");
            y += 34f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "RimTalk.TTS.ReferencePack.SampleText".Translate().ToString());
            y += 22f;
            _sampleText = Widgets.TextArea(new Rect(inRect.x, y, inRect.width, 72f), _sampleText ?? "");
            y += 78f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "RimTalk.TTS.ReferencePack.DeliveryHint".Translate().ToString());
            y += 22f;
            _deliveryHint = Widgets.TextArea(new Rect(inRect.x, y, inRect.width, 48f), _deliveryHint ?? "");
            y += 54f;

            Rect addRow = new Rect(inRect.x, y, inRect.width, 32f);
            Widgets.Label(new Rect(addRow.x, addRow.y + 5f, addRow.width - 210f, 24f),
                "RimTalk.TTS.ReferencePack.Count".Translate(_references.Count, MaximumReferences).ToString());
            GUI.enabled = !_isBusy && _references.Count < MaximumReferences;
            if (Widgets.ButtonText(new Rect(addRow.xMax - 200f, addRow.y, 200f, 30f),
                    _isBusy
                        ? "RimTalk.TTS.ReferencePack.Generating".Translate().ToString()
                        : "RimTalk.TTS.ReferencePack.GenerateAdd".Translate().ToString()))
            {
                BeginGenerateReference();
            }
            GUI.enabled = true;
            y += 38f;

            if (!string.IsNullOrWhiteSpace(_status))
            {
                GUI.color = _status.StartsWith("ERROR:", StringComparison.Ordinal)
                    ? Color.red
                    : new Color(0.75f, 1f, 0.75f);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), _status);
                GUI.color = Color.white;
            }
            y += 28f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f),
                "RimTalk.TTS.ReferencePack.References".Translate().ToString());
            y += 24f;

            float bottomButtons = 42f;
            Rect listOuter = new Rect(inRect.x, y, inRect.width, Math.Max(90f, inRect.yMax - y - bottomButtons - 8f));
            Widgets.DrawBox(listOuter);
            DrawReferences(listOuter.ContractedBy(4f));

            float buttonY = inRect.yMax - 34f;
            float buttonW = 170f;
            float gap = 10f;
            Rect saveRect = new Rect(inRect.center.x - buttonW - gap / 2f, buttonY, buttonW, 32f);
            Rect cancelRect = new Rect(inRect.center.x + gap / 2f, buttonY, buttonW, 32f);

            GUI.enabled = !_isBusy && _references.Count >= MinimumReferences;
            if (Widgets.ButtonText(saveRect, "RimTalk.TTS.ReferencePack.SaveAssign".Translate().ToString()))
                SaveAndAssign();
            GUI.enabled = !_isBusy;
            if (Widgets.ButtonText(cancelRect, "RimTalk.TTS.Cancel".Translate().ToString()))
                Close();
            GUI.enabled = true;
        }

        private void DrawReferences(Rect rect)
        {
            if (_references.Count == 0)
            {
                Widgets.Label(rect, "RimTalk.TTS.ReferencePack.NoReferences".Translate().ToString());
                return;
            }

            const float rowH = 44f;
            Rect view = new Rect(0f, 0f, rect.width - 18f, _references.Count * rowH);
            Widgets.BeginScrollView(rect, ref _scroll, view);
            for (int i = 0; i < _references.Count; i++)
            {
                PackReference reference = _references[i];
                Rect row = new Rect(0f, i * rowH, view.width, rowH - 2f);
                Widgets.DrawHighlightIfMouseover(row);
                string label = $"#{i + 1}  {Ellipsize(reference.Text, 56)}";
                Widgets.Label(new Rect(row.x + 6f, row.y + 7f, row.width - 170f, 26f), label);

                if (reference.Audio != null && reference.Audio.Length > 0 &&
                    Widgets.ButtonText(new Rect(row.xMax - 158f, row.y + 5f, 70f, 30f), "▶"))
                {
                    AudioPlaybackService.PlayPreviewAudio(reference.Audio,
                        _settings?.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori) ?? 1f);
                }

                GUI.enabled = !_isBusy;
                GUI.color = new Color(1f, 0.72f, 0.72f);
                if (Widgets.ButtonText(new Rect(row.xMax - 80f, row.y + 5f, 70f, 30f),
                        "RimTalk.TTS.ReferencePack.Remove".Translate().ToString()))
                {
                    RemoveReference(reference);
                    i--;
                }
                GUI.color = Color.white;
                GUI.enabled = true;
            }
            Widgets.EndScrollView();
        }

        private void BeginGenerateReference()
        {
            if (_isBusy || _references.Count >= MaximumReferences)
                return;
            if (_settings == null || _settings.Irodori == null ||
                _settings.Supplier != TTSSettings.TTSSupplier.Irodori)
            {
                _status = "ERROR: Irodori settings are unavailable.";
                return;
            }
            if (string.IsNullOrWhiteSpace(_anchorVoiceId))
            {
                _status = "ERROR: " + "RimTalk.TTS.ReferencePack.NoAnchor".Translate().ToString();
                return;
            }

            string spoken = UnifiedTtsPayloadStore.SanitizeForTts(_sampleText, _settings);
            if (string.IsNullOrWhiteSpace(spoken))
            {
                _status = "ERROR: " + "RimTalk.TTS.VoiceLab.EmptyText".Translate().ToString();
                return;
            }

            string apiKey = _settings.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori);
            string baseUrl = _settings.Irodori.BaseUrl;
            string refId = IrodoriReferencePackService.BuildReferenceId(_pawn, _references.Count + 1);
            string delivery = (_deliveryHint ?? "").Trim();

            var request = new TTSRequest
            {
                ApiKey = apiKey,
                Model = _settings.GetSupplierModel(TTSSettings.TTSSupplier.Irodori),
                Input = spoken,
                Voice = _anchorVoiceId,
                Speed = _settings.GetSupplierSpeed(TTSSettings.TTSSupplier.Irodori),
                Volume = _settings.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori),
                Emotion = delivery
            };

            _isBusy = true;
            _status = "RimTalk.TTS.ReferencePack.Generating".Translate().ToString();

            Task.Run(async () =>
            {
                PendingReference pending = new PendingReference();
                try
                {
                    var client = new IrodoriClient(_settings.Irodori);
                    byte[] audio = await client.GenerateVoiceRegistryPreviewAsync(request);
                    if (audio == null || audio.Length == 0)
                        throw new InvalidOperationException("Irodori returned no audio for the reference clone.");

                    string uploaded = await IrodoriClient.UploadVoiceBytesAsync(
                        baseUrl,
                        apiKey,
                        audio,
                        refId + ".wav",
                        refId);
                    if (string.IsNullOrWhiteSpace(uploaded))
                        throw new InvalidOperationException("Irodori reference upload failed.");

                    string serverPath = await IrodoriReferencePackService.ResolveRegistryVoicePathAsync(
                        baseUrl,
                        apiKey,
                        uploaded);
                    if (string.IsNullOrWhiteSpace(serverPath))
                    {
                        await IrodoriClient.DeleteVoiceAsync(baseUrl, apiKey, uploaded);
                        throw new InvalidOperationException("Uploaded reference exists, but its server-side ref_wav path could not be resolved.");
                    }

                    pending.Reference = new PackReference
                    {
                        VoiceId = uploaded,
                        ServerPath = serverPath,
                        Text = spoken,
                        Audio = audio
                    };
                }
                catch (Exception ex)
                {
                    pending.Error = ex.GetType().Name + ": " + ex.Message;
                    Log.Error($"[RimTalk.TTS/ReferencePack] Reference generation failed: {ex}");
                }
                finally
                {
                    lock (_asyncLock) _pendingReference = pending;
                    _isBusy = false;
                }
            });
        }

        private void ApplyPendingReference()
        {
            PendingReference pending = null;
            lock (_asyncLock)
            {
                if (_pendingReference == null) return;
                pending = _pendingReference;
                _pendingReference = null;
            }

            if (!string.IsNullOrWhiteSpace(pending.Error))
            {
                _status = "ERROR: " + pending.Error;
                return;
            }

            if (pending.Reference != null)
            {
                _references.Add(pending.Reference);
                _status = "RimTalk.TTS.ReferencePack.Added".Translate(_references.Count).ToString();
            }
        }

        private void RemoveReference(PackReference reference)
        {
            if (reference == null || _isBusy) return;
            _references.Remove(reference);
            string voiceId = reference.VoiceId;
            SecureDiscard(reference);
            string baseUrl = _settings?.Irodori?.BaseUrl;
            string apiKey = _settings?.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori);
            if (!string.IsNullOrWhiteSpace(voiceId) && !string.IsNullOrWhiteSpace(baseUrl))
            {
                Task.Run(async () =>
                {
                    try { await IrodoriClient.DeleteVoiceAsync(baseUrl, apiKey, voiceId); }
                    catch (Exception ex) { Log.Warning($"[RimTalk.TTS/ReferencePack] Remove reference '{voiceId}' failed: {ex.Message}"); }
                });
            }
            _status = "RimTalk.TTS.ReferencePack.Removed".Translate().ToString();
        }

        private void SaveAndAssign()
        {
            if (_isBusy || _references.Count < MinimumReferences || _settings == null || _settings.Irodori == null)
                return;

            string packId = IrodoriReferencePackService.BuildPackProfileId(_pawn);
            string displayName = string.IsNullOrWhiteSpace(_profileName)
                ? (_pawn?.LabelShort ?? "Pawn") + " Reference Pack"
                : _profileName.Trim();

            try
            {
                var models = _settings.GetSupplierVoiceModels(TTSSettings.TTSSupplier.Irodori)
                             ?? new List<VoiceModel>();
                models.RemoveAll(m => m == null);
                models.Add(new VoiceModel
                {
                    ModelId = packId,
                    ModelName = displayName
                });
                _settings.SetSupplierVoiceModels(TTSSettings.TTSSupplier.Irodori, models);

                IrodoriVoiceConfig cfg = _settings.Irodori.GetOrCreateVoiceConfig(packId);
                if (cfg == null)
                    throw new InvalidOperationException("Could not create Irodori Reference Pack config.");

                cfg.Mode = IrodoriVoiceConfig.ReferenceMode.DirectReferences;
                cfg.RefWav = "";
                cfg.RefWavs = _references.Select(r => r.ServerPath).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                cfg.RefLatent = "";
                cfg.RefLatents = new List<string>();
                cfg.RefEmbed = "";
                cfg.ReferenceVoiceIds = _references.Select(r => r.VoiceId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                cfg.ReferencePackSourceVoiceId = _anchorVoiceId;

                // Preserve the stable identity caption of the source voice. Per-line Fast Path delivery
                // remains dynamic and is appended by the normal Irodori request path.
                IrodoriVoiceConfig anchorCfg = _settings.Irodori.GetVoiceConfig(_anchorVoiceId);
                cfg.Caption = anchorCfg?.Caption ?? "";

                PawnVoiceManager.SetVoiceModel(_pawn, packId);
                _onPackAdopted?.Invoke(packId);
                WriteSettings();

                _saved = true;
                Messages.Message("RimTalk.TTS.ReferencePack.Saved".Translate(displayName, _references.Count).ToString(),
                    MessageTypeDefOf.TaskCompletion, false);
                Log.Message($"[RimTalk.TTS/ReferencePack] Created pack '{packId}' from anchor '{_anchorVoiceId}' with {_references.Count} references.");
                Close();
            }
            catch (Exception ex)
            {
                _status = "ERROR: " + ex.GetType().Name + ": " + ex.Message;
                Log.Error($"[RimTalk.TTS/ReferencePack] Failed to save Reference Pack: {ex}");
            }
        }

        private void WriteSettings()
        {
            try
            {
                var mod = LoadedModManager.GetMod(typeof(TTSMod)) as TTSMod;
                mod?.WriteSettings();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/ReferencePack] Failed to persist settings: {ex.Message}");
            }
        }

        public override void PostClose()
        {
            AudioPlaybackService.StopPreviewAudio();
            if (!_saved && _references.Count > 0)
            {
                var ids = _references.Select(r => r.VoiceId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                foreach (PackReference reference in _references) SecureDiscard(reference);
                _references.Clear();

                string baseUrl = _settings?.Irodori?.BaseUrl;
                string apiKey = _settings?.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori);
                if (!string.IsNullOrWhiteSpace(baseUrl) && ids.Count > 0)
                    Task.Run(() => IrodoriReferencePackService.DeleteOwnedReferencesAsync(baseUrl, apiKey, ids));
            }
            base.PostClose();
        }

        private static void SecureDiscard(PackReference reference)
        {
            if (reference?.Audio == null) return;
            Array.Clear(reference.Audio, 0, reference.Audio.Length);
            reference.Audio = null;
        }

        private static string Ellipsize(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, Math.Max(1, max - 1)) + "…";
        }
    }
}
