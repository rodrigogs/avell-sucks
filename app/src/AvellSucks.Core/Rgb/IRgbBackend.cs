namespace AvellSucks.Core.Rgb;

public interface IRgbBackend
{
    /// <summary>Read current keyboard RGB state if supported.</summary>
    ValueTask<RgbState?> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Apply an RGB effect profile to the keyboard.</summary>
    ValueTask ApplyEffectAsync(RgbEffect effect, CancellationToken cancellationToken = default);
}

public interface IRgbWriter
{
    /// <summary>Write color data for a specific RAM zone.</summary>
    ValueTask WriteZoneColorsAsync(
        int zoneIndex,
        IReadOnlyList<RgbColor> colors,
        CancellationToken cancellationToken = default);
}
