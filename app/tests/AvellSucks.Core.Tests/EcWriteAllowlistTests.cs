using AvellSucks.Core.Platforms;
using Xunit;

namespace AvellSucks.Core.Tests;

public class EcWriteAllowlistTests
{
    [Fact]
    public void Default_allowlist_registers_fan_control_byte()
    {
        var al = new EcWriteAllowlist();
        var rule = al.GetRule(1873);
        Assert.NotNull(rule);
        Assert.Equal("0x751", rule!.HexAddress);
    }

    [Theory]
    [InlineData(1873, 0,    true,  "auto/normal mode")]
    [InlineData(1873, 64,   true,  "boost mode")]
    [InlineData(1873, 160,  true,  "custom-advanced")]
    [InlineData(1873, 129,  true,  "basic L1")]
    [InlineData(1873, 133,  true,  "basic L5")]
    [InlineData(1873, 1,    false, "value 1 is not an approved fan mode")]
    [InlineData(1873, 100,  false, "100 is not approved")]
    [InlineData(1873, 255,  false, "255 not approved")]
    public void Fan_control_byte_values(int addr, int value, bool expected, string _label)
    {
        var al = new EcWriteAllowlist();
        Assert.Equal(expected, al.IsAllowed(addr, value));
    }

    [Theory]
    [InlineData(1859, 0x00, true)]
    [InlineData(1859, 0x40, true)]
    [InlineData(1859, 0x8C, true)]
    [InlineData(1859, 0x8D, false)]
    [InlineData(1859, 0xFF, false)]
    [InlineData(1860, 0x8C, true)]
    [InlineData(1861, 0x8C, true)]
    [InlineData(1862, 0x8C, true)]
    [InlineData(1863, 0x8C, true)]
    public void Pwm_addresses_accept_known_values(int addr, int value, bool expected)
    {
        var al = new EcWriteAllowlist();
        Assert.Equal(expected, al.IsAllowed(addr, value));
    }

    [Theory]
    [InlineData(0x75D, 0)]     // trigger byte — not writable
    [InlineData(0x782, 0)]     // BIOS OEM byte — not writable
    [InlineData(0x000, 0)]     // random low address
    [InlineData(0x999, 0)]     // random
    public void Unregistered_addresses_are_rejected(int addr, int value)
    {
        var al = new EcWriteAllowlist();
        Assert.False(al.IsAllowed(addr, value));
        Assert.Null(al.GetRule(addr));
    }

    [Fact]
    public void Custom_rules_override_defaults()
    {
        var rule = new EcWriteRule(0xABC, "test", 1, 2, 3);
        var al = new EcWriteAllowlist(new[] { rule });
        Assert.True(al.IsAllowed(0xABC, 2));
        Assert.False(al.IsAllowed(0x751, 0)); // not in custom set
    }

    [Fact]
    public void Rules_collection_exposes_all_registered_rules()
    {
        var al = new EcWriteAllowlist();
        Assert.True(al.Rules.Count >= 6); // 1 fan-control + 5 PWM
        Assert.Contains(al.Rules.Keys, k => k == 1873);
        Assert.Contains(al.Rules.Keys, k => k == 1859);
    }

    // ------------------------------------------------------------------
    // Power-limit speculative allowlist entries
    // ------------------------------------------------------------------

    [Fact]
    public void Power_limit_addresses_are_registered()
    {
        var al = new EcWriteAllowlist();
        Assert.NotNull(al.GetRule(1923)); // 0x783 PL1
        Assert.NotNull(al.GetRule(1924)); // 0x784 PL2
        Assert.NotNull(al.GetRule(1925)); // 0x785 PL4
    }

    [Fact]
    public void Old_speculative_power_addresses_are_gone()
    {
        var al = new EcWriteAllowlist();
        Assert.Null(al.GetRule(1919)); // old 0x77F guess
        Assert.Null(al.GetRule(1920));
        Assert.Null(al.GetRule(1921));
        Assert.Null(al.GetRule(1857)); // Tau is XTU, not an EC register
    }

    [Theory]
    [InlineData(1923, 0,   true)]
    [InlineData(1923, 45,  true)]
    [InlineData(1923, 254, true)]
    [InlineData(1923, 255, false)] // raw byte capped at 254
    [InlineData(1924, 90,  true)]
    [InlineData(1924, 254, true)]
    [InlineData(1925, 107, true)]
    [InlineData(1925, 254, true)]
    public void Power_limit_entries_accept_watt_byte_range(int addr, int value, bool expected)
    {
        var al = new EcWriteAllowlist();
        Assert.Equal(expected, al.IsAllowed(addr, value));
    }
}
