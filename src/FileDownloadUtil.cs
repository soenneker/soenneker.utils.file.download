using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Polly;
using Polly.Retry;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.HttpClientCache.Abstract;
using Soenneker.Utils.Path.Abstract;

namespace Soenneker.Utils.File.Download;

///<inheritdoc cref="IFileDownloadUtil"/>
public sealed class FileDownloadUtil : IFileDownloadUtil
{
    private readonly ILogger<FileDownloadUtil> _logger;
    private readonly IHttpClientCache _httpClientCache;
    private readonly IFileUtil _fileUtil;
    private readonly IPathUtil _pathUtil;
    private readonly AsyncLock _asyncLock = new();

    private const int _bufferSize = 128 * 1024; // 128 KB

    public FileDownloadUtil(ILogger<FileDownloadUtil> logger, IHttpClientCache httpClientCache, IFileUtil fileUtil, IPathUtil pathUtil)
    {
        _logger = logger;
        _httpClientCache = httpClientCache;
        _fileUtil = fileUtil;
        _pathUtil = pathUtil;
    }

    public async ValueTask<List<string>> DownloadMultiple(string directory, List<string> uris, int maxConcurrentDownloads,
        CancellationToken cancellationToken = default)
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
            try
            {
                RateLimitLease lease = await rateLimiter.AcquireAsync(1, cancellationToken).NoSync();

                if (!lease.IsAcquired)
                {
                    _logger.LogWarning("Failed to acquire rate limiter permit for URI: {Uri}", uri);
                    continue;
                }

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        string filePath = await _pathUtil.GetUniqueFilePathFromUri(directory, uri, cancellationToken).NoSync();
                        string? result = await Download(uri, filePath, null, null, client, cancellationToken).NoSync();

                        if (result != null)
                        {
                            using (await _asyncLock.LockAsync(cancellationToken).ConfigureAwait(false))
                                downloadedFilePaths.Add(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error downloading file from {Uri}: {Message}", uri, ex.Message);
                    }
                    finally
                    {
                        lease.Dispose(); // perfectly fine here
                    }
                }, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogError("Unexpected error acquiring permit: {Message}", ex.Message);
            }
        }

        await Task.WhenAll(tasks).NoSync();
        return downloadedFilePaths;
    }

    public async ValueTask<string?> Download(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        if (filePath == null)
        {
            if (directory != null && fileExtension != null)
                filePath = await _pathUtil.GetRandomUniqueFilePath(directory, fileExtension, cancellationToken).NoSync();
            else if (fileExtension != null)
                filePath = await _pathUtil.GetRandomTempFilePath(fileExtension, cancellationToken).NoSync();
            else
                throw new ArgumentException("Either filePath or fileExtension must be provided.");
        }

        _logger.LogDebug("Downloading file from URI ({uri}). Saving to {path} ...", uri, filePath);

        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, cancellationToken).NoSync();

            await using var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: _bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await response.Content.CopyToAsync(fs, cancellationToken).NoSync();

            _logger.LogDebug("Finished download of URI ({uri}). Saved to {filePath}", uri, filePath);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to download file from URI ({uri}): {message}", uri, ex.Message);
            return null;
        }
    }

    public async ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, int maxRetryAttempts = 3, double baseDelaySeconds = 2.0, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        // Build exponential back-off: delay = baseDelaySeconds ^ (retryAttempt)
        AsyncRetryPolicy<string?> retryPolicy = Policy<string?>.Handle<Exception>()
                                                               .OrResult(result => result == null)
                                                               .WaitAndRetryAsync(retryCount: maxRetryAttempts,
                                                                   sleepDurationProvider: retryAttempt =>
                                                                       TimeSpan.FromSeconds(Math.Pow(baseDelaySeconds, retryAttempt)),
                                                                   onRetry: (outcome, timespan, retryCount, context) =>
                                                                   {
                                                                       if (outcome.Exception != null)
                                                                       {
                                                                           _logger.LogWarning(outcome.Exception,
                                                                               "Download attempt {RetryCount}/{Max} for {Uri} threw, retrying in {Delay}s...",
                                                                               retryCount,
                                                                               maxRetryAttempts, uri, timespan.TotalSeconds);
                                                                       }
                                                                       else
                                                                       {
                                                                           _logger.LogWarning(
                                                                               "Download attempt {RetryCount}/{Max} for {Uri} returned null, retrying in {Delay}s...",
                                                                               retryCount, maxRetryAttempts,
                                                                               uri, timespan.TotalSeconds);
                                                                       }
                                                                   });

        // Execute under the retry policy
        return await retryPolicy.ExecuteAsync(ct => Download(uri, filePath, directory, fileExtension, client, ct).AsTask(), cancellationToken);
    }

    public async ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        // Retry policy: on exception or null return, retry 3 times with 2^retryAttempt seconds delay
        AsyncRetryPolicy<string?> retryPolicy = Policy<string?>.Handle<Exception>()
                                                               .OrResult(result => result == null)
                                                               .WaitAndRetryAsync(retryCount: 3,
                                                                   sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                                                   onRetry: (outcome, timespan, retryCount, context) =>
                                                                   {
                                                                       if (outcome.Exception != null)
                                                                       {
                                                                           _logger.LogWarning(outcome.Exception,
                                                                               "Download attempt {RetryCount} for {Uri} threw, retrying in {Delay}s...",
                                                                               retryCount, uri,
                                                                               timespan.TotalSeconds);
                                                                       }
                                                                       else
                                                                       {
                                                                           _logger.LogWarning(
                                                                               "Download attempt {RetryCount} for {Uri} returned null, retrying in {Delay}s...",
                                                                               retryCount, uri,
                                                                               timespan.TotalSeconds);
                                                                       }
                                                                   });

        // Execute the download under the retry policy
        return await retryPolicy.ExecuteAsync(ct => Download(uri, filePath, directory, fileExtension, client, ct).AsTask(), cancellationToken);
    }

    public async ValueTask<string?> DownloadAsStream(string uri, string filePath, HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken).NoSync();

        _logger.LogDebug("Downloading file from URI ({uri}). Saving to {path} ...", uri, filePath);

        try
        {
            await using (Stream responseStream = await client.GetStreamAsync(uri, cancellationToken).NoSync())
            {
                await _fileUtil.Write(filePath, responseStream, true, cancellationToken).NoSync();
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

    public void Dispose()
    {
        _httpClientCache.RemoveSync(nameof(FileDownloadUtil));
    }

    public ValueTask DisposeAsync()
    {
        return _httpClientCache.Remove(nameof(FileDownloadUtil));
    }
}