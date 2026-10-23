using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelper.Managers;

public class ImageDownloader
{
    private const int MaxConcurrentDownloads = 4;
    private const int MaxImageBytes = 16 * 1024 * 1024;
    private readonly List<string> _urls;
    private readonly string _savePath;
    private int _completedDownloads;

    public int CompletedDownloads => Volatile.Read(ref _completedDownloads);
    public int TotalDownloads => _urls.Count;

    public ImageDownloader(List<string> urls, string savePath)
    {
        _urls = urls.Distinct(StringComparer.Ordinal).ToList();
        _savePath = savePath;
    }


    public async Task<List<string>> BeginDownloadAsync(CancellationToken cancellationToken = default, bool replaceExisting = false)
    {
        Directory.CreateDirectory(_savePath);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var slots = new SemaphoreSlim(MaxConcurrentDownloads);
        var downloads = _urls.Select(async url =>
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await DownloadAsync(http, url, replaceExisting, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Increment(ref _completedDownloads);
                slots.Release();
            }
        });
        var results = await Task.WhenAll(downloads).ConfigureAwait(false);
        return results.Where(error => error.Length > 0).ToList();
    }

    public static bool IsCachedImageValid(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length < 4 || file.Length > MaxImageBytes) return false;
            if (file.ReadByte() != 0xff || file.ReadByte() != 0xd8) return false;
            file.Seek(-2, SeekOrigin.End);
            return file.ReadByte() == 0xff && file.ReadByte() == 0xd9;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static int ImportCachedImages(string sourceFolder, string destinationFolder)
    {
        if (!Directory.Exists(sourceFolder)) return 0;
        var copied = 0;
        foreach (var source in Directory.EnumerateFiles(sourceFolder, "*-data.jpg"))
        {
            var target = Path.Combine(destinationFolder, Path.GetFileName(source));
            if (IsCachedImageValid(target) || !IsCachedImageValid(source)) continue;
            Directory.CreateDirectory(destinationFolder);
            File.Copy(source, target, true);
            copied++;
        }
        return copied;
    }

    private async Task<string> DownloadAsync(HttpClient http, string url, bool replaceExisting, CancellationToken cancellationToken)
    {
        var filename = Path.GetFileName(Uri.UnescapeDataString(new Uri(url).AbsolutePath));
        var targetPath = Path.Combine(_savePath, filename);
        var temporaryPath = Path.Combine(_savePath, $".{filename}.{Guid.NewGuid():N}.tmp");
        if (!replaceExisting && IsCachedImageValid(targetPath)) return string.Empty;

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        var requestToken = requestTimeout.Token;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxImageBytes)
                return $"{filename}: image exceeds 16 MiB.";

            await using (var source = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false))
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                var totalBytes = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, requestToken).ConfigureAwait(false)) > 0)
                {
                    totalBytes += count;
                    if (totalBytes > MaxImageBytes) return $"{filename}: image exceeds 16 MiB.";
                    await destination.WriteAsync(buffer.AsMemory(0, count), requestToken).ConfigureAwait(false);
                }
                await destination.FlushAsync(requestToken).ConfigureAwait(false);
            }

            if (!IsCachedImageValid(temporaryPath)) return $"{filename}: server did not return a complete JPEG image.";
            cancellationToken.ThrowIfCancellationRequested();
            // Renaming on the same volume preserves the previous map if a request fails.
            File.Move(temporaryPath, targetPath, true);
            return string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return $"{filename}: download timed out after 30 seconds."; }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return $"{filename}: {error.Message}";
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
