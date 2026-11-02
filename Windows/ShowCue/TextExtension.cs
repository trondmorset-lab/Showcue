using System.Windows.Markup;
using ShowCue.Models;

namespace ShowCue;

/// <summary>Oversatt tekst i XAML: <c>Text="{local:T 'Lagre'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string text) => Text = text;

    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
