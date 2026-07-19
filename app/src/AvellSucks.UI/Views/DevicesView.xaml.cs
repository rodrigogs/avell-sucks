using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AvellSucks.Core.Hardware;
using AvellSucks.UI.Controls;
using AvellSucks.UI.Localization;
using AvellSucks.UI.Services;

namespace AvellSucks.UI.Views;

public partial class DevicesView : UserControl
{
    private readonly IMachineControlService? _controls = HardwareServices.MachineControls();
    private readonly LoadingGate _loading = new();
    private readonly Debouncer _brightnessWrite = new(450);
    private bool _initialized;

    public DevicesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _brightnessWrite.Cancel();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _initialized = true;
        var writesEnabled = WriteGateInfo.EcWritesEnabled;
        GateNotice.Visibility = writesEnabled ? Visibility.Collapsed : Visibility.Visible;
        SetInlineStatus(
            GateStatusDot,
            GateStatusText,
            Loc.T(writesEnabled ? "Devices.Overview.WritesOn" : "Devices.Overview.ReadOnly"),
            writesEnabled ? "Warn" : "Ink3");
        UnavailableNotice.Visibility = _controls is null ? Visibility.Visible : Visibility.Collapsed;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_controls is null)
        {
            WirelessToggle.IsEnabled = TouchpadToggle.IsEnabled = WebcamToggle.IsEnabled = false;
            BrightnessSlider.IsEnabled = DisplayOffButton.IsEnabled = false;
            SetInlineStatus(
                MachineStatusDot,
                MachineStatusText,
                Loc.T("Devices.Overview.Unavailable"),
                "Warn");
            SetDotStatus(WirelessStatusDot, null);
            SetDotStatus(TouchpadStatusDot, null);
            SetDotStatus(WebcamStatusDot, null);
            return;
        }

        var status = await _controls.GetStatusAsync();
        UnavailableNotice.Visibility = status.SupportedMachine ? Visibility.Collapsed : Visibility.Visible;
        SetInlineStatus(
            MachineStatusDot,
            MachineStatusText,
            Loc.T(status.SupportedMachine ? "Devices.Overview.Supported" : "Devices.Overview.Unsupported"),
            status.SupportedMachine ? "Ok" : "Warn");
        var canMutate = status.SupportedMachine && WriteGateInfo.EcWritesEnabled;
        using (_loading.Begin())
        {
            WirelessToggle.IsEnabled = canMutate && status.WirelessRadiosEnabled.HasValue;
            WirelessToggle.IsChecked = status.WirelessRadiosEnabled;
            SetDotStatus(WirelessStatusDot, status.WirelessRadiosEnabled);

            TouchpadToggle.IsEnabled = canMutate && status.TouchpadEnabled.HasValue;
            TouchpadToggle.IsChecked = status.TouchpadEnabled;
            SetDotStatus(TouchpadStatusDot, status.TouchpadEnabled);
            WebcamToggle.IsEnabled = canMutate && status.WebcamEnabled.HasValue;
            WebcamToggle.IsChecked = status.WebcamEnabled;
            SetDotStatus(WebcamStatusDot, status.WebcamEnabled);

            BrightnessSlider.IsEnabled = canMutate && status.BrightnessPercent.HasValue;
            if (status.BrightnessPercent is byte brightness)
            {
                BrightnessSlider.Value = brightness;
                BrightnessValue.Text = $"{brightness}%";
            }
            else
            {
                BrightnessValue.Text = "—";
            }

            DisplayOffButton.IsEnabled = canMutate && status.DisplayPowerControlAvailable;
        }

        if (!string.IsNullOrWhiteSpace(status.Error))
            App.Trace($"DevicesView status: {status.Error}");
    }

    /// <summary>Projects a nullable device state onto the decorative status shape.</summary>
    private static void SetDotStatus(StatusDot dot, bool? enabled)
    {
        dot.Status = enabled switch
        {
            true => DeviceStatus.On,
            false => DeviceStatus.Off,
            null => DeviceStatus.Unknown,
        };
    }

    private void SetInlineStatus(
        Ellipse dot,
        TextBlock text,
        string label,
        string accentBrushKey)
    {
        var accent = (Brush)FindResource(accentBrushKey);
        dot.Fill = accent;
        text.Foreground = accent;
        text.Text = label;
    }

    private async void OnWirelessChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _loading.Active || _controls is null) return;
        var enabled = WirelessToggle.IsChecked == true;
        var result = await ApplyAsync(
            Loc.T("Devices.Wireless"),
            enabled ? Loc.T("Devices.Wireless.Enabled") : Loc.T("Devices.Wireless.Disabled"),
            () => _controls.SetWirelessRadiosAsync(enabled, "ui:devices/wireless"));

        // Persist the user's verified intent so the Session 0 service can
        // reconcile the radios at boot (before login). Only a Verified outcome
        // touches the flag — never Requested/Failed/Blocked, so a flaky write
        // can't silently arm (or disarm) an automatic restore. See
        // Server.Hosting.WirelessBootRestoreService.
        if (result.Outcome == MachineControlOutcome.Verified)
            PersistBootRestoreIntent(restoreOnBoot: enabled);
    }

    private static void PersistBootRestoreIntent(bool restoreOnBoot)
    {
        try
        {
            var cfg = new ServiceConfigManager();
            var remote = cfg.Load();
            if (remote.RestoreWirelessRadiosOnBoot == restoreOnBoot) return;
            remote.RestoreWirelessRadiosOnBoot = restoreOnBoot;
            cfg.Save(remote);
        }
        catch (Exception ex)
        {
            // Persistence is best-effort; a failure here must not undo the
            // hardware change the user just made. Leave a trace instead.
            App.Trace($"DevicesView: wireless boot-restore persist failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnTouchpadChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _loading.Active || _controls is null) return;
        var enabled = TouchpadToggle.IsChecked == true;
        await ApplyAsync(
            Loc.T("Devices.Touchpad"),
            enabled ? Loc.T("Devices.Touchpad.Enabled") : Loc.T("Devices.Touchpad.Disabled"),
            () => _controls.SetTouchpadEnabledAsync(enabled, "ui:devices/touchpad"));
    }

    private async void OnWebcamChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _loading.Active || _controls is null) return;
        var enabled = WebcamToggle.IsChecked == true;
        await ApplyAsync(
            Loc.T("Devices.Webcam"),
            enabled ? Loc.T("Devices.Webcam.Enabled") : Loc.T("Devices.Webcam.Disabled"),
            () => _controls.SetWebcamEnabledAsync(enabled, "ui:devices/webcam"));
    }

    private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightnessValue is null) return;
        BrightnessValue.Text = $"{Math.Round(BrightnessSlider.Value):0}%";
        if (!_initialized || _loading.Active || _controls is null) return;
        _brightnessWrite.Trigger(ApplyBrightnessNow);
    }

    private async void ApplyBrightnessNow()
    {
        if (_controls is null) return;
        var percent = (int)Math.Round(BrightnessSlider.Value);
        await ApplyAsync(
            Loc.T("Devices.Brightness"),
            string.Format(Loc.T("Devices.Brightness.Set"), percent),
            () => _controls.SetBrightnessAsync(percent, "ui:devices/brightness"),
            refreshOnSuccess: false);
    }

    private async void OnDisplayOff(object sender, RoutedEventArgs e)
    {
        if (_controls is null) return;
        await ApplyAsync(
            Loc.T("Devices.Display.Off"),
            Loc.T("Devices.Display.Off.Requested"),
            () => _controls.TurnOffDisplayAsync("ui:devices/display/off"),
            refreshOnSuccess: false);
    }

    private async Task<MachineControlResult> ApplyAsync(
        string pendingLabel,
        string doneLabel,
        Func<ValueTask<MachineControlResult>> operation,
        bool refreshOnSuccess = true)
    {
        Toaster.Show(WriteState.Pending, pendingLabel);
        var result = await operation();
        switch (result.Outcome)
        {
            case MachineControlOutcome.Verified:
                Toaster.Show(WriteState.Verified, doneLabel);
                break;
            case MachineControlOutcome.Requested:
                Toaster.Info(doneLabel, result.Message);
                break;
            case MachineControlOutcome.Blocked:
                Toaster.Show(WriteState.Blocked, null, result.Message);
                break;
            default:
                Toaster.Show(WriteState.Failed, null, result.Message);
                break;
        }

        if (refreshOnSuccess || result.Outcome is MachineControlOutcome.Blocked or MachineControlOutcome.Failed)
            await RefreshAsync();
        return result;
    }
}
