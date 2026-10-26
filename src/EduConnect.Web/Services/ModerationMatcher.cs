using System.Text;
using System.Text.RegularExpressions;

namespace EduConnect.Web.Services
{
    // Finds and stars out listed words in Group Finder text. Matching is
    // whole-word only and sees through the usual dodges: c@ps and l33t
    // swaps, a * standing in for a letter, stretched letters, letters
    // spaced or dotted apart, and multi-word phrases. Allowed words are
    // never touched. Pure: the word lists come from ContentModerationService.
    public sealed class ModerationMatcher
    {
        public static readonly ModerationMatcher Empty =
            new(Array.Empty<string>(), Array.Empty<string>());

        // Characters that can sit inside a word: letters, digits and the
        // symbols people swap in for letters.
        private const string SymbolChars = "@$!|*";
        // Symbols that are ordinary punctuation at a word's edge ("hi!").
        private const string EdgeTrimChars = "!|*";
        // What may sit between spaced-out single letters ("z.o.r.b").
        private const string SpacerChars = " .-_";

        private readonly HashSet<string> _words = new();
        private readonly Dictionary<string, List<string>> _wordsByCollapsed = new();
        private readonly List<string[]> _phrases = new();
        private readonly HashSet<string> _allowed = new();

        public ModerationMatcher(IEnumerable<string> prohibited, IEnumerable<string> allowed)
        {
            foreach (var entry in prohibited.Select(NormalizeEntry).Where(e => e.Length > 0).Distinct())
            {
                if (entry.Contains(' '))
                {
                    _phrases.Add(entry.Split(' '));
                    continue;
                }
                _words.Add(entry);
                var key = Collapse(entry);
                if (!_wordsByCollapsed.TryGetValue(key, out var list))
                    _wordsByCollapsed[key] = list = new List<string>();
                list.Add(entry);
            }
            // Longest phrase first, so "a b c" wins over "a b".
            _phrases.Sort((a, b) => b.Length.CompareTo(a.Length));

            foreach (var entry in allowed.Select(NormalizeEntry).Where(e => e.Length > 0))
                _allowed.Add(entry);
        }

