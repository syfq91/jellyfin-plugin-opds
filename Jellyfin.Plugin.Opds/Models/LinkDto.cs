using System;
using System.ComponentModel;
using System.Globalization;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// The link dto.
/// </summary>
[XmlRoot(ElementName = "link")]
public class LinkDto
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkDto"/> class.
    /// </summary>
    protected LinkDto()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkDto"/> class.
    /// </summary>
    /// <param name="href">The link href.</param>
    /// <param name="type">The link type.</param>
    public LinkDto(string href, string type)
    {
        Href = href;
        Type = type;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkDto"/> class.
    /// </summary>
    /// <param name="rel">The link rel.</param>
    /// <param name="href">The link href.</param>
    /// <param name="type">The link type.</param>
    public LinkDto(string rel, string href, string type)
    {
        Rel = rel;
        Href = href;
        Type = type;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkDto"/> class.
    /// </summary>
    /// <param name="rel">The link rel.</param>
    /// <param name="href">The link href.</param>
    /// <param name="type">The link type.</param>
    /// <param name="title">The link title.</param>
    public LinkDto(string rel, string href, string type, string title)
    {
        Rel = rel;
        Href = href;
        Type = type;
        Title = title;
    }

    /// <summary>
    /// Gets or sets the link rel.
    /// </summary>
    [XmlAttribute(AttributeName = "rel")]
    public string? Rel { get; set; }

    /// <summary>
    /// Gets or sets the link destination.
    /// </summary>
    [XmlAttribute(AttributeName = "href")]
    public string? Href { get; set; }

    /// <summary>
    /// Gets or sets the link type.
    /// </summary>
    [XmlAttribute(AttributeName = "type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the link title.
    /// </summary>
    [XmlAttribute(AttributeName = "title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the file size.
    /// </summary>
    [XmlIgnore]
    public long? Length { get; set; }

    /// <summary>
    /// Gets or sets the string representation of the file size.
    /// </summary>
    /// <remarks>
    /// Xml is unable to serialize "Complex Types", so this is used for serialization.
    /// </remarks>
    [XmlAttribute(AttributeName = "length")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? LengthStr
    {
        get => Length?.ToString(CultureInfo.InvariantCulture);
        set
        {
            switch (value)
            {
                case null:
                    Length = null;
                    break;
                case var l:
                {
                    if (long.TryParse(l, out var val))
                    {
                        Length = val;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the number of pages of the document, as used by the
    /// OPDS page streaming extension.
    /// </summary>
    [XmlIgnore]
    public long? PageCount { get; set; }

    /// <summary>
    /// Gets or sets the string representation of the page count.
    /// </summary>
    /// <remarks>
    /// Xml is unable to serialize "Complex Types", so this is used for serialization.
    /// </remarks>
    [XmlAttribute(AttributeName = "count", Namespace = PseConstants.Namespace)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? PageCountStr
    {
        get => PageCount?.ToString(CultureInfo.InvariantCulture);
        set
        {
            switch (value)
            {
                case null:
                    PageCount = null;
                    break;
                case var l:
                {
                    if (long.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                    {
                        PageCount = val;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the last page read for this document, as used by the
    /// OPDS page streaming extension. The numbering starts at 1.
    /// </summary>
    [XmlIgnore]
    public long? LastRead { get; set; }

    /// <summary>
    /// Gets or sets the string representation of the last read page.
    /// </summary>
    /// <remarks>
    /// Xml is unable to serialize "Complex Types", so this is used for serialization.
    /// </remarks>
    [XmlAttribute(AttributeName = "lastRead", Namespace = PseConstants.Namespace)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? LastReadStr
    {
        get => LastRead?.ToString(CultureInfo.InvariantCulture);
        set
        {
            switch (value)
            {
                case null:
                    LastRead = null;
                    break;
                case var l:
                {
                    if (long.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
                    {
                        LastRead = val;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the date of when the <see cref="LastRead"/> attribute was last updated.
    /// </summary>
    [XmlIgnore]
    public DateTime? LastReadDate { get; set; }

    /// <summary>
    /// Gets or sets the string representation of the last read date.
    /// </summary>
    /// <remarks>
    /// Xml is unable to serialize "Complex Types", so this is used for serialization.
    /// The value conforms to Atom's date construct.
    /// </remarks>
    [XmlAttribute(AttributeName = "lastReadDate", Namespace = PseConstants.Namespace)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? LastReadDateStr
    {
        get => LastReadDate?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        set
        {
            switch (value)
            {
                case null:
                    LastReadDate = null;
                    break;
                case var l:
                {
                    if (DateTime.TryParse(
                        l,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var val))
                    {
                        LastReadDate = val;
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the book updated time.
    /// </summary>
    [XmlIgnore]
    public DateTime? UpdateTime { get; set; }

    /// <summary>
    /// Gets or sets the string representation of the file size.
    /// </summary>
    /// <remarks>
    /// Xml is unable to serialize "Complex Types", so this is used for serialization.
    /// </remarks>
    [XmlAttribute(AttributeName = "mtime")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? UpdateTimeSTr
    {
        get => UpdateTime?.ToString(CultureInfo.InvariantCulture);
        set
        {
            switch (value)
            {
                case null:
                    UpdateTime = null;
                    break;
                case var l:
                {
                    if (DateTime.TryParse(l, out var val))
                    {
                        UpdateTime = val;
                    }

                    break;
                }
            }
        }
    }
}
