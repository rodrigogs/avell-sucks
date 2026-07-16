using System.Text.Json;

namespace AvellSucks.Core.Service;

/// <summary>
/// Loads/saves <see cref="NetworkServiceConfig"/> as JSON. Load never throws:
/// a missing or corrupt file yields safe defaults (loopback, no auth, no remote
/// writes) so a bad file can't accidentally open exposure or crash the service.
/// The service watches this file for changes via IOptionsMonitor (wired in the
/// host); this store is the read/write primitive shared by UI and tests.
/// </summary>
public static class ServiceConfigStore
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static NetworkServiceConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new NetworkServiceConfig();
            return JsonSerializer.Deserialize<NetworkServiceConfig>(File.ReadAllText(path), s_json)
                   ?? new NetworkServiceConfig();
        }
        catch
        {
            return new NetworkServiceConfig();
        }
    }

    public static void Save(string path, NetworkServiceConfig cfg)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, s_json));
    }
}
