using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.File.Download.Abstract;

/// <summary>
/// Defines methods for streaming HTTP responses to files, including bounded concurrent downloads and retries.
/// </summary>
public interface IFileDownloadUtil : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Downloads multiple URIs into a directory with bounded concurrency.
    /// </summary>
    /// <param name="directory">The destination or root directory.</param>
    /// <param name="uris">The resource URIs.</param>
    /// <param name="maxConcurrentDownloads">The maximum simultaneous downloads.</param>
    /// <param name="log">True to emit operational logging.</param>
    /// <param name="cancellationToken">Signals that the operation should stop.</param>
    /// <returns>Paths of successfully downloaded files.</returns>
    ValueTask<List<string>> DownloadMultiple(string directory, List<string> uris, int maxConcurrentDownloads, bool log = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a URI to an explicit or generated path and returns the saved file name.
    /// </summary>
    /// <param name="uri">The remote resource URI.</param>
    /// <param name="filePath">An explicit destination path, or null to generate one.</param>
    /// <param name="directory">The destination or root directory.</param>
    /// <param name="fileExtension">The extension for a generated destination.</param>
    /// <param name="client">An optional HTTP client; null uses the configured client cache.</param>
    /// <param name="log">True to emit operational logging.</param>
    /// <param name="cancellationToken">Signals that the operation should stop.</param>
    /// <returns>The saved path, or null when the HTTP or file operation fails.</returns>
    /// <remarks>A generated destination requires <paramref name="fileExtension"/>. When <paramref name="directory"/> is also provided,
    /// the generated file is placed there; otherwise a temporary path is used. The caller retains ownership of a supplied <paramref name="client"/>.</remarks>
    ValueTask<string?> Download(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, bool log = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to download the given URI with a retry policy.
    /// </summary>
    /// <param name="uri">The resource URI.</param>
    /// <param name="filePath">Optional full file path to save to.</param>
    /// <param name="directory">Optional directory to auto-generate a filename in.</param>
    /// <param name="fileExtension">Optional extension to auto-generate a temp file.</param>
    /// <param name="client">Optional pre-configured HttpClient.</param>
    /// <param name="maxRetryAttempts">How many retries to perform after the initial attempt.</param>
    /// <param name="baseDelaySeconds">The base delay (in seconds) for exponential back-off.</param>
    /// <param name="log">Whether to log download progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Attempts to download the given URI with a retry policy.</returns>
    ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, int maxRetryAttempts = 3, double baseDelaySeconds = 2.0, bool log = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a URI with the utility's retry policy and returns the saved file name.
    /// </summary>
    /// <param name="uri">The remote resource URI.</param>
    /// <param name="filePath">An explicit destination path, or null to generate one.</param>
    /// <param name="directory">The destination or root directory.</param>
    /// <param name="fileExtension">The extension for a generated destination.</param>
    /// <param name="client">An optional HTTP client; null uses the configured client cache.</param>
    /// <param name="log">True to emit operational logging.</param>
    /// <param name="cancellationToken">Signals that the operation should stop.</param>
    /// <returns>The saved path, or null after retries are exhausted.</returns>
    /// <remarks>The method performs up to three retries after the initial attempt. The result is the saved path, not downloaded content.</remarks>
    ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null, HttpClient? client = null,
        bool log = true, CancellationToken cancellationToken = default) => DownloadWithRetry(uri, filePath, directory, fileExtension, client,
        maxRetryAttempts: 3, baseDelaySeconds: 2.0, log, cancellationToken);

    /// <summary>
    /// Downloads the content from the specified URI and saves it to the given file path as a stream asynchronously.
    /// </summary>
    /// <remarks>This method is asynchronous and may take time to complete depending on network conditions.
    /// Ensure that the specified file path is writable and that the caller has appropriate permissions.</remarks>
    /// <param name="uri">The URI from which to download the content. This parameter must be a valid, non-null URI string.</param>
    /// <param name="filePath">The file system path where the downloaded content will be saved. This parameter must be a valid, non-null file
    /// path.</param>
    /// <param name="client">An optional HttpClient instance to use for the download request. If null, a default HttpClient instance is used.</param>
    /// <param name="log">Whether to log download progress.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the download operation. The default value is
    /// CancellationToken.None.</param>
    /// <returns>A ValueTask that represents the asynchronous operation. The result contains the file path where the content was
    /// saved, or null if the download failed.</returns>
    ValueTask<string?> DownloadAsStream(string uri, string filePath, HttpClient? client = null, bool log = true, CancellationToken cancellationToken = default);
}
