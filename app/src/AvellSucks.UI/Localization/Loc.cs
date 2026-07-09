using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace AvellSucks.UI.Localization;

/// <summary>
/// Runtime localization provider. Bindings target the string indexer
/// (<c>Loc.Instance[key]</c>); changing <see cref="Culture"/> raises
/// PropertyChanged for the indexer, so every bound string re-reads live — no
/// restart. Backed by the embedded Strings.resx (EN, invariant/default) and
/// Strings.pt-BR.resx (PT) via a ResourceManager.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private static readonly ResourceManager Rm =
        new("AvellSucks.UI.Localization.Strings", typeof(Loc).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    private Loc() { }

    /// <summary>The active UI culture. Setting it re-localizes the whole UI live.</summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (Equals(_culture, value)) return;
            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            // Empty/null property name = "all properties changed" → every
            // {loc:Tr} binding on the indexer re-evaluates.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>Localized string for a key; falls back to the key itself if missing.</summary>
    public string this[string key]
        => Rm.GetString(key, _culture) ?? key;

    /// <summary>Convenience for code-behind: translate a key now.</summary>
    public static string T(string key) => Instance[key];

    /// <summary>
    /// Run <paramref name="relocalize"/> now and again on every runtime culture
    /// change. For views that set text imperatively (which drops the {loc:Tr}
    /// binding) and need to re-localize on language switch. Intended for cached,
    /// app-lifetime views, so it does not unsubscribe.
    /// </summary>
    public static void OnCultureChanged(Action relocalize)
    {
        relocalize();
        Instance.PropertyChanged += (_, _) => relocalize();
    }
}
