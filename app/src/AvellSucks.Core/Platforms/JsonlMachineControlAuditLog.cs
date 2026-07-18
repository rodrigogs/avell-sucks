using System.Text.Json;
using AvellSucks.Core.Hardware;

namespace AvellSucks.Core.Platforms;

/// <summary>Append-only JSONL audit for non-generic machine control operations.</summary>
public sealed class JsonlMachineControlAuditLog : IMachineControlAuditLog
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly bool _swallowWriteErrors;
    private readonly Lock _lock = new();

    public JsonlMachineControlAuditLog(string path, bool swallowWriteErrors = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _swallowWriteErrors = swallowWriteErrors;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch when (swallowWriteErrors) { }
    }

    public ValueTask RecordAsync(MachineControlResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            string line;
            lock (_lock) line = JsonSerializer.Serialize(result, s_json);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch when (_swallowWriteErrors) { }
        return ValueTask.CompletedTask;
    }
}
