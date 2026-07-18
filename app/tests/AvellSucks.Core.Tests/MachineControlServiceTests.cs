using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using Xunit;

namespace AvellSucks.Core.Tests;

public sealed class MachineControlServiceTests
{
    private static (MachineControlService Service, FakeEcBackend Ec, FakePlatformBackend Platform, ListMachineControlAuditLog Audit)
        Make(bool writesAllowed = true)
    {
        var ec = new FakeEcBackend();
        var platform = new FakePlatformBackend();
        var audit = new ListMachineControlAuditLog();
        var service = new MachineControlService(ec, ec, platform, new WriteGate(writesAllowed), audit);
        return (service, ec, platform, audit);
    }

    [Fact]
    public async Task Closed_gate_blocks_machine_control_without_touching_hardware()
    {
        var (service, ec, platform, audit) = Make(writesAllowed: false);
        ec.Seed(MachineControlService.DeviceStateAddress, 0x10);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);

        var result = await service.SetWirelessRadiosAsync(true, "test");

        Assert.Equal(MachineControlOutcome.Blocked, result.Outcome);
        Assert.Empty(ec.Writes);
        Assert.Equal(0, platform.WirelessReconcileCalls);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Enable_wireless_preserves_unrelated_bits_and_sets_state_then_trigger()
    {
        var (service, ec, platform, audit) = Make();
        ec.Seed(MachineControlService.DeviceStateAddress, 0x10);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);
        platform.Status = platform.Status with { WifiPresent = true, BluetoothPresent = true };

        var result = await service.SetWirelessRadiosAsync(true, "test");

        Assert.Equal(MachineControlOutcome.Verified, result.Outcome);
        Assert.Equal(
            [(MachineControlService.DeviceStateAddress, 0xB0), (MachineControlService.RadioTriggerAddress, 0xE0)],
            ec.Writes);
        Assert.Equal(1, platform.WirelessReconcileCalls);
        Assert.True(platform.LastWirelessEnabled);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Disable_wireless_clears_only_radio_state_bits_and_still_pulses_trigger()
    {
        var (service, ec, platform, _) = Make();
        ec.Seed(MachineControlService.DeviceStateAddress, 0xB0);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);

        var result = await service.SetWirelessRadiosAsync(false, "test");

