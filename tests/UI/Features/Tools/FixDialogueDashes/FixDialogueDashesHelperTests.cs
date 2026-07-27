using Nikse.SubtitleEdit.Features.Main;
using Nikse.SubtitleEdit.Features.Tools.FixDialogueDashes;

namespace UITests.Features.Tools.FixDialogueDashes;

public class FixDialogueDashesHelperTests
{
    private static SubtitleLineViewModel MakeSubtitle(int number, string text) =>
        new() { Number = number, Text = text };

    [Fact]
    public void Detect_AsymmetricDashEntry_ReturnsOneCandidateWithFixedText()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle(1, "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า"),
            MakeSubtitle(2, "Normal single line"),
        };

        var candidates = FixDialogueDashesHelper.Detect(subtitles);

        Assert.Single(candidates);
        Assert.Equal(1, candidates[0].Number);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", candidates[0].FixedText);
        Assert.True(candidates[0].IsSelected);
    }

    [Fact]
    public void Detect_NoIrregularEntries_ReturnsEmpty()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle(1, "- Hi" + Environment.NewLine + "- There"),
            MakeSubtitle(2, "Just one plain line"),
        };

        var candidates = FixDialogueDashesHelper.Detect(subtitles);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Apply_OnlyAppliesSelectedCandidates()
    {
        var subtitles = new List<SubtitleLineViewModel>
        {
            MakeSubtitle(1, "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า"),
            MakeSubtitle(2, "เอื้อมเด็ดยากมาก" + Environment.NewLine + "- แม่ผมก็เหมือนกัน"),
        };

        var candidates = FixDialogueDashesHelper.Detect(subtitles);
        Assert.Equal(2, candidates.Count);
        candidates[1].IsSelected = false; // deselect the second candidate

        var result = FixDialogueDashesHelper.Apply(subtitles, candidates);

        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result[0].Text);
        Assert.Equal("เอื้อมเด็ดยากมาก" + Environment.NewLine + "- แม่ผมก็เหมือนกัน", result[1].Text); // unchanged
    }
}
