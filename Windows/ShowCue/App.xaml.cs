using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShowCue.Models;

namespace ShowCue;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Tallfelt skal følge Windows' språkinnstilling (norsk komma som desimaltegn).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(
                System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));
        Loc.Use(AppSettings.Load().Language);
        // Programikonet på alle vinduer (oppgavelinja, Alt+Tab og tittellinja), også dem som ikke setter det selv.
        var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/ShowCue.ico"));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => { if (s is Window { Icon: null } w) w.Icon = icon; }));
        base.OnStartup(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Under et show skal programmet aldri krasje på grunn av én feil cue.
        MessageBox.Show(e.Exception.Message, Loc.T("ShowCue – feil"), MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
