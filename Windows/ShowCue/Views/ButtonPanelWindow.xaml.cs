using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ShowCue.Models;

namespace ShowCue.Views;

/// <summary>Knappepanel: store knapper som starter og stopper cuer direkte, uten å flytte GO-markøren. Viser cuer merket «Vis i knappepanelet» og cuer med hurtigtast.</summary>
public partial class ButtonPanelWindow : Window
{
    private readonly Action<Cue> _toggle;
    private readonly Action _pause, _panic;
    private readonly Action<int> _columnsChanged;
    private UniformGrid? _grid;
    private ListCollectionView? _view;

    public ButtonPanelWindow(int columns, Action<Cue> toggle, Action pause, Action panic, Action<int> columnsChanged)
    {
        InitializeComponent();
        ThemeManager.Register(this);
        _toggle = toggle; _pause = pause; _panic = panic; _columnsChanged = columnsChanged;
        ColumnSlider.Value = Math.Clamp(columns, 2, 8);
    }

    /// <summary>Kalles når et nytt show åpnes.</summary>
    public void SetCues(IList<Cue> cues)
    {
        _view = new ListCollectionView((System.Collections.IList)cues)
        {
            Filter = o => o is Cue c && IsPanelCue(c),
            IsLiveFiltering = true,
        };
        _view.LiveFilteringProperties.Add(nameof(Cue.Hotkey));
        _view.LiveFilteringProperties.Add(nameof(Cue.InButtonPanel));
        ((INotifyCollectionChanged)_view).CollectionChanged += (_, _) => UpdateEmpty();
        Tiles.ItemsSource = _view;
        UpdateEmpty();
    }

    public static bool IsPanelCue(Cue c) => c.InButtonPanel || !string.IsNullOrEmpty(c.Hotkey);

    private void UpdateEmpty() => EmptyText.Visibility = _view is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;

    private void TileGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _grid = (UniformGrid)sender;
        _grid.Columns = (int)ColumnSlider.Value;
    }

    private void ColumnSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_grid != null) _grid.Columns = (int)e.NewValue;
        if (IsLoaded) _columnsChanged((int)e.NewValue);
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is Cue c) _toggle(c);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _pause();
    private void Panic_Click(object sender, RoutedEventArgs e) => _panic();
}
