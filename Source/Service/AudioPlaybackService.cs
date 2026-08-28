using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimTalk.TTS.Patch;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.Service;

/// <summary>
/// Service for playing audio using Unity's audio system
/// </summary>
[StaticConstructorOnStartup]
public static class AudioPlaybackService
{
    private static readonly GameObject _audioPlayerObject;
    private static readonly AudioSource _audioSource;
    
    // === Audio State Tracking ===
    // Single source of truth: dialogue -> audio bytes (null means generation failed)
    private static readonly Dictionary<Guid, byte[]> _dialogueAudio = new Dictionary<Guid, byte[]>();
    
    // === Playback Control ===
    private static bool _isPlaying = false;
    // Preview playback is lower priority than normal RimTalk dialogue. A generation token
    // lets a new preview invalidate an older async preview without the old finally block
    // clearing the state of the newly-started preview.
    private static bool _isPreviewPlaying = false;
    private static int _previewPlaybackVersion = 0;
    private static readonly object _lock = new object();

    /// <summary>
    /// Static constructor - initializes Unity AudioSource on game startup (main thread)
    /// </summary>
    static AudioPlaybackService()
    {
        _audioPlayerObject = new GameObject("RimTalkAudioPlayer");
        UnityEngine.Object.DontDestroyOnLoad(_audioPlayerObject);
        _audioSource = _audioPlayerObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D sound
        // _audioSource.minDistance = 100f;
        // _audioSource.maxDistance = 10f;
        // _audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        _audioSource.dopplerLevel = 0f;
    }
    
    /// <summary>
    /// Initialize the audio playback service (no-op now, kept for compatibility)
    /// </summary>
    public static void Initialize()
    {
        // Initialization now happens in static constructor
    }

    /// <summary>
    /// Record audio generation result for a dialogue (null = generation failed).
    /// Playback is triggered by TalkService.DisplayTalk().
    /// </summary>
    public static void SetAudioResult(Guid dialogueId, byte[] wavData)
    {
        if (dialogueId == Guid.Empty) return;

        Initialize();
        lock (_lock)
        {
            _dialogueAudio[dialogueId] = wavData; // wavData may be null on failure

            var len = wavData?.Length ?? 0;
        }
    }

    /// <summary>
    /// Check if audio is currently playing
    /// </summary>
    public static bool IsCurrentlyPlaying()
    {
        lock (_lock)
        {
            return _isPlaying;
        }
    }

    /// <summary>
    /// Play audio for a dialogue. Waits for previous playback and TTS generation.
    /// </summary>
    public static async void PlayAudio(Guid dialogueId, Pawn pawn, float volume = 1.0f)
    {
        if (dialogueId == Guid.Empty) return;

        try
        {
            // Step 1: Wait for any existing playback to finish (infinite wait)
            int playbackWaitCycles = 0;
            while (IsCurrentlyPlaying())
            {
                await Task.Delay(1000);
                playbackWaitCycles++;
            }

            // Step 2: Set playing flag to true
            lock (_lock)
            {
                _isPlaying = true;
            }

            // Step 3: Wait for TTS generation to complete (max 30 seconds)
            int ttsWaitCycles = 0;
            const int maxTtsWaitCycles = 30; // 300 * 100ms = 30 seconds
            
            while (RimTalkPatches.IsBlocked(dialogueId) && ttsWaitCycles < maxTtsWaitCycles)
            {
                await Task.Delay(1000);
                ttsWaitCycles++;
            }

            if (ttsWaitCycles >= maxTtsWaitCycles)
            {
                Log.Warning($"[RimTalk.TTS] Timeout waiting for TTS (30s), skipping audio for dialogue {dialogueId}");
                lock (_lock)
                {
                    _isPlaying = false;
                    _dialogueAudio.Remove(dialogueId);
                }
                return;
            }

            // Small delay to ensure audio is fully stored
            await Task.Delay(100);

            // Step 4: Check if audio is valid
            byte[] wavData;
            lock (_lock)
            {
                if (!_dialogueAudio.TryGetValue(dialogueId, out wavData))
                {
                    Log.Message($"[RimTalk.TTS] No audio found for dialogue {dialogueId}, skipping playback");
                    _isPlaying = false;
                    return;
                }

                if (wavData == null || wavData.Length == 0)
                {
                    _dialogueAudio.Remove(dialogueId);
                    Log.Message($"[RimTalk.TTS] Audio is null or empty for dialogue {dialogueId}, skipping playback");
                    _isPlaying = false;
                    return;
                }
            }

            // Step 5: Play audio and wait for completion
            try
            {
                AudioClip clip = await LoadAudioClipFromData(wavData, dialogueId.ToString());
                if (clip != null && clip.length > 0)
                {
                    _audioSource.clip = clip;
                    _audioSource.volume = UnityEngine.Mathf.Clamp01(volume);
                    // var follower = _audioPlayerObject.GetComponent<FollowPawnBehaviour>() ?? _audioPlayerObject.AddComponent<FollowPawnBehaviour>();
                    // follower.pawn = pawn;
                    // follower.verticalOffset = 0.5f;
                    _audioSource.Play();
                    // follower.pawn = null;

                    // Wait for playback to complete based on clip length
                    int playbackDelayMs = (int)(clip.length * 1000f);
                    await Task.Delay(playbackDelayMs);
                }
                else
                {
                    Log.Error("[RimTalk.TTS] Failed to create audio clip from audio data or clip length is 0");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimTalk.TTS] AudioPlaybackService.PlayAudio - playback exception: {ex}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalk.TTS] AudioPlaybackService.PlayAudio - outer exception: {ex}");
        }
        finally
        {
            // Step 6: Release playing flag and cleanup audio data
            lock (_lock)
            {
                _isPlaying = false;
                _dialogueAudio.Remove(dialogueId);
            }
        }
    }

