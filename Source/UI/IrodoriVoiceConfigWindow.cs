using System;
using System.Linq;
using UnityEngine;
using Verse;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;

namespace RimTalk.TTS.UI
{
    /// <summary>
    /// Per-VoiceModel Irodori reference/style editor. The VoiceModel ID is what PawnVoiceManager already assigns per pawn.
    /// </summary>
    public sealed class IrodoriVoiceConfigWindow : Window
    {
        private readonly TTSSettings _settings;
        private readonly VoiceModel _model;
        private IrodoriVoiceConfig _config;
        private Vector2 _scroll;
        private string _refWavs;
        private string _refLatents;
        private volatile bool _testRunning;
        private volatile string _testStatus = "";

        public override Vector2 InitialSize => new Vector2(720f, 700f);

        public IrodoriVoiceConfigWindow(TTSSettings settings, VoiceModel model)
        {
            _settings = settings;
            _model = model;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            string id = _model?.ModelId ?? "";
            _config = _settings?.Irodori?.GetOrCreateVoiceConfig(id) ?? new IrodoriVoiceConfig();
            _refWavs = string.Join("\n", _config.RefWavs ?? new System.Collections.Generic.List<string>());
            _refLatents = string.Join("\n", _config.RefLatents ?? new System.Collections.Generic.List<string>());
        }

