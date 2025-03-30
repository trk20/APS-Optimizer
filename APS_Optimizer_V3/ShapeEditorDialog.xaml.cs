// ShapeEditorDialog.xaml.cs
using APS_Optimizer_V3.ViewModels; // Needed for ViewModel type
using Microsoft.UI.Xaml.Controls;

namespace APS_Optimizer_V3;
public sealed partial class ShapeEditorDialog : ContentDialog
{
    // Expose ViewModel for easy access after showing
    public ShapeEditorViewModel ViewModel => DataContext as ShapeEditorViewModel ??
                                            throw new InvalidOperationException("DataContext must be a ShapeEditorViewModel");


    public ShapeEditorDialog()
    {
        this.InitializeComponent();
        // DataContext should be set *before* calling ShowAsync
    }

}