using System.Windows;
using System.Windows.Controls;
using ShowCue.Models;

namespace ShowCue.Views;

/// <summary>«Sjekk showet»: viser funnene fra forhåndssjekken som en liste (feil, advarsler og ok). «Sjekk på nytt» kjører sjekken igjen.</summary>
public sealed class PreflightWindow : Window
{
    private readonly Func<List<PreflightFinding>> _run;
    private readonly TextBlock _summary = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 16, 18, 8) };
    private readonly StackPanel _rows = new() { Margin = new Thickness(18, 0, 18, 8) };

    public PreflightWindow(Func<List<PreflightFinding>> run)
    {
        _run = run;
        Title = Loc.T("Sjekk showet");
        Width = 720;
        Height = 520;
        MinWidth = 420;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Text");
        ThemeManager.Register(this);
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/ShowCue.ico"));

        var again = new Button { Content = Loc.T("Sjekk på nytt"), Width = 120, Margin = new Thickness(0, 0, 8, 0) };
        again.Click += (_, _) => Refresh();
        var close = new Button { Content = Loc.T("Lukk"), Width = 90, IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 4, 18, 16) };
        buttons.Children.Add(again);
        buttons.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(_summary, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_summary);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rows });
        Content = root;
        Refresh();
    }

    private void Refresh()
    {
        var findings = _run();
        var errors = PreflightCheck.Count(findings, PreflightSeverity.Error);
        var warnings = PreflightCheck.Count(findings, PreflightSeverity.Warning);
        _summary.Text = errors + warnings == 0 ? Loc.T("Alt ser bra ut.") : Loc.F("{0} feil og {1} advarsler.", errors, warnings);

        _rows.Children.Clear();
        foreach (var f in findings)
        {
            var (icon, brush) = f.Severity switch
            {
                PreflightSeverity.Error => ("✕", "Danger"),
                PreflightSeverity.Warning => ("⚠", "Wait"),
                _ => ("✓", "Running"),
            };
            var mark = new TextBlock { Text = icon, Width = 26, FontWeight = FontWeights.Bold };
            mark.SetResourceReference(TextBlock.ForegroundProperty, brush);
            DockPanel.SetDock(mark, Dock.Left);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(mark);
            row.Children.Add(new TextBlock { Text = f.Message, TextWrapping = TextWrapping.Wrap });
            _rows.Children.Add(row);
        }
    }
}
