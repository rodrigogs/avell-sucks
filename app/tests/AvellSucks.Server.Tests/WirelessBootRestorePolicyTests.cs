using AvellSucks.Core.Hardware;
using AvellSucks.Server.Hosting;
using Xunit;

namespace AvellSucks.Server.Tests;

public class WirelessBootRestorePolicyTests
{
    private static MachineControlStatus Status(
        bool supported = true, bool? radios = false, string? error = null) =>
        new(DateTimeOffset.UtcNow, supported, radios,
            WifiPresent: radios ?? false, BluetoothPresent: radios ?? false,
            TouchpadEnabled: null, WebcamEnabled: null,
            BrightnessPercent: null, DisplayPowerControlAvailable: false, error);

    [Fact]
    public void Env_gate_off_blocks_even_when_flag_and_radios_off()
    {
        var d = WirelessBootRestorePolicy.Decide(
            configFlag: true, restoreEnabled: false, Status(radios: false));
        Assert.False(d.ShouldRestore);
        Assert.Contains("GAMINGCENTER_RESTORE_RADIOS", d.Reason);
    }

    [Fact]
    public void Flag_off_never_restores()
    {
        var d = WirelessBootRestorePolicy.Decide(
            configFlag: false, restoreEnabled: true, Status(radios: false));
        Assert.False(d.ShouldRestore);
    }

    [Fact]
    public void Null_status_is_a_safe_no_op()
    {
        var d = WirelessBootRestorePolicy.Decide(configFlag: true, restoreEnabled: true, status: null);
        Assert.False(d.ShouldRestore);
        Assert.Contains("unavailable", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_machine_is_a_no_op()
    {
        var d = WirelessBootRestorePolicy.Decide(
            configFlag: true, restoreEnabled: true, Status(supported: false, radios: false));
        Assert.False(d.ShouldRestore);
    }

    [Fact]
    public void Unknown_radio_state_is_a_no_op_not_a_blind_write()
    {
        // CRITICAL: a null WirelessRadiosEnabled means the EC read was incomplete.
        // Writing the radio blind could fight a watchdog or a hardware kill switch.
        var d = WirelessBootRestorePolicy.Decide(
            configFlag: true, restoreEnabled: true, Status(radios: null));
        Assert.False(d.ShouldRestore);
        Assert.Contains("unknown", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Radios_already_on_is_a_no_op()
    {
        var d = WirelessBootRestorePolicy.Decide(
            configFlag: true, restoreEnabled: true, Status(radios: true));
        Assert.False(d.ShouldRestore);
        Assert.Contains("already ON", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Radios_off_with_flag_and_gate_restores()
    {
        var d = WirelessBootRestorePolicy.Decide(
            configFlag: true, restoreEnabled: true, Status(radios: false));
        Assert.True(d.ShouldRestore);
    }

    [Fact]
    public void Decision_is_one_directional_never_authorizes_disable()
    {
        // The policy only ever produces a restore decision; it has no "turn off"
        // path. The two ON states (radios already on, radios unknown) and all
        // OFF-intent cases are no-ops. This test pins that contract: even with
        // every flag set, the policy can only say restore=true for the single
        // "radios off + verified machine + known state" case.
        foreach (var radios in new bool?[] { true, null })
        {
            var d = WirelessBootRestorePolicy.Decide(
                configFlag: true, restoreEnabled: true, Status(radios: radios));
            Assert.False(d.ShouldRestore);
        }
    }
}
