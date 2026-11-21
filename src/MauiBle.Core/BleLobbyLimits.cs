namespace MauiBle.Core;

/// <summary>One host plus guests. Many phones stay reliable around three to five centrals.</summary>
public static class BleLobbyLimits
{
    public const int MaxPlayers = 8;
    public const int MaxGuests = MaxPlayers - 1;
}
