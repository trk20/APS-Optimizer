using APS_Optimizer_V3.ViewModels;

namespace APS_Optimizer_V3.Controls;

public sealed partial class ExportDialog : ContentDialog
{
    public ExportDialogViewModel? ViewModel => DataContext as ExportDialogViewModel;

    public ExportDialog()
    {
        InitializeComponent();
    }
}
