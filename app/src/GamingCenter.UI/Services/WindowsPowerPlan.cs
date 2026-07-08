using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GamingCenter.UI.Services;

/// <summary>
/// Switches the active Windows power scheme via <c>powercfg</c>. This is the
/// real "power plan" lever the Performance tab needs — the OEM machine ships
/// dedicated schemes (MyGamingMode / MyHighPerformance / MyBalanced /
/// MyPowerSaving) that map 1:1 onto our four modes. The decompiled OEM app did
/// NOT switch schemes itself (PowerPlan.cs is just an enum), so this is a real
/// gap we're filling, not a re-implementation.
///
/// GUIDs are resolved dynamically from <c>powercfg /list</c> by scheme NAME, so
/// we bind to whatever this machine actually has (and pick the first match when
/// duplicates exist for AC/DC). Falls back to the built-in Windows schemes
/// (High performance / Balanced / Power saver) when an OEM scheme is absent.
/// </summary>
public sealed class WindowsPowerPlan
{
    // Preferred OEM scheme names per mode, then Windows built-in fallbacks.
    private static readonly Dictionary<PerformanceMode, string[]> ModeNames = new()
    {
        [PerformanceMode.Gaming]   = ["MyGamingMode", "High performance", "Alto desempenho"],
        [PerformanceMode.High]     = ["MyHighPerformance", "High performance", "Alto desempenho"],
        [PerformanceMode.Balanced] = ["MyBalanced", "Balanced", "Equilibrado"],
        [PerformanceMode.Saving]   = ["MyPowerSaving", "Power saver", "Economia de energia"],
    };

    /// <summary>All schemes on this machine: (guid, name), parsed from powercfg /list.</summary>
    public static IReadOnlyList<(string Guid, string Name)> ListSchemes()
    {
        var outp = RunPowercfg("/list");
        var list = new List<(string, string)>();
        foreach (var line in outp.Split('\n'))
        {
            // "GUID do Esquema de Energia: <guid>  (Name) *"  /  "Power Scheme GUID: <guid> (Name)"
            var gi = line.IndexOf(':');
            if (gi < 0) continue;
            var rest = line[(gi + 1)..].Trim();
            var space = rest.IndexOf(' ');
            if (space < 0) continue;
            var guid = rest[..space].Trim();
            if (!Guid.TryParse(guid, out _)) continue;
            var name = "";
            var op = rest.IndexOf('('); var cp = rest.IndexOf(')');
            if (op >= 0 && cp > op) name = rest[(op + 1)..cp].Trim();
            list.Add((guid, name));
        }
        return list;
    }

    /// <summary>The GUID of the currently active scheme, or null.</summary>
    public static string? ActiveSchemeGuid()
    {
        var outp = RunPowercfg("/getactivescheme");
        var gi = outp.IndexOf(':');
        if (gi < 0) return null;
        var rest = outp[(gi + 1)..].Trim();
        var space = rest.IndexOf(' ');
        var guid = space > 0 ? rest[..space].Trim() : rest.Trim();
        return Guid.TryParse(guid, out _) ? guid : null;
    }

    /// <summary>Resolve the best scheme GUID for a mode on this machine.</summary>
    public static string? ResolveGuid(PerformanceMode mode)
    {
        var schemes = ListSchemes();
        foreach (var wanted in ModeNames[mode])
        {
            var hit = schemes.FirstOrDefault(s =>
                string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (hit.Guid is not null) return hit.Guid;
        }
        return null;
    }

    // GUID → mode, resolved once from the scheme list (schemes don't appear or
    // vanish at runtime). The polled path then costs ONE powercfg call
    // (/getactivescheme) per tick instead of two (it previously also ran /list
    // every tick to map the GUID back to a name).
    private static System.Collections.Generic.Dictionary<string, PerformanceMode>? s_guidToMode;
    private static readonly object s_mapLock = new();

    private static System.Collections.Generic.Dictionary<string, PerformanceMode> GuidMap()
    {
        var map = s_guidToMode;
        if (map is not null) return map;
        lock (s_mapLock)
        {
            if (s_guidToMode is not null) return s_guidToMode;
            var built = new System.Collections.Generic.Dictionary<string, PerformanceMode>(StringComparer.OrdinalIgnoreCase);
            foreach (var (guid, name) in ListSchemes())
                foreach (var kv in ModeNames)
                    if (kv.Value.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                        built.TryAdd(guid, kv.Key);
            s_guidToMode = built;
            return built;
        }
    }

    /// <summary>Which mode the active scheme corresponds to (best-effort), or null.</summary>
    public static PerformanceMode? ActiveMode()
    {
        var active = ActiveSchemeGuid();               // one powercfg call
        if (active is null) return null;
        return GuidMap().TryGetValue(active, out var m) ? m : null;
    }

    /// <summary>
    /// Activate the scheme for a mode. Returns true on success. All powercfg
    /// calls (resolve + setactive + verify) run on a background thread — this is
    /// safe to await from the UI thread without freezing rendering.
    /// </summary>
    public static Task<(bool ok, string? error)> SetAsync(PerformanceMode mode, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var guid = ResolveGuid(mode);
            if (guid is null) return (false, (string?)$"No Windows power scheme found for {mode}.");
            try
            {
                var setOut = RunPowercfg($"/setactive {guid}");
                var now = ActiveSchemeGuid();
                return string.Equals(now, guid, StringComparison.OrdinalIgnoreCase)
                    ? (true, (string?)null)
                    : (false, (string?)("Scheme did not become active. " + setOut.Trim()));
            }
            catch (Exception ex) { return (false, (string?)ex.Message); }
        }, ct);

    // --- powercfg plumbing ---
    private static string RunPowercfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }
        catch { return ""; }
    }
}
