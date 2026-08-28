using System;
using System.Collections.Generic;

namespace RimTalk.TTS.Service.IrodoriService
{
    public static class IrodoriEmotionMapper
    {
        // Emoji-based style control is optional. Caption remains the primary bridge because it can preserve nuance/intensity.
        private static readonly Dictionary<string, string> EmojiMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["happy"] = "😊", ["delighted"] = "😊", ["grateful"] = "😊", ["satisfied"] = "😊",
            ["sad"] = "😢", ["depressed"] = "😢", ["lonely"] = "😢", ["regretful"] = "😢",
            ["angry"] = "😠", ["frustrated"] = "😠", ["upset"] = "😠", ["disdainful"] = "😒",
            ["excited"] = "🤩", ["surprised"] = "😲", ["moved"] = "🥹",
            ["calm"] = "😌", ["relaxed"] = "😌", ["confident"] = "😌",
            ["fearful"] = "😨", ["scared"] = "😨", ["nervous"] = "😰", ["anxious"] = "😰", ["worried"] = "😟",
            ["embarrassed"] = "😳", ["ashamed"] = "😳", ["guilty"] = "😔",
            ["disgusted"] = "🤢", ["confused"] = "😕", ["uncertain"] = "😕", ["doubtful"] = "🤔",
            ["curious"] = "🤔", ["bored"] = "😑", ["indifferent"] = "😐", ["sarcastic"] = "🙃",
            ["hopeful"] = "🙂", ["optimistic"] = "🙂", ["pessimistic"] = "😞", ["nostalgic"] = "🥲",
            ["jealous"] = "😒", ["envious"] = "😒", ["proud"] = "😌", ["hysterical"] = "😵",

            // Common Japanese words so nuanced unified captions can still opt into emoji style control.
            ["嬉しい"] = "😊", ["喜び"] = "😊", ["明るく"] = "😊", ["幸せ"] = "😊",
            ["悲しい"] = "😢", ["悲しみ"] = "😢", ["泣き"] = "😢", ["寂しい"] = "😢",
            ["怒り"] = "😠", ["怒って"] = "😠", ["苛立"] = "😠", ["憤り"] = "😠",
            ["興奮"] = "🤩", ["高揚"] = "🤩", ["驚き"] = "😲", ["驚いて"] = "😲",
            ["穏やか"] = "😌", ["落ち着"] = "😌", ["安心"] = "😌", ["安堵"] = "😌",
            ["怖"] = "😨", ["恐れ"] = "😨", ["怯え"] = "😨", ["不安"] = "😰", ["心配"] = "😟",
            ["照れ"] = "😳", ["恥ずか"] = "😳", ["困惑"] = "😕", ["戸惑"] = "😕",
            ["好奇心"] = "🤔", ["退屈"] = "😑", ["皮肉"] = "🙃", ["希望"] = "🙂",
            ["誇ら"] = "😌", ["嫉妬"] = "😒", ["嫌悪"] = "🤢"
        };

        public static string ToEmoji(string emotion)
        {
            if (string.IsNullOrWhiteSpace(emotion)) return "";
            string normalized = emotion.Trim().Trim('[', ']', '(', ')').ToLowerInvariant();
            if (EmojiMap.TryGetValue(normalized, out var exact)) return exact;

            foreach (var pair in EmojiMap)
            {
                if (normalized.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return pair.Value;
            }
            return "";
        }

        public static string BuildCaption(string prefix, string voiceCaption, string emotion)
        {
            var parts = new List<string>();
            Add(parts, prefix);
            Add(parts, voiceCaption);
            Add(parts, emotion);
            return string.Join("; ", parts);
        }

        private static void Add(List<string> parts, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            value = value.Trim();
            if (!parts.Exists(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                parts.Add(value);
        }
    }
}
