#pragma warning disable CA2227
#pragma warning disable CA1002

using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Feed dto.
/// </summary>
[XmlRoot(ElementName = "feed")]
public class FeedDto
{
    /// <summary>
    /// Gets or sets the feed id.
    /// </summary>
    [XmlElement(ElementName = "id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the updated timestamp.
    /// </summary>
    [XmlElement(ElementName = "updated")]
    public DateTime Updated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the list of links.
    /// </summary>
    [XmlElement(ElementName = "link")]
    public LinkDto[] Links { get; set; } = Array.Empty<LinkDto>();

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    [XmlElement(ElementName = "title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    [XmlElement(ElementName = "author")]
    public AuthorDto? Author { get; set; }

    /// <summary>
    /// Gets or sets the total results count.
    /// </summary>
    [XmlElement(ElementName = "totalResults", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public int? TotalResults { get; set; }

    /// <summary>
    /// Gets or sets the start index (0-based) of the current page.
    /// </summary>
    [XmlElement(ElementName = "startIndex", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public int? StartIndex { get; set; }

    /// <summary>
    /// Gets or sets the number of items per page.
    /// </summary>
    [XmlElement(ElementName = "itemsPerPage", Namespace = "http://a9.com/-/spec/opensearch/1.1/")]
    public int? ItemsPerPage { get; set; }

    /// <summary>
    /// Gets or sets the list of entries.
    /// </summary>
    [XmlElement(ElementName = "entry")]
    public List<EntryDto>? Entries { get; set; }

    /// <summary>
    /// Gets or sets the xmlns attribute.
    /// </summary>
    [XmlAttribute(AttributeName = "xmlns")]
    public string Xmlns { get; set; } = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// Gets or sets the OPDS page streaming extension namespace declaration.
    /// </summary>
    [XmlAttribute(AttributeName = "pse", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string XmlnsPse { get; set; } = PseConstants.Namespace;

    /// <summary>
    /// Gets or sets the OpenSearch namespace declaration.
    /// </summary>
    [XmlAttribute(AttributeName = "opensearch", Namespace = "http://www.w3.org/2000/xmlns/")]
    public string XmlnsOpenSearch { get; set; } = "http://a9.com/-/spec/opensearch/1.1/";

    /// <summary>
    /// Gets or sets the xml text.
    /// </summary>
    [XmlText]
    public string? Text { get; set; }
}
