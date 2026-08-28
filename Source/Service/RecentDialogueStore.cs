using System;
using System.Collections.Generic;
using System.Linq;
using RimTalk.Data;
using RimWorld;
using Verse;

namespace RimTalk.TTS.Service
{
    /// <summary>
    /// Small in-memory cache of recent RimTalk lines per pawn for the Irodori Voice Lab.
    /// This is intentionally session-local; when the cache is sparse we also fall back to
    /// RimTalk's own simplified conversation history.
    /// </summary>
    public static class RecentDialogueStore
    {
        public sealed class Entry
        {
            public Guid DialogueId;
            public string Text;
            public string Emotion;
            public int Tick;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, LinkedList<Entry>> ByPawn = new Dictionary<int, LinkedList<Entry>>();
        private const int MaxPerPawn = 24;

        public static void Capture(Pawn pawn, TalkResponse response)
        {
            if (pawn == null || response == null || string.IsNullOrWhiteSpace(response.Text)) return;

            string emotion = string.Empty;
            try
            {
                if (UnifiedTtsPayloadStore.TryPeek(response.Id, out var payload) && payload != null)
                    emotion = payload.Emotion ?? string.Empty;
            }
            catch { }

            var entry = new Entry
            {
                DialogueId = response.Id,
                Text = response.Text.Trim(),
                Emotion = emotion.Trim(),
                Tick = GenTicks.TicksGame
            };

            lock (Sync)
            {
                if (!ByPawn.TryGetValue(pawn.thingIDNumber, out var list))
                {
                    list = new LinkedList<Entry>();
                    ByPawn[pawn.thingIDNumber] = list;
                }

                // Avoid filling the picker with duplicate repeated lines.
                var node = list.First;
                while (node != null)
                {
                    var next = node.Next;
                    if (string.Equals(node.Value?.Text, entry.Text, StringComparison.Ordinal))
                        list.Remove(node);
                    node = next;
                }

                list.AddFirst(entry);
                while (list.Count > MaxPerPawn)
                    list.RemoveLast();
            }
        }

        public static List<Entry> GetRecent(Pawn pawn, int max = 12)
        {
            var result = new List<Entry>();
            if (pawn == null || max <= 0) return result;

            lock (Sync)
            {
                if (ByPawn.TryGetValue(pawn.thingIDNumber, out var list))
                {
                    foreach (var entry in list)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Text)) continue;
                        result.Add(Clone(entry));
                        if (result.Count >= max) return result;
                    }
                }
            }

            // Fallback: RimTalk's own conversation history is useful immediately after opening a save,
            // before this session-local cache has had time to observe many new lines.
            try
            {
                var history = TalkHistory.GetMessageHistory(pawn, simplified: true);
                if (history != null)
                {
                    string prefix = (pawn.LabelShort ?? string.Empty) + ":";
                    for (int i = history.Count - 1; i >= 0 && result.Count < max; i--)
                    {
                        var item = history[i];
                        if (item.role != Role.AI || string.IsNullOrWhiteSpace(item.message)) continue;

                        var lines = item.message.Replace("\r", "").Split('\n');
                        for (int j = lines.Length - 1; j >= 0 && result.Count < max; j--)
                        {
                            string line = lines[j]?.Trim();
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            string text = line;
                            int colon = line.IndexOf(':');
                            if (colon > 0)
                            {
                                // Simplified multi-speaker history uses "Name: text". Prefer this pawn's lines.
                                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                                text = line.Substring(prefix.Length).Trim();
                            }

                            if (string.IsNullOrWhiteSpace(text) || result.Any(x => x.Text == text)) continue;
                            result.Add(new Entry { DialogueId = Guid.Empty, Text = text, Emotion = string.Empty, Tick = -1 });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimTalk.TTS/VoiceLab] Failed to read RimTalk history: {ex.Message}");
            }

            return result.Take(max).ToList();
        }

        public static void Clear()
        {
            lock (Sync) ByPawn.Clear();
        }

        private static Entry Clone(Entry src)
        {
            return new Entry
            {
                DialogueId = src.DialogueId,
                Text = src.Text,
                Emotion = src.Emotion,
                Tick = src.Tick
            };
        }
    }
}
