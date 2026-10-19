using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Opds.Services;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the natural sort ordering of page names.
/// </summary>
public class NaturalSortComparerTests
{
    [Theory]
    [InlineData(new[] { "10.jpg", "2.jpg", "1.jpg" }, new[] { "1.jpg", "2.jpg", "10.jpg" })]
    [InlineData(new[] { "page10.png", "page2.png", "page1.png" }, new[] { "page1.png", "page2.png", "page10.png" })]
    [InlineData(new[] { "001.jpg", "002.jpg", "010.jpg" }, new[] { "001.jpg", "002.jpg", "010.jpg" })]
    [InlineData(new[] { "2.jpg", "02.jpg" }, new[] { "2.jpg", "02.jpg" })]
    [InlineData(new[] { "B.jpg", "a.jpg" }, new[] { "a.jpg", "B.jpg" })]
    [InlineData(new[] { "1.jpg", "1.png" }, new[] { "1.jpg", "1.png" })]
    public void Orders_Naturally(string[] input, string[] expected)
    {
        var ordered = input.OrderBy(key => key, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(expected, ordered);
    }

    [Fact]
    public void Handles_Empty_List()
    {
        var ordered = new List<string>().OrderBy(key => key, NaturalSortComparer.Instance).ToArray();

        Assert.Empty(ordered);
    }
}
