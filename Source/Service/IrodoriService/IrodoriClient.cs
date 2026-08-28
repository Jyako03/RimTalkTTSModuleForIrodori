using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using Verse;

namespace RimTalk.TTS.Service.IrodoriService
{
    public sealed class IrodoriClient
    {
        private static readonly HttpClient Http = new HttpClient();
        private readonly IrodoriSettings _settings;

        public IrodoriClient(IrodoriSettings settings)
        {
            _settings = settings ?? new IrodoriSettings();
        }

        public async Task<byte[]> GenerateSpeechAsync(TTSRequest request, CancellationToken cancellationToken = default)
        {
            string format = string.IsNullOrWhiteSpace(_settings.ResponseFormat) ? "wav" : _settings.ResponseFormat.Trim().ToLowerInvariant();
            bool useSse = _settings.UseSse;
            if (useSse && format != "wav")
            {
                Log.Warning("[RimTalk.TTS/Irodori] SSE collection currently requires WAV; falling back to standard response for non-WAV format.");
                useSse = false;
            }

            var voiceConfig = _settings.GetVoiceConfig(request.Voice);
            string input = request.Input ?? "";
            string emotion = request.Emotion ?? "";
            string caption = IrodoriEmotionMapper.BuildCaption(
                _settings.CaptionPrefix,
                voiceConfig?.Caption,
                _settings.EmotionToCaption ? emotion : "");

            if (_settings.EmotionToEmoji)
            {
                string emoji = IrodoriEmotionMapper.ToEmoji(emotion);
                if (!string.IsNullOrEmpty(emoji) && !input.StartsWith(emoji, StringComparison.Ordinal))
                    input = emoji + " " + input;
            }

            string body = BuildSpeechJson(request, input, caption, voiceConfig, format, useSse);
            string url = SpeechUrl(_settings.BaseUrl);

            using var msg = new HttpRequestMessage(HttpMethod.Post, url);
            msg.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());
            if (useSse) msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await Http.SendAsync(msg, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string error = response.Content == null ? "" : await response.Content.ReadAsStringAsync();
                Log.Error($"[RimTalk.TTS/Irodori] HTTP {(int)response.StatusCode} {response.StatusCode}: {error}");
                return null;
            }

            if (!useSse)
                return await response.Content.ReadAsByteArrayAsync();

            string sse = await response.Content.ReadAsStringAsync();
            return ParseSseWav(sse);
        }

