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
/// AttaCut prompts for CPU / DirectML / CUDA like CrispEmbed's engine download.
/// </summary>
public static class ThaiSpellEnsureHelper
{
    public static async Task<bool> EnsureReadyAsync(
        Window window,
        IWindowService? windowService,
        IThaiSpellDownloadService downloadService,
        string segmenterKind,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(segmenterKind, ThaiSegmenterKinds.None, StringComparison.OrdinalIgnoreCase))
        {
            ThaiTokenizerService.Reset();
            return true;
        }

        if (string.Equals(segmenterKind, ThaiSegmenterKinds.AttacutC, StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureAttacutAsync(window, windowService, downloadService, cancellationToken);
        }

        if (string.Equals(segmenterKind, ThaiSegmenterKinds.Nlpo3, StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureNlpo3Async(window, windowService, downloadService, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        if (ThaiSpellPaths.IsAttacutInstalled())
        {
            ThaiTokenizerService.Reset();
            return true;
        }

        var answer = await MessageBox.Show(
            window,
            "Download AttaCut (ONNX)?",
            $"{Environment.NewLine}Thai word breaking with AttaCut requires downloading the ONNX model (~0.7 MB).{Environment.NewLine}{Environment.NewLine}Choose the ONNX Runtime backend:",
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

        PleaseWaitViewModel? pleaseWaitVm = null;
        if (windowService != null)
        {
            pleaseWaitVm = windowService.ShowWindow<PleaseWaitWindow, PleaseWaitViewModel>(window);
            pleaseWaitVm.StatusText = "Downloading AttaCut model...";
        }

        try
        {
            Directory.CreateDirectory(ThaiSpellPaths.GetAttacutFolder());
            await downloadService.DownloadFileAsync(
                ThaiSpellPaths.AttacutOnnxUrl,
                ThaiSpellPaths.GetAttacutOnnxPath(),
                MakeDownloadProgress(pleaseWaitVm, "Downloading AttaCut model", 0f, 0.5f),
                cancellationToken);

            pleaseWaitVm?.ReportProgress(50, 100, "Downloading AttaCut character map...");

            await downloadService.DownloadFileAsync(
                ThaiSpellPaths.AttacutCharactersUrl,
                ThaiSpellPaths.GetAttacutCharactersPath(),
                MakeDownloadProgress(pleaseWaitVm, "Downloading AttaCut character map", 0.5f, 1f),
                cancellationToken);
        }
        catch (Exception ex)
        {
            pleaseWaitVm?.Close();
            await MessageBox.Show(
                window,
                "Download failed",
                "Could not download AttaCut model:" + Environment.NewLine + ex.Message,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            pleaseWaitVm?.Close();
        }

        ThaiTokenizerService.Reset();
        return ThaiSpellPaths.IsAttacutInstalled();
    }

    private static async Task<bool> EnsureNlpo3Async(
        Window window,
        IWindowService? windowService,
        IThaiSpellDownloadService downloadService,
        CancellationToken cancellationToken)
    {
        if (ThaiSpellPaths.IsNlpo3Installed())
        {
            ThaiTokenizerService.Reset();
            return true;
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
