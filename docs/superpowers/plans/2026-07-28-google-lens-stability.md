# Google Lens OCR Stability Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make both Google Lens OCR engines (`GoogleLensOcrSharp` and `GoogleLensOcr`) resilient to transient failures and stalls during long unattended batch runs, instead of silently truncating the batch or hanging forever.

**Architecture:** Two independent, narrowly-scoped fixes in the two existing engine classes — no shared abstraction between them, since their failure modes and execution models (async HTTP loop vs. synchronous external process) are unrelated. Each fix isolates its pure timing/retry logic into a small testable helper so the fix has real test coverage without requiring a live network call or a real `chrome-lens.exe` process.

**Tech Stack:** C# / .NET, xUnit v3.

## Global Constraints

- No new NuGet dependencies.
- No mocking library is available in `tests\UI\UITests.csproj` — hand-write a fake `ILens` directly in the test file for Task 1.
- Preserve existing method signatures (`GoogleLensOcrSharp.OcrBatch(...)`, `GoogleLensOcr.OcrBatch(...)`) exactly — callers in `OcrViewModel.cs` must not need to change.
- Preserve existing behavior that already works: the `"-"`/`"..."` line-joining heuristic in `GoogleLensOcrSharp.OcrBatch`, and the stdout marker-parsing/cancellation logic in `GoogleLensOcr.OcrBatch`.

---

### Task 1: `GoogleLensOcrSharp` — per-item retry instead of batch-aborting

**Files:**
- Modify: `src\ui\Features\Ocr\Engines\GoogleLensOcrSharp.cs`
- Test: `tests\UI\Features\Ocr\Engines\GoogleLensOcrSharpTests.cs`

**Interfaces:**
- Consumes: `ILens.ScanByBitmap(SKBitmap bitmap, string twoLetterLanguageCode) : Task<LensResult>` (`src\ui\Logic\Ocr\GoogleLens\Lens.cs:11`), where `LensResult.Segments` is a list of objects with a `Text` property (used via `result.Segments.Select(x => x.Text)`, matching the existing code at line 40).
- Produces: `GoogleLensOcrSharp.OcrBatch(List<PaddleOcrBatchInput>, string, IProgress<PaddleOcrBatchProgress>, CancellationToken) : Task` — unchanged signature. Internal (but unit-testable) `ScanWithRetry(SKBitmap, string) : Task<List<string>>`.

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\UI\UITests.csproj --filter GoogleLensOcrSharpTests`
Expected: FAIL — the second test shows the batch currently throws/aborts on the first item's exhausted failures instead of continuing to the second item (and the first test fails since there's no retry loop yet at all).

- [ ] **Step 3: Implement retry-with-backoff in `GoogleLensOcrSharp`**

Replace the body of `OcrBatch` and add `ScanWithRetry`:

```csharp
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Ocr.GoogleLens;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Ocr.Engines;

public class GoogleLensOcrSharp
{
    private IProgress<PaddleOcrBatchProgress>? _batchProgress;
    private ILens _lens;

    public GoogleLensOcrSharp(ILens lens)
    {
        _lens = lens;
    }

    private Lock _lockObject = new Lock();

    private static readonly int[] RetryDelaysMs = { 1000, 2000, 4000 };

