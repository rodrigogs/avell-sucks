using GamingCenter.Core.Rgb;
using GamingCenter.Core.Windows.Interop;

namespace GamingCenter.Core.Windows.Hardware;

public sealed class IteRgbProbe(ITEHidDeviceProvider provider)
{
    public bool IsSupported()
    {
        try
        {
            return provider.TryOpenKeyboard(out _);
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<string>> ListSupportedUsagesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(provider.GetAvailableUsagePages(ct).ReadOnly());
    }
}
