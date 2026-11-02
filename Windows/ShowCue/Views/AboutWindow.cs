using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ShowCue.Models;

namespace ShowCue.Views;

/// <summary>«Om ShowCue»: versjon, opphavsrett, at det ikke gis garanti (GPL 3, punkt 5d) og lisensene som følger med.</summary>
public sealed class AboutWindow : Window
{
    public const string Copyright = "Copyright © 2026 Trond Morset";
    public const string Source = "https://github.com/trondmorset-lab/Showcue-";

    public static string Resource(string name)
    {
        using var s = typeof(AboutWindow).Assembly.GetManifestResourceStream(name);
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public AboutWindow()
    {
        Title = Loc.T("Om ShowCue");
        Width = 640; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Text");
        ThemeManager.Register(this);

        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/ShowCue.ico"));
        var head = new StackPanel { Margin = new Thickness(18, 16, 18, 8) };
        head.Children.Add(new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo-256.png")),
            Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10),
        });
        head.Children.Add(new TextBlock { Text = "ShowCue " + MainWindow.AppVersion, FontSize = 22, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = Loc.T("Showavvikling for lyd, video, bilde, lys og tidskode."), Margin = new Thickness(0, 2, 0, 10) });
        head.Children.Add(new TextBlock { Text = Copyright });
        head.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6),
            Text = Loc.T("ShowCue er fri programvare: du kan dele det videre og endre det under vilkårene i GNU General Public License versjon 3. ") +
                   Loc.T("Programmet leveres UTEN NOEN FORM FOR GARANTI, heller ikke underforstått garanti for salgbarhet eller egnethet for et bestemt formål."),
        });
        var link = new Hyperlink(new Run(Source)) { NavigateUri = new Uri(Source) };
        link.RequestNavigate += (_, e) => { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; };
        head.Children.Add(new TextBlock(link));

        var tabs = new TabControl { Margin = new Thickness(18, 4, 18, 8) };
        tabs.Items.Add(Page(Loc.T("Tredjepartslisenser"), Resource("ThirdPartyNotices.txt")));
        tabs.Items.Add(Page(Loc.T("GPL versjon 3"), Resource("License.txt")));

        var ok = new Button { Content = Loc.T("Lukk"), Width = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 0, 18, 16) };
        ok.Click += (_, _) => Close();

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(ok, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(ok);
        root.Children.Add(tabs);
        Content = root;
    }

    private static TabItem Page(string header, string text) => new()
    {
        Header = header,
        Content = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0),
        },
    };
}
