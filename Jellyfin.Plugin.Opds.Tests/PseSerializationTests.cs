using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the serialization of the OPDS page streaming extension feed elements.
/// </summary>
public class PseSerializationTests
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string PseNamespace = "http://vaemendis.net/opds-pse/ns";

    [Fact]
    public void Feed_Declares_Pse_Namespace()
    {
        var xml = Serialize(CreateFeed());

        Assert.Contains("xmlns:pse=\"" + PseNamespace + "\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamLink_Serializes_PageCount_In_Pse_Namespace()
    {
        var link = GetStreamLink(Serialize(CreateFeed(35)));

        Assert.Equal("35", link.Attribute(XName.Get("count", PseNamespace))?.Value);
        Assert.Equal("image/jpeg", link.Attribute("type")?.Value);
        Assert.Contains("{pageNumber}", link.Attribute("href")?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamLink_Omits_Progress_When_Null()
    {
        var link = GetStreamLink(Serialize(CreateFeed(null)));

        Assert.Null(link.Attribute(XName.Get("count", PseNamespace)));
        Assert.Null(link.Attribute(XName.Get("lastRead", PseNamespace)));
        Assert.Null(link.Attribute(XName.Get("lastReadDate", PseNamespace)));
    }

    [Fact]
    public void StreamLink_Serializes_Reading_Progress_In_Pse_Namespace()
    {
        var lastReadDate = new DateTime(2010, 1, 10, 10, 1, 11, DateTimeKind.Utc);
        var link = GetStreamLink(Serialize(CreateFeed(35, 10, lastReadDate)));

        Assert.Equal("10", link.Attribute(XName.Get("lastRead", PseNamespace))?.Value);
        Assert.Equal("2010-01-10T10:01:11Z", link.Attribute(XName.Get("lastReadDate", PseNamespace))?.Value);
    }

    [Fact]
    public void LastReadDate_Roundtrips_As_Atom_Date_Construct()
    {
        var link = new LinkDto(PseConstants.StreamRel, "/opds/stream/1?page={pageNumber}", "image/jpeg");

        link.LastReadDateStr = "2010-01-10T10:01:11Z";
        Assert.Equal(new DateTime(2010, 1, 10, 10, 1, 11, DateTimeKind.Utc), link.LastReadDate);
        Assert.Equal("2010-01-10T10:01:11Z", link.LastReadDateStr);

        link.LastReadDateStr = null;
        Assert.Null(link.LastReadDate);

        link.LastReadStr = "10";
        Assert.Equal(10, link.LastRead);
        Assert.Equal("10", link.LastReadStr);

        link.LastReadStr = null;
        Assert.Null(link.LastRead);
    }

    [Fact]
    public void UpdateTime_Roundtrips_Via_UpdateTimeStr()
    {
        var link = new LinkDto(PseConstants.StreamRel, "/opds/stream/1?page={pageNumber}", "image/jpeg");
        var date = new DateTime(2026, 10, 20, 15, 30, 0, DateTimeKind.Utc);

        link.UpdateTime = date;
        Assert.NotNull(link.UpdateTimeStr);

        link.UpdateTime = null;
        Assert.Null(link.UpdateTimeStr);

        link.UpdateTimeStr = "2026-10-20T15:30:00Z";
        Assert.NotNull(link.UpdateTime);
        Assert.Equal(date, link.UpdateTime.Value.ToUniversalTime());

        link.UpdateTimeStr = null;
        Assert.Null(link.UpdateTime);
    }

    [Fact]
    public void PageCount_Roundtrips_Through_Serialization()
    {
        var link = new LinkDto(PseConstants.StreamRel, "/opds/stream/1?page={pageNumber}", "image/jpeg")
        {
            PageCount = 35
        };

        Assert.Equal("35", link.PageCountStr);
        Assert.Equal(35, link.PageCount);

        link.PageCountStr = "42";
        Assert.Equal(42, link.PageCount);

        link.PageCountStr = null;
        Assert.Null(link.PageCount);
    }

    private static XElement GetStreamLink(string xml)
    {
        return XDocument.Parse(xml)
            .Descendants(XName.Get("link", AtomNamespace))
            .Single(e => string.Equals(e.Attribute("rel")?.Value, PseConstants.StreamRel, StringComparison.Ordinal));
    }

    private static FeedDto CreateFeed(long? pageCount = 0, long? lastRead = null, DateTime? lastReadDate = null)
    {
        return new FeedDto
        {
            Id = "test",
            Title = "Test",
            Entries = new List<EntryDto>
            {
                new EntryDto("Book", "1", DateTime.UtcNow)
                {
                    Links = new List<LinkDto>
                    {
                        new LinkDto(PseConstants.StreamRel, "/opds/stream/1?page={pageNumber}&width={maxWidth}", "image/jpeg")
                        {
                            PageCount = pageCount,
                            LastRead = lastRead,
                            LastReadDate = lastReadDate
                        }
                    }
                }
            }
        };
    }

    private static string Serialize(FeedDto feed)
    {
        using var writer = new StringWriter();
        XmlHelper.Create(typeof(FeedDto), AtomNamespace).Serialize(writer, feed);
        return writer.ToString();
    }
}
