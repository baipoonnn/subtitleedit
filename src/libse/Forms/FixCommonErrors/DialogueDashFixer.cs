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

            // Conservative scope limit: the classification below looks at raw line text,
            // so a dash hidden behind a formatting tag (e.g. "<i>- Hello" or "{\an8}- Hello")
            // would be misclassified as plain and get a second dash prepended, corrupting
            // already-correct formatted dialogue. Properly parsing tags is out of scope for
            // this pass, so just bail out whenever any line might contain one - a false
            // positive here only means "skip this paragraph," which is always safe.
            if (lines.Any(HasTag))
            {
                return new DialogueDashFixResult(false, text);
            }

            var original = new List<string>(lines);

            MergeOrphanDashes(lines);
            EnforceDashInvariant(lines);

            if (LinesEqual(original, lines))
            {
                return new DialogueDashFixResult(false, text);
            }

            var fixedText = string.Join(Environment.NewLine, lines);
            if (string.IsNullOrWhiteSpace(fixedText))
            {
                // All lines collapsed to nothing (e.g. the whole "paragraph" was orphan
                // dashes with no plain text to attach to) - that's not a confident fix,
                // it's data loss. Leave the original text untouched.
                return new DialogueDashFixResult(false, text);
            }

            return new DialogueDashFixResult(true, fixedText);
        }

        private static bool HasTag(string line) => line.Contains('<') || line.Contains('{');

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
            // Any line that starts with "-" at all (dash-only, "- text", or the
            // unsupported no-space "-text" style) is never plain: we don't want to
            // prepend a second dash onto something that already looks dash-prefixed,
            // even if we don't (yet) normalize its exact style.
            var trimmed = line.Trim();
            return trimmed.Length > 0 && !trimmed.StartsWith("-", StringComparison.Ordinal);
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