    /// <summary>
    /// Play an in-memory audition clip from Voice Lab. It shares the normal TTS AudioSource so
    /// previews cannot overlap regular dialogue playback.
    /// </summary>
    /// <summary>
    /// Stop only audition/preview audio. Normal RimTalk dialogue playback is never interrupted by
    /// this method. Calling it while a preview is still loading also invalidates that pending load.
    /// </summary>
    public static void StopPreviewAudio()
    {
        lock (_lock)
        {
            if (!_isPreviewPlaying)
            {
                // Still bump the token so a preview that has not reached its playing state yet can
                // be invalidated by a new BIO preview request.
                _previewPlaybackVersion++;
                return;
            }

            _previewPlaybackVersion++;
            _isPreviewPlaying = false;
            _isPlaying = false;
            try
            {
                if (_audioSource != null && _audioSource.isPlaying)
                    _audioSource.Stop();
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/Preview] Failed to stop preview audio: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Play an in-memory audition clip. Pressing another preview button interrupts the current
    /// preview immediately and starts the newly selected one. Normal RimTalk dialogue keeps higher
    /// priority: if regular dialogue is playing, a preview request is skipped rather than cutting it.
    /// </summary>
    public static async void PlayPreviewAudio(byte[] audioData, float volume = 1.0f)
    {
        if (audioData == null || audioData.Length == 0) return;

        int myVersion;
        lock (_lock)
        {
            if (_isPlaying && !_isPreviewPlaying)
            {
                Log.Message("[RimTalk.TTS/Preview] Regular dialogue is playing; preview skipped.");
                return;
            }

            // Invalidate any older preview load/playback. If it is already audible, stop it now.
            _previewPlaybackVersion++;
            myVersion = _previewPlaybackVersion;
            if (_isPreviewPlaying)
            {
                try
                {
                    if (_audioSource != null && _audioSource.isPlaying)
                        _audioSource.Stop();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimTalk.TTS/Preview] Failed to interrupt old preview: {ex.Message}");
                }
            }

            _isPlaying = true;
            _isPreviewPlaying = true;
        }

        try
        {
            AudioClip clip = await LoadAudioClipFromData(audioData, "irodori_preview");
            if (clip == null || clip.length <= 0f)
            {
                Log.Warning("[RimTalk.TTS/Preview] Failed to create preview AudioClip.");
                return;
            }

            lock (_lock)
            {
                if (!_isPreviewPlaying || myVersion != _previewPlaybackVersion)
                    return; // A newer preview button was pressed while this clip was loading.
            }

            _audioSource.clip = clip;
            _audioSource.volume = UnityEngine.Mathf.Clamp01(volume);
            _audioSource.Play();

            // Poll in short intervals so a superseding preview does not have to wait for this clip's
            // original duration before the old async method exits.
            int remainingMs = Math.Max(50, (int)(clip.length * 1000f));
            while (remainingMs > 0)
            {
                int slice = Math.Min(50, remainingMs);
                await Task.Delay(slice);
                remainingMs -= slice;
                lock (_lock)
                {
                    if (!_isPreviewPlaying || myVersion != _previewPlaybackVersion)
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalk.TTS/Preview] Preview playback failed: {ex}");
        }
        finally
        {
            lock (_lock)
            {
                // Never let an older preview's finally block clear a newer preview's state.
                if (myVersion == _previewPlaybackVersion)
                {
                    _isPreviewPlaying = false;
                    _isPlaying = false;
                }
            }
        }
    }

    /// <summary>
    /// Complete reset of audio system - ONLY call when exiting/loading save game.
    /// Clears all state and resets sequences to allow fresh start.
    /// </summary>
    public static void FullReset()
    {
        lock (_lock)
        {
            // Clear all state
            _dialogueAudio.Clear();
            
            // Reset all counters and flags
            _previewPlaybackVersion++;
            _isPreviewPlaying = false;
            _isPlaying = false;
            try { if (_audioSource != null && _audioSource.isPlaying) _audioSource.Stop(); } catch { }
        }
    }

    /// <summary>
    /// Load AudioClip from audio data (MP3 or WAV)
    /// Uses temporary file approach since Unity can't load MP3 from byte array directly
    /// </summary>
    private static async Task<AudioClip> LoadAudioClipFromData(byte[] audioData, string dialogueId)
    {
        try
        {
            // Check if it's WAV or MP3 based on header
            bool isWav = audioData.Length > 12 && 
                         System.Text.Encoding.ASCII.GetString(audioData, 0, 4) == "RIFF" &&
                         System.Text.Encoding.ASCII.GetString(audioData, 8, 4) == "WAVE";

            if (isWav)
            {
                // Use existing WAV parser
                return LoadAudioClipFromWav(audioData);
            }
            else
            {
                // MP3 - use temporary file approach with UnityWebRequestMultimedia
                return await LoadAudioClipFromMP3(audioData, dialogueId);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalk.TTS] AudioPlaybackService.LoadAudioClipFromData exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Load AudioClip from MP3 byte array using temporary file
    /// </summary>
    private static async Task<AudioClip> LoadAudioClipFromMP3(byte[] mp3Data, string dialogueId)
    {
        string tempFile = null;
        try
        {
            // Write to temp file
            tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rimtalk_audio_{dialogueId}.mp3");
            await System.IO.File.WriteAllBytesAsync(tempFile, mp3Data);

            // Load using UnityWebRequestMultimedia on main thread
            AudioClip clip = null;
            await Task.Run(async () =>
            {
                using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file:///" + tempFile, UnityEngine.AudioType.MPEG))
                {
                    var operation = www.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Delay(10);
                    }

                    if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                    }
                    else
                    {
                        Log.Error($"[RimTalk.TTS] Failed to load MP3: {www.error}");
                    }
                }
            });

            return clip;
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalk.TTS] AudioPlaybackService.LoadAudioClipFromMP3 exception: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            // Clean up temp file
            try
            {
                if (tempFile != null && System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Load AudioClip from WAV byte array
    /// </summary>
    private static AudioClip LoadAudioClipFromWav(byte[] wavData)
    {
        try
        {
            // Basic header/logging to help debug malformed WAVs
            int totalLen = wavData?.Length ?? 0;

            // Parse WAV header (best-effort; some files may have extra chunks before fmt/data)
            int channels = -1;
            int sampleRate = -1;
            int bitsPerSample = -1;

            try
            {
                if (wavData.Length >= 36)
                {
                    channels = BitConverter.ToInt16(wavData, 22);
                    sampleRate = BitConverter.ToInt32(wavData, 24);
                    bitsPerSample = BitConverter.ToInt16(wavData, 34);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS] AudioPlaybackService: Failed to read basic header fields: {ex.GetType().Name}: {ex.Message}");
            }

            // Find data chunk safely
            int dataPos = 12; // start after RIFF header
            while (dataPos + 8 <= wavData.Length)
            {
                // read chunk id and size safely
                string chunkId;
                try
                {
                    chunkId = System.Text.Encoding.ASCII.GetString(wavData, dataPos, 4);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimTalk.TTS] AudioPlaybackService: Failed to read chunk id at pos {dataPos}: {ex.GetType().Name}: {ex.Message}");
                    return null;
                }

                int chunkSize = 0;
                try
                {
                    if (dataPos + 8 <= wavData.Length)
                        chunkSize = BitConverter.ToInt32(wavData, dataPos + 4);
                }
                catch (Exception ex)
                {
                    Log.Error($"[RimTalk.TTS] AudioPlaybackService: Failed to read chunk size for '{chunkId}' at pos {dataPos}: {ex.GetType().Name}: {ex.Message}");
                    return null;
                }

                if (chunkId == "fmt ")
                {
                    // attempt to parse fmt chunk for reliable format info
                    try
                    {
                        int fmtPos = dataPos + 8;
                        if (fmtPos + 16 <= wavData.Length)
                        {
                            // audio format (2 bytes), channels (2), sampleRate (4), byteRate (4), blockAlign (2), bitsPerSample (2)
                            int audioFormat = BitConverter.ToInt16(wavData, fmtPos);
                            channels = BitConverter.ToInt16(wavData, fmtPos + 2);
                            sampleRate = BitConverter.ToInt32(wavData, fmtPos + 4);
                            bitsPerSample = BitConverter.ToInt16(wavData, fmtPos + 14);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[RimTalk.TTS] AudioPlaybackService: Failed to parse fmt chunk: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (chunkId == "data")
                {
                    dataPos += 8;
                    break;
                }

                // advance to next chunk with bounds check
                long nextPos = (long)dataPos + 8 + chunkSize;
                if (nextPos <= dataPos || nextPos > wavData.Length)
                {
                    Log.Error($"[RimTalk.TTS] AudioPlaybackService: Invalid chunk size leading to overflow: chunkId='{chunkId}', chunkSize={chunkSize}, pos={dataPos}, len={wavData.Length}");
                    return null;
                }
                dataPos = (int)nextPos;
            }

            if (dataPos >= wavData.Length)
            {
                Log.Error("AudioPlaybackService: Could not find data chunk in WAV file");
                return null;
            }

            // Convert byte data to float array
            int sampleCount = (wavData.Length - dataPos) / (bitsPerSample / 8);
            float[] audioData = new float[sampleCount];

            if (bitsPerSample == 16)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = BitConverter.ToInt16(wavData, dataPos + i * 2);
                    audioData[i] = sample / 32768f;
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    audioData[i] = (wavData[dataPos + i] - 128) / 128f;
                }
            }

            // Create AudioClip
            AudioClip clip = AudioClip.Create("RimTalkTTS", sampleCount / channels, channels, sampleRate, false);
            clip.SetData(audioData, 0);

            return clip;
        }
        catch (Exception ex)
        {
            Log.Error($"[RimTalk.TTS] AudioPlaybackService.LoadAudioClipFromWav exception: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Stop playback and clear all state. Called on game exit, save load, or map change.
    /// Marks all pending dialogues as spoken to prevent them from blocking future dialogues.
    /// Preserves sequence counters to maintain ordering across cleanup events.
    /// </summary>
    public static void StopAndClear()
    {
        // Stop audio playback immediately
        if (_audioSource != null && _audioSource.isPlaying)
        {
            _audioSource.Stop();
            _audioSource.clip = null;
        }
        
        lock (_lock)
        {
            
            // Clear all state dictionaries and collections
            // Do NOT mark as AudioSpoken - let dialogues display normally without audio
            _dialogueAudio.Clear();
            
            // Reset playback state
            _isPlaying = false;
        }
    }

    /// <summary>
    /// Set playback volume (0.0 to 1.0)
    /// </summary>
    public static void SetVolume(float volume)
    {
        Initialize();
        _audioSource.volume = Mathf.Clamp01(volume);
    }

    /// <summary>
    /// Remove a dialogue's audio data from pending queue (thread-safe).
    /// Used for cleanup when dialogue is cancelled or ignored.
    /// </summary>
    public static void RemovePendingAudio(Guid dialogueId)
    {
        if (dialogueId == Guid.Empty) return;

        lock (_lock)
        {
            _dialogueAudio.Remove(dialogueId);
        }
    }

    public class FollowPawnBehaviour : MonoBehaviour
    {
        public Pawn pawn;
        public float verticalOffset = 0.0f;

        void Update()
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            // 使用 DrawPos 以获得平滑的位置（会随 pawn 移动）
            transform.position = pawn.DrawPos + Vector3.up * verticalOffset;
        }
    }
}
