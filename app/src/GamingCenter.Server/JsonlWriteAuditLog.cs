using System.Text.Json;
using GamingCenter.Core.Platforms;

namespace GamingCenter.Server;

/// <summary>
/// Append-only JSONL audit log for EC write attempts. Each line is a
/// serialized <see cref="EcWriteResult"/>. Thread-safe via a single lock.
/// </summary>
public sealed class JsonlWriteAuditLog : IWriteAuditLog
{
    private readonly string _path;
    private readonly Lock _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <param name="path">Absolute path to the JSONL file. Created if missing.</param>
    public JsonlWriteAuditLog(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public ValueTask RecordAsync(EcWriteResult result, CancellationToken cancellationToken = default)
    {
        string line;
        lock (_lock)
        {
            line = JsonSerializer.Serialize(result, _jsonOptions);
        }

        // Write synchronously — audit must be durable before we proceed.
        File.AppendAllText(_path, line + Environment.NewLine);
        return ValueTask.CompletedTask;
    }
}
