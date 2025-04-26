using APS_Optimizer_V3.Services;
using APS_Optimizer_V3.Services.Export;
using System.Diagnostics;

namespace APS_Optimizer_V3.ViewModels;

public partial class ExportDialogViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly ImmutableList<Placement> _solutionPlacements;

    [ObservableProperty]
    private string _placementSummaryText = "Calculating...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCostText))]
    [NotifyPropertyChangedFor(nameof(TotalBlockCountText))]
    [NotifyCanExecuteChangedFor(nameof(CalculateCostCommand))]
    private int _targetHeight = 1;

    [ObservableProperty]
    private int _minHeight = 1;

    [ObservableProperty]
    private int _maxHeight = 8;

    [ObservableProperty]
    private int _heightStep = 1;

    [ObservableProperty]
    private double? _totalCost = null;

    [ObservableProperty]
    private int? _blockCount = null;

    public string TotalCostText => TotalCost.HasValue
        ? $"Total Material Cost: {TotalCost.Value:F0}"
        : "Cost: N/A";

    public string TotalBlockCountText => BlockCount.HasValue
        ? $"Block Count: {BlockCount.Value}"
        : "Block Count: N/A";

    public ExportDialogViewModel(
        ImmutableList<Placement> solutionPlacements,
        ExportService exportService,
        string placementSummary,
        int minHeight,
        int maxHeight,
        int heightStep)
    {
        _solutionPlacements = solutionPlacements;
        _exportService = exportService;
        _placementSummaryText = placementSummary;
        _minHeight = minHeight;
        _maxHeight = maxHeight;
        _heightStep = heightStep;

        _targetHeight = Math.Clamp(minHeight, _minHeight, _maxHeight);

        // Calculate initial cost
        CalculateCost();
    }


    // Recalculate cost when TargetHeight changes
    partial void OnTargetHeightChanged(int value)
    {
        // Make sure height stays within bounds
        int clampedValue = Math.Clamp(value, MinHeight, MaxHeight);
        if (clampedValue != value)
        {
            // If clamped, update property without re-triggering handler
            SetProperty(ref _targetHeight, clampedValue, nameof(TargetHeight));
        }

        CalculateCostCommand.Execute(null);
    }

    [RelayCommand]
    private void CalculateCost()
    {
        TotalCost = null;
        BlockCount = null;

        try
        {
            var (cost, blocks) = _exportService.CalculateExportMetrics(_solutionPlacements, TargetHeight);
            TotalCost = cost;
            BlockCount = blocks;

            //Debug.WriteLine($"Calculated cost of {cost} and {blocks} blocks");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error calculating export cost: {ex}");
            TotalCost = -1;
        }
    }

}