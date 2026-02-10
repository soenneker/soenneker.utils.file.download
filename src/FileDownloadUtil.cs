using System;
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
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.HttpClientCache.Abstract;
using Soenneker.Utils.Path.Abstract;

namespace Soenneker.Utils.File.Download;

/// <inheritdoc cref="IFileDownloadUtil"/>
public sealed class FileDownloadUtil : IFileDownloadUtil
{
    private readonly ILogger<FileDownloadUtil> _logger;
    private readonly IHttpClientCache _httpClientCache;
    private readonly IFileUtil _fileUtil;
    private readonly IPathUtil _pathUtil;
    private readonly IDirectoryUtil _directoryUtil;

    private const int _bufferSize = 128 * 1024; // 128 KB

    // Small cache to avoid building Polly policies per call
    // Key = (maxRetries, baseDelaySecondsBits)
    private static readonly ConcurrentDictionary<(int maxRetries, long baseDelayBits), AsyncRetryPolicy<string?>> _retryPolicies = new();

    public FileDownloadUtil(ILogger<FileDownloadUtil> logger, IHttpClientCache httpClientCache, IFileUtil fileUtil, IPathUtil pathUtil, IDirectoryUtil directoryUtil)
    {
        _logger = logger;
        _httpClientCache = httpClientCache;
        _fileUtil = fileUtil;
        _pathUtil = pathUtil;
        _directoryUtil = directoryUtil;
    }

    public async ValueTask<List<string>> DownloadMultiple(string directory, List<string> uris, int maxConcurrentDownloads,
        CancellationToken cancellationToken = default)
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

                              string? downloaded = await Download(uri, filePath, null, null, client, ct)
                                  .NoSync();

                              if (downloaded is not null)
                                  results.Add(downloaded);
                          }
                          catch (OperationCanceledException) when (ct.IsCancellationRequested)
                          {
                              // expected
                          }
                          catch (Exception ex)
                          {
                              _logger.LogError(ex, "Error downloading file from {Uri}", uri);
                          }
                      })
                      .NoSync();

        // Bag -> List (one allocation). Use IsEmpty to avoid O(n) Count.
        return results.IsEmpty ? [] : results.ToList();
    }

    public async ValueTask<string?> Download(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, CancellationToken cancellationToken = default)
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
                await _directoryUtil.CreateIfDoesNotExist(dir, false, cancellationToken);

            await using var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: _bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken)
                                                     .NoSync();

            // CopyToAsync with explicit buffer size
            await input.CopyToAsync(fs, _bufferSize, cancellationToken)
                       .NoSync();

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from URI ({uri})", uri);
            return null;
        }
    }

    public async ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, int maxRetryAttempts = 3, double baseDelaySeconds = 2.0, CancellationToken cancellationToken = default)
    {
        client ??= await _httpClientCache.Get(nameof(FileDownloadUtil), cancellationToken: cancellationToken)
                                         .NoSync();

        AsyncRetryPolicy<string?> policy = GetOrCreateRetryPolicy(maxRetryAttempts, baseDelaySeconds);

        // No per-call policy allocation; pass uri via Context so onRetry doesn't close over it.
        var context = new Context
        {
            ["uri"] = uri
        };

        return await policy.ExecuteAsync((ctx, ct) => Download((string)ctx["uri"]!, filePath, directory, fileExtension, client, ct)
                               .AsTask(), context, cancellationToken)
                           .NoSync();
    }

    public ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, CancellationToken cancellationToken = default) => DownloadWithRetry(uri, filePath, directory, fileExtension, client,
        maxRetryAttempts: 3, baseDelaySeconds: 2.0, cancellationToken);

    public async ValueTask<string?> DownloadAsStream(string uri, string filePath, HttpClient? client = null, CancellationToken cancellationToken = default)
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

            await _fileUtil.Write(filePath, responseStream, true, cancellationToken)
                           .NoSync();

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from URI ({uri})", uri);
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

    private static AsyncRetryPolicy<string?> GetOrCreateRetryPolicy(int maxRetryAttempts, double baseDelaySeconds)
    {
        // Normalize key
        if (maxRetryAttempts <= 0)
            maxRetryAttempts = 1;

        if (baseDelaySeconds <= 0)
            baseDelaySeconds = 1;

        long bits = BitConverter.DoubleToInt64Bits(baseDelaySeconds);
        (int maxRetryAttempts, long bits) key = (maxRetryAttempts, bits);

        return _retryPolicies.GetOrAdd(key, static k =>
        {
            int retries = k.maxRetries;
            double baseSeconds = BitConverter.Int64BitsToDouble(k.baseDelayBits);

            return Policy<string?>.Handle<Exception>()
                                  .OrResult(static r => r is null)
                                  .WaitAndRetryAsync(retryCount: retries,
                                      sleepDurationProvider: retryAttempt =>
                                          TimeSpan.FromSeconds(Math.Pow(baseSeconds, retryAttempt)),
                                      onRetryAsync: static (outcome, timespan, retryCount, context) =>
                                      {
                                          // Keep policy cacheable: no closure over instance logger.
                                          // If you want logging, do it at the call site (or swap to a non-static policy creation per instance).
                                          return Task.CompletedTask;
                                      });
        });
    }
}