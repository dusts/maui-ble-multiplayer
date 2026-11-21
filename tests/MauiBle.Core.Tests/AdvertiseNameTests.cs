using MauiBle.Core;

namespace MauiBle.Core.Tests;

public class AdvertiseNameTests
{
    [Fact]
    public void Display_name_gets_a_short_prefix()
    {
        Assert.Equal("MBM Ada", BleAdvertiseName.FromDisplayName("Ada"));
    }

    [Fact]
    public void Long_name_stays_within_the_scan_response_budget()
    {
        var advertised = BleAdvertiseName.FromDisplayName("Alexandria the Long");

        Assert.True(advertised.Length <= BleAdvertiseName.MaxAdvertisedLength);
        Assert.StartsWith("MBM ", advertised);
    }

    [Fact]
    public void Ui_label_strips_the_prefix()
    {
        Assert.Equal("Ada", BleAdvertiseName.ToUiLabel("MBM Ada"));
        Assert.Equal("Ada", BleAdvertiseName.ToUiLabel("MBM-Ada"));
        Assert.Null(BleAdvertiseName.ToUiLabel("(no name)"));
    }

    [Fact]
    public void Empty_display_name_still_advertises_a_host()
    {
        Assert.Equal("MBM Host", BleAdvertiseName.FromDisplayName("   "));
    }
}
