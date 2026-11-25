using System.Windows;
using System.Windows.Controls;
using ShowCue.Models;

namespace ShowCue.Views;

/// <summary>Lite dialogvindu som ber om én tekst (f.eks. navnet på en scene).</summary>
public sealed class NamePrompt : Window
{
    private readonly TextBox _box = new() { Margin = new Thickness(0, 6, 0, 0) };

    private NamePrompt(string title, string label, string initial)
    {
        Title = title;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Text");
        ThemeManager.Register(this);

        _box.Text = initial;
        var ok = new Button { Content = Loc.T("OK"), Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = Loc.T("Avbryt"), Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_box);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Viser dialogen. Returnerer teksten, eller null hvis brukeren avbryter.</summary>
    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var dlg = new NamePrompt(title, label, initial) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg._box.Text : null;
    }
}