        // How a list entry is stored: trimmed, lowercase, single spaces.
        public static string NormalizeEntry(string word) =>
            Regex.Replace((word ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

        public bool ContainsProhibited(string? text) =>
            !string.IsNullOrWhiteSpace(text) && FindSpans(text).Count > 0;

        // Each match becomes '*' of the same length; the rest is untouched.
        public string Mask(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var spans = FindSpans(text);
            if (spans.Count == 0) return text;

            var chars = text.ToCharArray();
            foreach (var (start, length) in spans)
                for (var i = start; i < start + length; i++)
                    chars[i] = '*';
            return new string(chars);
        }

        private readonly record struct Token(int Start, int Length, string Raw)
        {
            public int End => Start + Length;
        }

        private List<(int Start, int Length)> FindSpans(string text)
        {
            var spans = new List<(int, int)>();
            if (_words.Count == 0 && _phrases.Count == 0) return spans;

            var tokens = Tokenize(text);
            var covered = new bool[tokens.Count];

            // 1. Phrases.
            for (var i = 0; i < tokens.Count; i++)
            {
                if (covered[i]) continue;
                foreach (var phrase in _phrases)
                {
                    if (i + phrase.Length > tokens.Count) continue;
                    var all = true;
                    for (var p = 0; p < phrase.Length && all; p++)
                        all = !covered[i + p] && !IsAllowed(tokens[i + p].Raw)
                            && Variants(tokens[i + p].Raw).Any(v => MatchesWord(v, phrase[p]));
                    if (!all) continue;

                    var last = tokens[i + phrase.Length - 1];
                    spans.Add((tokens[i].Start, last.End - tokens[i].Start));
                    for (var p = 0; p < phrase.Length; p++) covered[i + p] = true;
                    break;
                }
            }

            // 2. Single letters spaced or dotted apart.
            var run = new List<int>();
            for (var i = 0; i <= tokens.Count; i++)
            {
                var joins = i < tokens.Count && !covered[i] && tokens[i].Length == 1
                    && (run.Count == 0 || IsSpacerGap(text, tokens[run[^1]].End, tokens[i].Start));
                if (joins)
                {
                    run.Add(i);
                    continue;
                }
                MatchLetterRun(run, tokens, covered, spans);
                run.Clear();
                if (i < tokens.Count && !covered[i] && tokens[i].Length == 1) run.Add(i);
            }

            // 3. Whole tokens.
            for (var i = 0; i < tokens.Count; i++)
            {
                if (covered[i] || IsAllowed(tokens[i].Raw)) continue;
                if (Variants(tokens[i].Raw).Any(MatchesAnyWord))
                {
                    spans.Add((tokens[i].Start, tokens[i].Length));
                    covered[i] = true;
                }
            }
            return spans;
        }

        // Tries every stretch of a spaced-letter run, longest first.
        private void MatchLetterRun(List<int> run, List<Token> tokens, bool[] covered,
            List<(int, int)> spans)
        {
            var at = 0;
            while (at < run.Count - 1)
            {
                var matched = false;
                for (var len = run.Count - at; len >= 2 && !matched; len--)
                {
                    var joined = string.Concat(run.Skip(at).Take(len).Select(t => tokens[t].Raw));
                    if (IsAllowed(joined) || !Variants(joined).Any(MatchesAnyWord)) continue;

                    var first = tokens[run[at]];
                    var last = tokens[run[at + len - 1]];
                    spans.Add((first.Start, last.End - first.Start));
                    for (var k = at; k < at + len; k++) covered[run[k]] = true;
                    at += len;
                    matched = true;
                }
                if (!matched) at++;
            }
        }

        private static bool IsSpacerGap(string text, int from, int to)
        {
            if (to <= from) return false;
            for (var i = from; i < to; i++)
                if (!SpacerChars.Contains(text[i])) return false;
            return true;
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < text.Length)
            {
                if (!IsWordChar(text[i])) { i++; continue; }
                var start = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                var end = i;

                while (start < end && EdgeTrimChars.Contains(text[start])) start++;
                while (end > start && EdgeTrimChars.Contains(text[end - 1])) end--;
                if (end > start)
                    tokens.Add(new Token(start, end - start, text[start..end]));
            }
            return tokens;
        }

        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || SymbolChars.Contains(c);

        // The token as letters. A 1, | or ! may stand for an i or an l, so
        // both readings are returned. Digit-only tokens stay digits.
        private static IEnumerable<string> Variants(string raw)
        {
            var lower = raw.ToLowerInvariant();
            if (lower.All(char.IsDigit))
            {
                yield return lower;
                yield break;
            }

            var asI = new StringBuilder(lower.Length);
            var asL = new StringBuilder(lower.Length);
            var ambiguous = false;
            foreach (var c in lower)
            {
                switch (c)
                {
                    case '4': case '@': asI.Append('a'); asL.Append('a'); break;
                    case '3': asI.Append('e'); asL.Append('e'); break;
                    case '0': asI.Append('o'); asL.Append('o'); break;
                    case '$': case '5': asI.Append('s'); asL.Append('s'); break;
                    case '7': asI.Append('t'); asL.Append('t'); break;
                    case '1': case '|': case '!':
                        asI.Append('i'); asL.Append('l'); ambiguous = true; break;
                    default: asI.Append(c); asL.Append(c); break;
                }
            }
            yield return asI.ToString();
            if (ambiguous) yield return asL.ToString();
        }

        private bool IsAllowed(string raw) =>
            _allowed.Count > 0 && (_allowed.Contains(raw.ToLowerInvariant())
                || Variants(raw).Any(_allowed.Contains));

        private bool MatchesAnyWord(string norm)
        {
            if (_words.Contains(norm)) return true;
            if (norm.Contains('*') && _words.Any(w => MatchesWildcard(norm, w))) return true;
            // Stretched letters: never let squashing make a token shorter
            // than the word, or "zab" would match "zabb".
            return _wordsByCollapsed.TryGetValue(Collapse(norm), out var words)
                && words.Any(w => norm.Length >= w.Length);
        }

        private static bool MatchesWord(string norm, string word) =>
            norm == word
            || MatchesWildcard(norm, word)
            || (Collapse(norm) == Collapse(word) && norm.Length >= word.Length);

        private static bool MatchesWildcard(string norm, string word)
        {
            if (norm.Length != word.Length || !norm.Contains('*')) return false;
            for (var i = 0; i < norm.Length; i++)
                if (norm[i] != '*' && norm[i] != word[i]) return false;
            return true;
        }

        // "zooorbaak" -> "zorbak".
        private static string Collapse(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
            return sb.ToString();
        }
    }
}
