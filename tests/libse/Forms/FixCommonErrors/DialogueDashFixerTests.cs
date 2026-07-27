using Nikse.SubtitleEdit.Core.Forms.FixCommonErrors;

namespace LibSETests.Forms.FixCommonErrors;

public class DialogueDashFixerTests
{
    [Fact]
    public void Analyze_PlainSingleLine_NotChanged()
    {
        var result = DialogueDashFixer.Analyze("Hello there");

        Assert.False(result.Changed);
        Assert.Equal("Hello there", result.FixedText);
    }

    [Fact]
    public void Analyze_PlainMultiLineNoDashEvidence_NotChanged()
    {
        // No line has a dash anywhere, so this is not dialogue - leave it for
        // the generic 3+ lines reflow (Fix3PlusLines), not this fixer.
        var input = "การ์เร็ต เจค็อบ ฮ็อบส์" + Environment.NewLine +
                    "ไม่ได้ฆ่าแคสซี่" + Environment.NewLine +
                    "บอยล์";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    [Fact]
    public void Analyze_OrphanDashBeforePlainLine_MergesForward()
    {
        var input = "-" + Environment.NewLine +
                    "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "- ฉันมีน้องชายน่า";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_AsymmetricMissingDash_AddsDashToBareLine()
    {
        var input = "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "- ฉันมีน้องชายน่า";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_OrphanDashAfterPlainLine_MergesBackward()
    {
        var input = "- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "ฉันมีน้องชายน่า" + Environment.NewLine +
                    "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_FourLineAlternatingOrphans_MergesBoth()
    {
        var input = "-" + Environment.NewLine +
                    "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "ฉันมีน้องชายน่า" + Environment.NewLine +
                    "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Fact]
    public void Analyze_OrphanDashSandwichedBetweenDashedLines_IsDropped()
    {
        var input = "- ว่าไง" + Environment.NewLine +
                    "-" + Environment.NewLine +
                    "- ไม่ได้มีแค่ซีเจ ลินคอล์น";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ว่าไง" + Environment.NewLine + "- ไม่ได้มีแค่ซีเจ ลินคอล์น", result.FixedText);
    }

    [Fact]
    public void Analyze_TrailingOrphanDashWithNoPlainNeighbor_IsDropped()
    {
        var input = "- นั่นคือที่อยู่ในชาเหรอคะ" + Environment.NewLine +
                    "- ใช่" + Environment.NewLine +
                    "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- นั่นคือที่อยู่ในชาเหรอคะ" + Environment.NewLine + "- ใช่", result.FixedText);
    }

    [Fact]
    public void Analyze_TwoLeadingOrphansBeforeTwoPlainLines_MergesBothViaInvariant()
    {
        var input = "-" + Environment.NewLine +
                    "-" + Environment.NewLine +
                    "พ่อแม่ของเด็กนี่อยู่ไหน" + Environment.NewLine +
                    "ฟาแยตวิล นอร์ธแคโรไลนา";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- พ่อแม่ของเด็กนี่อยู่ไหน" + Environment.NewLine + "- ฟาแยตวิล นอร์ธแคโรไลนา", result.FixedText);
    }

    [Fact]
    public void Analyze_EmptyText_ReturnsNotChanged()
    {
        var result = DialogueDashFixer.Analyze(string.Empty);

        Assert.False(result.Changed);
        Assert.Equal(string.Empty, result.FixedText);
    }
}
