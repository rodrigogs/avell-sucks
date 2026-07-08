using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GamingCenter.UI.Services;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Shell-anchored toast for write feedback. Drops in from the top edge,
/// centered over the content. Replaces the inline StateBadge pill: an outcome
/// is transient news, not permanent layout.
///
/// Copy is plain: success is the caller's own words ("Fan set to Boost"), and
/// the check mark carries "it worked" — no "applied &amp; verified" jargon.
///
///   Verified → success toast, auto-dismisses (a thin bar shows the wait).
///   Failed   → error toast with the reason; persists until dismissed.
///   Blocked  → gated toast; persists (writes are off).
///   Idle     → dismiss the current toast.
///
/// One toast at a time — every result comes from the same live edit, so a new
/// one replaces the last rather than stacking. Colour never carries state
/// alone; it's always paired with an icon and a label.
/// </summary>
public sealed class ToastHost : Grid
{
    // Palette (mirrors Theme/Palette.xaml).
    private static readonly Color Overlay = Color.FromRgb(0x2E, 0x24, 0x38);
    private static readonly Color Ink     = Color.FromRgb(0xF2, 0xEE, 0xF6);
    private static readonly Color Ink2    = Color.FromRgb(0xC1, 0xB6, 0xCF);
    private static readonly Color Ink3    = Color.FromRgb(0x94, 0x8A, 0xA3);
    private static readonly Color Ok      = Color.FromRgb(0x34, 0xE5, 0xA0);
    private static readonly Color Cyan    = Color.FromRgb(0x22, 0xD3, 0xEE);
    private static readonly Color Danger  = Color.FromRgb(0xF5, 0x48, 0x4A);
    private static readonly Color Warn    = Color.FromRgb(0xF4, 0xC0, 0x4A);

    // Segoe MDL2 Assets glyphs.
    private const string GlyphOk      = ""; // Completed (check circle)
    private const string GlyphSync    = ""; // Sync
    private const string GlyphError   = ""; // ErrorBadge
    private const string GlyphBlocked = ""; // Warning
    private const string GlyphClose   = ""; // Cancel

    private ToastCard? _current;

    public ToastHost()
    {
        // Top-center; the card animates down from the top edge.
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 14, 0, 0);
        Panel.SetZIndex(this, 100);

