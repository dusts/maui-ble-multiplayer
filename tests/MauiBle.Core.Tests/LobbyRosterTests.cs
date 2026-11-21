using MauiBle.Core;

namespace MauiBle.Core.Tests;

public class LobbyRosterTests
{
    [Fact]
    public void Hello_seats_a_guest_and_a_second_hello_renames_them()
    {
        var roster = new LobbyRoster();
        roster.SetHost("host", "Director");

        Assert.True(roster.TrySeatGuest("aa:bb", "p1", "Ada", out _));
        Assert.True(roster.TrySeatGuest("aa:bb", "p1", "Ada B", out _));

        var guest = Assert.Single(roster.Players, p => p.Id == "p1");
        Assert.Equal("Ada B", guest.Name);
        Assert.Equal(2, roster.Players.Count);
    }

    [Fact]
    public void Missing_hello_fields_do_not_take_a_seat()
    {
        var roster = new LobbyRoster();

        Assert.False(roster.TrySeatGuest("aa:bb", "  ", "Ada", out var reason));

        Assert.Contains("player id", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(roster.Players);
    }

    [Fact]
    public void Eighth_player_is_rejected()
    {
        var roster = new LobbyRoster();
        roster.SetHost("host", "Director");
        for (var i = 0; i < BleLobbyLimits.MaxGuests; i++)
            Assert.True(roster.TrySeatGuest($"mac-{i}", $"p{i}", $"G{i}", out _));

        Assert.False(roster.TrySeatGuest("mac-extra", "extra", "Extra", out var reason));

        Assert.Contains("full", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BleLobbyLimits.MaxPlayers, roster.Players.Count);
    }

    [Fact]
    public void Disconnect_frees_the_seat()
    {
        var roster = new LobbyRoster();
        roster.SetHost("host", "Director");
        roster.TrySeatGuest("aa:bb", "p1", "Ada", out _);

        Assert.True(roster.RemoveGuest("aa:bb"));
        Assert.False(roster.RemoveGuest("aa:bb"));
        Assert.Single(roster.Players);
    }

    [Fact]
    public void Roster_wire_omits_the_transport_address()
    {
        var roster = new LobbyRoster();
        roster.SetHost("host", "Director");
        roster.TrySeatGuest("aa:bb", "p1", "Ada", out _);

        var json = LobbyWire.Roster(roster.Players);
        var parsed = LobbyWire.Parse(json);

        Assert.DoesNotContain("aa:bb", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Director, Ada", LobbyWire.FormatRoster(parsed!.Players));
    }
}
