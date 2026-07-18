using AvellSucks.Core.Hardware;
using Xunit;

namespace AvellSucks.Server.Tests;

public sealed class WindowsMachineControlBackendTests
{
    [Fact]
    public void Pnp_rescan_uses_the_trusted_system_directory_binary()
    {
        Assert.True(Path.IsPathFullyQualified(WindowsMachineControlBackend.PnPUtilPath));
        Assert.Equal(
            Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            WindowsMachineControlBackend.PnPUtilPath);
    }

    [Theory]
    [InlineData("Avell High Performance", "1555", true)]
    [InlineData("AVELL", "G1555", true)]
    [InlineData("Avell High Performance", "A65", false)]
    [InlineData("Other", "1555", false)]
    public void Supported_identity_is_fail_closed(string manufacturer, string model, bool expected)
        => Assert.Equal(expected,
            WindowsMachineControlBackend.IsSupportedMachineIdentity(manufacturer, model));

    [Theory]
    [InlineData(@"ACPI\UNIW0001\1", true)]
    [InlineData(@"acpi\uniw0001\9", true)]
    [InlineData(@"ACPI\PNP0C14\1", false)]
    [InlineData(@"HID\UNIW0001&COL01\5&X", false)]
    public void Touchpad_selector_targets_only_the_model_specific_i2c_parent(string id, bool expected)
        => Assert.Equal(expected, WindowsMachineControlBackend.IsTargetTouchpad(id));

    [Theory]
    [InlineData(@"USB\VID_5986&PID_069E&MI_00\6&ABC", true)]
    [InlineData(@"usb\vid_5986&pid_069e&mi_00\other", true)]
    [InlineData(@"USB\VID_5986&PID_069E\200901010001", false)]
    [InlineData(@"USB\VID_8087&PID_0AAA\1", false)]
    public void Webcam_selector_targets_only_the_verified_camera_interface(string id, bool expected)
        => Assert.Equal(expected, WindowsMachineControlBackend.IsTargetWebcam(id));

    [Theory]
    [InlineData(0, true)]
    [InlineData(22, false)]
    [InlineData(24, null)]
    public void Pnp_problem_code_maps_to_truthful_enabled_state(int problemCode, bool? expected)
        => Assert.Equal(expected, WindowsMachineControlBackend.EnabledFromProblemCode(problemCode));
}
