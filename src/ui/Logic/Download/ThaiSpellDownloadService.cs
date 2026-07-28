using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nikse.SubtitleEdit.UiLogic.SpellCheck.Thai;

namespace Nikse.SubtitleEdit.Logic.Download;

public interface IThaiSpellDownloadService
{
    Task DownloadFileAsync(string url, string destinationPath, IProgress<float>? progress, CancellationToken cancellationToken);
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
}
