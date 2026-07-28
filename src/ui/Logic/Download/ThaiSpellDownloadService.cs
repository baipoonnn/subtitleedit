using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

namespace Nikse.SubtitleEdit.Logic.Download;

public interface IThaiSpellDownloadService
{
    Task DownloadFileAsync(string url, string destinationPath, IProgress<float>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads Microsoft.ML.OnnxRuntime.DirectML natives into AppData/ThaiSpell/onnxruntime.
    /// </summary>
    Task DownloadOnnxRuntimeAsync(IProgress<float>? progress, CancellationToken cancellationToken);
}

public class ThaiSpellDownloadService : IThaiSpellDownloadService
{
    private readonly HttpClient _httpClient;

    public ThaiSpellDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task DownloadFileAsync(string url, string destinationPath, IProgress<float>? progress, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var temp = destinationPath + ".tmp";
        await using (var fs = File.Create(temp))
        {
            await DownloadHelper.DownloadFileAsync(_httpClient, url, fs, progress, cancellationToken);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(temp, destinationPath);
    }

    public async Task DownloadOnnxRuntimeAsync(IProgress<float>? progress, CancellationToken cancellationToken)
    {
        var destDir = ThaiSpellPaths.GetOnnxRuntimeFolder();
        Directory.CreateDirectory(destDir);

        var nupkgPath = Path.Combine(Path.GetTempPath(), $"se-ort-{Guid.NewGuid():N}.nupkg");
        try
        {
            await DownloadFileAsync(ThaiSpellPaths.GetOnnxRuntimeNupkgUrl(), nupkgPath, progress, cancellationToken);
            ExtractWinNativeDlls(nupkgPath, destDir);
        }
        finally
        {
            try
            {
                if (File.Exists(nupkgPath))
                {
                    File.Delete(nupkgPath);
                }
            }
            catch
            {
                // ignore temp cleanup
            }
        }

        if (!ThaiSpellPaths.IsOnnxRuntimeInstalled())
        {
            throw new InvalidOperationException(
                "ONNX Runtime download completed but native DLLs were not found in the package for this platform.");
        }
    }

    private static void ExtractWinNativeDlls(string nupkgPath, string destDir)
    {
        var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var prefix = $"runtimes/{rid}/native/";

        using var zip = ZipFile.OpenRead(nupkgPath);
        var extracted = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(name);
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            var dest = Path.Combine(destDir, fileName);
            var temp = dest + ".tmp";
            entry.ExtractToFile(temp, overwrite: true);
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }

            File.Move(temp, dest);
            extracted++;
        }

        if (extracted == 0)
        {
            throw new InvalidOperationException($"No native DLLs found under {prefix} in ONNX Runtime package.");
        }
    }
}
