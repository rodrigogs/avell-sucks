using System.Collections.Generic;
using AvellSucks.UI.Services;
using AvellSucks.UI.Settings;
using Xunit;

namespace AvellSucks.Server.Tests;

/// <summary>
/// Guards the #1 risk of the startup-restore feature: forgetting to register the
/// new persisted types (<see cref="RestoreProfile"/>, <see cref="FanCurvePoint"/>,
/// <c>List&lt;FanCurvePoint&gt;</c>, <see cref="PerformanceMode"/>) in the
/// source-gen <c>SettingsJsonContext</c>. A missing <c>[JsonSerializable]</c>
/// entry compiles fine but throws at runtime the moment the app persists or
/// reloads settings — exactly the class of bug that has bitten this project
/// before. This round-trips through the SAME serialize/deserialize path
/// <c>SettingsStore</c> uses on disk.
/// </summary>
public class RestoreProfileJsonTests
{
    [Fact]
    public void RestoreProfile_with_fan_curve_and_power_mode_round_trips()
    {
        var settings = new AppSettings
        {
            RestoreProfile = new RestoreProfile
            {
                FanMode = "custom",
                FanCurve = new List<FanCurvePoint>
                {
                    new() { TemperatureC = 50, Pwm = 20 },
                    new() { TemperatureC = 60, Pwm = 40 },
                    new() { TemperatureC = 70, Pwm = 60 },
                    new() { TemperatureC = 80, Pwm = 90 },
                    new() { TemperatureC = 90, Pwm = 140 },
                },
                PowerMode = PerformanceMode.Gaming,
            },
        };

        var json = SettingsStore.Serialize(settings);
        var restored = SettingsStore.Deserialize(json);

        Assert.NotNull(restored);
        var profile = restored!.RestoreProfile;
        Assert.NotNull(profile);
        Assert.Equal("custom", profile!.FanMode);
        Assert.Equal(PerformanceMode.Gaming, profile.PowerMode);

        Assert.NotNull(profile.FanCurve);
        Assert.Equal(5, profile.FanCurve!.Count);
        Assert.Equal(50, profile.FanCurve[0].TemperatureC);
        Assert.Equal(20, profile.FanCurve[0].Pwm);
        Assert.Equal(90, profile.FanCurve[4].TemperatureC);
        Assert.Equal(140, profile.FanCurve[4].Pwm);
    }

    [Fact]
    public void RestoreProfile_with_fan_mode_only_round_trips()
    {
        var settings = new AppSettings
        {
            RestoreProfile = new RestoreProfile
            {
                FanMode = "boost",
                FanCurve = null,
                PowerMode = PerformanceMode.Balanced,
            },
        };

        var restored = SettingsStore.Deserialize(SettingsStore.Serialize(settings));

        Assert.NotNull(restored);
        Assert.Equal("boost", restored!.RestoreProfile!.FanMode);
        Assert.Null(restored.RestoreProfile.FanCurve);
        Assert.Equal(PerformanceMode.Balanced, restored.RestoreProfile.PowerMode);
    }

    [Fact]
    public void Null_RestoreProfile_round_trips_as_null()
    {
        var restored = SettingsStore.Deserialize(SettingsStore.Serialize(new AppSettings()));

        Assert.NotNull(restored);
        Assert.Null(restored!.RestoreProfile);
    }
}
