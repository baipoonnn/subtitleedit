using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Download;
using Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

namespace Nikse.SubtitleEdit.Logic.SpellCheck;

/// <summary>
/// Ensures Thai word-breaker packs are on disk under AppData/ThaiSpell.
/// CPU / DirectML / CUDA is prompted only when the user selects AttaCut in Word break
/// (<paramref name="promptForBackend"/> = true), not when OCR/spell-check merely starts.
/// </summary>
public static class ThaiSpellEnsureHelper
{
    public static async Task<bool> EnsureReadyAsync(
        Window window,
        IWindowService? windowService,
        IThaiSpellDownloadService downloadService,
        string segmenterKind,
        bool promptForBackend = false,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(segmenterKind, ThaiSegmenterKinds.None, StringComparison.OrdinalIgnoreCase))
        {
            ThaiTokenizerService.Reset();
            return true;
        }

        if (string.Equals(segmenterKind, ThaiSegmenterKinds.AttacutC, StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureAttacutAsync(window, windowService, downloadService, promptForBackend, cancellationToken);
        }

        if (string.Equals(segmenterKind, ThaiSegmenterKinds.Nlpo3, StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureNlpo3Async(window, windowService, downloadService, promptForBackend, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Loads the active Thai tokenizer after settings have been saved. AttaCut ONNX init
    /// (especially GPU EPs) can take several seconds — show progress so the UI does not look hung.
    /// </summary>
    public static async Task WarmUpTokenizerAsync(
        Window window,
        IWindowService? windowService,
        string segmenterKind)
    {
        if (string.Equals(segmenterKind, ThaiSegmenterKinds.None, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PleaseWaitViewModel? pleaseWaitVm = null;
        if (windowService != null)
        {
            pleaseWaitVm = windowService.ShowWindow<PleaseWaitWindow, PleaseWaitViewModel>(window);
            pleaseWaitVm.StatusText = string.Equals(segmenterKind, ThaiSegmenterKinds.AttacutC, StringComparison.OrdinalIgnoreCase)
                ? "Initializing AttaCut..."
                : "Loading Thai word list...";
        }

        try
        {
            await Task.Run(() => ThaiTokenizerService.GetActiveTokenizer());
        }
        finally
        {
            pleaseWaitVm?.Close();
        }
    }

    private static async Task<bool> EnsureAttacutAsync(
        Window window,
        IWindowService? windowService,
        IThaiSpellDownloadService downloadService,
        bool promptForBackend,
        CancellationToken cancellationToken)
    {
        var needModel = !ThaiSpellPaths.IsAttacutInstalled();
        var needRuntime = !OnnxNativeBootstrap.IsRuntimeAvailable();

        if (!promptForBackend)
        {
            // OCR / warm-up paths: never ask CPU/GPU here. Use whatever is already on disk.
            if (needModel || needRuntime)
            {
                return false;
            }

            ThaiTokenizerService.Reset();
            return true;
        }

        // Word-break combo only: offer backend choice (including when already installed).
        var answer = await MessageBox.Show(
            window,
            needModel ? "Download AttaCut (ONNX)?" : "AttaCut ONNX backend",
            needModel
                ? $"{Environment.NewLine}Thai word breaking with AttaCut requires downloading:{Environment.NewLine}" +
                  $"• AttaCut model (~0.7 MB){Environment.NewLine}" +
                  $"• ONNX Runtime (~17 MB){Environment.NewLine}{Environment.NewLine}" +
                  "Choose the ONNX Runtime backend:"
                : $"{Environment.NewLine}AttaCut is already installed.{Environment.NewLine}{Environment.NewLine}" +
                  (needRuntime ? "ONNX Runtime is missing and will be downloaded (~17 MB).{Environment.NewLine}{Environment.NewLine}" : string.Empty) +
                  "Choose the ONNX Runtime backend:",
            MessageBoxButtons.Cancel,
            MessageBoxIcon.Question,
            "CPU",
            "DirectML",
            "CUDA");

        if (answer == MessageBoxResult.Cancel)
        {
            return false;
        }

        Se.Settings.SpellCheck.ThaiOnnxProvider = answer switch
        {
            MessageBoxResult.Custom1 => ThaiOnnxProviders.Cpu,
            MessageBoxResult.Custom2 => ThaiOnnxProviders.DirectMl,
            MessageBoxResult.Custom3 => ThaiOnnxProviders.Cuda,
            _ => ThaiOnnxProviders.Cpu,
        };
        Se.SaveSettings();

        if (!needModel && !needRuntime)
        {
            ThaiTokenizerService.Reset();
            return true;
        }

        PleaseWaitViewModel? pleaseWaitVm = null;
        if (windowService != null)
        {
            pleaseWaitVm = windowService.ShowWindow<PleaseWaitWindow, PleaseWaitViewModel>(window);
            pleaseWaitVm.StatusText = "Downloading AttaCut...";
        }

        try
        {
            if (needModel)
            {
                Directory.CreateDirectory(ThaiSpellPaths.GetAttacutFolder());
                await downloadService.DownloadFileAsync(
                    ThaiSpellPaths.AttacutOnnxUrl,
                    ThaiSpellPaths.GetAttacutOnnxPath(),
                    MakeDownloadProgress(pleaseWaitVm, "Downloading AttaCut model", 0f, 0.25f),
                    cancellationToken);

                pleaseWaitVm?.ReportProgress(25, 100, "Downloading AttaCut character map...");
                await downloadService.DownloadFileAsync(
                    ThaiSpellPaths.AttacutCharactersUrl,
                    ThaiSpellPaths.GetAttacutCharactersPath(),
                    MakeDownloadProgress(pleaseWaitVm, "Downloading AttaCut character map", 0.25f, 0.35f),
                    cancellationToken);
            }

            if (needRuntime)
            {
                pleaseWaitVm?.ReportProgress(needModel ? 35 : 0, 100, "Downloading ONNX Runtime...");
                var runtimeStart = needModel ? 0.35f : 0f;
                await downloadService.DownloadOnnxRuntimeAsync(
                    MakeDownloadProgress(pleaseWaitVm, "Downloading ONNX Runtime", runtimeStart, 1f),
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            pleaseWaitVm?.Close();
            await MessageBox.Show(
                window,
                "Download failed",
                "Could not download AttaCut / ONNX Runtime:" + Environment.NewLine + ex.Message,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            pleaseWaitVm?.Close();
        }

        ThaiTokenizerService.Reset();
        return ThaiSpellPaths.IsAttacutInstalled() && OnnxNativeBootstrap.IsRuntimeAvailable();
    }

    private static async Task<bool> EnsureNlpo3Async(
        Window window,
        IWindowService? windowService,
        IThaiSpellDownloadService downloadService,
        bool promptForBackend,
        CancellationToken cancellationToken)
    {
        if (ThaiSpellPaths.IsNlpo3Installed())
        {
            ThaiTokenizerService.Reset();
            return true;
        }

        if (!promptForBackend)
        {
            return false;
        }

        var answer = await MessageBox.Show(
            window,
            "Download Thai word list?",
            $"{Environment.NewLine}nlpo3-style maximal matching needs a Thai word list (~2.5 MB).{Environment.NewLine}{Environment.NewLine}Download now?",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        PleaseWaitViewModel? pleaseWaitVm = null;
        if (windowService != null)
        {
            pleaseWaitVm = windowService.ShowWindow<PleaseWaitWindow, PleaseWaitViewModel>(window);
            pleaseWaitVm.StatusText = "Downloading Thai word list...";
        }

        try
        {
            Directory.CreateDirectory(ThaiSpellPaths.GetNlpo3Folder());
            await downloadService.DownloadFileAsync(
                ThaiSpellPaths.Nlpo3WordsUrl,
                ThaiSpellPaths.GetNlpo3WordsPath(),
                MakeDownloadProgress(pleaseWaitVm, "Downloading Thai word list", 0f, 1f),
                cancellationToken);
        }
        catch (Exception ex)
        {
            pleaseWaitVm?.Close();
            await MessageBox.Show(
                window,
                "Download failed",
                "Could not download Thai word list:" + Environment.NewLine + ex.Message,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            pleaseWaitVm?.Close();
        }

        ThaiTokenizerService.Reset();
        return ThaiSpellPaths.IsNlpo3Installed();
    }

    private static IProgress<float>? MakeDownloadProgress(
        PleaseWaitViewModel? pleaseWaitVm,
        string statusPrefix,
        float rangeStart,
        float rangeEnd)
    {
        if (pleaseWaitVm == null)
        {
            return null;
        }

        return new Progress<float>(fraction =>
        {
            var mapped = rangeStart + fraction * (rangeEnd - rangeStart);
            var percent = (int)(mapped * 100);
            pleaseWaitVm.ReportProgress(percent, 100, $"{statusPrefix} ({percent}%)");
        });
    }
}
