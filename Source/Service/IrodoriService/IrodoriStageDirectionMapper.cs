using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RimTalk.TTS.Service.IrodoriService
{
    /// <summary>
    /// Compatibility mapper for older prose stage directions plus shared helpers for Irodori
    /// inline emoji acting controls. New Fast Path prompts ask the LLM to emit a conservative set
    /// of localized Irodori emojis directly; prose -> emoji conversion remains only as a fallback.
    /// </summary>
    public static class IrodoriStageDirectionMapper
    {
        private sealed class CueRule
        {
            public readonly string Emoji;
            public readonly string[] Terms;

            public CueRule(string emoji, params string[] terms)
            {
                Emoji = emoji;
                Terms = terms;
            }
        }

        // Direct Fast Path output is intentionally conservative. These are useful as localized
        // audible controls inside a line; whole-line mood/emotion stays in the RTTTS caption.
        private static readonly string[] DirectControlEmojis =
        {
            "👂",   // whisper / close to ear
            "😮‍💨", // breath / sigh
            "⏸️",   // pause / silence
            "🤭",   // chuckle / giggle
            "🥵",   // panting / moan / groan
            "😏",   // teasing / coaxing
            "🥺",   // trembling / timid
            "🌬️",  // shortness of breath / heavy breathing
            "😮",   // gasp
            "🤧",   // cough / sneeze / sniffle
            "😭",   // crying / sobbing
            "👅",   // licking / chewing / wet mouth sounds
            "💋",   // lip noise / lip smack
            "🤐"    // muffled / covered-mouth voice
        };

        // Legacy prose conversion rules. Kept broader than DirectControlEmojis so old/model-deviant
        // stage directions can still be handled without being spoken literally.
        private static readonly CueRule[] Rules =
        {
            new CueRule("😮", "息をのむ", "息を呑む", "息を飲む", "息をのみ", "息を呑み", "gasp"),
            new CueRule("🌬️", "息切れ", "息を切ら", "荒い息", "荒く息", "呼吸を乱", "呼吸が乱", "heavy breathing", "out of breath", "breathless"),
            new CueRule("😮‍💨", "ため息", "溜め息", "溜息", "吐息", "寝息", "sigh", "exhale"),
            new CueRule("🤧", "咳払い", "せき払い", "咳き込", "せき込", "咳を", "せきを", "くしゃみ", "鼻をすす", "cough", "sneeze", "sniffle", "clear throat", "clears throat"),
            new CueRule("😭", "すすり泣", "嗚咽", "泣き声", "泣く", "泣いて", "泣きながら", "sobb", "crying", "cries"),
            new CueRule("🤭", "くすくす", "クスクス", "含み笑", "忍び笑", "笑い声", "笑う", "笑って", "笑いながら", "chuckle", "giggle", "laughs", "laughing"),
            new CueRule("👅", "舐める", "舐めて", "舐めながら", "なめる", "なめて", "舌で舐め", "咀嚼音", "咀嚼", "もぐもぐ", "水音", "licking", "licks", "chewing", "wet mouth sound"),
            new CueRule("💋", "リップノイズ", "唇を鳴ら", "唇をなら", "口を鳴ら", "口をなら", "lip smack", "lip-smack", "lip noise", "smacks lips"),
            new CueRule("🤐", "口を塞", "口をふさ", "口を覆", "口をおお", "くぐもった声", "こもった声", "声がこも", "muffled", "covered mouth", "mouth covered"),
            new CueRule("😱", "悲鳴", "絶叫", "叫び声", "叫ぶ", "叫ん", "scream", "shriek", "shouts", "shouting"),
            new CueRule("🥱", "あくび", "欠伸", "yawn"),
            new CueRule("😒", "舌打ち", "舌を鳴ら", "tut", "clicks tongue", "clicking tongue"),
            new CueRule("🥵", "うめき声", "呻き声", "唸り声", "うめく", "呻く", "唸る", "moan", "groan"),
            new CueRule("🎵", "鼻歌", "ハミング", "humming", "hums"),
            new CueRule("⏸️", "一拍置", "一拍お", "間を置", "間をお", "少し間", "しばし沈黙", "沈黙", "pause", "silence"),
            new CueRule("👂", "囁", "ささや", "小声で", "耳元で", "whisper"),
            new CueRule("⏩", "早口", "まくした", "捲し立", "急いで話", "rapid-fire", "rapidly", "speaks quickly", "speaking quickly"),
            new CueRule("🐢", "ゆっくり", "ゆるやかに話", "slowly", "speaks slowly", "speaking slowly"),
            new CueRule("😰", "慌て", "動揺", "緊張", "どもり", "どもって", "panicked", "agitated", "nervous", "stutter"),
            new CueRule("🥺", "声を震", "震える声", "自信なさげ", "おずおず", "timid", "trembling voice", "uncertainly"),
            new CueRule("🫣", "照れ", "恥ずかしそう", "恥じら", "bashful", "shyly"),
            new CueRule("🙄", "呆れ", "あきれ", "うんざり", "exasperat"),
            new CueRule("😏", "からか", "茶化", "甘えるよう", "teasing", "playfully", "coaxing"),
            new CueRule("🫶", "優しく", "やさしく", "慈しむ", "gentle", "tender"),
            new CueRule("😪", "眠そう", "眠たそう", "気だる", "sleepily", "languid"),
            new CueRule("😠", "怒り", "怒って", "不満げ", "苛立", "いら立", "angry", "irritat", "displeased"),
            new CueRule("😲", "驚いて", "驚き", "びっくり", "感嘆", "surpris", "astonish", "in awe"),
            new CueRule("😖", "苦しげ", "苦しそう", "痛が", "painfully", "agoniz"),
            new CueRule("😟", "心配そう", "不安そう", "心配げ", "worried", "anxious"),
            new CueRule("😆", "大喜び", "歓喜", "喜びながら", "joyfully"),
            new CueRule("😊", "嬉しそう", "楽しげ", "楽しそう", "明るく笑", "cheerfully", "gladly", "happily"),
            new CueRule("😎", "得意げ", "自信ありげ", "誇らしげ", "confidently", "proudly"),
            new CueRule("🙏", "懇願", "すがるよう", "begging", "pleading"),
            new CueRule("🥴", "酔っ払", "酔って", "酔いながら", "drunken"),
            new CueRule("😌", "安堵", "ほっと", "満足げ", "relieved", "contentedly"),
            new CueRule("🤔", "疑問の声", "首をかしげ", "考え込みながら", "questioning", "wondering"),
            new CueRule("💪", "力を込め", "力強く", "踏ん張", "with effort", "strongly"),
            new CueRule("💥", "勢いよく", "勢いに任せ", "吐き捨てるよう", "forcefully", "with force"),
            new CueRule("📖", "独白", "モノローグ", "ナレーション", "monologue", "narration")
        };

        private static readonly Regex StageDirectionRegex = new Regex(
            @"\((?<stage>[^()]*)\)|（(?<stage>[^（）]*)）|\[(?<stage>[^\[\]]*)\]|【(?<stage>[^【】]*)】|\*(?<stage>[^*]+)\*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Legacy fallback: convert recognizable prose stage directions to Irodori emojis.
        /// New Fast Path output should already contain the emoji directly.
        /// </summary>
        public static string Transform(string text, bool stripUnmapped, out int convertedCount)
        {
            convertedCount = 0;
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

            int count = 0;
            string result = StageDirectionRegex.Replace(text, match =>
            {
                string stage = match.Groups["stage"].Value;
                string emoji = MapStageDirection(stage);
                if (!string.IsNullOrEmpty(emoji))
                {
                    count++;
                    return emoji;
                }
                return stripUnmapped ? " " : match.Value;
            });

            convertedCount = count;
            result = result.Normalize(NormalizationForm.FormKC);
            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result;
        }

        /// <summary>
        /// Remove only the conservative direct-control set from RimTalk display/history. This avoids
        /// treating ordinary character emojis as hidden machine controls.
        /// </summary>
        public static string StripControlEmojisForDisplay(string text, out int strippedCount)
        {
            strippedCount = 0;
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

            string result = text;
            foreach (string emoji in DirectControlEmojis)
            {
                int searchFrom = 0;
                while (searchFrom < result.Length)
                {
                    int index = result.IndexOf(emoji, searchFrom, StringComparison.Ordinal);
                    if (index < 0) break;

                    result = result.Remove(index, emoji.Length);
                    strippedCount++;
                    searchFrom = index;
                }
            }

            result = Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
            return result;
        }

        /// <summary>
        /// Remove only legacy stage directions that are understood as acting cues. Unknown
        /// parentheticals/brackets remain visible so ordinary dialogue content is not lost.
        /// </summary>
        public static string StripRecognizedForDisplay(string text, out int strippedCount)
        {
            strippedCount = 0;
            if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

            int count = 0;
            string result = StageDirectionRegex.Replace(text, match =>
            {
                string stage = match.Groups["stage"].Value;
                if (string.IsNullOrEmpty(MapStageDirection(stage)))
                    return match.Value;

                count++;
                return string.Empty;
            });

            strippedCount = count;
            result = Regex.Replace(result, @"[ \t]{2,}", " ").Trim();
            return result;
        }

        public static string StripActingControlsForDisplay(string text, out int strippedCount)
        {
            string result = StripControlEmojisForDisplay(text, out int emojiCount);
            result = StripRecognizedForDisplay(result, out int stageCount);
            strippedCount = emojiCount + stageCount;
            return result;
        }

        public static string MapStageDirection(string stageDirection)
        {
            if (string.IsNullOrWhiteSpace(stageDirection)) return string.Empty;

            string stage = stageDirection.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
            if (stage.Length > 64) return string.Empty;

            var emojis = new List<string>(2);
            foreach (CueRule rule in Rules)
            {
                if (!ContainsAny(stage, rule.Terms)) continue;
                if (!emojis.Contains(rule.Emoji)) emojis.Add(rule.Emoji);
                if (emojis.Count >= 2) break;
            }

            return emojis.Count == 0 ? string.Empty : string.Concat(emojis);
        }

        private static bool ContainsAny(string value, IEnumerable<string> terms)
        {
            foreach (string term in terms)
            {
                if (string.IsNullOrEmpty(term)) continue;
                if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
