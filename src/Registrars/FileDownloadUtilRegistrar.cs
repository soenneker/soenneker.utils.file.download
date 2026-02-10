using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.HttpClientCache.Registrar;
using Soenneker.Utils.Path.Registrars;

namespace Soenneker.Utils.File.Download.Registrars;

/// <summary>
/// Provides a flexible utility for downloading files from specified URIs, with automatic file name conflict handling and asynchronous support
/// </summary>
public static class FileDownloadUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IFileDownloadUtil"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddFileDownloadUtilAsSingleton(this IServiceCollection services)
    {
        services.AddDirectoryUtilAsSingleton().AddHttpClientCacheAsSingleton().AddPathUtilAsSingleton().AddFileUtilAsSingleton().TryAddSingleton<IFileDownloadUtil, FileDownloadUtil>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="IFileDownloadUtil"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddFileDownloadUtilAsScoped(this IServiceCollection services)
    {
        services.AddDirectoryUtilAsScoped().AddHttpClientCacheAsSingleton().AddPathUtilAsScoped().AddFileUtilAsScoped().TryAddScoped<IFileDownloadUtil, FileDownloadUtil>();

        return services;
    }
}