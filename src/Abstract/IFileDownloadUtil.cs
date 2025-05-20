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

    ValueTask<string?> Download(string uri, string? filePath = null, string? directory = null, string? fileExtension = null,
        HttpClient? client = null, CancellationToken cancellationToken = default);

    
    ValueTask<string?> DownloadAsStream(string uri, string filePath, HttpClient? client = null, CancellationToken cancellationToken = default);
}