        public override void DoWindowContents(Rect inRect)
        {
            float contentHeight = 1120f;
            Rect view = new Rect(0f, 0f, inRect.width - 20f, contentHeight);
            Widgets.BeginScrollView(inRect, ref _scroll, view);
            var listing = new Listing_Standard();
            listing.Begin(view);

            Text.Font = GameFont.Medium;
            listing.Label("Irodori Voice Profile: " + (_model?.GetDisplayName() ?? "(unnamed)"));
            Text.Font = GameFont.Small;
            listing.Label("Irodori server voice ID: " + (_model?.ModelId ?? ""));
            listing.Label("Pawn assignment is selected from each pawn's Bio > Voice. This window only configures the selected Voice Profile.");
            listing.Gap(8f);

            listing.Label("Reference mode");
            if (listing.RadioButton("Server voice registry (recommended for remote/sub-PC Irodori)", _config.Mode == IrodoriVoiceConfig.ReferenceMode.RegistryVoice))
                _config.Mode = IrodoriVoiceConfig.ReferenceMode.RegistryVoice;
            if (listing.RadioButton("Direct server-visible reference paths", _config.Mode == IrodoriVoiceConfig.ReferenceMode.DirectReferences))
                _config.Mode = IrodoriVoiceConfig.ReferenceMode.DirectReferences;
            if (listing.RadioButton("No reference / Voice Design", _config.Mode == IrodoriVoiceConfig.ReferenceMode.NoReference))
                _config.Mode = IrodoriVoiceConfig.ReferenceMode.NoReference;

            listing.Gap(8f);
            listing.Label("Per-voice caption/style (merged with RimTalk emotion)");
            _config.Caption = listing.TextEntry(_config.Caption ?? "");
            listing.Label("Per-voice LoRA adapter (blank = global/server default)");
            _config.LoraAdapter = listing.TextEntry(_config.LoraAdapter ?? "");

            listing.Gap(8f);
            listing.Label("ref_wav (single server-visible path)");
            _config.RefWav = listing.TextEntry(_config.RefWav ?? "");
            listing.Label("ref_wavs (one server-visible path per line; order is preserved)");
            Rect rw = listing.GetRect(120f);
            _refWavs = Widgets.TextArea(rw, _refWavs ?? "");

            listing.Gap(8f);
            listing.Label("ref_latent (single .pt/.pth server-visible path)");
            _config.RefLatent = listing.TextEntry(_config.RefLatent ?? "");
            listing.Label("ref_latents (one server-visible path per line; order is preserved)");
            Rect rl = listing.GetRect(100f);
            _refLatents = Widgets.TextArea(rl, _refLatents ?? "");

            listing.Gap(8f);
            listing.Label("ref_embed (.speaker.safetensors server-visible path)");
            _config.RefEmbed = listing.TextEntry(_config.RefEmbed ?? "");

            listing.Gap(12f);
            listing.Label("Direct paths are resolved by the Irodori server, not by RimWorld. For a different PC/container, use the server voice registry or paths mounted into that server.");
            if (_config.Mode == IrodoriVoiceConfig.ReferenceMode.DirectReferences && !HasAnyDirectReferenceInEditor())
            {
                GUI.color = Color.yellow;
                listing.Label("No direct reference path is set. Runtime will safely fall back to server registry voice ID: " + (_model?.ModelId ?? ""));
                GUI.color = Color.white;
            }

            listing.Gap(12f);
            if (!_testRunning && listing.ButtonText("Synthesis test (real Irodori POST; no playback)"))
            {
                SaveLists();
                _testRunning = true;
                _testStatus = "Testing...";
                var settingsSnapshot = _settings;
                var modelSnapshot = _model;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var client = new IrodoriClient(settingsSnapshot?.Irodori);
                        var req = new TTSRequest
                        {
                            ApiKey = settingsSnapshot?.GetSupplierApiKey(TTSSettings.TTSSupplier.Irodori),
                            Model = settingsSnapshot?.GetSupplierModel(TTSSettings.TTSSupplier.Irodori),
                            Input = "これはIrodori音声設定のテストです。",
                            Emotion = "calm and natural",
                            Voice = modelSnapshot?.ModelId,
                            Speed = settingsSnapshot?.GetSupplierSpeed(TTSSettings.TTSSupplier.Irodori) ?? 1.0f,
                            Volume = settingsSnapshot?.GetSupplierVolume(TTSSettings.TTSSupplier.Irodori) ?? 1.0f,
                            Temperature = settingsSnapshot?.GetSupplierTemperature(TTSSettings.TTSSupplier.Irodori) ?? 0.9f,
                            TopP = settingsSnapshot?.GetSupplierTopP(TTSSettings.TTSSupplier.Irodori) ?? 0.9f
                        };
                        byte[] data = await client.GenerateSpeechAsync(req);
                        _testStatus = data != null && data.Length > 0
                            ? $"Synthesis OK ({data.Length:N0} bytes)"
                            : "Synthesis failed. Check RimWorld and Irodori logs.";
                    }
                    catch (Exception ex)
                    {
                        _testStatus = "Synthesis failed: " + ex.Message;
                    }
                    finally
                    {
                        _testRunning = false;
                    }
                });
            }
            else if (_testRunning)
            {
                listing.Label("Synthesis test running...");
            }
            if (!string.IsNullOrWhiteSpace(_testStatus))
                listing.Label(_testStatus);

            listing.Gap(16f);
            if (listing.ButtonText("Save / Close"))
            {
                SaveLists();
                Close();
            }

            listing.End();
            Widgets.EndScrollView();
        }

        public override void PreClose()
        {
            SaveLists();
            base.PreClose();
        }

        private void SaveLists()
        {
            _config.RefWavs = SplitLines(_refWavs);
            _config.RefLatents = SplitLines(_refLatents);
            string id = _model?.ModelId ?? "";
            if (!string.IsNullOrWhiteSpace(id) && _settings?.Irodori != null)
                _settings.Irodori.VoiceConfigs[id] = _config;
        }

        private bool HasAnyDirectReferenceInEditor()
        {
            if (!string.IsNullOrWhiteSpace(_config.RefWav)) return true;
            if (!string.IsNullOrWhiteSpace(_config.RefLatent)) return true;
            if (!string.IsNullOrWhiteSpace(_config.RefEmbed)) return true;
            if (!string.IsNullOrWhiteSpace(_refWavs)) return true;
            if (!string.IsNullOrWhiteSpace(_refLatents)) return true;
            return false;
        }

        private static System.Collections.Generic.List<string> SplitLines(string value)
        {
            return (value ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }
    }
}
