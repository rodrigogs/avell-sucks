using System.Text.Json;
using AvellSucks.Core.Hardware;

namespace AvellSucks.Core.Platforms;

/// <summary>
/// Append-only JSONL audit log for EC write attempts: one serialized
/// <see cref="EcWriteResult"/> per line, camelCase (Web) JSON, thread-safe.
///
/// Shared by the Server (durable: a write error should surface) and the UI
/// (best-effort: auditing must never break a hardware write). The
/// <c>swallowWriteErrors</c> flag selects the behavior.
/// </summary>
public sealed class JsonlAuditLog : IWriteAuditLog
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly bool _swallowWriteErrors;
    private readonly Lock _lock = new();

    /// <param name="path">Absolute path to the JSONL file (its directory is created).</param>
    /// <param name="swallowWriteErrors">
    /// True (UI): a failed append is ignored so auditing can't break a write.
    /// False (Server): let the append throw so a durability failure is visible.
    /// </param>
    public JsonlAuditLog(string path, bool swallowWriteErrors = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _swallowWriteErrors = swallowWriteErrors;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch when (swallowWriteErrors) { /* best-effort */ }
    }

    public ValueTask RecordAsync(EcWriteResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            string line;
            lock (_lock) { line = JsonSerializer.Serialize(result, s_json); }
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch when (_swallowWriteErrors) { /* auditing must never break a write */ }
        return ValueTask.CompletedTask;
    }
}
