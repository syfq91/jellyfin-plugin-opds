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

        _service = new ProgressionService(_userDataManager.Object);
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

    private void VerifyNeverSaved()
    {
        _userDataManager.Verify(
            x => x.SaveUserData(It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<UserItemData>(), It.IsAny<UserDataSaveReason>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
