using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.File.Download.Abstract;

/// <summary>
/// Defines methods for downloading files asynchronously with thread-safe and rate-limited access.
/// </summary>
public interface IFileDownloadUtil : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Downloads multiple files asynchronously with rate-limiting and thread-safety for storing unique file paths.
    /// </summary>
    /// <param name="directory">The directory where files will be saved.</param>
    /// <param name="uris">A list of URIs representing the files to be downloaded.</param>
    /// <param name="maxConcurrentDownloads">The maximum number of concurrent downloads allowed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing a list of file paths for successfully downloaded files.
    /// </returns>
    ValueTask<List<string>> DownloadMultiple(string directory, List<string> uris, int maxConcurrentDownloads, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the content from the specified URI and optionally saves it to a file.
    /// </summary>
    /// <remarks>If both 'filePath' and 'directory' are null, the method downloads the content but does not
    /// save it to disk. If a directory is specified without a file path, a file will be created in the directory using
    /// the provided file extension, if any. The caller is responsible for disposing the provided HttpClient instance if
    /// one is supplied.</remarks>
    /// <param name="uri">The URI from which to download the content. This parameter must be a valid, absolute URI.</param>
    /// <param name="filePath">The optional file path where the downloaded content will be saved. If not specified, the content will not be
    /// saved to a file unless a directory is provided.</param>
    /// <param name="directory">The optional directory in which to save the file if 'filePath' is not specified. If both 'filePath' and
    /// 'directory' are null, the content will not be saved.</param>
    /// <param name="fileExtension">The optional file extension to use when saving the file. The extension should include the leading dot (for
    /// example, ".txt").</param>
    /// <param name="client">An optional HttpClient instance to use for the download operation. If null, a new HttpClient instance will be
    /// created and used.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the download operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the downloaded content as a string,
    /// or null if the download fails.</returns>
    ValueTask<string?> Download(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to download the given URI with a retry policy.
    /// </summary>
    /// <param name="uri">The resource URI.</param>
    /// <param name="filePath">Optional full file path to save to.</param>
    /// <param name="directory">Optional directory to auto-generate a filename in.</param>
    /// <param name="fileExtension">Optional extension to auto-generate a temp file.</param>
    /// <param name="client">Optional pre-configured HttpClient.</param>
    /// <param name="maxRetryAttempts">How many times to retry on failure or null result.</param>
    /// <param name="baseDelaySeconds">The base delay (in seconds) for exponential back-off.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, int maxRetryAttempts = 3, double baseDelaySeconds = 2.0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the content from the specified URI, retrying the operation on failure up to a default number of
    /// attempts.
    /// </summary>
    /// <remarks>The method automatically retries the download operation up to three times with a default
    /// delay between attempts. If a file path is provided, the content is saved to the specified location in addition
    /// to being returned as a string.</remarks>
    /// <param name="uri">The URI from which to download the content. This value must be a valid, absolute URI string.</param>
    /// <param name="filePath">The optional file path where the downloaded content will be saved. If not specified, the content is not saved to
    /// a file.</param>
    /// <param name="directory">The optional directory in which to save the file if <paramref name="filePath"/> is specified. If not provided,
    /// the default directory is used.</param>
    /// <param name="fileExtension">The optional file extension to use when saving the downloaded content. If not specified, the file extension is
    /// determined from the content type.</param>
    /// <param name="client">An optional <see cref="HttpClient"/> instance to use for the download. If not provided, a new instance is
    /// created and disposed after use.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the download operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the downloaded content as a string,
    /// or <see langword="null"/> if the download fails.</returns>
    ValueTask<string?> DownloadWithRetry(string uri, string? filePath = null, string? directory = null, string? fileExtension = null, HttpClient? client = null,
        CancellationToken cancellationToken = default) => DownloadWithRetry(uri, filePath, directory, fileExtension, client, maxRetryAttempts: 3,
        baseDelaySeconds: 2.0, cancellationToken);

    /// <summary>
    /// Downloads the content from the specified URI and saves it to the given file path as a stream asynchronously.
    /// </summary>
    /// <remarks>This method is asynchronous and may take time to complete depending on network conditions.
    /// Ensure that the specified file path is writable and that the caller has appropriate permissions.</remarks>
    /// <param name="uri">The URI from which to download the content. This parameter must be a valid, non-null URI string.</param>
    /// <param name="filePath">The file system path where the downloaded content will be saved. This parameter must be a valid, non-null file
    /// path.</param>
    /// <param name="client">An optional HttpClient instance to use for the download request. If null, a default HttpClient instance is used.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the download operation. The default value is
    /// CancellationToken.None.</param>
    /// <returns>A ValueTask that represents the asynchronous operation. The result contains the file path where the content was
    /// saved, or null if the download failed.</returns>
    ValueTask<string?> DownloadAsStream(string uri, string filePath, HttpClient? client = null, CancellationToken cancellationToken = default);
}