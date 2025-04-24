using System.Collections;

namespace APS_Optimizer_V3.Controls;

public sealed partial class CellGridControl : UserControl
{
    public CellGridControl()
    {
        InitializeComponent();
    }

    public IEnumerable ItemsSource
    {
        get { return (IEnumerable)GetValue(ItemsSourceProperty); }
        set { SetValue(ItemsSourceProperty, value); }
    }
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(CellGridControl), new PropertyMetadata(null));


    public double CellSize
    {
        get { return (double)GetValue(CellSizeProperty); }
        set { SetValue(CellSizeProperty, value); }
    }
    public static readonly DependencyProperty CellSizeProperty =
        DependencyProperty.Register("CellSize", typeof(double), typeof(CellGridControl), new PropertyMetadata(15.0)); // Default size


    public double CellSpacing
    {
        get { return (double)GetValue(CellSpacingProperty); }
        set { SetValue(CellSpacingProperty, value); }
    }
    public static readonly DependencyProperty CellSpacingProperty =
        DependencyProperty.Register("CellSpacing", typeof(double), typeof(CellGridControl), new PropertyMetadata(0.0)); // Default spacing


    public bool IsEditorGrid
    {
        get { return (bool)GetValue(IsEditorGridProperty); }
        set { SetValue(IsEditorGridProperty, value); }
    }
    public static readonly DependencyProperty IsEditorGridProperty =
        DependencyProperty.Register("IsEditorGrid", typeof(bool), typeof(CellGridControl), new PropertyMetadata(false));

}
