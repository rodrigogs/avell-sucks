using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AvellSucks.UI.Settings;

namespace AvellSucks.UI.Services;

/// <summary>
/// Re-actuates the last successfully-applied Fan + Power profile on startup. The
/// EC forgets fan mode/curve and power limits across a machine reboot, and the
/// app only ever READ the EC at launch — so a reboot silently dropped the user's
/// chosen profile. This reapplies it once, at startup, off the UI-blocking path.
///
/// Policy (locked): reapply ALWAYS on startup — no opt-in — EXCEPT it honors the
/// existing write gate. If hardware writes are off
/// (<see cref="WriteGateInfo.EcWritesEnabled"/> false) we skip quietly: writes
/// would be denied downstream anyway, and reapplying against a closed gate would
/// only produce a burst of blocked-write noise.
///
/// It NEVER throws: startup must not crash if a reapply fails. Every outcome is
/// traced (<see cref="App.Trace"/>) so a silent no-op is diagnosable.
/// </summary>
public static class ProfileRestorer
{
    /// <summary>
    /// Reapply the persisted profile: power first (mode → Windows plan + PL
    /// preset), then fan (curve if present, else mode). Safe to fire-and-forget.
    /// </summary>
    public static async Task RestoreAsync()
    {
        try
        {
            var profile = SettingsStore.Current.Settings.RestoreProfile;
            if (profile is null)
            {
                App.Trace("ProfileRestorer: no saved profile — nothing to restore.");
                return;
            }

            if (!WriteGateInfo.EcWritesEnabled)
            {
                App.Trace("ProfileRestorer: hardware writes are OFF — skipping restore (gate-respecting).");
                return;
            }

            App.Trace($"ProfileRestorer: restoring profile (power={profile.PowerMode?.ToString() ?? "none"}, " +
                      $"fanMode={profile.FanMode ?? "none"}, fanCurve={(profile.FanCurve is { Count: > 0 } c ? $"{c.Count}pts" : "none")}).");

            // Power first: it switches the Windows power plan (the primary lever),
            // independent of the fan surface.
            if (profile.PowerMode is { } powerMode)
            {
                try
                {
                    var power = HardwareServices.CreatePowerService();
                    var r = await power.SetModeAsync(powerMode).ConfigureAwait(false);
                    App.Trace($"ProfileRestorer: power SetModeAsync({powerMode}) → {r.State} (error={r.Error ?? "none"}).");
                }
                catch (Exception ex)
                {
                    App.Trace($"ProfileRestorer: power restore threw (ignored) — {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Fan: a saved curve takes precedence (it also flips the mode to custom);
            // otherwise reapply the plain mode.
            try
            {
                var fan = HardwareServices.CreateFanService();
                if (profile.FanCurve is { Count: > 0 } points)
                {
                    IReadOnlyList<FanPoint> curve = points
                        .Select(p => new FanPoint(p.TemperatureC, p.Pwm))
                        .ToList();
                    var r = await fan.SetCurveAsync(curve).ConfigureAwait(false);
                    App.Trace($"ProfileRestorer: fan SetCurveAsync({points.Count}pts) → {r.State} (error={r.Error ?? "none"}).");
                }
                else if (!string.IsNullOrWhiteSpace(profile.FanMode))
                {
                    var r = await fan.SetModeAsync(profile.FanMode!).ConfigureAwait(false);
                    App.Trace($"ProfileRestorer: fan SetModeAsync({profile.FanMode}) → {r.State} (error={r.Error ?? "none"}).");
                }
            }
            catch (Exception ex)
            {
                App.Trace($"ProfileRestorer: fan restore threw (ignored) — {ex.GetType().Name}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            // Absolute backstop — a restore must never take the app down at launch.
            App.Trace($"ProfileRestorer: RestoreAsync threw (ignored) — {ex.GetType().Name}: {ex.Message}");
        }
    }
}
