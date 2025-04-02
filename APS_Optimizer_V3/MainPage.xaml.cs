// MainPage.xaml.cs
using APS_Optimizer_V3.ViewModels; // Ensure ViewModel namespace is included
using Microsoft.UI.Xaml; // Needed for RoutedEventArgs
using Microsoft.UI.Xaml.Controls;

namespace APS_Optimizer_V3;
public sealed partial class MainPage : Page
{
    // Helper to access the ViewModel strongly-typed
    public MainViewModel ViewModel => DataContext as MainViewModel ??
                                      throw new InvalidOperationException("DataContext is not MainViewModel");

    public MainPage()
    {
        this.InitializeComponent();
        // Hook into Unloaded event for cleanup if MainViewModel is IDisposable
        // (Assuming MainViewModel implements IDisposable from previous steps)
        this.Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Dispose the ViewModel when the page is unloaded
        if (this.DataContext is IDisposable disposableViewModel)
        {
            disposableViewModel.Dispose();
        }
    }

    private async void EditShapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Get the MenuFlyoutItem that was clicked
        if (sender is MenuFlyoutItem menuItem)
        {
            // Get the DataContext of the MenuFlyoutItem, which is the ShapeViewModel
            if (menuItem.DataContext is ShapeViewModel shapeToEdit)
            {
                // Get the XamlRoot from the MenuFlyoutItem itself (most reliable way from a flyout)
                var xamlRoot = menuItem.XamlRoot;

                if (xamlRoot != null)
                {
                    // Call the ViewModel's method to show the dialog
                    // No need for the separate ShowEditShapeDialogCommand anymore
                    await CustomViewModel.ShowEditShapeDialog(shapeToEdit, XamlRoot);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Error: Could not get XamlRoot from MenuFlyoutItem.");
                    // TODO: Show error to user?
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Error: DataContext of MenuFlyoutItem is not a ShapeViewModel.");
            }
        }
    }

    private async void RemoveShapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem)
        {
            if (menuItem.DataContext is ShapeViewModel shapeToRemove)
            {
                var xamlRoot = menuItem.XamlRoot;
                if (xamlRoot != null)
                {
                    await ViewModel.RequestRemoveShape(shapeToRemove, xamlRoot);
                }
                else
                {
                    Console.WriteLine("Error: Could not get XamlRoot from MenuFlyoutItem for removal.");
                }
            }
            else
            {
                Console.WriteLine("Error: DataContext of MenuFlyoutItem is not a ShapeViewModel for removal.");
            }
        }
    }
}