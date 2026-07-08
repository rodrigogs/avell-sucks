using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace AvellSucks.UI.Localization;

/// <summary>
/// XAML markup extension for localized strings: <c>Text="{loc:Tr Key}"</c>.
/// Expands to a one-way binding onto <see cref="Loc.Instance"/>'s string
/// indexer (<c>Loc.Instance[Key]</c>). Because <see cref="Loc"/> raises
/// PropertyChanged for the indexer when the culture changes, every {loc:Tr}
/// re-reads its translation live — the UI re-localizes without a restart.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    /// <summary>Resource key to look up in Strings.resx.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