        Toaster.Attach(this);
        Unloaded += (_, _) => Toaster.Detach(this);
    }

    /// <summary>Show (or replace) the single active toast.</summary>
    /// <param name="label">Plain success headline (e.g. "Fan set to Boost").</param>
    /// <param name="message">Optional detail — usually an error reason.</param>
    public void Show(WriteState state, string? label, string? message)
    {
        if (state == WriteState.Idle) { Clear(); return; }

        if (_current is not null)
        {
            Children.Remove(_current);
            _current.Stop();
        }

        var (spec, detail) = Resolve(state, label, message);
        var card = new ToastCard(spec, detail, Dismiss);
        _current = card;
        Children.Add(card);
        card.PlayEnter();
    }

    /// <summary>Dismiss the current toast, if any (animated).</summary>
    public void Clear() => Dismiss(_current);

    private void Dismiss(ToastCard? card)
    {
        if (card is null || card != _current) return;
        _current = null;
        card.PlayExit(() => Children.Remove(card));
    }

    private static (ToastSpec, string?) Resolve(WriteState state, string? label, string? message) => state switch
    {
        // Success headline is the caller's plain words; the check says "it worked".
        WriteState.Verified => (new(GlyphOk, Blank(label, "Done"), Ok, Persist: false, Spin: false), null),
        WriteState.Pending  => (new(GlyphSync, Blank(label, "Applying") + "…", Cyan, Persist: true, Spin: true), null),
        WriteState.Failed   => (new(GlyphError, "Didn’t apply", Danger, Persist: true, Spin: false), message),
        WriteState.Blocked  => (new(GlyphBlocked, "Writes are off", Warn, Persist: true, Spin: false), message),
        _                   => (new(GlyphOk, Blank(label, "Ready"), Ink3, Persist: true, Spin: false), message),
    };

    private static string Blank(string? s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s!;

    private readonly record struct ToastSpec(
        string Glyph, string Title, Color Accent, bool Persist, bool Spin);

    // ================================================================
    // One toast card. Owns its enter/exit/spin/countdown animation.
    // ================================================================
    private sealed class ToastCard : Border
    {
        private const double AutoDismissMs = 3200;
        private const double DropFrom = -16; // slides down from above

        private readonly TranslateTransform _slide = new(0, DropFrom);
        private readonly Action<ToastCard?> _dismiss;
        private readonly ScaleTransform? _countdownScale;
        private DispatcherTimer? _timer;
        private RotateTransform? _spinTx;

        public ToastCard(ToastSpec spec, string? message, Action<ToastCard?> dismiss)
        {
            _dismiss = dismiss;

            Background = Frozen(Overlay);
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(1);
            // Edge tinted toward the accent — reads on the dark surface without a
            // full neon border (no neon-on-neon).
            BorderBrush = Frozen(Color.FromArgb(0x66, spec.Accent.R, spec.Accent.G, spec.Accent.B));
            MinWidth = 220;
            MaxWidth = 420;
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Cursor = System.Windows.Input.Cursors.Hand; // click to dismiss
            RenderTransform = _slide;
            RenderTransformOrigin = new Point(0.5, 0);
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5,
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });

            var row = new Grid { Margin = new Thickness(13, 11, 11, 11) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // icon chip: accent glyph on a low-alpha accent tint
            var chip = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(8),
                Background = Frozen(Color.FromArgb(0x26, spec.Accent.R, spec.Accent.G, spec.Accent.B)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var glyph = new TextBlock
            {
                Text = spec.Glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = Frozen(spec.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (spec.Spin && !MotionPrefs.ReducedMotion)
            {
                _spinTx = new RotateTransform(0);
                glyph.RenderTransform = _spinTx;
                glyph.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            chip.Child = glyph;
            Grid.SetColumn(chip, 0);
            row.Children.Add(chip);

            var text = new StackPanel { Margin = new Thickness(11, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = spec.Title,
                FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Frozen(Ink),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!string.IsNullOrWhiteSpace(message))
            {
                text.Children.Add(new TextBlock
                {
                    Text = message,
                    FontFamily = (FontFamily)Application.Current.FindResource("UiFont"),
                    FontSize = 12,
                    Foreground = Frozen(Ink2),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            if (spec.Persist)
            {
                var close = new TextBlock
                {
                    Text = GlyphClose,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 11,
                    Foreground = Frozen(Ink3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 2, 0),
                    Padding = new Thickness(4),
                };
                close.MouseEnter += (_, _) => close.Foreground = Frozen(Ink);
                close.MouseLeave += (_, _) => close.Foreground = Frozen(Ink3);
                Grid.SetColumn(close, 2);
                row.Children.Add(close);
            }

            Grid.SetRow(row, 0);
            root.Children.Add(row);

            if (!spec.Persist)
            {
                var bar = new Border
                {
                    Background = Frozen(spec.Accent),
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    RenderTransformOrigin = new Point(0, 0.5),
                };
                var scale = new ScaleTransform(1, 1);
                bar.RenderTransform = scale;
                Grid.SetRow(bar, 1);
                root.Children.Add(bar);
                _countdownScale = scale;
            }

            Child = root;
            MouseLeftButtonUp += (_, _) => _dismiss(this);

            if (!spec.Persist) ArmAutoDismiss();
        }

        public void PlayEnter()
        {
            if (MotionPrefs.ReducedMotion)
            {
                _slide.Y = 0; Opacity = 1;
                StartSpin(); StartCountdownVisual();
                return;
            }

            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            _slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(DropFrom, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

            StartSpin(); StartCountdownVisual();
        }

        public void PlayExit(Action onDone)
        {
            Stop();
            if (MotionPrefs.ReducedMotion) { onDone(); return; }

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease };
            fade.Completed += (_, _) => onDone();
            _slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(_slide.Y, DropFrom + 4, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
            BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>Stop timers/loops so a replaced card can't fire later.</summary>
        public void Stop()
        {
            _timer?.Stop();
            _timer = null;
            _spinTx?.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        private void StartSpin()
        {
            if (_spinTx is null) return;
            _spinTx.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private void StartCountdownVisual()
        {
            if (_countdownScale is null || MotionPrefs.ReducedMotion) return;
            // Linear drain over the auto-dismiss window (no easing = constant rate).
            _countdownScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(AutoDismissMs)));
        }

        private void ArmAutoDismiss()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoDismissMs) };
            _timer.Tick += (_, _) => { _timer?.Stop(); _dismiss(this); };
            _timer.Start();
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}

/// <summary>
/// Static hub the views raise write-result notifications through. The app has no
/// DI container (views new-up their own services), so a single attached
/// <see cref="ToastHost"/> in the shell receives every notification. Keeps the
/// feedback vocabulary identical across Fan, Power and RGB.
/// </summary>
public static class Toaster
{
    private static ToastHost? s_host;

    internal static void Attach(ToastHost host) => s_host = host;
    internal static void Detach(ToastHost host) { if (s_host == host) s_host = null; }

    /// <summary>Show or replace the active toast. <paramref name="label"/> is the plain success headline.</summary>
    public static void Show(WriteState state, string? label = null, string? message = null) => s_host?.Show(state, label, message);

    /// <summary>Dismiss the active toast.</summary>
    public static void Clear() => s_host?.Clear();
}
