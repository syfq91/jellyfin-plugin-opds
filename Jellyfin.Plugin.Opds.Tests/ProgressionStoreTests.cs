using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the plugin-side store of progression report fidelity.
/// </summary>
public class ProgressionStoreTests : IDisposable
{
    private static readonly DateTime Modified = new(2026, 1, 27, 11, 0, 0, DateTimeKind.Utc);

    private readonly string _directory;
    private readonly string _filePath;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _bookId = Guid.NewGuid();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionStoreTests"/> class.
    /// </summary>
    public ProgressionStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-progression-store-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "store.json");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void SaveAndGet_Round_Trips_Entry()
    {
        var store = new ProgressionStore(_filePath);
        var entry = CreateEntry(Modified, 175000);

        store.Save(_userId, _bookId, entry);

        var stored = store.Get(_userId, _bookId);
        Assert.NotNull(stored);
        Assert.Equal(Modified, stored!.Modified);
        Assert.Equal(175000, stored.PositionTicks);
        Assert.Equal("urn:uuid:device-1", stored.Device!.Id);
        Assert.Equal("Thorium", stored.Device.Name);
        Assert.Equal("Chapter 3", stored.Title);
        Assert.Equal(new List<string> { "#page=18" }, stored.References);
    }

    [Fact]
    public void Get_Returns_Null_When_Nothing_Stored()
    {
        var store = new ProgressionStore(_filePath);

        Assert.Null(store.Get(_userId, _bookId));
    }

    [Fact]
    public void Save_Overwrites_Entry_Of_Same_User_And_Book()
    {
        var store = new ProgressionStore(_filePath);
        store.Save(_userId, _bookId, CreateEntry(Modified, 100000));

        var newer = CreateEntry(Modified.AddDays(1), 200000);
        newer.Device = new ProgressionDevice { Id = "urn:uuid:device-2", Name = "Komga" };
        store.Save(_userId, _bookId, newer);

        var stored = store.Get(_userId, _bookId);
        Assert.NotNull(stored);
        Assert.Equal(200000, stored!.PositionTicks);
        Assert.Equal("urn:uuid:device-2", stored.Device!.Id);
    }

    [Fact]
    public void Entries_Are_Independent_Per_User_And_Book()
    {
        var store = new ProgressionStore(_filePath);
        store.Save(_userId, _bookId, CreateEntry(Modified, 100000));

        Assert.NotNull(store.Get(_userId, _bookId));
        Assert.Null(store.Get(Guid.NewGuid(), _bookId));
        Assert.Null(store.Get(_userId, Guid.NewGuid()));
    }

    [Fact]
    public void Persists_Entries_Across_Instances()
    {
        new ProgressionStore(_filePath).Save(_userId, _bookId, CreateEntry(Modified, 175000));

        var reopened = new ProgressionStore(_filePath);

        var stored = reopened.Get(_userId, _bookId);
        Assert.NotNull(stored);
        Assert.Equal("Chapter 3", stored!.Title);
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_An_Empty_Store()
    {
        File.WriteAllText(_filePath, "{ this is not json");

        var store = new ProgressionStore(_filePath);

        Assert.Null(store.Get(_userId, _bookId));

        // The store keeps working after the bad file was replaced.
        store.Save(_userId, _bookId, CreateEntry(Modified, 100000));
        Assert.NotNull(new ProgressionStore(_filePath).Get(_userId, _bookId));
    }

    [Fact]
    public void Evicts_Oldest_Entry_Beyond_The_Cap()
    {
        var store = new ProgressionStore(_filePath, maxEntries: 3);
        var firstBookId = Guid.NewGuid();

        store.Save(_userId, firstBookId, CreateEntry(Modified, 100000));
        store.Save(_userId, Guid.NewGuid(), CreateEntry(Modified.AddDays(1), 100000));
        store.Save(_userId, Guid.NewGuid(), CreateEntry(Modified.AddDays(2), 100000));
        store.Save(_userId, Guid.NewGuid(), CreateEntry(Modified.AddDays(3), 100000));

        Assert.Null(store.Get(_userId, firstBookId));

        // The eviction also survives a reload of the file.
        Assert.Null(new ProgressionStore(_filePath, maxEntries: 3).Get(_userId, firstBookId));
    }

    private static ProgressionStoreEntry CreateEntry(DateTime modified, long positionTicks)
    {
        return new ProgressionStoreEntry
        {
            Modified = modified,
            PositionTicks = positionTicks,
            Device = new ProgressionDevice { Id = "urn:uuid:device-1", Name = "Thorium" },
            Title = "Chapter 3",
            References = new List<string> { "#page=18" }
        };
    }
}
