using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the mapping of Jellyfin user data to OPDS progression documents.
/// </summary>
public class ProgressionServiceTests
{
    private static readonly DateTime Modified = new(2026, 1, 27, 11, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUserDataManager> _userDataManager = new();
    private readonly Mock<IPageStreamService> _pageStreamService = new();
    private readonly Mock<IProgressionStore> _progressionStore = new();
    private readonly User _user;
    private readonly Book _book;
    private readonly ProgressionService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionServiceTests"/> class.
    /// </summary>
    public ProgressionServiceTests()
    {
        _user = new User("reader", "auth", "pass");

        // 35 pages of 10000 ticks each, like the server's book convention.
        _book = new Book { RunTimeTicks = 350000 };

        // Loose mocks default to no pages and no stored reports.
        _service = new ProgressionService(
            _userDataManager.Object,
            _pageStreamService.Object,
            _progressionStore.Object);
    }

    [Fact]
    public void GetProgression_Returns_Null_When_Not_Read()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 0 });

        Assert.Null(_service.GetProgression(_book, _user));
    }

    [Fact]
    public void GetProgression_Returns_Null_When_Total_Unknown()
    {
        _book.RunTimeTicks = null;
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 1234 });

        Assert.Null(_service.GetProgression(_book, _user));
    }

    [Theory]
    [InlineData(175000, 0.5)]
    [InlineData(350000, 1.0)]
    [InlineData(400000, 1.0)]
    public void GetProgression_Computes_Fraction_From_Page_Ticks(long positionTicks, double expected)
    {
        SetupData(new UserItemData
        {
            Key = "k",
            PlaybackPositionTicks = positionTicks,
            LastPlayedDate = Modified
        });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal(expected, document!.Progression);
        Assert.Equal(Modified, document.Modified);
    }

    [Fact]
    public void GetProgression_Uses_Epub_Percentage_Convention()
    {
        _book.RunTimeTicks = TimeSpan.TicksPerSecond;
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 174920 });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal(0.017492, document!.Progression);
    }

    [Fact]
    public void GetProgression_Synthesizes_Device_From_User()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 10000 });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal("urn:uuid:" + _user.Id, document!.Device!.Id);
        Assert.Equal("Jellyfin (reader)", document.Device.Name);
    }

    [Fact]
    public void GetProgression_Includes_Entry_Key_Reference_For_Archives()
    {
        _pageStreamService.Setup(x => x.GetPageCount(_book)).Returns(35);
        _pageStreamService.Setup(x => x.GetPageReference(_book, 10)).Returns("page11.jpg");
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 100000, LastPlayedDate = Modified });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal(new[] { "page11.jpg" }, document!.References);
    }

    [Fact]
    public void GetProgression_Includes_Page_Reference_For_Pdfs()
    {
        _pageStreamService.Setup(x => x.GetPageCount(_book)).Returns(35);
        _pageStreamService.Setup(x => x.GetPageReference(_book, 17)).Returns("#page=18");
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 175000, LastPlayedDate = Modified });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal(new[] { "#page=18" }, document!.References);
    }

    [Fact]
    public void GetProgression_Omits_References_When_No_Page_Model()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 100000, LastPlayedDate = Modified });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Null(document!.References);
        _pageStreamService.Verify(x => x.GetPageReference(It.IsAny<Book>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GetProgression_Uses_Stored_Fidelity_When_Fresh()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 100000, LastPlayedDate = Modified });
        SetupStoredEntry(new ProgressionStoreEntry
        {
            Modified = Modified,
            PositionTicks = 100000,
            Device = new ProgressionDevice { Id = "urn:uuid:device-1", Name = "Thorium" },
            Title = "Chapter 3",
            References = new List<string> { "page11.jpg" }
        });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal("urn:uuid:device-1", document!.Device!.Id);
        Assert.Equal("Thorium", document.Device.Name);
        Assert.Equal("Chapter 3", document.Title);
        Assert.Equal(new[] { "page11.jpg" }, document.References);
        _pageStreamService.Verify(x => x.GetPageReference(It.IsAny<Book>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GetProgression_Ignores_Store_Entry_From_Older_Position()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 100000, LastPlayedDate = Modified });
        SetupStoredEntry(new ProgressionStoreEntry
        {
            Modified = Modified,
            PositionTicks = 50000,
            Device = new ProgressionDevice { Id = "urn:uuid:device-1", Name = "Thorium" },
            Title = "Chapter 2"
        });
        _pageStreamService.Setup(x => x.GetPageCount(_book)).Returns(35);
        _pageStreamService.Setup(x => x.GetPageReference(_book, 10)).Returns("page11.jpg");

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal("urn:uuid:" + _user.Id, document!.Device!.Id);
        Assert.Equal("Jellyfin (reader)", document.Device.Name);
        Assert.Null(document.Title);
        Assert.Equal(new[] { "page11.jpg" }, document.References);
    }

    [Fact]
    public void GetProgression_Ignores_Store_Entry_With_Older_Timestamp()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 100000, LastPlayedDate = Modified });
        SetupStoredEntry(new ProgressionStoreEntry
        {
            Modified = Modified.AddDays(-1),
            PositionTicks = 100000,
            Device = new ProgressionDevice { Id = "urn:uuid:device-1", Name = "Thorium" },
            Title = "Chapter 3"
        });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal("urn:uuid:" + _user.Id, document!.Device!.Id);
        Assert.Null(document.Title);
    }

    [Fact]
    public void GetProgression_Derives_References_When_Store_Has_None()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 100000, LastPlayedDate = Modified });
        SetupStoredEntry(new ProgressionStoreEntry
        {
            Modified = Modified,
            PositionTicks = 100000,
            Device = new ProgressionDevice { Id = "urn:uuid:device-1", Name = "Thorium" }
        });
        _pageStreamService.Setup(x => x.GetPageCount(_book)).Returns(35);
        _pageStreamService.Setup(x => x.GetPageReference(_book, 10)).Returns("page11.jpg");

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal("urn:uuid:device-1", document!.Device!.Id);
        Assert.Equal(new[] { "page11.jpg" }, document.References);
    }

    [Fact]
    public void GetProgression_Returns_Document_For_Zero_Progression_With_Fresh_Entry()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 0, LastPlayedDate = Modified });
        SetupStoredEntry(new ProgressionStoreEntry
        {
            Modified = Modified,
            PositionTicks = 0,
            Device = new ProgressionDevice { Id = "urn:uuid:device-1", Name = "Thorium" }
        });

        var document = _service.GetProgression(_book, _user);

        Assert.NotNull(document);
        Assert.Equal(0d, document!.Progression);
        Assert.Equal("urn:uuid:device-1", document.Device!.Id);
    }

    [Fact]
    public void UpdateProgression_Stores_Reported_Fidelity()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 0 });
        var incoming = CreateIncoming(0.5, Modified);
        incoming.Title = "Chapter 3";
        incoming.References = new List<string> { "#page=18" };

        var result = _service.UpdateProgression(_book, _user, incoming);

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        _progressionStore.Verify(
            x => x.Save(
                _user.Id,
                _book.Id,
                It.Is<ProgressionStoreEntry>(
                    e => e.Modified == Modified
                        && e.PositionTicks == 175000
                        && e.Device!.Id == "urn:uuid:test-device"
                        && e.Device.Name == "Test Reader"
                        && e.Title == "Chapter 3"
                        && e.References!.Count == 1
                        && e.References[0] == "#page=18")),
            Times.Once);
    }

    [Fact]
    public void UpdateProgression_Response_Uses_Reported_Fidelity()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 0 });
        SetupStoredEntry(new ProgressionStoreEntry
        {
            Modified = Modified,
            PositionTicks = 175000,
            Device = new ProgressionDevice { Id = "urn:uuid:test-device", Name = "Test Reader" },
            Title = "Chapter 3",
            References = new List<string> { "#page=18" }
        });

        var incoming = CreateIncoming(0.5, Modified);
        incoming.Title = "Chapter 3";
        incoming.References = new List<string> { "#page=18" };

        var result = _service.UpdateProgression(_book, _user, incoming);

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        Assert.NotNull(result.Document);
        Assert.Equal("urn:uuid:test-device", result.Document!.Device!.Id);
        Assert.Equal("Test Reader", result.Document.Device.Name);
        Assert.Equal("Chapter 3", result.Document.Title);
        Assert.Equal(new[] { "#page=18" }, result.Document.References);
    }

    [Fact]
    public void UpdateProgression_Includes_Reference_In_Response()
    {
        _pageStreamService.Setup(x => x.GetPageCount(_book)).Returns(35);
        _pageStreamService.Setup(x => x.GetPageReference(_book, 10)).Returns("page11.jpg");
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 0 });

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(100000d / 350000d, Modified));

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        Assert.Equal(new[] { "page11.jpg" }, result.Document!.References);
    }

    [Fact]
    public void UpdateProgression_Writes_Position_And_Date()
    {
        var data = new UserItemData { Key = "k", PlaybackPositionTicks = 0 };
        SetupData(data);

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(0.5, Modified));

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        Assert.Equal(175000, data.PlaybackPositionTicks);
        Assert.Equal(Modified, data.LastPlayedDate);
        _userDataManager.Verify(
            x => x.SaveUserData(_user, _book, data, UserDataSaveReason.PlaybackProgress, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void UpdateProgression_Returns_Document_After_Update()
    {
        SetupData(new UserItemData { Key = "k", PlaybackPositionTicks = 0 });

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(0.5, Modified));

        Assert.NotNull(result.Document);
        Assert.Equal(0.5, result.Document!.Progression);
        Assert.Equal(Modified, result.Document.Modified);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void UpdateProgression_Rejects_Out_Of_Range_Progression(double progression)
    {
        SetupData(new UserItemData { Key = "k" });

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(progression, Modified));

        Assert.Equal(ProgressionUpdateStatus.InvalidPayload, result.Status);
        VerifyNeverSaved();
    }

    [Fact]
    public void UpdateProgression_Rejects_Invalid_Payloads()
    {
        SetupData(new UserItemData { Key = "k" });

        Assert.Equal(ProgressionUpdateStatus.InvalidPayload, _service.UpdateProgression(_book, _user, null).Status);
        Assert.Equal(ProgressionUpdateStatus.InvalidPayload, _service.UpdateProgression(_book, _user, CreateIncoming(0.5, null)).Status);
        Assert.Equal(
            ProgressionUpdateStatus.InvalidPayload,
            _service.UpdateProgression(
                _book,
                _user,
                new ProgressionDocument { Progression = 0.5, Modified = Modified, Device = null }).Status);
        Assert.Equal(
            ProgressionUpdateStatus.InvalidPayload,
            _service.UpdateProgression(
                _book,
                _user,
                new ProgressionDocument
                {
                    Progression = 0.5,
                    Modified = Modified,
                    Device = new ProgressionDevice { Id = "", Name = "Reader" }
                }).Status);

        VerifyNeverSaved();
    }

    [Fact]
    public void UpdateProgression_Rejects_Too_Many_References()
    {
        SetupData(new UserItemData { Key = "k" });

        var incoming = CreateIncoming(0.5, Modified);
        incoming.References = new List<string>();
        for (var i = 0; i <= 64; i++)
        {
            incoming.References.Add("#page=" + i);
        }

        Assert.Equal(ProgressionUpdateStatus.InvalidPayload, _service.UpdateProgression(_book, _user, incoming).Status);
        VerifyNeverSaved();
    }

    [Fact]
    public void UpdateProgression_Returns_Stale_For_Older_Timestamp()
    {
        SetupData(new UserItemData
        {
            Key = "k",
            PlaybackPositionTicks = 100000,
            LastPlayedDate = Modified
        });

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(0.9, Modified.AddDays(-1)));

        Assert.Equal(ProgressionUpdateStatus.StaleProgression, result.Status);
        VerifyNeverSaved();
    }

    [Fact]
    public void UpdateProgression_Accepts_Equal_Timestamp()
    {
        var data = new UserItemData
        {
            Key = "k",
            PlaybackPositionTicks = 100000,
            LastPlayedDate = Modified
        };
        SetupData(data);

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(0.6, Modified));

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        Assert.Equal(210000, data.PlaybackPositionTicks);
    }

    [Fact]
    public void UpdateProgression_Keeps_Position_When_Total_Unknown()
    {
        _book.RunTimeTicks = null;
        var data = new UserItemData { Key = "k", PlaybackPositionTicks = 42000 };
        SetupData(data);

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(0.25, Modified));

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        Assert.Equal(42000, data.PlaybackPositionTicks);
        Assert.NotNull(result.Document);
        Assert.Equal(0.25, result.Document!.Progression);
    }

    [Fact]
    public void UpdateProgression_Creates_User_Data_When_Missing()
    {
        _userDataManager.Setup(x => x.GetUserData(_user, _book)).Returns((UserItemData?)null);

        var result = _service.UpdateProgression(_book, _user, CreateIncoming(0.5, Modified));

        Assert.Equal(ProgressionUpdateStatus.Success, result.Status);
        _userDataManager.Verify(
            x => x.SaveUserData(
                _user,
                _book,
                It.Is<UserItemData>(d => d.PlaybackPositionTicks == 175000),
                UserDataSaveReason.PlaybackProgress,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ProgressionDocument CreateIncoming(double progression, DateTime? modified)
    {
        return new ProgressionDocument
        {
            Progression = progression,
            Modified = modified,
            Device = new ProgressionDevice { Id = "urn:uuid:test-device", Name = "Test Reader" }
        };
    }

    private void SetupData(UserItemData data)
    {
        _userDataManager.Setup(x => x.GetUserData(_user, _book)).Returns(data);
    }

    private void SetupStoredEntry(ProgressionStoreEntry entry)
    {
        _progressionStore.Setup(x => x.Get(_user.Id, _book.Id)).Returns(entry);
    }

    private void VerifyNeverSaved()
    {
        _userDataManager.Verify(
            x => x.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
