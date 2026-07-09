using AvellSucks.Core.Platforms;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AvellSucks.Server.Tests;

public class JsonlWriteAuditLogTests
{
    private static EcWriteResult SampleResult(bool allowed = false, int address = 1873, int value = 0) =>
        new EcWriteResult(
            Attempt: new EcWriteAttempt(
                System.DateTimeOffset.UtcNow, address, $"0x{address:X}", value, $"0x{value:X}", "test"),
            Allowed: allowed, Executed: false, Verified: false,
            Before: null, After: null, RolledBackTo: null,
            RollbackAttempted: false, Error: "test entry");

    [Fact]
    public async Task RecordAsync_creates_file_and_writes_valid_jsonl_line()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gc-audit-{System.Guid.NewGuid():N}.jsonl");
        try
        {
            var log = new JsonlAuditLog(path);
            await log.RecordAsync(SampleResult(allowed: true, address: 1873, value: 64));

            Assert.True(File.Exists(path));
            var lines = await File.ReadAllLinesAsync(path);
            Assert.Single(lines);
            // Validate the line contains the actual data we wrote, not just valid JSON
            var root = JsonDocument.Parse(lines[0]).RootElement;
            Assert.True(root.GetProperty("allowed").GetBoolean());
            Assert.Equal(1873, root.GetProperty("attempt").GetProperty("address").GetInt32());
            Assert.Equal(64, root.GetProperty("attempt").GetProperty("requestedValue").GetInt32());
            Assert.Equal("test", root.GetProperty("attempt").GetProperty("reason").GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task RecordAsync_appends_multiple_distinct_entries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gc-audit-{System.Guid.NewGuid():N}.jsonl");
        try
        {
            var log = new JsonlAuditLog(path);
            await log.RecordAsync(SampleResult(allowed: false, address: 0x999, value: 0));
            await log.RecordAsync(SampleResult(allowed: true, address: 1873, value: 64));
            await log.RecordAsync(SampleResult(allowed: false, address: 1860, value: 140));

            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(3, lines.Length);

            // Each line must be valid JSON with the correct address
            var first = JsonDocument.Parse(lines[0]).RootElement;
            Assert.Equal(0x999, first.GetProperty("attempt").GetProperty("address").GetInt32());

            var second = JsonDocument.Parse(lines[1]).RootElement;
            Assert.True(second.GetProperty("allowed").GetBoolean());

            var third = JsonDocument.Parse(lines[2]).RootElement;
            Assert.Equal(1860, third.GetProperty("attempt").GetProperty("address").GetInt32());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task RecordAsync_creates_parent_directory_and_writes_content()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gc-sub-{System.Guid.NewGuid():N}");
        var path = Path.Combine(dir, "nested", "audit.jsonl");
        try
        {
            var log = new JsonlAuditLog(path);
            await log.RecordAsync(SampleResult(allowed: true, address: 1873, value: 0));

            Assert.True(File.Exists(path));
            // Verify content was actually written, not just that the file exists
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\"address\":1873", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
