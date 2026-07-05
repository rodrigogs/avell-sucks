using System;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Central switch for reduced-motion. Reads the OS "show animations" setting
/// once and lets the app force it off. Animations across the UI gate on this.
/// </summary>
public static class MotionPrefs
{
    private static bool? s_override;

    /// <summary>
    /// True when animations should be suppressed: either the OS requests
    /// reduced motion, or the app override is set.
    /// </summary>
    public static bool ReducedMotion =>
        s_override ?? !SystemAnimationsEnabled();

    /// <summary>Force reduced motion on/off regardless of the OS setting.</summary>
    public static void SetOverride(bool? reduced) => s_override = reduced;

    private static bool SystemAnimationsEnabled()
    {
        try
        {
            // SystemParameters.ClientAreaAnimation reflects the OS "show animations
            // in Windows" toggle. False → the user asked for reduced motion.
            return System.Windows.SystemParameters.ClientAreaAnimation;
        }
        catch
        {
            return true; // default to motion enabled if the query fails
        }
    }
}
