using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;
using Xunit.Abstractions;

namespace Soenneker.Utils.File.Download.Tests;

[Collection("Collection")]
public class FileDownloadUtilTests : FixturedUnitTest
{
    private readonly IFileDownloadUtil _util;

    public FileDownloadUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<IFileDownloadUtil>(true);
    }
}
