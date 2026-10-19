using Jellyfin.Plugin.Opds.Models;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the conversion of Jellyfin book progress to the last read page.
/// </summary>
public class PseProgressTests
{
    [Theory]
    [InlineData(50000, 35, 5)]
    [InlineData(10000, 35, 1)]
    [InlineData(350000, 35, 35)]
    [InlineData(500000, 35, 35)]
    [InlineData(12345, 35, 1)]
    public void Converts_Progress_To_LastRead_Page(long playbackPositionTicks, int pageCount, long expected)
    {
        Assert.Equal(expected, PseConstants.GetLastRead(playbackPositionTicks, pageCount));
    }

    [Theory]
    [InlineData(0, 35)]
    [InlineData(-1, 35)]
    [InlineData(50000, 0)]
    public void Returns_Null_When_Not_Read(long playbackPositionTicks, int pageCount)
    {
        Assert.Null(PseConstants.GetLastRead(playbackPositionTicks, pageCount));
    }
}