        Assert.Equal(MachineControlOutcome.Verified, result.Outcome);
        Assert.Equal(
            [(MachineControlService.DeviceStateAddress, 0x10), (MachineControlService.RadioTriggerAddress, 0xE0)],
            ec.Writes);
        Assert.False(platform.LastWirelessEnabled);
    }

    [Fact]
    public async Task Unverified_machine_is_rejected_before_any_EC_or_PnP_mutation()
    {
        var (service, ec, platform, audit) = Make();
        platform.Status = platform.Status with { SupportedMachine = false };
        ec.Seed(MachineControlService.DeviceStateAddress, 0x10);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);

        var radio = await service.SetWirelessRadiosAsync(true, "test");
        var touchpad = await service.SetTouchpadEnabledAsync(false, "test");

        Assert.Equal(MachineControlOutcome.Failed, radio.Outcome);
        Assert.Equal(MachineControlOutcome.Failed, touchpad.Outcome);
        Assert.Empty(ec.Writes);
        Assert.Equal(0, platform.WirelessReconcileCalls);
        Assert.Equal(0, platform.TouchpadCalls);
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Trigger_bits_may_be_consumed_immediately_without_causing_false_failure()
    {
        var (service, ec, platform, _) = Make();
        ec.Seed(MachineControlService.DeviceStateAddress, 0x10);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);
        ec.ReadBackOverride = (address, value) =>
            address == MachineControlService.RadioTriggerAddress
                ? value & ~MachineControlService.WirelessRadioMask
                : value;

        var result = await service.SetWirelessRadiosAsync(true, "test");

        Assert.Equal(MachineControlOutcome.Verified, result.Outcome);
        Assert.Equal(1, platform.WirelessReconcileCalls);
    }

    [Fact]
    public async Task Wireless_failure_rolls_state_back_and_retriggers_original_state()
    {
        var (service, ec, platform, audit) = Make();
        ec.Seed(MachineControlService.DeviceStateAddress, 0x10);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);
        platform.WirelessResults.Enqueue(PlatformMutationResult.Failure("rescan failed"));
        platform.WirelessResults.Enqueue(PlatformMutationResult.Success());

        var result = await service.SetWirelessRadiosAsync(true, "test");

        Assert.Equal(MachineControlOutcome.Failed, result.Outcome);
        Assert.Equal(
            [
                (MachineControlService.DeviceStateAddress, 0xB0),
                (MachineControlService.RadioTriggerAddress, 0xE0),
                (MachineControlService.DeviceStateAddress, 0x10),
                (MachineControlService.RadioTriggerAddress, 0xE0),
            ],
            ec.Writes);
        Assert.Contains("rollback verified", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Zero_baseline_is_restored_when_trigger_write_throws()
    {
        var (service, ec, platform, _) = Make();
        ec.Seed(MachineControlService.DeviceStateAddress, 0x00);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x00);
        ec.ThrowOnWriteNumber = 2;

        var result = await service.SetWirelessRadiosAsync(true, "test");

        Assert.Equal(MachineControlOutcome.Failed, result.Outcome);
        Assert.Equal(
            [
                (MachineControlService.DeviceStateAddress, 0xA0),
                (MachineControlService.DeviceStateAddress, 0x00),
                (MachineControlService.RadioTriggerAddress, 0xA0),
            ],
            ec.Writes);
        Assert.False(platform.LastWirelessEnabled);
        Assert.Contains("rollback verified", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_rollback_is_reported_as_attempted_not_completed()
    {
        var (service, ec, platform, _) = Make();
        ec.Seed(MachineControlService.DeviceStateAddress, 0x10);
        ec.Seed(MachineControlService.RadioTriggerAddress, 0x40);
        platform.WirelessResult = PlatformMutationResult.Failure("rescan failed");
        ec.ThrowOnWriteNumber = 3;

        var result = await service.SetWirelessRadiosAsync(true, "test");

        Assert.Equal(MachineControlOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("rollback verified", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback was attempted", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Touchpad_toggle_is_gated_delegated_verified_and_audited()
    {
        var (service, _, platform, audit) = Make();
        platform.TouchpadResult = PlatformMutationResult.Success();

        var result = await service.SetTouchpadEnabledAsync(false, "test");

        Assert.Equal(MachineControlOutcome.Verified, result.Outcome);
        Assert.False(platform.LastTouchpadEnabled);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Brightness_rejects_out_of_range_values_without_backend_call()
    {
        var (service, _, platform, audit) = Make();

        var result = await service.SetBrightnessAsync(101, "test");

        Assert.Equal(MachineControlOutcome.Failed, result.Outcome);
        Assert.Equal(0, platform.BrightnessCalls);
        Assert.Single(audit.Entries);
    }

    [Fact]
    public async Task Display_off_is_reported_as_requested_not_falsely_verified()
    {
        var (service, _, platform, audit) = Make();
        platform.DisplayResult = PlatformMutationResult.Accepted("Monitor power-off request dispatched.");

        var result = await service.TurnOffDisplayAsync("test");

        Assert.Equal(MachineControlOutcome.Requested, result.Outcome);
        Assert.True(result.Executed);
        Assert.False(result.Verified);
        Assert.Single(audit.Entries);
    }

    private sealed class FakePlatformBackend : IPlatformMachineControlBackend
    {
        public PlatformMachineControlStatus Status { get; set; } = new(
            SupportedMachine: true,
            WifiPresent: true,
            BluetoothPresent: true,
            TouchpadEnabled: true,
            WebcamEnabled: true,
            BrightnessPercent: 75,
            DisplayPowerControlAvailable: true,
            Error: null);

        public PlatformMutationResult WirelessResult { get; set; } = PlatformMutationResult.Success();
        public Queue<PlatformMutationResult> WirelessResults { get; } = new();
        public PlatformMutationResult TouchpadResult { get; set; } = PlatformMutationResult.Success();
        public PlatformMutationResult WebcamResult { get; set; } = PlatformMutationResult.Success();
        public PlatformMutationResult BrightnessResult { get; set; } = PlatformMutationResult.Success();
        public PlatformMutationResult DisplayResult { get; set; } = PlatformMutationResult.Accepted();

        public int WirelessReconcileCalls { get; private set; }
        public bool LastWirelessEnabled { get; private set; }
        public bool LastTouchpadEnabled { get; private set; }
        public int TouchpadCalls { get; private set; }
        public int BrightnessCalls { get; private set; }

        public ValueTask<PlatformMachineControlStatus> GetStatusAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Status);

        public ValueTask<PlatformMutationResult> ReconcileWirelessRadiosAsync(bool enabled, CancellationToken ct = default)
        {
            WirelessReconcileCalls++;
            LastWirelessEnabled = enabled;
            return ValueTask.FromResult(
                WirelessResults.Count > 0 ? WirelessResults.Dequeue() : WirelessResult);
        }

        public ValueTask<PlatformMutationResult> SetTouchpadEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            TouchpadCalls++;
            LastTouchpadEnabled = enabled;
            return ValueTask.FromResult(TouchpadResult);
        }

        public ValueTask<PlatformMutationResult> SetWebcamEnabledAsync(bool enabled, CancellationToken ct = default)
            => ValueTask.FromResult(WebcamResult);

        public ValueTask<PlatformMutationResult> SetBrightnessAsync(byte percent, CancellationToken ct = default)
        {
            BrightnessCalls++;
            return ValueTask.FromResult(BrightnessResult);
        }

        public ValueTask<PlatformMutationResult> TurnOffDisplayAsync(CancellationToken ct = default)
            => ValueTask.FromResult(DisplayResult);
    }

    private sealed class ListMachineControlAuditLog : IMachineControlAuditLog
    {
        public List<MachineControlResult> Entries { get; } = [];

        public ValueTask RecordAsync(MachineControlResult result, CancellationToken ct = default)
        {
            Entries.Add(result);
            return ValueTask.CompletedTask;
        }
    }
}
