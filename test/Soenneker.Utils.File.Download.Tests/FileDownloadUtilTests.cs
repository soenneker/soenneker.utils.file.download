using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Tests.HostedUnit;


namespace Soenneker.Utils.File.Download.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public class FileDownloadUtilTests : HostedUnitTest
{
    private readonly IFileDownloadUtil _util;

    public FileDownloadUtilTests(Host host) : base(host)
    {
        _util = Resolve<IFileDownloadUtil>(true);
    }

    [Test]
    public void Default()
    {

    }
}
