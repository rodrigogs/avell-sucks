using AvellSucks.Core.Hardware;
using Xunit;

namespace AvellSucks.Core.Tests;

/// <summary>
/// FanModeMap is the single source of truth for the fan mode↔byte mapping, wired
/// into WmiFanService, WmiEcBackend, FanController, FanStateMonitor and the write
/// allowlist. These pin the branching that every layer trusts — especially the
/// Describe() level boundaries, which carried a documented off-by-one (the old
/// ">=128 and <=132" range mislabelled L5=133 and wrongly matched 128).
/// </summary>
public class FanModeMapTests
{
    [Theory]
    [InlineData("auto", 0)]
    [InlineData("boost", 64)]
    [InlineData("custom", 160)]
    [InlineData("L1", 129)]
    [InlineData("L5", 133)]
    public void TryByteFor_maps_known_keys(string key, int expected)
    {
        Assert.True(FanModeMap.TryByteFor(key, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("AUTO")]
    [InlineData("Boost")]
    [InlineData("l3")]
    public void TryByteFor_is_case_insensitive(string key)
        => Assert.True(FanModeMap.TryByteFor(key, out _));

    [Fact]
    public void TryByteFor_unknown_key_returns_false()
        => Assert.False(FanModeMap.TryByteFor("turbo", out _));

    [Fact]
    public void TryByteFor_null_returns_false_without_throwing()
        => Assert.False(FanModeMap.TryByteFor(null!, out _));

    [Theory]
    [InlineData(0, "auto")]
    [InlineData(64, "boost")]
    [InlineData(160, "custom")]
    [InlineData(129, "L1")]
    [InlineData(133, "L5")]
    public void KeyFor_round_trips_known_bytes(int controlByte, string expected)
        => Assert.Equal(expected, FanModeMap.KeyFor(controlByte));

    [Fact]
    public void KeyFor_masks_to_low_byte()
        // 0x140 & 0xFF == 0x40 == boost: high bits are ignored, not treated as unknown.
        => Assert.Equal("boost", FanModeMap.KeyFor(0x140));

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(200)]
    public void KeyFor_unknown_byte_falls_back_to_auto(int controlByte)
        => Assert.Equal("auto", FanModeMap.KeyFor(controlByte));

    [Theory]
    [InlineData(0, "Normal/Smart")]
    [InlineData(64, "FanBoost/Cold Mode")]
    [InlineData(160, "Advanced Custom")]
    [InlineData(129, "Custom Level 1")]
    [InlineData(133, "Custom Level 5")]
    public void Describe_names_known_bytes(int controlByte, string expected)
        => Assert.Equal(expected, FanModeMap.Describe(controlByte));

    [Theory]
    // The off-by-one guard: 128 is NOT a custom level and 134 is past L5. The old
    // ">=128 and <=132" range got both of these wrong.
    [InlineData(128, "Unknown Control (128)")]
    [InlineData(134, "Unknown Control (134)")]
    public void Describe_rejects_bytes_outside_the_custom_level_range(int controlByte, string expected)
        => Assert.Equal(expected, FanModeMap.Describe(controlByte));

    [Theory]
    [InlineData(0, true)]    // normal — EC drives the fan
    [InlineData(64, true)]   // boost — EC drives the fan
    [InlineData(129, false)] // fixed level — user-pinned
    [InlineData(160, false)] // advanced custom — user curve
    public void IsAutoManaged_only_for_normal_and_boost(int controlByte, bool expected)
        => Assert.Equal(expected, FanModeMap.IsAutoManaged(controlByte));

    [Fact]
    public void ControlBytes_matches_every_mapped_key()
    {
        var bytes = FanModeMap.ControlBytes;
        Assert.Equal(8, bytes.Length); // auto, boost, custom, L1..L5
        Assert.Contains(0, bytes);
        Assert.Contains(64, bytes);
        Assert.Contains(160, bytes);
        Assert.Contains(133, bytes);
    }
}
