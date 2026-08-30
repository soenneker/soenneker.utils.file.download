[![](https://img.shields.io/nuget/v/soenneker.utils.file.download.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.utils.file.download/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.file.download/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.file.download/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.utils.file.download.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.utils.file.download/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.file.download/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.utils.file.download/actions/workflows/codeql.yml)

# Soenneker.Utils.File.Download

Streams HTTP responses to files, with generated destination paths, bounded multi-file concurrency, and optional retries.

## Installation

```bash
dotnet add package Soenneker.Utils.File.Download
```

## Registration

```csharp
builder.Services.AddFileDownloadUtilAsScoped();
```

Singleton registration is also available. The HTTP client cache remains singleton in either case; disposing a scoped downloader does not remove the shared cached client.

## Download one file

```csharp
string? savedPath = await downloader.Download(
    "https://cdn.example.com/report.csv",
    filePath: destinationPath,
    cancellationToken: cancellationToken);
```

Instead of an explicit path, provide `directory` and `fileExtension` to generate a unique file there, or provide only `fileExtension` to generate a temporary path:

```csharp
string? savedPath = await downloader.Download(
    uri,
    directory: downloadDirectory,
    fileExtension: ".pdf",
    cancellationToken: cancellationToken);
```

At least `filePath` or `fileExtension` is required. HTTP, stream, and filesystem failures return `null` and are logged when `log` is enabled. Requested cancellation throws `OperationCanceledException`.

Downloads are written to a temporary file beside the destination and moved into place only after the response body is complete. A failed or cancelled request therefore leaves an existing destination unchanged and removes its partial temporary file.

## Retry

```csharp
string? savedPath = await downloader.DownloadWithRetry(
    uri,
    filePath: destinationPath,
    maxRetryAttempts: 3,
    baseDelaySeconds: 2,
    cancellationToken: cancellationToken);
```

`maxRetryAttempts` is the number of retries after the initial request. Non-positive values are normalized to one retry. With a base of `2`, retry delays are 2, 4, and 8 seconds. A failed attempt returns `null` to the policy and is retried; cancellation is never retried.

## Download multiple files

```csharp
List<string> downloaded = await downloader.DownloadMultiple(
    downloadDirectory,
    uris,
    maxConcurrentDownloads: 4,
    cancellationToken: cancellationToken);
```

Each URI receives a unique destination derived from the URI. Per-file failures are logged and omitted from the result. Result order is completion-dependent and does not match input order. Cancellation stops the operation and throws.

## HTTP ownership and security

Pass a configured `HttpClient` when you need custom handlers, authentication, proxy behavior, timeouts, or SSRF controls. The caller retains ownership of a supplied client. When no client is supplied, the utility reuses its named client from the singleton cache.

This utility does not restrict URI schemes or hosts, limit redirects, or enforce a maximum response size. Validate untrusted URIs, prevent access to internal/link-local destinations, and enforce size limits in the HTTP pipeline before exposing downloads to user input. A `Content-Length` header is used for progress reporting only and is not trusted as an enforcement boundary.
