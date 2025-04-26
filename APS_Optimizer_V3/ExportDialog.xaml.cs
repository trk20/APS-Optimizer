using APS_Optimizer_V3.ViewModels;
using Microsoft.UI.Xaml.Controls; // Added using

// --- Corrected Namespace ---
namespace APS_Optimizer_V3.Controls;

public sealed partial class ExportDialog : ContentDialog
{
    // Expose ViewModel for MainViewModel to access TargetHeight easily
    public ExportDialogViewModel? ViewModel => DataContext as ExportDialogViewModel;

    public ExportDialog()
    {
        this.InitializeComponent();
        // DataContext should be set by the caller (MainViewModel)
    }
}
