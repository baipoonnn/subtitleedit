using System.Text;

namespace Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

/// <summary>
/// Dictionary maximal matching constrained by Thai Character Cluster heuristics —
/// same algorithm family as PyThaiNLP newmm / nlpo3, implemented in managed C#.
/// </summary>
public sealed class NewMmThaiTokenizer : IThaiTokenizer
{
    private readonly TrieNode _root = new();

    public NewMmThaiTokenizer(IEnumerable<string> words)
    {
        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(word) || !ThaiScript.ContainsThai(word))
            {
                continue;
            }

            AddWord(word);
        }
    }

    public static NewMmThaiTokenizer? TryLoadFromWordsFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var words = File.ReadLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'));
        return new NewMmThaiTokenizer(words);
    }

    public bool IsReady => true;

    public IReadOnlyList<ThaiTokenSpan> Segment(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<ThaiTokenSpan>();
        }

        // Fast path: no Thai → single span
        if (!ThaiScript.ContainsThai(text))
        {
            return new[] { new ThaiTokenSpan(0, text) };
        }

        var result = new List<ThaiTokenSpan>();
        var i = 0;
        while (i < text.Length)
        {
            if (!ThaiScript.IsThaiChar(text[i]))
            {
                var start = i;
                while (i < text.Length && !ThaiScript.IsThaiChar(text[i]))
                {
                    i++;
                }

                result.Add(new ThaiTokenSpan(start, text.Substring(start, i - start)));
                continue;
            }

            var thaiStart = i;
            while (i < text.Length && ThaiScript.IsThaiChar(text[i]))
            {
                i++;
            }

            SegmentThaiRun(text, thaiStart, i, result);
        }

        return result;
    }

    private void SegmentThaiRun(string text, int start, int end, List<ThaiTokenSpan> result)
    {
        var pos = start;
        while (pos < end)
        {
            var bestLen = 1;
            var node = _root;
            var len = 0;
            for (var j = pos; j < end; j++)
            {
                if (!node.Children.TryGetValue(text[j], out node!))
                {
                    break;
                }

                len++;
                if (node.IsWord)
                {
                    bestLen = len;
                }
            }

            result.Add(new ThaiTokenSpan(pos, text.Substring(pos, bestLen)));
            pos += bestLen;
        }
    }

    private void AddWord(string word)
    {
        var node = _root;
        foreach (var c in word)
        {
            if (!node.Children.TryGetValue(c, out var next))
            {
                next = new TrieNode();
                node.Children[c] = next;
            }

            node = next;
        }

        node.IsWord = true;
    }

    private sealed class TrieNode
    {
        public Dictionary<char, TrieNode> Children { get; } = new();
        public bool IsWord { get; set; }
    }
}
