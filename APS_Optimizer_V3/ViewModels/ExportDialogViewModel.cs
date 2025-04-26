// --- START OF FILE ViewModels/ExportDialogViewModel.cs ---
using APS_Optimizer_V3.Services; // For Placement
using APS_Optimizer_V3.Services.Export;
using CommunityToolkit.Mvvm.ComponentModel; // Added for ObservableObject/Property
using CommunityToolkit.Mvvm.Input;          // Added for RelayCommand
using System.Collections.Immutable;          // Added for ImmutableList
using System.Diagnostics;

namespace APS_Optimizer_V3.ViewModels;

public partial class ExportDialogViewModel : ObservableObject
{
    // ... (rest of the ViewModel code remains the same) ...
    private readonly ExportService _exportService;
    private readonly ImmutableList<Placement> _solutionPlacements;

    [ObservableProperty]
    private string _placementSummaryText = "Calculating..."; // Displayed text for shape counts

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCostText))] // Update text when cost changes
    [NotifyCanExecuteChangedFor(nameof(CalculateCostCommand))] // Re-evaluate if command can run
    private int _targetHeight = 1; // Default height

    [ObservableProperty]
    private int _minHeight = 1; // Min allowed height

    [ObservableProperty]
    private int _maxHeight = 8; // Max allowed height

    [ObservableProperty]
    private double? _totalCost = null; // Null initially or during calculation

    public string TotalCostText => TotalCost.HasValue
        ? $"Total Material Cost: {TotalCost.Value:F0}"
        : "Cost: (Calculating...)";

    [ObservableProperty]
    private bool _isCalculatingCost = false;

    // Constructor receives necessary data
    public ExportDialogViewModel(
        ImmutableList<Placement> solutionPlacements,
        ExportService exportService,
        string placementSummary,
        int minHeight = 1,
        int maxHeight = 8)
    {
        _solutionPlacements = solutionPlacements;
        _exportService = exportService;
        _placementSummaryText = placementSummary; // Set pre-calculated summary
        _minHeight = minHeight;
        _maxHeight = maxHeight;
        _targetHeight = Math.Clamp(1, _minHeight, _maxHeight); // Ensure default is valid

        // Calculate initial cost when dialog is created
        // Use _ = CalculateCostAsync(); // Fire-and-forget async call from constructor
        CalculateCostCommand.Execute(null); // Or trigger command
    }

    // Recalculate cost when TargetHeight property changes
    partial void OnTargetHeightChanged(int value)
    {
        // Ensure height stays within bounds (NumberBox might handle this, but belt-and-suspenders)
        int clampedValue = Math.Clamp(value, MinHeight, MaxHeight);
        if (clampedValue != value)
        {
            // If clamped, update the property without re-triggering this handler
            SetProperty(ref _targetHeight, clampedValue, nameof(TargetHeight));
        }

        // Trigger cost calculation asynchronously
        // Use _ = CalculateCostAsync(); // Fire-and-forget better if command isn't needed immediately
        CalculateCostCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanCalculateCost))]
    private async Task CalculateCostAsync()
    {
        if (IsCalculatingCost) return; // Prevent concurrent calculations

        IsCalculatingCost = true;
        TotalCost = null; // Indicate calculation start
        CalculateCostCommand.NotifyCanExecuteChanged(); // Update command state

        try
        {
            await Task.Run(() =>
            {
                var (cost, _) = _exportService.CalculateExportMetrics(_solutionPlacements, TargetHeight);
                // Update property on UI thread (ObservableObject handles this)
                TotalCost = cost;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error calculating export cost: {ex}");
            TotalCost = -1; // Indicate error
        }
        finally
        {
            IsCalculatingCost = false;
            CalculateCostCommand.NotifyCanExecuteChanged(); // Update command state
        }
    }

    private bool CanCalculateCost() => !IsCalculatingCost;
}
// --- END OF FILE ViewModels/ExportDialogViewModel.cs ---