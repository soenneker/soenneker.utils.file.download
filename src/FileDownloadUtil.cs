using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.HttpClientCache.Abstract;
using Soenneker.Utils.Path.Abstract;

namespace Soenneker.Utils.File.Download;

///<inheritdoc cref="IFileDownloadUtil"/>
public class FileDownloadUtil : IFileDownloadUtil
{
    private readonly ILogger<FileDownloadUtil> _logger;
    private readonly IHttpClientCache _httpClientCache;
    private readonly IFileUtil _fileUtil;
    private readonly IPathUtil _pathUtil;
    private readonly AsyncLock _asyncLock = new();

    public FileDownloadUtil(ILogger<FileDownloadUtil> logger, IHttpClientCache httpClientCache, IFileUtil fileUtil, IPathUtil pathUtil)
    {
        _logger = logger;
        _httpClientCache = httpClientCache;
        _fileUtil = fileUtil;
        _pathUtil = pathUtil;
    }

    public async ValueTask<List<string>> DownloadFiles(string directory, List<string> uris, int maxConcurrentDownloads, CancellationToken cancellationToken = default)
    {
        var downloadedFilePaths = new List<string>(uris.Count);

        var rateLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = maxConcurrentDownloads,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = maxConcurrentDownloads
        });

        var tasks = new List<Task>(uris.Count);

        HttpClient client = await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        foreach (string uri in uris)
        {
            RateLimitLease lease = await rateLimiter.AcquireAsync(1, cancellationToken).NoSync();

            if (lease.IsAcquired)
            {
                Task task = Task.Run(async () =>
                {
                    try
                    {
                        string filePath = await _pathUtil.GetThreadSafeUniqueFilePath(directory, uri, cancellationToken).NoSync();

                        string? result = await DownloadFileAsStream(uri, filePath, client, cancellationToken).NoSync();

                        if (result != null)
                        {
                            using (await _asyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
                            {
                                downloadedFilePaths.Add(result);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error downloading file from {Uri}: {Message}", uri, ex.Message);
                    }
                    finally
                    {
                        lease.Dispose();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }
            else
            {
                _logger.LogWarning("Failed to acquire rate limiter permit for URI: {Uri}", uri);
            }
        }

        await Task.WhenAll(tasks).NoSync();
        return downloadedFilePaths;
    }


    public async ValueTask<string?> DownloadFile(string uri, string fileExtension, CancellationToken cancellationToken = default)
    {
        HttpClient client = await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        string filePath = await _pathUtil.GetThreadSafeTempUniqueFilePath(fileExtension, cancellationToken).NoSync();

        return await DownloadFileAsStream(uri, filePath, client, cancellationToken).NoSync();
    }

    public async ValueTask<string?> DownloadFile(string uri, string directory, string fileExtension, CancellationToken cancellationToken = default)
    {
        HttpClient client = await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        string filePath = await _pathUtil.GetThreadSafeRandomUniqueFilePath(directory, fileExtension, cancellationToken).NoSync();

        return await DownloadFileAsStream(uri, filePath, client, cancellationToken).NoSync();
    }

    public async ValueTask<string?> DownloadFileAsStream(string uri, string filePath, HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        _logger.LogDebug("Downloading file from URI ({uri}) ...", uri);

        try
        {
            await using (Stream responseStream = await client.GetStreamAsync(uri, cancellationToken))
            {
                await _fileUtil.WriteFile(filePath, responseStream, cancellationToken).NoSync();
            }

            _logger.LogDebug("Finished download of URI ({uri}). Saved to {filePath}", uri, filePath);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to download file from URI ({uri}): {message}", uri, ex.Message);
            return null;
        }
    }
}