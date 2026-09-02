using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimTalk.TTS.Service.IrodoriService
{
    /// <summary>
    /// Small helper for Reference Pack bookkeeping.
    /// The official Irodori server exposes resolved registry paths from GET /v1/audio/voices,
    /// allowing a remote RimWorld client to build request-level ref_wavs without assuming where
    /// IRODORI_VOICES_DIR lives on the server/sub-PC.
    /// </summary>
    public static class IrodoriReferencePackService
    {
        public const string InternalReferencePrefix = "rttts_packref_";
        public const string PackProfilePrefix = "rttts_pack_";

        private static readonly HttpClient Http = new HttpClient();

        [DataContract]
        private sealed class VoiceListResponse
        {
            [DataMember(Name = "data")]
            public List<VoiceEntry> Data;
        }

        [DataContract]
        private sealed class VoiceEntry
        {
            [DataMember(Name = "id")]
            public string Id;

            [DataMember(Name = "ref_wav")]
            public string RefWav;
        }

        public static async Task<string> ResolveRegistryVoicePathAsync(
            string baseUrl,
            string apiKey,
            string voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
                return null;

            using (var request = new HttpRequestMessage(HttpMethod.Get, VoicesUrl(baseUrl)))
            {
                AddAuth(request, apiKey);
                using (var response = await Http.SendAsync(request))
                {
                    string json = response.Content == null ? "" : await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Warning($"[RimTalk.TTS/ReferencePack] Voice path lookup failed: HTTP {(int)response.StatusCode} {json}");
                        return null;
                    }

                    try
                    {
                        var serializer = new DataContractJsonSerializer(typeof(VoiceListResponse));
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                        {
                            var parsed = serializer.ReadObject(stream) as VoiceListResponse;
                            var entry = parsed?.Data?.FirstOrDefault(v =>
                                v != null && string.Equals(v.Id, voiceId, StringComparison.Ordinal));
                            return string.IsNullOrWhiteSpace(entry?.RefWav) ? null : entry.RefWav;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimTalk.TTS/ReferencePack] Failed to parse Irodori voice list: {ex.Message}");
                        return null;
                    }
                }
            }
        }

        public static async Task DeleteOwnedReferencesAsync(
            string baseUrl,
            string apiKey,
            IEnumerable<string> voiceIds)
        {
            if (voiceIds == null) return;
            foreach (string voiceId in voiceIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList())
            {
                try
                {
                    bool ok = await IrodoriClient.DeleteVoiceAsync(baseUrl, apiKey, voiceId);
                    if (!ok)
                        Log.Warning($"[RimTalk.TTS/ReferencePack] Failed to delete backing reference '{voiceId}'.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimTalk.TTS/ReferencePack] Delete backing reference '{voiceId}' failed: {ex.Message}");
                }
            }
        }

        public static string BuildReferenceId(Verse.Pawn pawn, int ordinal)
        {
            long pawnId = pawn?.thingIDNumber ?? 0;
            string nonce = Guid.NewGuid().ToString("N").Substring(0, 6);
            return $"{InternalReferencePrefix}{Math.Abs(pawnId)}_{DateTime.UtcNow:yyyyMMddHHmmss}_{ordinal}_{nonce}";
        }

        public static string BuildPackProfileId(Verse.Pawn pawn)
        {
            long pawnId = pawn?.thingIDNumber ?? 0;
            string nonce = Guid.NewGuid().ToString("N").Substring(0, 6);
            return $"{PackProfilePrefix}{Math.Abs(pawnId)}_{DateTime.UtcNow:yyyyMMddHHmmss}_{nonce}";
        }

        private static string VoicesUrl(string baseUrl)
        {
            string url = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:8088/v1"
                : baseUrl.Trim().TrimEnd('/');

            if (url.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - "/audio/speech".Length);

            if (url.EndsWith("/audio", StringComparison.OrdinalIgnoreCase))
                return url + "/voices";
            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return url + "/audio/voices";
            return url + "/v1/audio/voices";
        }

        private static void AddAuth(HttpRequestMessage request, string apiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }
    }
}
