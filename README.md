[![](https://img.shields.io/nuget/v/soenneker.utils.file.download.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.utils.file.download/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.utils.file.download/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.utils.file.download/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.utils.file.download.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.utils.file.download/)

# ![](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Utils.File.Download
### Provides a flexible utility for downloading files from specified URIs, with thread-safe concurrency and automatic file name conflict handling

## Installation

```
dotnet add package Soenneker.Utils.File.Download
```

## Usage

1. Register `IFileDownloadUtil` within DI (`Program.cs`).

```csharp
public static async Task Main(string[] args)
{
    ...
    builder.Services.AddFileDownloadUtilAsScoped();
}
```

2. Inject `IFileDownloadUtil`

Example:

```csharp
public class TestClass
{
    private readonly IFileDownloadUtil _util;

    public TestClass(IFileDownloadUtil util)
    {
        _util = util;
    }

    public async ValueTask Download()
    {
        string directory = "path/to/save/files";
        List<string> uris = new List<string>
        {
            "https://example.com/file1.jpg",
            "https://example.com/file2.jpg",
            "https://example.com/file3.jpg"
        };

        int maxConcurrentDownloads = 3;

        List<string> downloadedFiles = await _util.DownloadFiles(directory, uris, maxConcurrentDownloads);
    }
}

```