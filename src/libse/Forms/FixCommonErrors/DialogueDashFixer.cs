using System;
using System.Collections.Generic;
using System.Linq;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Core.Forms.FixCommonErrors
{
    public sealed class DialogueDashFixResult
    {
        public bool Changed { get; }
        public string FixedText { get; }

        public DialogueDashFixResult(bool changed, string fixedText)
        {
            Changed = changed;
            FixedText = fixedText;
        }
    }

    /// <summary>
    /// Detects and fixes OCR mis-splits of dash-prefixed dialogue lines: orphan "-" lines
    /// separated from the text they belong to, and dialogue entries where only some lines
    /// carry the leading dash. Subtitle convention: if any line in an entry uses
    /// dash-dialogue formatting, every line in that entry must have the dash.
    /// </summary>
    public static class DialogueDashFixer
    {
        public static DialogueDashFixResult Analyze(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new DialogueDashFixResult(false, text ?? string.Empty);
            }

            var lines = text.SplitToLines();
            var original = new List<string>(lines);

            MergeOrphanDashes(lines);
            EnforceDashInvariant(lines);

            if (LinesEqual(original, lines))
            {
                return new DialogueDashFixResult(false, text);
            }

            return new DialogueDashFixResult(true, string.Join(Environment.NewLine, lines));
        }

        private static void MergeOrphanDashes(List<string> lines)
        {
            var i = 0;
            while (i < lines.Count)
            {
                if (!IsDashOnly(lines[i]))
                {
                    i++;
                    continue;
                }

                if (i + 1 < lines.Count && IsPlain(lines[i + 1]))
                {
                    lines[i + 1] = "- " + lines[i + 1].Trim();
                }
                else if (i - 1 >= 0 && IsPlain(lines[i - 1]))
                {
                    lines[i - 1] = "- " + lines[i - 1].Trim();
                }
                // else: no plain neighbor to attach to - the remaining lines are already
                // all dashed, so dropping this orphan cannot break the dash invariant.

                lines.RemoveAt(i);
            }
        }

        private static void EnforceDashInvariant(List<string> lines)
        {
            var anyDashText = lines.Any(IsDashText);
            var anyPlain = lines.Any(IsPlain);
            if (!anyDashText || !anyPlain)
            {
                return;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                if (IsPlain(lines[i]))
                {
                    lines[i] = "- " + lines[i].Trim();
                }
            }
        }

        private static bool IsDashOnly(string line) => line.Trim() == "-";

        private static bool IsDashText(string line)
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith("- ", StringComparison.Ordinal) && trimmed.Length > 2;
        }

        private static bool IsPlain(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && !IsDashOnly(line) && !IsDashText(line);
        }

        private static bool LinesEqual(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
