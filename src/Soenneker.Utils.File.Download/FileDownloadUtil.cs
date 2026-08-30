using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.HttpClientCache.Abstract;
using Soenneker.Utils.Path.Abstract;

namespace Soenneker.Utils.File.Download;

/// <inheritdoc cref="IFileDownloadUtil"/>
public sealed class FileDownloadUtil : IFileDownloadUtil
{
    private readonly ILogger<FileDownloadUtil> _logger;
    private readonly IHttpClientCache _httpClientCache;
    private readonly IPathUtil _pathUtil;
    private readonly IDirectoryUtil _directoryUtil;

    private const int _bufferSize = 128 * 1024; // 128 KB
    private const int _maxCachedRetryPolicies = 32;
    private static readonly TimeSpan _progressLogInterval = TimeSpan.FromSeconds(2);

    // Small cache to avoid building Polly policies per call
    // Key = (maxRetries, baseDelaySecondsBits)
    private static readonly ConcurrentDictionary<(int maxRetries, long baseDelayBits), AsyncRetryPolicy<string?>> _retryPolicies = new();

    public FileDownloadUtil(ILogger<FileDownloadUtil> logger, IHttpClientCache httpClientCache, IPathUtil pathUtil, IDirectoryUtil directoryUtil)
    {
        _logger = logger;
        _httpClientCache = httpClientCache;
        _pathUtil = pathUtil;
        _directoryUtil = directoryUtil;
    }

    public async ValueTask<List<string>> DownloadMultiple(string directory, List<string> uris, int maxConcurrentDownloads,
        bool log = true, CancellationToken cancellationToken = default)
    {
        if (uris.Count == 0)
            return [];

        HttpClient client = await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken)
                                                  .NoSync();

        var results = new ConcurrentBag<string>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrentDownloads,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(uris, options, async (uri, ct) =>
                      {
                          try
                          {
                              string filePath = await _pathUtil.GetUniqueFilePathFromUri(directory, uri, ct)
                                                               .NoSync();

                              string? downloaded = await Download(uri, filePath, null, null, client, log, ct)
                                  .NoSync();

                              if (downloaded is not null)
                                  results.Add(downloaded);
                          }
                          catch (OperationCanceledException) when (ct.IsCancellationRequested)
                          {
                              throw;
                          }
                          catch (Exception ex)
                          {
                              if (log)
                                  _logger.LogError(ex, "Error downloading file from {Uri}", uri);
                          }
                      })
                      .NoSync();

