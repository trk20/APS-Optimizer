
// Controls/CellGridControl.xaml.cs
using APS_Optimizer_V3.ViewModels; // For CellViewModel
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections; // For IEnumerable

namespace APS_Optimizer_V3.Controls;

public sealed partial class CellGridControl : UserControl
{
    public CellGridControl()
    {
        this.InitializeComponent();
    }

    // ItemsSource Dependency Property (for the collection of CellViewModel)
    public IEnumerable ItemsSource
    {
        get { return (IEnumerable)GetValue(ItemsSourceProperty); }
        set { SetValue(ItemsSourceProperty, value); }
    }
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(CellGridControl), new PropertyMetadata(null));

    // CellSize Dependency Property
    public double CellSize
    {
        get { return (double)GetValue(CellSizeProperty); }
        set { SetValue(CellSizeProperty, value); }
    }
    public static readonly DependencyProperty CellSizeProperty =
        DependencyProperty.Register("CellSize", typeof(double), typeof(CellGridControl), new PropertyMetadata(15.0)); // Default size

    // CellSpacing Dependency Property
    public double CellSpacing
    {
        get { return (double)GetValue(CellSpacingProperty); }
        set { SetValue(CellSpacingProperty, value); }
    }
    public static readonly DependencyProperty CellSpacingProperty =
        DependencyProperty.Register("CellSpacing", typeof(double), typeof(CellGridControl), new PropertyMetadata(0.0)); // Default spacing

    // IsEditorGrid Dependency Property (to switch between Button/Border template)
    public bool IsEditorGrid
    {
        get { return (bool)GetValue(IsEditorGridProperty); }
        set { SetValue(IsEditorGridProperty, value); }
    }
    public static readonly DependencyProperty IsEditorGridProperty =
        DependencyProperty.Register("IsEditorGrid", typeof(bool), typeof(CellGridControl), new PropertyMetadata(false));

    // Expose MainViewModel's IsSolving for Button IsEnabled binding
    // This assumes the DataContext of the UserControl will be set to the MainViewModel
    // or accessible via ElementName binding in XAML.
    // An alternative is passing IsSolving as another DependencyProperty.
    // For simplicity with ElementName binding, we might not need this DP here if
    // the Button's IsEnabled binding path is correct in XAML. Let's rely on the XAML binding first.
    /*
    public bool IsSolving
    {
        get { return (bool)GetValue(IsSolvingProperty); }
        set { SetValue(IsSolvingProperty, value); }
    }
    public static readonly DependencyProperty IsSolvingProperty =
        DependencyProperty.Register("IsSolving", typeof(bool), typeof(CellGridControl), new PropertyMetadata(false));
    */
}
