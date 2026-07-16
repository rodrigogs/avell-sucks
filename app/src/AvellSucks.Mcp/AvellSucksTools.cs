using System.ComponentModel;
using System.Text.Json;
using AvellSucks.Api.Security;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using ModelContextProtocol.Server;

namespace AvellSucks.Mcp;

/// <summary>
/// MCP tools over the same Core services the REST controllers use. Reads are
/// available to any authenticated caller; writes pass the remote-write gate
/// (loopback always; remote only when AllowRemoteWrites is on) and the existing
/// WriteGate inside SafeEcWriter. Blocked writes return a truthful message — the
/// tool never claims a gated write succeeded (PRODUCT.md honesty principle).
/// </summary>
[McpServerToolType]
public sealed class AvellSucksTools(
    IEcBackend backend,
    SafeEcWriter writer,
    RemoteWriteAuthorizer remoteWrite)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "get_system_snapshot"), Description("Current system snapshot (memory, top processes).")]
    public string GetSystemSnapshot()
        => JsonSerializer.Serialize(AvellSucks.Api.SystemSnapshotBuilder.Build(), s_json);

    [McpServerTool(Name = "get_fan_mode"), Description("Current interpreted fan mode (auto/boost/custom/L1..L5).")]
    public async Task<string> GetFanModeAsync(CancellationToken ct)
    {
        var mode = await backend.InterpretFanModeAsync(ct).ConfigureAwait(false);
        return mode is null ? "Could not read fan mode from EC." : JsonSerializer.Serialize(mode, s_json);
    }

    [McpServerTool(Name = "get_power_profile"), Description("Current CPU power-limit profile (PL1/PL2/PL4 watts).")]
    public async Task<string> GetPowerProfileAsync(CancellationToken ct)
    {
        var state = await backend.ReadPowerProfileAsync(ct).ConfigureAwait(false);
        return state is null ? "Power profile not supported on this hardware." : JsonSerializer.Serialize(state, s_json);
    }

    [McpServerTool(Name = "set_fan_mode"), Description("Set fan mode: auto, boost, custom, or L1..L5. Gated by remote-write policy.")]
    public async Task<string> SetFanModeAsync(
        [Description("One of: auto, boost, custom, L1, L2, L3, L4, L5")] string mode,
        CancellationToken ct)
    {
        var gate = remoteWrite.Check();
        if (!gate.Allowed) return gate.Reason!;
        if (!FanModeMap.TryByteFor(mode, out var value))
            return $"Unknown mode '{mode}'. Valid: auto, boost, custom, L1..L5.";

        var result = await writer.TryWriteAsync(
            FanModeMap.ControlByteAddress, value,
            reason: $"mcp:set_fan_mode={mode}", cancellationToken: ct,
            origin: remoteWrite.DescribeCaller(), identity: "mcp").ConfigureAwait(false);
        return JsonSerializer.Serialize(result, s_json);
    }

    [McpServerTool(Name = "set_power_profile"), Description("Set CPU power limits (watts). Any subset of pl1/pl2/pl4. Gated by remote-write policy.")]
    public async Task<string> SetPowerProfileAsync(int? pl1, int? pl2, int? pl4, CancellationToken ct)
    {
        var gate = remoteWrite.Check();
        if (!gate.Allowed) return gate.Reason!;

        var results = new List<EcWriteResult>();
        var caller = remoteWrite.DescribeCaller();
        if (pl1 is int a) results.Add(await writer.TryWriteAsync(PowerRegisters.Pl1, a, "mcp:set_power_profile:pl1", ct, caller, "mcp").ConfigureAwait(false));
        if (pl2 is int b) results.Add(await writer.TryWriteAsync(PowerRegisters.Pl2, b, "mcp:set_power_profile:pl2", ct, caller, "mcp").ConfigureAwait(false));
        if (pl4 is int c) results.Add(await writer.TryWriteAsync(PowerRegisters.Pl4, c, "mcp:set_power_profile:pl4", ct, caller, "mcp").ConfigureAwait(false));
        return JsonSerializer.Serialize(results, s_json);
    }
}
