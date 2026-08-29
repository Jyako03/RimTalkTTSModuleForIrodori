using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RimTalk.TTS.Service.IrodoriService
{
    /// <summary>
    /// Converts short stage directions embedded in RimTalk dialogue into Irodori-TTS v4/v4.1
    /// emoji annotations. The emoji set is intentionally limited to annotations documented by
    /// Irodori-TTS. Unknown stage directions can be stripped so they are never spoken literally.
    ///
    /// Example:
    ///   （小さくため息をつく）まったく……（くすくす笑う）またなの？
    /// becomes:
    ///   😮‍💨まったく……🤭またなの？
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

        // Sound-producing / physiological cues come first so a direction such as
        // "驚いて息をのむ" can retain the actual gasp before a broader emotion cue.
        // At most two distinct annotations are inserted for one stage direction to avoid
        // over-conditioning the model.
        private static readonly CueRule[] Rules =
        {
            new CueRule("😮", "息をのむ", "息を呑む", "息を飲む", "息をのみ", "息を呑み", "gasp"),
            new CueRule("🌬️", "息切れ", "息を切ら", "荒い息", "荒く息", "呼吸を乱", "呼吸が乱", "heavy breathing", "out of breath", "breathless"),
            new CueRule("😮‍💨", "ため息", "溜め息", "溜息", "吐息", "寝息", "sigh", "exhale"),
            new CueRule("🤧", "咳払い", "せき払い", "咳き込", "せき込", "咳を", "せきを", "くしゃみ", "鼻をすす", "cough", "sneeze", "sniffle", "clear throat", "clears throat"),
            new CueRule("😭", "すすり泣", "嗚咽", "泣き声", "泣く", "泣いて", "泣きながら", "sobb", "crying", "cries"),
            new CueRule("🤭", "くすくす", "クスクス", "含み笑", "忍び笑", "笑い声", "笑う", "笑って", "笑いながら", "chuckle", "giggle", "laughs", "laughing"),
            new CueRule("😱", "悲鳴", "絶叫", "叫び声", "叫ぶ", "叫ん", "scream", "shriek", "shouts", "shouting"),
            new CueRule("🥱", "あくび", "欠伸", "yawn"),
            new CueRule("😒", "舌打ち", "舌を鳴ら", "tut", "clicks tongue", "clicking tongue"),
            new CueRule("🥵", "うめき声", "呻き声", "唸り声", "うめく", "呻く", "唸る", "moan", "groan"),
            new CueRule("🎵", "鼻歌", "ハミング", "humming", "hums"),
            new CueRule("⏸️", "一拍置", "一拍お", "間を置", "間をお", "少し間", "しばし沈黙", "沈黙", "pause", "silence"),

            // Delivery/style cues.
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

        public static string MapStageDirection(string stageDirection)
        {
            if (string.IsNullOrWhiteSpace(stageDirection)) return string.Empty;

            string stage = stageDirection.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();

            // Long parentheticals are much more likely to be actual prose/asides than a compact
            // performance direction. Refuse to infer an acting cue from them.
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
