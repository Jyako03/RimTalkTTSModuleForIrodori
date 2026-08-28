using System;
using System.Threading;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using RimTalk.TTS.Service.IrodoriService;
using Verse;

namespace RimTalk.TTS.Provider
{
    public sealed class IrodoriProvider : ITTSProvider
    {
        private readonly IrodoriClient _client;

        public IrodoriProvider(TTSSettings settings)
        {
            _client = new IrodoriClient(settings?.Irodori ?? new IrodoriSettings());
        }

        public async Task<byte[]> GenerateSpeechAsync(TTSRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _client.GenerateSpeechAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS/Irodori] Generation failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        public void Shutdown() { }

        // Official Irodori server only requires Bearer auth when IRODORI_API_KEY is configured.
        public bool IsApiKeyValid(string apiKey) => true;
    }
}