    public async Task OcrBatch(List<PaddleOcrBatchInput> input, string language, IProgress<PaddleOcrBatchProgress> progress, CancellationToken cancellationToken)
    {
        _batchProgress = progress;

        foreach (var bmpInput in input)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (bmpInput.Bitmap == null)
            {
                continue;
            }

            var lines = await ScanWithRetry(bmpInput.Bitmap, language);

            //join "-" with next line
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == "-" && i + 1 < lines.Count)
                {
                    lines[i] = $"- {lines[i + 1]}";
                    lines.RemoveAt(i + 1);
                }
            }

            //join "..." with line
            for (int i = 0; i < lines.Count; i++)
            {
                //it has previous line not ending with .
                if (lines[i] == "..." && i - 1 >= 0 && !lines[i-1].EndsWith("."))
                {
                    lines[i-1] = $"{lines[i - 1]} ...";
                    lines.RemoveAt(i);
                } 
                else if (lines[i] == "..." && i + 1 < lines.Count)
                {
                    lines[i] = $"... {lines[i + 1]}";
                    lines.RemoveAt(i + 1);
                }
            }

            bmpInput.Text = string.Join(Environment.NewLine, lines).Trim();
            lock (_lockObject)
            {
                var progressReport = new PaddleOcrBatchProgress
                {
                    Index = bmpInput.Index,
                    Text = bmpInput.Text,
                    Item = bmpInput.Item,
                };
                _batchProgress?.Report(progressReport);
            }
        }
    }

    /// <summary>
    /// Scans one bitmap, retrying transient failures with backoff. A single item that
    /// still fails after all retries returns an empty result instead of throwing, so the
    /// rest of the batch keeps running (previously, any exception here escaped the
    /// foreach in OcrBatch and silently killed the whole remaining batch).
    /// </summary>
    private async Task<List<string>> ScanWithRetry(SKBitmap bitmap, string language)
    {
        for (var attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            try
            {
                var result = await _lens.ScanByBitmap(bitmap, language);
                return result.Segments.Select(x => x.Text).ToList();
            }
            catch (Exception ex)
            {
                SeLogger.Error(ex, $"GoogleLensOcrSharp: OCR attempt {attempt + 1}/{RetryDelaysMs.Length + 1} failed");
                if (attempt < RetryDelaysMs.Length)
                {
                    await Task.Delay(RetryDelaysMs[attempt]);
                }
            }
        }

        return new List<string>();
    }

    public static List<OcrLanguage2> GetLanguages()
    {
        // ... unchanged, see existing implementation
    }
}
```

(Keep the existing `GetLanguages()` body exactly as-is — only `OcrBatch` and the new `ScanWithRetry` change. Add `using SkiaSharp;` and `using Nikse.SubtitleEdit.Logic;` to the file's using directives if not already present.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\UI\UITests.csproj --filter GoogleLensOcrSharpTests`
Expected: PASS (both tests). Note the retry test takes ~7 seconds of real wall-clock time (1s+2s+4s backoff) since `RetryDelaysMs` is not injectable in this minimal fix — acceptable for a single test, but flag to a human reviewer if the test suite's timeout is under ~15s.

- [ ] **Step 5: Commit**

```bash
git add src/ui/Features/Ocr/Engines/GoogleLensOcrSharp.cs tests/UI/Features/Ocr/Engines/GoogleLensOcrSharpTests.cs
git commit -m "Retry transient GoogleLensOcrSharp failures instead of aborting the batch"
```

---

### Task 2: `GoogleLensOcr` (exe wrapper) — stall watchdog + cancellable wait

**Files:**
- Modify: `src\ui\Features\Ocr\Engines\GoogleLensOcr.cs`
- Test: `tests\UI\Features\Ocr\Engines\GoogleLensOcrStallDetectionTests.cs`

**Interfaces:**
- Produces: an internal, pure, testable `GoogleLensOcr.HasStalled(DateTime lastOutputUtc, DateTime nowUtc, int stallTimeoutSeconds) : bool` helper, plus the polling/watchdog loop in `OcrBatch` that uses it. `OcrBatch`'s public signature is unchanged.

The polling loop itself (which touches a live `Process`) is not covered by an automated test — this codebase has no existing tests for `GoogleLensOcr` and spawning a real hung process in a unit test would be slow/flaky. The stall-detection *condition* is extracted into a pure function so at least that logic has real coverage; the wiring is verified manually per Step 5.

- [ ] **Step 1: Write the failing test**

```csharp
using Nikse.SubtitleEdit.Features.Ocr.Engines;

namespace UITests.Features.Ocr.Engines;

public class GoogleLensOcrStallDetectionTests
{
    [Fact]
    public void HasStalled_NoOutputPastThreshold_ReturnsTrue()
    {
        var lastOutput = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var now = lastOutput.AddSeconds(91);

        Assert.True(GoogleLensOcr.HasStalled(lastOutput, now, stallTimeoutSeconds: 90));
    }

    [Fact]
    public void HasStalled_WithinThreshold_ReturnsFalse()
    {
        var lastOutput = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var now = lastOutput.AddSeconds(89);

        Assert.False(GoogleLensOcr.HasStalled(lastOutput, now, stallTimeoutSeconds: 90));
    }

    [Fact]
    public void HasStalled_ExactlyAtThreshold_ReturnsFalse()
    {
        var lastOutput = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var now = lastOutput.AddSeconds(90);

        Assert.False(GoogleLensOcr.HasStalled(lastOutput, now, stallTimeoutSeconds: 90));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests\UI\UITests.csproj --filter GoogleLensOcrStallDetectionTests`
Expected: FAIL — `GoogleLensOcr.HasStalled` does not exist yet (compile error).

- [ ] **Step 3: Add the stall watchdog**

Add a field for tracking the last output timestamp, next to the existing fields (line 20-27):

