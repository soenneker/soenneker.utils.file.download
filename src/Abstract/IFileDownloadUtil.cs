using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Utils.File.Download.Abstract;

/// <summary>
/// Defines methods for downloading files asynchronously with thread-safe and rate-limited access.
/// </summary>
public interface IFileDownloadUtil
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
    ValueTask<List<string>> DownloadFiles(string directory, List<string> uris, int maxConcurrentDownloads, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a single file asynchronously, ensuring a unique file path and thread-safe access.
    /// </summary>
    /// <param name="directory">The directory where the file will be saved.</param>
    /// <param name="uri">The URI representing the file to be downloaded.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing the file path of the downloaded file, or null if the download fails.
    /// </returns>
    ValueTask<string?> DownloadFile(string directory, string uri, CancellationToken cancellationToken = default);

    ValueTask<string?> DownloadFile(string uri, string directory, string fileExtension, CancellationToken cancellationToken = default);

    ValueTask<string?> DownloadFileAsStream(string uri, string filePath, HttpClient? client = null, CancellationToken cancellationToken = default);
}