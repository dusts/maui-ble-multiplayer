using MauiBle.Core;

namespace MauiBle.Core.Tests;

public class FramingTests
{
    [Fact]
    public void Short_payload_is_one_raw_frame()
    {
        var json = "{\"v\":1,\"type\":\"hello\",\"name\":\"Ada\"}";

        var frames = BleFraming.Encode(json);

        Assert.Single(frames);
        Assert.Equal(json, frames[0]);
    }

    [Fact]
    public void Long_payload_round_trips_through_parts()
    {
        var json = new string('x', BleFraming.MaxChunkPayloadBytes + 50);
        var frames = BleFraming.Encode(json);
        var buffer = new FrameReassembly();

        Assert.True(frames.Count > 1);

        string? done = null;
        foreach (var frame in frames)
            done = BleFraming.Feed(frame, buffer);

        Assert.Equal(json, done);
    }

    [Fact]
    public void Parts_out_of_order_wait_until_the_set_is_complete()
    {
        var json = new string('y', 200);
        var frames = BleFraming.Encode(json);
        var buffer = new FrameReassembly();

        Assert.Equal(2, frames.Count);
        Assert.Null(BleFraming.Feed(frames[1], buffer));
        Assert.Equal(json, BleFraming.Feed(frames[0], buffer));
    }
}
