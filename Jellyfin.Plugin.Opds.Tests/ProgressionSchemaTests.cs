using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.Opds.Models;
using Json.Schema;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Validates the JSON documents served by the plugin against the schemas
/// published by the OPDS community drafts.
/// </summary>
public class ProgressionSchemaTests
{
    // Schemas register their $id in a global registry, so each one may only
    // be loaded once per test run.
    private static readonly JsonSchema ProgressionSchema = LoadSchema("progression.schema.json");
    private static readonly JsonSchema AuthenticationSchema = LoadSchema("authentication.schema.json");

    private static readonly DateTime Modified = new(2026, 1, 27, 11, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Progression_Documents_Conform_To_Draft_Schema()
    {
        var schema = ProgressionSchema;

        // A full document, as served when a progression is known.
        var full = ProgressionSerializer.Serialize(new ProgressionDocument
        {
            Title = "A Practical Guide to Climate-Resilient Gardening",
            Modified = Modified,
            Device = new ProgressionDevice
            {
                Id = "urn:uuid:f81d4fae-7dec-11d0-a765-00a0c91e6bf6",
                Name = "EPUB on iPhone"
            },
            Progression = 0.72,
            References = new List<string> { "#page=78" }
        });

        // A minimal document with only the required members.
        var minimal = ProgressionSerializer.Serialize(new ProgressionDocument
        {
            Modified = Modified,
            Device = new ProgressionDevice
            {
                Id = "urn:uuid:f81d4fae-7dec-11d0-a765-00a0c91e6bf6",
                Name = "EPUB on iPhone"
            },
            Progression = 0.5
        });

        AssertConforms(schema, "full progression document", full);
        AssertConforms(schema, "minimal progression document", minimal);
    }

    [Fact]
    public void Progression_Schema_Rejects_Documents_Without_Required_Members()
    {
        // Guards against the schema validation passing vacuously.
        var schema = ProgressionSchema;
        using var document = JsonDocument.Parse("""{ "title": "missing everything else" }""");

        Assert.False(schema.Evaluate(document.RootElement).IsValid);
    }

    [Fact]
    public void Authentication_Documents_Conform_To_Draft_Schema()
    {
        var schema = AuthenticationSchema;
        var document = ProgressionSerializer.Serialize(
            AuthenticationDocumentBuilder.Create("https://jellyfin.example"));

        AssertConforms(schema, "authentication document", document);
    }

    private static JsonSchema LoadSchema(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Schemas", fileName);
        return JsonSchema.FromText(File.ReadAllText(path));
    }

    private static void AssertConforms(JsonSchema schema, string description, string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = schema.Evaluate(document.RootElement);
        Assert.True(results.IsValid, $"The {description} does not conform to the draft schema: {json}");
    }
}