```csharp
    public string Error { get; set; }
    private bool _hasErrors = false;
    private StringBuilder _log = new StringBuilder();
    public const string ExeFileName = "chrome-lens.exe";
    private IProgress<PaddleOcrBatchProgress>? _batchProgress;
    private DateTime _lastOutputUtc = DateTime.UtcNow;
    private const int StallTimeoutSeconds = 90;
```

Reset the watchdog clock at the top of both `OutputDataReceived` and `ErrorDataReceived` (line 102-107 and 169-174):

```csharp
                process.OutputDataReceived += (sendingProcess, outLine) =>
                {
                    _lastOutputUtc = DateTime.UtcNow;

                    if (string.IsNullOrWhiteSpace(outLine.Data))
                    {
                        return;
                    }
```

```csharp
                process.ErrorDataReceived += (sendingProcess, errorLine) =>
                {
                    _lastOutputUtc = DateTime.UtcNow;

                    if (errorLine == null || string.IsNullOrWhiteSpace(errorLine.Data))
                    {
                        return;
                    }
```

Reset it again immediately before starting the process (so a slow-to-launch process doesn't look stalled from time zero), replacing line 181-188:

```csharp
#pragma warning disable CA1416 // Validate platform compatibility
                _lastOutputUtc = DateTime.UtcNow;
                process.Start();
#pragma warning restore CA1416 // Validate platform compatibility;

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(500))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKillStalledProcess(process, "Cancellation requested");
                        break;
                    }

                    if (HasStalled(_lastOutputUtc, DateTime.UtcNow, StallTimeoutSeconds))
                    {
                        TryKillStalledProcess(process, $"No output for {StallTimeoutSeconds}s");
                        break;
                    }
                }
```

Add the two new methods near `AddLineToResult` (after line 263):

```csharp
    /// <summary>
    /// Pure timing check, extracted so it can be unit tested without a real process.
    /// </summary>
    internal static bool HasStalled(DateTime lastOutputUtc, DateTime nowUtc, int stallTimeoutSeconds)
    {
        return (nowUtc - lastOutputUtc).TotalSeconds > stallTimeoutSeconds;
    }

    private void TryKillStalledProcess(Process process, string reason)
    {
        Error = $"GoogleLens process terminated: {reason}.";
        SeLogger.Error(Error + " Log: " + _log.ToString());
        try
        {
            process.Kill(true);
            process.WaitForExit(1000);
        }
        catch (Exception ex)
        {
            Error = $"Error terminating GoogleLens: {ex.Message}";
            SeLogger.Error(ex, "Log: " + _log.ToString());
        }
    }
```

This replaces the single blocking `process.WaitForExit();` call (previously line 188) with a polling loop that (a) observes cancellation even when the process has stopped producing output entirely — previously cancellation was only checked inside `OutputDataReceived`, so a process that stopped emitting output could never be cancelled — and (b) kills the process outright if no output arrives for 90 seconds while it's still nominally running, which is the "stuck at 200/400 forever" symptom.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests\UI\UITests.csproj --filter GoogleLensOcrStallDetectionTests`
Expected: PASS (all 3 tests)

Also run: `dotnet build src\ui\SubtitleEdit.csproj`
Expected: Build succeeds (confirms the `OcrBatch` rewiring compiles).

- [ ] **Step 5: Manually verify the watchdog end-to-end**

This cannot be automated without a real `chrome-lens.exe` (or a stand-in that hangs on purpose), so verify by hand:
1. Temporarily rename the real `chrome-lens.exe` and replace it with a trivial script/exe that starts, prints one line, then sleeps forever without exiting (e.g. a batch file or small console app that does `Console.WriteLine("start"); Thread.Sleep(Timeout.Infinite);`).
2. Run a Google Lens OCR batch of several images through the SE OCR dialog with this stand-in in place.
3. Confirm that after roughly 90 seconds, SE logs a "GoogleLens process terminated: No output for 90s" error and the OCR run completes/returns instead of hanging indefinitely.
4. Restore the real `chrome-lens.exe` afterward.
5. Separately, confirm that clicking "Stop"/cancel mid-run while the stand-in is still sleeping now terminates the process promptly (previously this depended on stdout events firing, which a fully silent hung process would never produce).

- [ ] **Step 6: Commit**

```bash
git add src/ui/Features/Ocr/Engines/GoogleLensOcr.cs tests/UI/Features/Ocr/Engines/GoogleLensOcrStallDetectionTests.cs
git commit -m "Add stall watchdog and cancellable wait to GoogleLensOcr exe wrapper"
```
