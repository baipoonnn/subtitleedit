using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Ocr;
using Nikse.SubtitleEdit.Features.Ocr.OcrSubtitle;
using Nikse.SubtitleEdit.UiLogic.Ocr.FixEngine;

namespace UITests.Features.Ocr;

public class OcrRefreshSpellCheckAfterDictionaryTests
{
    [Fact]
    public void NormalizeWordsForSpellCheckRefresh_TrimsDropsEmptyDistinct()
    {
        var result = OcrViewModel.NormalizeWordsForSpellCheckRefresh(new[]
        {
            "  Alice ",
            "",
            "Bob",
            "Alice",
            "   ",
            "Bob",
        });

        Assert.Equal(new[] { "Alice", "Bob" }, result);
    }

    [Fact]
    public void CollectItemsWithMatchingUnknownWords_ReturnsDistinctMatchingItems()
    {
        var items = MakeItems(3);
        var item1 = items[0];
        var item2 = items[1];
        var item3 = items[2];

        var unknown = new List<UnknownWordItem>
        {
            MakeUnknown(item1, "Alice"),
            MakeUnknown(item2, "Alice"),
            MakeUnknown(item2, "Bob"),
            MakeUnknown(item3, "Charlie"),
        };

        var result = OcrViewModel.CollectItemsWithMatchingUnknownWords(unknown, new[] { "Alice" });

        Assert.Equal(2, result.Count);
        Assert.Contains(item1, result);
        Assert.Contains(item2, result);
        Assert.DoesNotContain(item3, result);
    }

    [Fact]
    public void CollectItemsWithMatchingUnknownWords_OrdinalCaseSensitive()
    {
        var item = MakeItems(1)[0];
        var unknown = new List<UnknownWordItem> { MakeUnknown(item, "Alice") };

        var result = OcrViewModel.CollectItemsWithMatchingUnknownWords(unknown, new[] { "alice" });

        Assert.Empty(result);
    }

    private static List<OcrSubtitleItem> MakeItems(int count)
    {
        var subtitle = new Subtitle();
        for (var i = 0; i < count; i++)
        {
            subtitle.Paragraphs.Add(new Paragraph(
                new TimeCode(i * 1000),
                new TimeCode(i * 1000 + 900),
                $"line {i + 1}"));
        }

        return new OcrSubtitleDummy(subtitle).MakeOcrSubtitleItems();
    }

    private static UnknownWordItem MakeUnknown(OcrSubtitleItem item, string word)
    {
        var part = new OcrFixLinePartResult
        {
            Word = word,
            FixedWord = word,
            IsSpellCheckedOk = false,
            LinePartType = OcrFixLinePartType.Word,
        };
        var result = new OcrFixLineResult
        {
            LineIndex = item.Number - 1,
            Words = new List<OcrFixLinePartResult> { part },
        };
        return new UnknownWordItem(item, result, part);
    }
}
