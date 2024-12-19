using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;


namespace Soenneker.Utils.File.Download.Tests;

[Collection("Collection")]
public class FileDownloadUtilTests : FixturedUnitTest
{
    private readonly IFileDownloadUtil _util;

    public FileDownloadUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IFileDownloadUtil>(true);
    }

    [Fact]
    public void Default()
    {

    }
}
