using Nikse.SubtitleEdit.Core.Forms.FixCommonErrors;
using Nikse.SubtitleEdit.Features.Main;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

public static class FixDialogueDashesHelper
{
    public static List<FixDialogueDashesCandidate> Detect(IReadOnlyList<SubtitleLineViewModel> subtitles)
    {
        var result = new List<FixDialogueDashesCandidate>();
        if (subtitles == null)
        {
            return result;
        }

        for (var i = 0; i < subtitles.Count; i++)
        {
            var text = subtitles[i].Text ?? string.Empty;
            var analysis = DialogueDashFixer.Analyze(text);
            if (!analysis.Changed)
            {
                continue;
            }

            result.Add(new FixDialogueDashesCandidate
            {
                Index = i,
                Number = subtitles[i].Number,
                OriginalText = text,
                FixedText = analysis.FixedText,
                IsSelected = true,
            });
        }

        return result;
    }

    public static List<SubtitleLineViewModel> Apply(
        IReadOnlyList<SubtitleLineViewModel> subtitles,
        IReadOnlyList<FixDialogueDashesCandidate> candidates)
    {
        if (subtitles == null)
        {
            return new List<SubtitleLineViewModel>();
        }

        var fixedByIndex = new Dictionary<int, string>();
        if (candidates != null)
        {
            foreach (var c in candidates)
            {
                if (c.IsSelected)
                {
                    fixedByIndex[c.Index] = c.FixedText;
                }
            }
        }

        var result = new List<SubtitleLineViewModel>(subtitles.Count);
        for (var i = 0; i < subtitles.Count; i++)
        {
            var current = new SubtitleLineViewModel(subtitles[i]);
            if (fixedByIndex.TryGetValue(i, out var fixedText))
            {
                current.Text = fixedText;
            }

            result.Add(current);
        }

        return result;
    }
}