        // Bag -> List (one allocation). Use IsEmpty to avoid O(n) Count.
        return results.IsEmpty ? [] : results.ToList();
    }

    public async ValueTask<string?> Download(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, bool log = true, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken)
                                         .NoSync();

        if (filePath is null)
        {
            if (directory is not null && fileExtension is not null)
                filePath = await _pathUtil.GetRandomUniqueFilePath(directory, fileExtension, cancellationToken)
                                          .NoSync();
            else if (fileExtension is not null)
                filePath = await _pathUtil.GetRandomTempFilePath(fileExtension, cancellationToken)
                                          .NoSync();
            else
                throw new ArgumentException("Either filePath or fileExtension must be provided.");
        }

        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                                                             .NoSync();

            response.EnsureSuccessStatusCode();

            string? dir = System.IO.Path.GetDirectoryName(filePath);

            if (dir is not null)
                await _directoryUtil.Create(dir, false, cancellationToken).NoSync();

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                     .NoSync();

            await CopyToFile(uri, filePath, input, response.Content.Headers.ContentLength, log, cancellationToken)
                .NoSync();

            return filePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (log)
                _logger.LogError(ex, "Failed to download file from URI ({uri})", uri);

            return null;
        }
    }

    public async ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, int maxRetryAttempts = 3, double baseDelaySeconds = 2.0, bool log = true,
        CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken)
                                         .NoSync();

        AsyncRetryPolicy<string?> policy = GetOrCreateRetryPolicy(maxRetryAttempts, baseDelaySeconds);

        // No per-call policy allocation; pass uri via Context so onRetry doesn't close over it.
        var context = new Context
        {
            ["uri"] = uri
        };

        return await policy.ExecuteAsync((ctx, ct) => Download((string)ctx["uri"]!, filePath, directory, fileExtension, client, log, ct)
                               .AsTask(), context, cancellationToken)
                           .NoSync();
    }

    public ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, bool log = true, CancellationToken cancellationToken = default) => DownloadWithRetry(uri, filePath, directory, fileExtension,
        client, maxRetryAttempts: 3, baseDelaySeconds: 2.0, log, cancellationToken);

    public async ValueTask<string?> DownloadAsStream(string uri, string filePath, HttpClient? client = null, bool log = true,
        CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken)
                                         .NoSync();

        try
        {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                                                             .NoSync();

            response.EnsureSuccessStatusCode();

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                              .NoSync();

            string? dir = System.IO.Path.GetDirectoryName(filePath);

            if (dir is not null)
                await _directoryUtil.Create(dir, false, cancellationToken).NoSync();

            await CopyToFile(uri, filePath, responseStream, response.Content.Headers.ContentLength, log, cancellationToken)
                .NoSync();

            return filePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (log)
                _logger.LogError(ex, "Failed to download file from URI ({uri})", uri);

            return null;
        }
    }

    private async ValueTask CopyToFile(string uri, string filePath, Stream source, long? contentLength, bool log, CancellationToken cancellationToken)
    {
        string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".download";
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);
        long totalBytesRead = 0;
        DateTimeOffset nextProgressLogAt = DateTimeOffset.UtcNow.Add(_progressLogInterval);

        if (log)
        {
            if (contentLength is > 0)
                _logger.LogInformation("Starting download from {uri} to {filePath} ({contentLength} bytes)", uri, filePath, contentLength.Value);
            else
                _logger.LogInformation("Starting download from {uri} to {filePath} (unknown size)", uri, filePath);
        }

        try
        {
            await using (var fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    int bytesRead = await source.ReadAsync(buffer.AsMemory(0, _bufferSize), cancellationToken)
                                                .NoSync();

                    if (bytesRead == 0)
                        break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                                    .NoSync();

                    totalBytesRead += bytesRead;

                    if (!log)
                        continue;

                    DateTimeOffset now = DateTimeOffset.UtcNow;

                    if (now < nextProgressLogAt)
                        continue;

                    nextProgressLogAt = now.Add(_progressLogInterval);

                    if (contentLength is > 0)
                    {
                        int percentage = (int) Math.Min(100, totalBytesRead * 100 / contentLength.Value);
                        _logger.LogInformation("Download progress for {uri}: {percentage}% ({bytesDownloaded}/{totalBytes} bytes)", uri, percentage,
                            totalBytesRead, contentLength.Value);
                    }
                    else
                    {
                        _logger.LogInformation("Download progress for {uri}: {bytesDownloaded} bytes", uri, totalBytesRead);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            System.IO.File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            System.IO.File.Delete(temporaryPath);
        }

        if (log)
            _logger.LogInformation("Finished download from {uri} to {filePath} ({bytesDownloaded} bytes)", uri, filePath, totalBytesRead);
    }

    /// <summary>
    /// Releases resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Asynchronously releases resources used by the current instance.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private static AsyncRetryPolicy<string?> GetOrCreateRetryPolicy(int maxRetryAttempts, double baseDelaySeconds)
    {
        // Normalize key
        if (maxRetryAttempts <= 0)
            maxRetryAttempts = 1;

        if (baseDelaySeconds <= 0)
            baseDelaySeconds = 1;

        long bits = BitConverter.DoubleToInt64Bits(baseDelaySeconds);
        (int maxRetryAttempts, long bits) key = (maxRetryAttempts, bits);

        if (_retryPolicies.TryGetValue(key, out AsyncRetryPolicy<string?>? cached))
            return cached;

        AsyncRetryPolicy<string?> created = CreateRetryPolicy(key);

        if (_retryPolicies.Count >= _maxCachedRetryPolicies)
            return created;

        return _retryPolicies.GetOrAdd(key, created);
    }

    private static AsyncRetryPolicy<string?> CreateRetryPolicy((int maxRetries, long baseDelayBits) key)
    {
        int retries = key.maxRetries;
        double baseSeconds = BitConverter.Int64BitsToDouble(key.baseDelayBits);

        return Policy<string?>.Handle<Exception>(static exception => exception is not OperationCanceledException)
                              .OrResult(static r => r is null)
                              .WaitAndRetryAsync(retryCount: retries,
                                  sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(baseSeconds, retryAttempt)),
                                  onRetryAsync: static (outcome, timespan, retryCount, context) => Task.CompletedTask);
    }
}
