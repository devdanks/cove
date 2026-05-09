using System.Reflection;
using Cove.Api.Controllers;

namespace Cove.Tests;

public class FileOpsControllerTests
{
    [Fact]
    public void NormalizeLocalPath_OnWindows_RepairsDriveRelativeImportedPaths()
    {
        var method = typeof(FileOpsController).GetMethod("NormalizeLocalPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var normalized = Assert.IsType<string>(method.Invoke(null, ["E:test/Content/video.mp4"]));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.GetFullPath(@"E:\test\Content\video.mp4"), normalized);
        }
        else
        {
            Assert.Equal(Path.GetFullPath("E:test/Content/video.mp4"), normalized);
        }
    }
}
