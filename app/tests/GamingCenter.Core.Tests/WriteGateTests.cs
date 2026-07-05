using GamingCenter.Core.Platforms;
using Xunit;

namespace GamingCenter.Core.Tests;

public class WriteGateTests
{
    [Fact]
    public void Disabled_shared_instance_denies_writes()
    {
        Assert.False(WriteGate.Disabled.IsWriteAllowed);
    }

    [Fact]
    public void Explicitly_constructed_gate_can_be_enabled()
    {
        var gate = new WriteGate(allowWrites: true);
        Assert.True(gate.IsWriteAllowed);
    }

    [Fact]
    public void Explicitly_constructed_gate_defaults_to_disabled()
    {
        var gate = new WriteGate(allowWrites: false);
        Assert.False(gate.IsWriteAllowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("no")]
    [InlineData("random")]
    public void FromEnvironment_is_disabled_for_unsafe_values(string? value)
    {
        Environment.SetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES", value);
        var gate = WriteGate.FromEnvironment();
        Assert.False(gate.IsWriteAllowed);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    public void FromEnvironment_is_enabled_for_truthy_values(string value)
    {
        Environment.SetEnvironmentVariable("GAMINGCENTER_ALLOW_EC_WRITES", value);
        var gate = WriteGate.FromEnvironment();
        Assert.True(gate.IsWriteAllowed);
    }

    [Fact]
    public void EnsureAllowed_throws_when_disabled()
    {
        var gate = WriteGate.Disabled;
        var ex = Assert.Throws<InvalidOperationException>(() => gate.EnsureAllowed());
        Assert.Contains("disabled", ex.Message);
    }

    [Fact]
    public void EnsureAllowed_does_not_throw_when_enabled()
    {
        var gate = new WriteGate(allowWrites: true);
        gate.EnsureAllowed(); // should not throw
    }
}
