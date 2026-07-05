using GamingCenter.Core.Platforms;
using Xunit;

namespace GamingCenter.Core.Tests;

public class SafeEcWriterTests
{
    private const int FanAddr = 1873; // 0x751

    private static (SafeEcWriter writer, FakeEcBackend fake) MakeWriter(
        bool allowWrites = true)
    {
        var fake = new FakeEcBackend();
        var writer = new SafeEcWriter(
            new WriteGate(allowWrites),
            new EcWriteAllowlist(),
            reader: fake,
            writer: fake,
            audit: fake);
        return (writer, fake);
    }

    // ------------------------------------------------------------------
    // DELIVERABLE: tests proving writes are disabled by default
    // ------------------------------------------------------------------

    [Fact]
    public async Task Write_is_denied_when_gate_is_closed()
    {
        var (writer, fake) = MakeWriter(allowWrites: false);
        fake.Seed(FanAddr, 0);

        var result = await writer.TryWriteAsync(FanAddr, 0, "test");

        Assert.False(result.Allowed);
        Assert.False(result.Executed);
        Assert.False(result.Verified);
        Assert.Contains("gate is closed", result.Error);
        Assert.Empty(fake.Writes); // no backend write happened
    }

    [Fact]
    public async Task Disabled_gate_produces_audit_entry()
    {
        var (writer, fake) = MakeWriter(allowWrites: false);
        fake.Seed(FanAddr, 0);

        await writer.TryWriteAsync(FanAddr, 0, "test");

        Assert.Single(fake.AuditEntries);
        var entry = fake.AuditEntries[0];
        Assert.False(entry.Allowed);
        Assert.False(entry.Executed);
    }

    // ------------------------------------------------------------------
    // DELIVERABLE: allowlist enforcement
    // ------------------------------------------------------------------

    [Fact]
    public async Task Write_is_denied_when_address_not_in_allowlist()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(0x999, 0);

        var result = await writer.TryWriteAsync(0x999, 0, "test");

        Assert.False(result.Allowed);
        Assert.Contains("allowlist", result.Error);
        Assert.Empty(fake.Writes);
    }

    [Fact]
    public async Task Write_is_denied_when_value_not_in_allowlist()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 0);

        var result = await writer.TryWriteAsync(FanAddr, 99, "test");

        Assert.False(result.Allowed);
        Assert.Contains("allowlist", result.Error);
        Assert.Empty(fake.Writes);
    }

    // ------------------------------------------------------------------
    // DELIVERABLE: before/after snapshot logging
    // ------------------------------------------------------------------

    [Fact]
    public async Task Successful_write_captures_before_and_after_snapshots()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 0);

        var result = await writer.TryWriteAsync(FanAddr, 64, "boost");

        Assert.True(result.Allowed);
        Assert.True(result.Executed);
        Assert.True(result.Verified);
        Assert.NotNull(result.Before);
        Assert.Equal(0, result.Before!.Value);
        Assert.NotNull(result.After);
        Assert.Equal(64, result.After!.Value);
    }

    [Fact]
    public async Task Successful_write_is_audit_logged()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 0);

        await writer.TryWriteAsync(FanAddr, 0, "auto");

        Assert.Single(fake.AuditEntries);
        var entry = fake.AuditEntries[0];
        Assert.True(entry.Allowed);
        Assert.True(entry.Executed);
        Assert.True(entry.Verified);
        Assert.Null(entry.Error);
    }

    [Fact]
    public async Task Before_snapshot_is_taken_before_write()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 42);

        var result = await writer.TryWriteAsync(FanAddr, 64, "boost");

        Assert.Equal(42, result.Before!.Value);
        Assert.Equal(64, result.After!.Value);
        // read-back counts: 1 for before-snapshot, WriteAsync does the write
        Assert.Equal(1, fake.ReadCount(FanAddr));
    }

    // ------------------------------------------------------------------
    // DELIVERABLE: rollback/restore
    // ------------------------------------------------------------------

    [Fact]
    public async Task Read_back_mismatch_triggers_rollback_to_original()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 10);
        // Simulate EC returning a different value only for the boost write (64).
        // The rollback write (10) should echo correctly.
        fake.ReadBackOverride = (_, v) => v == 64 ? 255 : v;

        var result = await writer.TryWriteAsync(FanAddr, 64, "boost");

        Assert.False(result.Verified);
        Assert.True(result.RollbackAttempted);
        Assert.NotNull(result.RolledBackTo);
        Assert.Equal(10, result.RolledBackTo!.Value); // restored to original
        Assert.Contains("mismatch", result.Error);
    }

    [Fact]
    public async Task Rollback_write_is_audit_logged()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 10);
        fake.ReadBackOverride = (_, _) => 255;

        await writer.TryWriteAsync(FanAddr, 64, "boost");

        Assert.Single(fake.AuditEntries);
        var entry = fake.AuditEntries[0];
        Assert.True(entry.RollbackAttempted);
        Assert.False(entry.Verified);
        Assert.Contains("Rolled back", entry.Error);
    }

    // ------------------------------------------------------------------
    // Backend failure during write
    // ------------------------------------------------------------------

    [Fact]
    public async Task Backend_exception_during_write_is_audit_logged_and_rethrown()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 0);
        fake.WriteException = new InvalidOperationException("WMI failure");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => writer.TryWriteAsync(FanAddr, 0, "auto").AsTask());

        Assert.Single(fake.AuditEntries);
        var entry = fake.AuditEntries[0];
        Assert.True(entry.Allowed);
        Assert.False(entry.Executed);
        Assert.Contains("WMI failure", entry.Error);
    }

    // ------------------------------------------------------------------
    // Full success path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Full_success_path_writes_exactly_once()
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 0);

        var result = await writer.TryWriteAsync(FanAddr, 64, "boost");

        Assert.True(result.Allowed && result.Executed && result.Verified);
        Assert.False(result.RollbackAttempted);
        Assert.Null(result.Error);
        Assert.Single(fake.Writes);
        Assert.Equal((FanAddr, 64), fake.Writes[0]);
    }

    [Theory]
    [InlineData(0,   "auto")]
    [InlineData(64,  "boost")]
    [InlineData(160, "custom-advanced")]
    [InlineData(129, "basic-L1")]
    [InlineData(133, "basic-L5")]
    public async Task All_approved_fan_modes_succeed(int value, string label)
    {
        var (writer, fake) = MakeWriter(allowWrites: true);
        fake.Seed(FanAddr, 0);

        var result = await writer.TryWriteAsync(FanAddr, value, label);

        Assert.True(result.Verified, $"mode {label} (0x{value:X}) should succeed");
    }
}
