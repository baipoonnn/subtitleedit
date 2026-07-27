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

    [Fact]
    public void Analyze_HtmlTagHidesDash_LeftUnchanged()
    {
        // "<i>- Hello" is already correctly dashed once you look past the tag, but the
        // raw-line classifier can't see past "<i>" - it must bail out rather than
        // prepend a second dash and corrupt the formatting.
        var input = "<i>- Hello" + Environment.NewLine + "- Goodbye</i>";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    [Fact]
    public void Analyze_AssOverrideTagHidesDash_LeftUnchanged()
    {
        var input = "{\\an8}- Hello" + Environment.NewLine + "- Goodbye";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    [Fact]
    public void Analyze_ItalicTagOnOneLineOnly_LeftUnchanged()
    {
        var input = "- What?" + Environment.NewLine + "<i>- I said no.</i>";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    [Fact]
    public void Analyze_NoSpaceDashStyle_NotCorrupted()
    {
        // "-Hi" uses a no-space dash style that this fixer doesn't (yet) normalize -
        // but it must never get a second dash prepended ("- -Hi").
        var input = "-Hi" + Environment.NewLine + "- There";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    [Fact]
    public void Analyze_SingleOrphanDash_NoConfidentFix_ReturnsUnchanged()
    {
        var result = DialogueDashFixer.Analyze("-");

        Assert.False(result.Changed);
        Assert.Equal("-", result.FixedText);
    }

    [Fact]
    public void Analyze_AllOrphanDashes_NoConfidentFix_ReturnsUnchanged()
    {
        var input = "-" + Environment.NewLine + "-";

        var result = DialogueDashFixer.Analyze(input);

        Assert.False(result.Changed);
        Assert.Equal(input, result.FixedText);
    }

    // Google Lens OCR often emits Unicode dashes (en dash U+2013, em dash U+2014,
    // hyphen U+2010) instead of ASCII hyphen-minus for dialogue markers.
    [Theory]
    [InlineData('\u2013')] // en dash –
    [InlineData('\u2014')] // em dash —
    [InlineData('\u2010')] // hyphen ‐
    public void Analyze_AsymmetricUnicodeDash_AddsAsciiDashAndNormalizes(char unicodeDash)
    {
        var input = "เอื้อมเด็ดยากมาก" + Environment.NewLine +
                    unicodeDash + " แม่ผมก็เหมือนกัน";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- เอื้อมเด็ดยากมาก" + Environment.NewLine + "- แม่ผมก็เหมือนกัน", result.FixedText);
    }

    [Theory]
    [InlineData('\u2013')]
    [InlineData('\u2014')]
    [InlineData('\u2010')]
    public void Analyze_OrphanUnicodeDashBeforePlainLine_MergesForward(char unicodeDash)
    {
        var input = unicodeDash.ToString() + Environment.NewLine +
                    "ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine +
                    "- ฉันมีน้องชายน่า";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- ผมอยากหาอะไรมาปิดตัว" + Environment.NewLine + "- ฉันมีน้องชายน่า", result.FixedText);
    }

    [Theory]
    [InlineData('\u2013')]
    [InlineData('\u2014')]
    public void Analyze_SymmetricUnicodeDashes_NormalizesToAscii(char unicodeDash)
    {
        var input = unicodeDash + " Hello" + Environment.NewLine +
                    unicodeDash + " There";

        var result = DialogueDashFixer.Analyze(input);

        Assert.True(result.Changed);
        Assert.Equal("- Hello" + Environment.NewLine + "- There", result.FixedText);
    }
}
