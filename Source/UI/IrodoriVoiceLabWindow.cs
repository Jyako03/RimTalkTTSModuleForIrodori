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
    /// Voice hunting workflow for Irodori-TTS.
    /// Pick a recent RimTalk line -> generate no-reference Voice Design candidates -> audition ->
    /// upload a liked WAV to Irodori's registry -> register it as a normal Voice Profile and assign
    /// it to the pawn. Normal per-pawn selection still lives in Bio > Voice.
    /// </summary>
    public class IrodoriVoiceLabWindow : Window
    {
        private sealed class Candidate
        {
            public byte[] Audio;
            public int Seed;
            public string Caption;
            public string Text;
            // Set after this candidate has been promoted to Irodori's persistent voice registry.
            // Before adoption the candidate only exists as in-memory WAV bytes; there is no server file to delete.
            public string RegisteredVoiceId;
            public string RegisteredProfileName;
            public bool IsDeleting;
        }

        private sealed class AdoptionResult
        {
            public string VoiceId;
            public string ProfileName;
            public string Caption;
            public Candidate Candidate;
            public string Error;
        }

        private sealed class DeletionResult
        {
            public Candidate Candidate;
            public string VoiceId;
            public bool Success;
            public string Error;
        }

        private readonly Pawn _pawn;
        private readonly TTSSettings _settings;
        private readonly Action<string> _onVoiceAdopted;
        private readonly object _asyncLock = new object();

        private List<RecentDialogueStore.Entry> _recent = new List<RecentDialogueStore.Entry>();
        private readonly List<Candidate> _candidates = new List<Candidate>();
        private Vector2 _recentScroll = Vector2.zero;
        private Vector2 _candidateScroll = Vector2.zero;

        private string _sampleText = "";
        private string _voiceDescription = "自然で聞き取りやすい日本語の声。キャラクターに合った自然な声質。";
        private string _deliveryHint = "";
        private string _profileName = "";
        private string _seedText = "";
        private string _status = "";

        private volatile bool _isGenerating;
        private volatile bool _isUploading;
        private Candidate _pendingCandidate;
        private string _pendingError;
        private AdoptionResult _pendingAdoption;
        private DeletionResult _pendingDeletion;

        public IrodoriVoiceLabWindow(Pawn pawn, TTSSettings settings, Action<string> onVoiceAdopted = null)
        {
            _pawn = pawn;
            _settings = settings;
            _onVoiceAdopted = onVoiceAdopted;

            doCloseX = true;
            draggable = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;

            _profileName = ((pawn?.LabelShort ?? "Pawn") + " Voice").Trim();
            _seedText = NewSeed().ToString();
            RefreshRecent();
            if (_recent.Count > 0) SelectRecent(0);
        }

        public override Vector2 InitialSize => new Vector2(820f, 760f);

        public override void DoWindowContents(Rect inRect)
        {
            ApplyAsyncResults();

            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 30f),
                "RimTalk.TTS.VoiceLab.Title".Translate(_pawn?.LabelShort ?? "Pawn"));
            Text.Font = GameFont.Small;
            y += 34f;

            if (_settings == null || _settings.Supplier != TTSSettings.TTSSupplier.Irodori || _settings.Irodori == null)
            {
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 60f), "RimTalk.TTS.VoiceLab.IrodoriOnly".Translate());
                return;
            }

            GUI.color = new Color(0.75f, 0.85f, 1f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 38f), "RimTalk.TTS.VoiceLab.Description".Translate());
            GUI.color = Color.white;
            y += 42f;

            // Recent dialogue picker
            Rect recentHeader = new Rect(inRect.x, y, inRect.width, 24f);
            Widgets.Label(recentHeader, "RimTalk.TTS.VoiceLab.RecentLines".Translate());
            Rect refreshRect = new Rect(recentHeader.xMax - 100f, recentHeader.y, 100f, 24f);
            if (Widgets.ButtonText(refreshRect, "RimTalk.TTS.VoiceLab.Refresh".Translate()))
                RefreshRecent();
            y += 28f;

            Rect recentOuter = new Rect(inRect.x, y, inRect.width, 145f);
            Widgets.DrawBox(recentOuter);
            DrawRecentList(recentOuter.ContractedBy(4f));
            y += 151f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "RimTalk.TTS.VoiceLab.SampleText".Translate());
            y += 22f;
            Rect textRect = new Rect(inRect.x, y, inRect.width, 72f);
            _sampleText = Widgets.TextArea(textRect, _sampleText ?? "");
            y += 78f;

            // Voice identity description and per-line delivery hint are separate. The former is
            // persisted into the new Voice Profile; the latter is only for this candidate sample.
            float half = (inRect.width - 8f) / 2f;
            Widgets.Label(new Rect(inRect.x, y, half, 22f), "RimTalk.TTS.VoiceLab.VoiceDescription".Translate());
            Widgets.Label(new Rect(inRect.x + half + 8f, y, half, 22f), "RimTalk.TTS.VoiceLab.DeliveryHint".Translate());
            y += 22f;
            Rect voiceDescRect = new Rect(inRect.x, y, half, 66f);
            Rect deliveryRect = new Rect(inRect.x + half + 8f, y, half, 66f);
            _voiceDescription = Widgets.TextArea(voiceDescRect, _voiceDescription ?? "");
            _deliveryHint = Widgets.TextArea(deliveryRect, _deliveryHint ?? "");
            y += 72f;

            // Profile name + seed + generate
            Rect controls = new Rect(inRect.x, y, inRect.width, 30f);
            Widgets.Label(new Rect(controls.x, controls.y + 5f, 95f, 24f), "RimTalk.TTS.VoiceLab.ProfileName".Translate());
            _profileName = Widgets.TextField(new Rect(controls.x + 98f, controls.y, 220f, 30f), _profileName ?? "");

            Widgets.Label(new Rect(controls.x + 330f, controls.y + 5f, 42f, 24f), "Seed");
            _seedText = Widgets.TextField(new Rect(controls.x + 372f, controls.y, 112f, 30f), _seedText ?? "");
            if (Widgets.ButtonText(new Rect(controls.x + 490f, controls.y, 92f, 30f), "RimTalk.TTS.VoiceLab.RandomSeed".Translate()))
                _seedText = NewSeed().ToString();

            GUI.enabled = !_isGenerating && !_isUploading;
            if (Widgets.ButtonText(new Rect(controls.xMax - 190f, controls.y, 190f, 30f),
                    _isGenerating ? "RimTalk.TTS.VoiceLab.Generating".Translate() : "RimTalk.TTS.VoiceLab.Generate".Translate()))
                BeginGenerate();
            GUI.enabled = true;
            y += 36f;

            if (!string.IsNullOrWhiteSpace(_status))
            {
                GUI.color = _status.StartsWith("ERROR:", StringComparison.Ordinal) ? Color.red : new Color(0.75f, 1f, 0.75f);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), _status);
                GUI.color = Color.white;
            }
            y += 26f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "RimTalk.TTS.VoiceLab.Candidates".Translate());
            y += 24f;
            Rect candidatesOuter = new Rect(inRect.x, y, inRect.width, Math.Max(80f, inRect.yMax - y));
            Widgets.DrawBox(candidatesOuter);
            DrawCandidates(candidatesOuter.ContractedBy(4f));
        }

        private void DrawRecentList(Rect rect)
        {
            if (_recent == null || _recent.Count == 0)
            {
                Widgets.Label(rect, "RimTalk.TTS.VoiceLab.NoRecent".Translate());
                return;
            }

            float rowH = 42f;
            Rect view = new Rect(0f, 0f, rect.width - 18f, _recent.Count * rowH);
            Widgets.BeginScrollView(rect, ref _recentScroll, view);
            for (int i = 0; i < _recent.Count; i++)
            {
                var entry = _recent[i];
                Rect row = new Rect(0f, i * rowH, view.width, rowH - 2f);
                Widgets.DrawHighlightIfMouseover(row);
                string label = Ellipsize(entry?.Text, 92);
                if (Widgets.ButtonInvisible(row)) SelectRecent(i);
                Widgets.Label(new Rect(row.x + 6f, row.y + 3f, row.width - 12f, row.height - 6f), label);
            }
            Widgets.EndScrollView();
        }

        private void DrawCandidates(Rect rect)
        {
            if (_candidates.Count == 0)
            {
                Widgets.Label(rect, "RimTalk.TTS.VoiceLab.NoCandidates".Translate());
                return;
            }

            float rowH = 44f;
            Rect view = new Rect(0f, 0f, rect.width - 18f, _candidates.Count * rowH);
            Widgets.BeginScrollView(rect, ref _candidateScroll, view);

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                Rect row = new Rect(0f, i * rowH, view.width, rowH - 2f);
                Widgets.DrawHighlightIfMouseover(row);
                float kb = (c.Audio?.Length ?? 0) / 1024f;
                string registryInfo = string.IsNullOrWhiteSpace(c.RegisteredVoiceId)
                    ? ""
                    : $"   Irodori ID={c.RegisteredVoiceId}";
                Widgets.Label(new Rect(row.x + 6f, row.y + 7f, row.width - 330f, 26f),
                    $"#{i + 1}   seed={c.Seed}   {kb:F0} KB{registryInfo}");

                GUI.enabled = !_isUploading && !c.IsDeleting;
                if (Widgets.ButtonText(new Rect(row.xMax - 310f, row.y + 5f, 82f, 30f), "RimTalk.TTS.VoiceLab.Play".Translate()))
                    AudioPlaybackService.PlayPreviewAudio(c.Audio, _settings.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori));

                if (Widgets.ButtonText(new Rect(row.xMax - 222f, row.y + 5f, 166f, 30f),
                        _isUploading ? "RimTalk.TTS.VoiceLab.Uploading".Translate() : "RimTalk.TTS.VoiceLab.UseReference".Translate()))
                    BeginAdopt(c);
                GUI.enabled = true;

                if (Widgets.ButtonText(new Rect(row.xMax - 48f, row.y + 5f, 42f, 30f), c.IsDeleting ? "..." : "X")
                    && !_isUploading && !c.IsDeleting)
                {
                    // Unadopted candidates only exist in RAM, so X securely discards the bytes immediately.
                    // Adopted candidates have a real file in Irodori's voice registry; X deletes that server file first.
                    if (BeginDeleteCandidate(c))
                        i--;
                }
            }

            Widgets.EndScrollView();
        }

        private void RefreshRecent()
        {
            _recent = RecentDialogueStore.GetRecent(_pawn, 16) ?? new List<RecentDialogueStore.Entry>();
            if (_recent.Count > 0 && string.IsNullOrWhiteSpace(_sampleText)) SelectRecent(0);
        }

        private void SelectRecent(int index)
        {
            if (_recent == null || index < 0 || index >= _recent.Count) return;
            var entry = _recent[index];
            if (entry == null) return;
            _sampleText = entry.Text ?? "";
            _deliveryHint = entry.Emotion ?? "";
            _status = "";
        }

        private void BeginGenerate()
        {
            if (_isGenerating || _isUploading) return;

            string spoken = UnifiedTtsPayloadStore.SanitizeForTts(_sampleText, _settings);
            if (string.IsNullOrWhiteSpace(spoken))
            {
                _status = "ERROR: " + "RimTalk.TTS.VoiceLab.EmptyText".Translate();
                return;
            }

            if (!int.TryParse(_seedText, out int seed) || seed < 0)
            {
                seed = NewSeed();
                _seedText = seed.ToString();
            }

            string stableVoiceCaption = (_voiceDescription ?? "").Trim();
            string caption = IrodoriEmotionMapper.BuildCaption("", stableVoiceCaption, (_deliveryHint ?? "").Trim());
            var request = new TTSRequest
            {
                ApiKey = _settings.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori),
                Model = _settings.GetSupplierModel(TTSSettings.TTSSupplier.Irodori),
                Input = spoken,
                Voice = "none",
                Speed = _settings.GetSupplierSpeed(TTSSettings.TTSSupplier.Irodori),
                Volume = _settings.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori)
            };

            _isGenerating = true;
            _status = "RimTalk.TTS.VoiceLab.Generating".Translate();
            int capturedSeed = seed;

            Task.Run(async () =>
            {
                try
                {
                    var client = new IrodoriClient(_settings.Irodori);
                    byte[] audio = await client.GenerateVoiceDesignAsync(request, caption, capturedSeed);
                    lock (_asyncLock)
                    {
                        if (audio == null || audio.Length == 0)
                            _pendingError = "Voice Design returned no audio. Check the RimWorld/Irodori logs and ensure the server allows the built-in 'none' voice.";
                        else
                            _pendingCandidate = new Candidate { Audio = audio, Seed = capturedSeed, Caption = stableVoiceCaption, Text = spoken };
                    }
                }
                catch (Exception ex)
                {
                    lock (_asyncLock) _pendingError = ex.Message;
                }
                finally
                {
                    _isGenerating = false;
                }
            });
        }

        private void BeginAdopt(Candidate candidate)
        {
            if (candidate == null || candidate.Audio == null || candidate.Audio.Length == 0 || _isUploading) return;

            string profile = string.IsNullOrWhiteSpace(_profileName)
                ? ((_pawn?.LabelShort ?? "Pawn") + " Voice")
                : _profileName.Trim();
            string voiceId = BuildVoiceId(candidate.Seed);
            string apiKey = _settings.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori);
            string baseUrl = _settings.Irodori.BaseUrl;
            string stableCaption = candidate.Caption ?? "";

            _isUploading = true;
            _status = "RimTalk.TTS.VoiceLab.Uploading".Translate();

            Task.Run(async () =>
            {
                try
                {
                    string uploaded = await IrodoriClient.UploadVoiceBytesAsync(
                        baseUrl,
                        apiKey,
                        candidate.Audio,
                        voiceId + ".wav",
                        voiceId);

                    lock (_asyncLock)
                    {
                        _pendingAdoption = new AdoptionResult
                        {
                            VoiceId = uploaded,
                            ProfileName = profile,
                            Caption = stableCaption,
                            Candidate = candidate,
                            Error = string.IsNullOrWhiteSpace(uploaded) ? "Irodori voice upload failed. See logs for the HTTP response." : null
                        };
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimTalk.TTS/VoiceLab] Voice upload exception: {ex}");
                    lock (_asyncLock)
                    {
                        _pendingAdoption = new AdoptionResult { Error = ex.GetType().Name + ": " + ex.Message };
                    }
                }
                finally
                {
                    _isUploading = false;
                }
            });
        }

        /// <summary>
        /// Returns true when the candidate was removed synchronously.
        /// Unadopted Voice Design candidates are only byte[] data in memory, so there is no Irodori file to delete.
        /// Once adopted, X means destructive delete: delete the registered voice file on the Irodori server first,
        /// then remove the matching local Voice Profile/config and this candidate row.
        /// </summary>
        private bool BeginDeleteCandidate(Candidate candidate)
        {
            if (candidate == null || candidate.IsDeleting || _isUploading)
                return false;

            if (string.IsNullOrWhiteSpace(candidate.RegisteredVoiceId))
            {
                SecureDiscardCandidate(candidate);
                _candidates.Remove(candidate);
                _status = "RimTalk.TTS.VoiceLab.DiscardedCandidate".Translate();
                return true;
            }

            string voiceId = candidate.RegisteredVoiceId;
            string baseUrl = _settings?.Irodori?.BaseUrl;
            string apiKey = _settings?.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _status = "ERROR: Irodori Base URL is empty; registered voice file was not deleted.";
                return false;
            }

            candidate.IsDeleting = true;
            _status = "RimTalk.TTS.VoiceLab.DeletingReference".Translate(voiceId);

            Task.Run(async () =>
            {
                try
                {
                    bool ok = await IrodoriClient.DeleteVoiceAsync(baseUrl, apiKey, voiceId);
                    lock (_asyncLock)
                    {
                        _pendingDeletion = new DeletionResult
                        {
                            Candidate = candidate,
                            VoiceId = voiceId,
                            Success = ok,
                            Error = ok ? null : "Irodori server rejected or could not find the voice file."
                        };
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimTalk.TTS/VoiceLab] Voice delete exception for '{voiceId}': {ex}");
                    lock (_asyncLock)
                    {
                        _pendingDeletion = new DeletionResult
                        {
                            Candidate = candidate,
                            VoiceId = voiceId,
                            Success = false,
                            Error = ex.GetType().Name + ": " + ex.Message
                        };
                    }
                }
            });

            return false;
        }

        private void ApplyDeletedVoice(DeletionResult deletion)
        {
            if (deletion == null || deletion.Candidate == null)
                return;

            deletion.Candidate.IsDeleting = false;
            if (!deletion.Success)
            {
                _status = "ERROR: " + "RimTalk.TTS.VoiceLab.DeleteFailed".Translate(
                    deletion.VoiceId ?? "?", deletion.Error ?? "Unknown delete error");
                return;
            }

            string voiceId = deletion.VoiceId;
            try
            {
                // Remove the now-invalid local profile as well, otherwise Bio would keep offering a voice
                // whose backing reference file no longer exists on the Irodori server.
                var models = _settings?.GetSupplierVoiceModels(TTSSettings.TTSSupplier.Irodori);
                if (models != null)
                {
                    models.RemoveAll(m => m != null && m.ModelId == voiceId);
                    _settings.SetSupplierVoiceModels(TTSSettings.TTSSupplier.Irodori, models);
                }

                if (_settings?.Irodori?.VoiceConfigs != null)
                    _settings.Irodori.VoiceConfigs.Remove(voiceId);

                if (_pawn != null && PawnVoiceManager.GetRawVoiceModel(_pawn) == voiceId)
                {
                    PawnVoiceManager.SetVoiceModel(_pawn, VoiceModel.DEFAULT_MODEL_ID);
                    _onVoiceAdopted?.Invoke(VoiceModel.DEFAULT_MODEL_ID);
                }

                SecureDiscardCandidate(deletion.Candidate);
                _candidates.Remove(deletion.Candidate);
                _status = "RimTalk.TTS.VoiceLab.DeletedReference".Translate(voiceId);
                Log.Message($"[RimTalk.TTS/VoiceLab] Deleted Irodori voice file/profile '{voiceId}'.");
            }
            catch (Exception ex)
            {
                // At this point the server file is already gone. Log local cleanup failure explicitly.
                _status = $"ERROR [local cleanup after delete]: {ex.GetType().Name}: {ex.Message}";
                Log.Error($"[RimTalk.TTS/VoiceLab] Server voice '{voiceId}' was deleted, but local cleanup failed: {ex}");
            }
        }

        private static void SecureDiscardCandidate(Candidate candidate)
        {
            if (candidate?.Audio != null)
            {
                Array.Clear(candidate.Audio, 0, candidate.Audio.Length);
                candidate.Audio = null;
            }
        }

        private void ApplyAsyncResults()
        {
            Candidate candidate = null;
            AdoptionResult adoption = null;
            DeletionResult deletion = null;
            string error = null;
            lock (_asyncLock)
            {
                candidate = _pendingCandidate;
                _pendingCandidate = null;
                adoption = _pendingAdoption;
                _pendingAdoption = null;
                deletion = _pendingDeletion;
                _pendingDeletion = null;
                error = _pendingError;
                _pendingError = null;
            }

            if (candidate != null)
            {
                _candidates.Insert(0, candidate);
                while (_candidates.Count > 8)
                {
                    var old = _candidates[_candidates.Count - 1];
                    SecureDiscardCandidate(old);
                    _candidates.RemoveAt(_candidates.Count - 1);
                }
                _status = "RimTalk.TTS.VoiceLab.Generated".Translate(candidate.Seed);
                _seedText = NewSeed().ToString();
            }

            if (!string.IsNullOrWhiteSpace(error))
                _status = "ERROR: " + error;

            if (deletion != null)
                ApplyDeletedVoice(deletion);

            if (adoption != null)
            {
                if (!string.IsNullOrWhiteSpace(adoption.Error) || string.IsNullOrWhiteSpace(adoption.VoiceId))
                {
                    _status = "ERROR: " + (adoption.Error ?? "Unknown upload error");
                }
                else
                {
                    RegisterAndAssign(adoption);
                }
            }
        }

        private void RegisterAndAssign(AdoptionResult adoption)
        {
            string stage = "validate adoption";
            try
            {
                if (adoption == null)
                    throw new InvalidOperationException("Adoption result was null.");
                if (_settings == null)
                    throw new InvalidOperationException("TTS settings were null.");
                if (string.IsNullOrWhiteSpace(adoption.VoiceId))
                    throw new InvalidOperationException("Uploaded Irodori voice ID was empty.");

                // The server file already exists at this point. Record its ID immediately so X can
                // still delete the real Irodori file even if a later local registration step fails.
                if (adoption.Candidate != null)
                {
                    adoption.Candidate.RegisteredVoiceId = adoption.VoiceId;
                    adoption.Candidate.RegisteredProfileName = adoption.ProfileName ?? adoption.VoiceId;
                }

                stage = "register Voice Profile";
                var models = _settings.GetSupplierVoiceModels(TTSSettings.TTSSupplier.Irodori)
                             ?? new List<VoiceModel>();
                // Be tolerant of partially deserialized lists from older settings.
                models.RemoveAll(x => x == null);
                var existing = models.FirstOrDefault(x => x.ModelId == adoption.VoiceId);
                if (existing == null)
                {
                    models.Add(new VoiceModel
                    {
                        ModelId = adoption.VoiceId,
                        ModelName = adoption.ProfileName ?? adoption.VoiceId
                    });
                }
                else
                {
                    existing.ModelName = adoption.ProfileName ?? adoption.VoiceId;
                }
                _settings.SetSupplierVoiceModels(TTSSettings.TTSSupplier.Irodori, models);

                stage = "create Irodori per-voice config";
                var irodori = _settings.Irodori ?? (_settings.Irodori = new IrodoriSettings());
                if (irodori.VoiceConfigs == null)
                    irodori.VoiceConfigs = new Dictionary<string, IrodoriVoiceConfig>();
                var cfg = irodori.GetOrCreateVoiceConfig(adoption.VoiceId);
                if (cfg == null)
                    throw new InvalidOperationException("Could not create Irodori per-voice config.");

                cfg.Mode = IrodoriVoiceConfig.ReferenceMode.RegistryVoice;
                // Persist only the stable voice identity description. Dynamic per-line delivery
                // continues to come from RTTTS Fast Path emotion metadata.
                cfg.Caption = adoption.Caption ?? "";

                stage = "assign voice to pawn";
                if (_pawn == null)
                    throw new InvalidOperationException("Pawn was null.");
                PawnVoiceManager.SetVoiceModel(_pawn, adoption.VoiceId);

                stage = "refresh Bio voice selection";
                _onVoiceAdopted?.Invoke(adoption.VoiceId);
                _status = "RimTalk.TTS.VoiceLab.Adopted".Translate(adoption.ProfileName ?? adoption.VoiceId, adoption.VoiceId);
                Messages.Message("RimTalk.TTS.VoiceLab.AdoptedMessage".Translate(_pawn.LabelShort, adoption.ProfileName ?? adoption.VoiceId),
                    MessageTypeDefOf.TaskCompletion, false);
                Log.Message($"[RimTalk.TTS/VoiceLab] Adopted Irodori voice '{adoption.VoiceId}' for pawn '{_pawn.LabelShort}'.");
            }
            catch (Exception ex)
            {
                _status = $"ERROR [{stage}]: {ex.GetType().Name}: {ex.Message}";
                Log.Error($"[RimTalk.TTS/VoiceLab] Failed during '{stage}' for uploaded voice '{adoption?.VoiceId ?? "<null>"}': {ex}");
            }
        }

        private string BuildVoiceId(int seed)
        {
            long pawnId = _pawn?.thingIDNumber ?? 0;
            string nonce = Guid.NewGuid().ToString("N").Substring(0, 6);
            return $"rttts_{Math.Abs(pawnId)}_{DateTime.UtcNow:yyyyMMddHHmmss}_{nonce}";
        }

        private static int NewSeed()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            return BitConverter.ToInt32(bytes, 0) & 0x7fffffff;
        }

        private static string Ellipsize(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length <= max ? oneLine : oneLine.Substring(0, Math.Max(1, max - 1)) + "…";
        }
    }
}
