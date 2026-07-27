using Nikse.SubtitleEdit.Features.Ocr;
using Nikse.SubtitleEdit.Features.Ocr.Engines;
using Nikse.SubtitleEdit.Logic.Ocr.GoogleLens;
using SkiaSharp;
using System.Net.Http;

namespace UITests.Features.Ocr.Engines;

public class GoogleLensOcrSharpTests
{
    private static LensResult MakeResult(string text)
    {
        var box = new BoundingBox(new double[] { 0.5, 0.5, 0.1, 0.1 }, new[] { 100, 100 });
        return new LensResult("en", new List<Segment> { new(text, box) });
    }

    // Fails a fixed number of times, then succeeds, so we can prove the batch keeps
    // going instead of dying on the first transient error.
    private class FlakyLens : ILens
    {
        private readonly int _failuresBeforeSuccess;
        private int _callCount;
        public int CallCount => _callCount;

        public FlakyLens(int failuresBeforeSuccess)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public Task<LensResult> ScanByBitmap(SKBitmap bitmap, string twoLetterLanguageCode)
        {
            _callCount++;
            if (_callCount <= _failuresBeforeSuccess)
            {
                throw new HttpRequestException("simulated transient failure");
            }

            return Task.FromResult(MakeResult("OK"));
        }
    }

    // Fails every call for the first `failingCallCount` calls (covering one item's full
    // retry budget), then succeeds - so we can prove one permanently-failing item doesn't
    // stop the next item in the batch from being processed.
    private class FailsFirstNCallsLens : ILens
    {
        private readonly int _failingCallCount;
        private int _callCount;

        public FailsFirstNCallsLens(int failingCallCount)
        {
            _failingCallCount = failingCallCount;
        }

        public Task<LensResult> ScanByBitmap(SKBitmap bitmap, string twoLetterLanguageCode)
        {
            _callCount++;
            if (_callCount <= _failingCallCount)
            {
                throw new HttpRequestException("simulated permanent failure for the first item");
            }

            return Task.FromResult(MakeResult("OK"));
        }
    }

    private static PaddleOcrBatchInput MakeInput(int index) => new()
    {
        Index = index,
        Bitmap = new SKBitmap(1, 1),
    };

    [Fact]
    public async Task OcrBatch_TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var lens = new FlakyLens(failuresBeforeSuccess: 2);
        var engine = new GoogleLensOcrSharp(lens);
        var input = new List<PaddleOcrBatchInput> { MakeInput(0) };

        await engine.OcrBatch(input, "en", new Progress<PaddleOcrBatchProgress>(), CancellationToken.None);

        Assert.Equal("OK", input[0].Text);
        Assert.Equal(3, lens.CallCount); // 2 failures + 1 success
    }

    [Fact]
    public async Task OcrBatch_OneItemPermanentlyFails_RestOfBatchStillProcessed()
    {
        // GoogleLensOcrSharp retries up to 3 times (4 total attempts) per item, so the
        // first item exhausts calls 1-4 and comes back empty; the second item's call (5)
        // succeeds, proving the batch didn't abort after the first item's failure.
        var lens = new FailsFirstNCallsLens(failingCallCount: 4);
        var engine = new GoogleLensOcrSharp(lens);
        var input = new List<PaddleOcrBatchInput> { MakeInput(0), MakeInput(1) };

        await engine.OcrBatch(input, "en", new Progress<PaddleOcrBatchProgress>(), CancellationToken.None);

        Assert.Equal(string.Empty, input[0].Text); // exhausted retries, empty rather than thrown
        Assert.Equal("OK", input[1].Text); // second item still processed
    }
}
