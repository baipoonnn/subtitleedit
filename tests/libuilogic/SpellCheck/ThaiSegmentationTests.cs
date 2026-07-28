using Nikse.SubtitleEdit.UiLogic.SpellCheck;
using Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

namespace Nikse.SubtitleEdit.Tests.LibUiLogic.SpellCheck;

public class ThaiSegmentationTests
{
    [Fact]
    public void Split_WithoutThaiLanguage_DoesNotResegment()
    {
        SpellCheckConfig.ActiveTwoLetterLanguage = () => "en";
        SpellCheckConfig.ThaiSegmenter = () => ThaiSegmenterKinds.Nlpo3;

        var words = SpellCheckWordLists.Split("ที่ยังตามตัวไม่ได้นี้");
        Assert.Single(words);
        Assert.Equal("ที่ยังตามตัวไม่ได้นี้", words[0].Text);
        Assert.Equal(0, words[0].Index);
    }

    [Fact]
    public void Split_ThaiWithStubTokenizer_ProducesOffsets()
    {
        var root = Path.Combine(Path.GetTempPath(), "se-thai-test-" + Guid.NewGuid().ToString("N"));
        SpellCheckConfig.ActiveTwoLetterLanguage = () => "th";
        SpellCheckConfig.ThaiSegmenter = () => ThaiSegmenterKinds.Nlpo3;
        SpellCheckConfig.ThaiSpellFolder = () => root;

        try
        {
            Directory.CreateDirectory(ThaiSpellPaths.GetNlpo3Folder());
            File.WriteAllLines(ThaiSpellPaths.GetNlpo3WordsPath(), new[]
            {
                "ที่", "ยัง", "ตาม", "ตัว", "ไม่", "ได้", "นี้", "ตามตัว", "ไม่ได้",
            });
            ThaiTokenizerService.Reset();

            var words = SpellCheckWordLists.Split("ที่ยังตามตัวไม่ได้นี้");
            Assert.True(words.Count > 1);
            Assert.Equal(0, words[0].Index);
            var rebuilt = string.Concat(words.Select(w => w.Text));
            Assert.Equal("ที่ยังตามตัวไม่ได้นี้", rebuilt);

            for (var i = 1; i < words.Count; i++)
            {
                Assert.Equal(words[i - 1].Index + words[i - 1].Text.Length, words[i].Index);
            }
        }
        finally
        {
            ThaiTokenizerService.Reset();
            SpellCheckConfig.ThaiSegmenter = () => ThaiSegmenterKinds.None;
            SpellCheckConfig.ActiveTwoLetterLanguage = () => string.Empty;
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
                // ignore cleanup
            }
        }
    }

    [Fact]
    public void NewMm_SegmentsKnownPhrase()
    {
        var tok = new NewMmThaiTokenizer(new[] { "ที่", "ยัง", "ตาม", "ตัว", "ไม่", "ได้", "นี้" });
        var spans = tok.Segment("ที่ยังตามตัวไม่ได้นี้");
        Assert.Equal(new[] { "ที่", "ยัง", "ตาม", "ตัว", "ไม่", "ได้", "นี้" }, spans.Select(s => s.Text).ToArray());
    }

    [Fact]
    public void ApplyToSplitWords_LeavesEnglishAlone()
    {
        SpellCheckConfig.ActiveTwoLetterLanguage = () => "th";
        SpellCheckConfig.ThaiSegmenter = () => ThaiSegmenterKinds.Nlpo3;
        var input = new List<SpellCheckWord>
        {
            new() { Text = "Hello", Index = 0 },
            new() { Text = "world", Index = 6 },
        };
        var output = ThaiSegmentation.ApplyToSplitWords(input);
        Assert.Equal(2, output.Count);
        Assert.Equal("Hello", output[0].Text);
    }

    [Fact]
    public void Attacut_TryCreate_ReturnsNullWhenNotInstalled()
    {
        var root = Path.Combine(Path.GetTempPath(), "se-thai-missing-" + Guid.NewGuid().ToString("N"));
        SpellCheckConfig.ThaiSpellFolder = () => root;
        ThaiTokenizerService.Reset();
        Assert.Null(AttacutCOnnxTokenizer.TryCreate(ThaiOnnxProviders.DirectMl));
        Assert.Null(AttacutCOnnxTokenizer.TryCreate(ThaiOnnxProviders.Cuda));
        Assert.Null(AttacutCOnnxTokenizer.TryCreate(ThaiOnnxProviders.Cpu));
    }

    [Fact]
    public void OnnxRuntime_IsInstalled_RequiresBothDlls()
    {
        var root = Path.Combine(Path.GetTempPath(), "se-thai-ort-" + Guid.NewGuid().ToString("N"));
        SpellCheckConfig.ThaiSpellFolder = () => root;
        try
        {
            Assert.False(ThaiSpellPaths.IsOnnxRuntimeInstalled());
            Directory.CreateDirectory(ThaiSpellPaths.GetOnnxRuntimeFolder());
            File.WriteAllBytes(ThaiSpellPaths.GetOnnxRuntimeDllPath(), new byte[1_000_001]);
            Assert.False(ThaiSpellPaths.IsOnnxRuntimeInstalled());
            File.WriteAllBytes(ThaiSpellPaths.GetOnnxProvidersSharedDllPath(), new byte[2_000]);
            Assert.True(ThaiSpellPaths.IsOnnxRuntimeInstalled());
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void Split_WithNoneSegmenter_KeepsThaiBlob()
    {
        SpellCheckConfig.ActiveTwoLetterLanguage = () => "th";
        SpellCheckConfig.ThaiSegmenter = () => ThaiSegmenterKinds.None;
        var words = SpellCheckWordLists.Split("ที่ยังตามตัวไม่ได้นี้");
        Assert.Single(words);
        Assert.Equal("ที่ยังตามตัวไม่ได้นี้", words[0].Text);
    }
}