        /// <summary>
        /// Generate a short BIO voice-selection preview using an existing Irodori registry voice.
        /// WAV/non-SSE is forced so the game preview path can always decode the returned bytes.
        /// Normal per-voice caption/style settings are still honored.
        /// </summary>
        public async Task<byte[]> GenerateVoiceRegistryPreviewAsync(
            TTSRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Input) || string.IsNullOrWhiteSpace(request.Voice))
                return null;

            var voiceConfig = _settings.GetVoiceConfig(request.Voice);
            string input = request.Input ?? "";
            string emotion = request.Emotion ?? "";
            string caption = IrodoriEmotionMapper.BuildCaption(
                _settings.CaptionPrefix,
                voiceConfig?.Caption,
                _settings.EmotionToCaption ? emotion : "");

            string body = BuildSpeechJson(request, input, caption, voiceConfig, "wav", false);
            string url = SpeechUrl(_settings.BaseUrl);

            using (var msg = new HttpRequestMessage(HttpMethod.Post, url))
            {
                msg.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(request.ApiKey))
                    msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());

                using (var response = await Http.SendAsync(msg, HttpCompletionOption.ResponseContentRead, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string error = response.Content == null ? "" : await response.Content.ReadAsStringAsync();
                        Log.Error($"[RimTalk.TTS/BioPreview] HTTP {(int)response.StatusCode} {response.StatusCode}: {error}");
                        return null;
                    }
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
        }

        /// <summary>
        /// Generate one pure Voice Design candidate. This intentionally bypasses the normal
        /// per-voice configuration and always uses Irodori's built-in no-reference voice.
        /// The requested seed is kept explicit so the user can reproduce a candidate.
        /// </summary>
        public async Task<byte[]> GenerateVoiceDesignAsync(
            TTSRequest request,
            string caption,
            int seed,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Input)) return null;

            string body = BuildVoiceDesignJson(request, caption ?? string.Empty, seed);
            string url = SpeechUrl(_settings.BaseUrl);

            using var msg = new HttpRequestMessage(HttpMethod.Post, url);
            msg.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.ApiKey.Trim());

            using var response = await Http.SendAsync(msg, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string error = response.Content == null ? "" : await response.Content.ReadAsStringAsync();
                Log.Error($"[RimTalk.TTS/VoiceLab] Voice Design HTTP {(int)response.StatusCode} {response.StatusCode}: {error}");
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

        public static async Task<List<(string id, string name)>> ListVoicesAsync(string baseUrl, string apiKey = null)
        {
            var result = new List<(string id, string name)>();
            using var msg = new HttpRequestMessage(HttpMethod.Get, VoicesUrl(baseUrl));
            AddAuth(msg, apiKey);
            using var response = await Http.SendAsync(msg);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning($"[RimTalk.TTS/Irodori] Voice sync failed: {(int)response.StatusCode}");
                return result;
            }
            string json = await response.Content.ReadAsStringAsync();
            foreach (Match match in Regex.Matches(json, "\\\"id\\\"\\s*:\\s*\\\"([^\\\"]+)\\\""))
            {
                string id = Unescape(match.Groups[1].Value);
                if (!result.Exists(x => x.id == id)) result.Add((id, id));
            }
            return result;
        }

        public static async Task<string> UploadVoiceAsync(string baseUrl, string apiKey, string filePath, string voiceId)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            byte[] bytes = File.ReadAllBytes(filePath);
            return await UploadVoiceBytesAsync(baseUrl, apiKey, bytes, Path.GetFileName(filePath), voiceId);
        }

        /// <summary>
        /// Upload generated in-memory audio directly to Irodori's voice registry. This is used by
        /// Voice Lab so a liked synthetic candidate can immediately become a cloning reference
        /// without writing a temporary file on the RimWorld PC.
        /// </summary>
        public static async Task<string> UploadVoiceBytesAsync(
            string baseUrl,
            string apiKey,
            byte[] bytes,
            string fileName,
            string voiceId)
        {
            if (bytes == null || bytes.Length == 0) return null;
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "rttts_voice.wav";

            // RimWorld's Mono implementation has shown NullReferenceException inside
            // MultipartContent.Dispose even when ownership is correct. Avoid
            // MultipartFormDataContent/MultipartContent entirely and build the small
            // multipart/form-data payload as one ByteArrayContent buffer instead.
            string boundary = "----RimTalkTTS" + Guid.NewGuid().ToString("N");
            string safeFileName = SanitizeMultipartFileName(fileName);
            string safeVoiceId = string.IsNullOrWhiteSpace(voiceId) ? null : voiceId.Trim();

            byte[] fileHeader = Encoding.UTF8.GetBytes(
                "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename=\"" + safeFileName + "\"\r\n" +
                "Content-Type: audio/wav\r\n\r\n");

            byte[] tail;
            if (!string.IsNullOrWhiteSpace(safeVoiceId))
            {
                tail = Encoding.UTF8.GetBytes(
                    "\r\n--" + boundary + "\r\n" +
                    "Content-Disposition: form-data; name=\"voice_id\"\r\n\r\n" +
                    safeVoiceId +
                    "\r\n--" + boundary + "--\r\n");
            }
            else
            {
                tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            }

            byte[] body = new byte[fileHeader.Length + bytes.Length + tail.Length];
            Buffer.BlockCopy(fileHeader, 0, body, 0, fileHeader.Length);
            Buffer.BlockCopy(bytes, 0, body, fileHeader.Length, bytes.Length);
            Buffer.BlockCopy(tail, 0, body, fileHeader.Length + bytes.Length, tail.Length);

            using (var msg = new HttpRequestMessage(HttpMethod.Post, VoicesUrl(baseUrl)))
            {
                var content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
                content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));
                msg.Content = content;
                AddAuth(msg, apiKey);

                using (var response = await Http.SendAsync(msg))
                {
                    string payload = response.Content == null ? "" : await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error($"[RimTalk.TTS/Irodori] Voice upload failed: {(int)response.StatusCode} {payload}");
                        return null;
                    }

                    var m = Regex.Match(payload, "\\\"id\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                    string resolvedVoiceId = m.Success ? Unescape(m.Groups[1].Value) : safeVoiceId;
                    Log.Message($"[RimTalk.TTS/VoiceLab] Voice upload succeeded: '{resolvedVoiceId}' ({bytes.Length} bytes, manual multipart)");
                    return resolvedVoiceId;
                }
            }
        }

        private static string SanitizeMultipartFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "rttts_voice.wav";
            string name = Path.GetFileName(fileName).Replace("\r", "_").Replace("\n", "_").Replace("\"", "_");
            return string.IsNullOrWhiteSpace(name) ? "rttts_voice.wav" : name;
        }

        public static async Task<bool> DeleteVoiceAsync(string baseUrl, string apiKey, string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId)) return false;
            using var msg = new HttpRequestMessage(HttpMethod.Delete, VoicesUrl(baseUrl) + "/" + Uri.EscapeDataString(voiceId));
            AddAuth(msg, apiKey);
            using var response = await Http.SendAsync(msg);
            return response.IsSuccessStatusCode;
        }

        public static async Task<bool> CheckConnectionAsync(string baseUrl, string apiKey = null)
        {
            string url = NormalizeBase(baseUrl);
            // /health is outside /v1 in the official server.
            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 3);
            url = url.TrimEnd('/') + "/health";
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                AddAuth(msg, apiKey);
                using var response = await Http.SendAsync(msg);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private string BuildSpeechJson(TTSRequest request, string input, string caption, IrodoriVoiceConfig voiceConfig, string format, bool useSse)
        {
            var root = new List<string>
            {
                Pair("model", string.IsNullOrWhiteSpace(request.Model) ? "irodori-tts" : request.Model),
                Pair("input", input),
                Pair("response_format", format),
                Pair("speed", request.Speed > 0 ? request.Speed : 1.0f)
            };
            if (useSse) root.Add(Pair("stream_format", "sse"));

            bool directRequested = voiceConfig != null && voiceConfig.Mode == IrodoriVoiceConfig.ReferenceMode.DirectReferences;
            bool hasDirectReferences = HasAnyDirectReference(voiceConfig);
            bool direct = directRequested && hasDirectReferences;
            bool noRef = voiceConfig != null && voiceConfig.Mode == IrodoriVoiceConfig.ReferenceMode.NoReference;

            // Safety fallback: DirectReferences without any actual reference path used to omit both
            // `voice` and `ref_*`, causing Irodori to return HTTP 400 (No voice was provided...).
            // In that case, transparently fall back to the server registry voice ID.
            if (directRequested && !hasDirectReferences)
                Log.Warning($"[RimTalk.TTS/Irodori] Direct reference mode has no ref_* values for voice '{request.Voice}'. Falling back to server voice registry.");

            if (!direct)
                root.Add(Pair("voice", noRef ? "none" : request.Voice));

            var opts = new List<string>();
            if (!string.IsNullOrWhiteSpace(caption)) opts.Add(Pair("caption", caption));
            if (direct)
            {
                AddIf(opts, "ref_wav", voiceConfig.RefWav);
                AddArrayIf(opts, "ref_wavs", voiceConfig.RefWavs);
                AddIf(opts, "ref_latent", voiceConfig.RefLatent);
                AddArrayIf(opts, "ref_latents", voiceConfig.RefLatents);
                AddIf(opts, "ref_embed", voiceConfig.RefEmbed);
            }
            if (noRef) opts.Add(Pair("no_ref", true));

            opts.Add(Pair("num_steps", _settings.NumSteps));
            opts.Add(Pair("t_schedule_mode", string.IsNullOrWhiteSpace(_settings.TScheduleMode) ? "linear" : _settings.TScheduleMode));
            opts.Add(Pair("sway_coeff", _settings.SwayCoeff));
            opts.Add(Pair("duration_scale", _settings.DurationScale));
            opts.Add(Pair("cfg_scale_text", _settings.CfgScaleText));
            opts.Add(Pair("cfg_scale_speaker", _settings.CfgScaleSpeaker));
            if (_settings.CfgScaleCaption > 0) opts.Add(Pair("cfg_scale_caption", _settings.CfgScaleCaption));
            if (!string.IsNullOrWhiteSpace(_settings.CfgGuidanceMode)) opts.Add(Pair("cfg_guidance_mode", _settings.CfgGuidanceMode));
            if (_settings.Seed >= 0) opts.Add(Pair("seed", _settings.Seed));
            if (_settings.MaxRefSeconds > 0) opts.Add(Pair("max_ref_seconds", _settings.MaxRefSeconds));
            opts.Add(Pair("chunking_enabled", _settings.ChunkingEnabled));
            if (_settings.ChunkMinChars > 0) opts.Add(Pair("chunk_min_chars", _settings.ChunkMinChars));
            if (_settings.FirstSentenceChunkMinChars > 0) opts.Add(Pair("first_sentence_chunk_min_chars", _settings.FirstSentenceChunkMinChars));

            string lora = !string.IsNullOrWhiteSpace(voiceConfig?.LoraAdapter) ? voiceConfig.LoraAdapter : _settings.GlobalLoraAdapter;
            AddIf(opts, "lora_adapter", lora);

            // Raw object entries go LAST intentionally: expert settings can override typed values and use future fields.
            string raw = ObjectInner(_settings.AdvancedOptionsJson);
            if (!string.IsNullOrWhiteSpace(raw)) opts.Add(raw);

            root.Add("\"irodori\":{" + string.Join(",", opts) + "}");
            return "{" + string.Join(",", root) + "}";
        }

        private string BuildVoiceDesignJson(TTSRequest request, string caption, int seed)
        {
            var root = new List<string>
            {
                Pair("model", string.IsNullOrWhiteSpace(request.Model) ? "irodori-tts" : request.Model),
                Pair("input", request.Input ?? string.Empty),
                Pair("voice", "none"),
                Pair("response_format", "wav"),
                Pair("speed", request.Speed > 0 ? request.Speed : 1.0f)
            };

            var opts = new List<string>();
            if (!string.IsNullOrWhiteSpace(caption)) opts.Add(Pair("caption", caption.Trim()));
            opts.Add(Pair("no_ref", true));
            opts.Add(Pair("num_steps", _settings.NumSteps));
            opts.Add(Pair("t_schedule_mode", string.IsNullOrWhiteSpace(_settings.TScheduleMode) ? "linear" : _settings.TScheduleMode));
            opts.Add(Pair("sway_coeff", _settings.SwayCoeff));
            opts.Add(Pair("duration_scale", _settings.DurationScale));
            opts.Add(Pair("cfg_scale_text", _settings.CfgScaleText));
            opts.Add(Pair("cfg_scale_speaker", _settings.CfgScaleSpeaker));
            if (_settings.CfgScaleCaption > 0) opts.Add(Pair("cfg_scale_caption", _settings.CfgScaleCaption));
            if (!string.IsNullOrWhiteSpace(_settings.CfgGuidanceMode)) opts.Add(Pair("cfg_guidance_mode", _settings.CfgGuidanceMode));
            if (seed >= 0) opts.Add(Pair("seed", seed));
            opts.Add(Pair("chunking_enabled", false));
            AddIf(opts, "lora_adapter", _settings.GlobalLoraAdapter);

            // Voice Lab deliberately does not merge AdvancedOptionsJson here. Candidate generation
            // must stay one-WAV-per-seed and reproducible even if the normal gameplay request has
            // experimental overrides such as num_candidates or batch decoding.
            root.Add("\"irodori\":{" + string.Join(",", opts) + "}");
            return "{" + string.Join(",", root) + "}";
        }

        private static bool HasAnyDirectReference(IrodoriVoiceConfig config)
        {
            if (config == null) return false;
            if (!string.IsNullOrWhiteSpace(config.RefWav)) return true;
            if (!string.IsNullOrWhiteSpace(config.RefLatent)) return true;
            if (!string.IsNullOrWhiteSpace(config.RefEmbed)) return true;
            if (config.RefWavs != null && config.RefWavs.Any(x => !string.IsNullOrWhiteSpace(x))) return true;
            if (config.RefLatents != null && config.RefLatents.Any(x => !string.IsNullOrWhiteSpace(x))) return true;
            return false;
        }

        private static byte[] ParseSseWav(string sse)
        {
            var chunks = new List<byte[]>();
            foreach (Match eventMatch in Regex.Matches(sse ?? "", @"event:\s*audio_chunk\s*\r?\ndata:\s*(\{[^\r\n]*\})", RegexOptions.Multiline))
            {
                string data = eventMatch.Groups[1].Value;
                var b64 = Regex.Match(data, "\\\"audio_base64\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                if (b64.Success) chunks.Add(Convert.FromBase64String(b64.Groups[1].Value));
            }
            if (chunks.Count == 0)
            {
                Log.Error("[RimTalk.TTS/Irodori] SSE response contained no audio_chunk events.");
                return null;
            }
            return WavConcatUtil.Concatenate(chunks);
        }

        private static void AddAuth(HttpRequestMessage msg, string apiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        private static string NormalizeBase(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return "http://127.0.0.1:8088/v1";
            return baseUrl.Trim().TrimEnd('/');
        }

        private static string SpeechUrl(string baseUrl)
        {
            string b = NormalizeBase(baseUrl);
            if (b.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase)) return b;
            if (!b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) b += "/v1";
            return b + "/audio/speech";
        }

        private static string VoicesUrl(string baseUrl)
        {
            string b = NormalizeBase(baseUrl);
            if (b.EndsWith("/audio/voices", StringComparison.OrdinalIgnoreCase)) return b;
            if (b.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase)) b = b.Substring(0, b.Length - "/speech".Length) + "/voices";
            else
            {
                if (!b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) b += "/v1";
                b += "/audio/voices";
            }
            return b;
        }

        private static string Pair(string key, string value) => Quote(key) + ":" + Quote(value ?? "");
        private static string Pair(string key, bool value) => Quote(key) + ":" + (value ? "true" : "false");
        private static string Pair(string key, int value) => Quote(key) + ":" + value.ToString(CultureInfo.InvariantCulture);
        private static string Pair(string key, float value) => Quote(key) + ":" + value.ToString("R", CultureInfo.InvariantCulture);

        private static void AddIf(List<string> dst, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) dst.Add(Pair(key, value.Trim()));
        }

        private static void AddArrayIf(List<string> dst, string key, IEnumerable<string> values)
        {
            if (values == null) return;
            var clean = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Quote(x.Trim())).ToList();
            if (clean.Count > 0) dst.Add(Quote(key) + ":[" + string.Join(",", clean) + "]");
        }

        private static string ObjectInner(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim();
            if (s == "{}") return "";
            if (!s.StartsWith("{") || !s.EndsWith("}"))
            {
                Log.Warning("[RimTalk.TTS/Irodori] AdvancedOptionsJson must be a JSON object; ignoring it.");
                return "";
            }
            return s.Substring(1, s.Length - 2).Trim();
        }

        private static string Quote(string value)
        {
            if (value == null) value = "";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }

        private static string Unescape(string value) => value?.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
