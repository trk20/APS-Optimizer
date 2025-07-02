using APS_Optimizer_V3.Services;
using APS_Optimizer_V3.Services.Export;
using System.Diagnostics;

namespace APS_Optimizer_V3.ViewModels;

public partial class ExportDialogViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly ImmutableList<Placement> _solutionPlacements;

    private string _placementSummaryText = "Calculating...";
    public string PlacementSummaryText
    {
        get => _placementSummaryText;
        set => SetProperty(ref _placementSummaryText, value);
    }

    public string EffectiveHeightText => IncludeBottomLayer && ShowBottomLayerOption
        ? $"Effective Height: {TargetHeight + 1}m (includes bottom layer)"
        : $"Target Height: {TargetHeight}m";

    private int _targetHeight = 1;
    public int TargetHeight
    {
        get => _targetHeight;
        set
        {
            int newValue = value;
            newValue = Math.Clamp(newValue, MinHeight, MaxHeight);
            if (HeightStep > 1 && newValue >= MinHeight)
            {
                int remainder = (newValue - MinHeight) % HeightStep;
                if (remainder != 0) { newValue -= remainder; }
                newValue = Math.Max(MinHeight, newValue);
            }

            if (SetProperty(ref _targetHeight, newValue))
            {
                OnPropertyChanged(nameof(TotalCostText));
                OnPropertyChanged(nameof(TotalBlockCountText));
                OnPropertyChanged(nameof(EffectiveHeightText));
                CalculateCostCommand.NotifyCanExecuteChanged();
                CalculateCostCommand.Execute(null);
            }
        }
    }

    private int _minHeight = 1;
    public int MinHeight
    {
        get => _minHeight;
        private set => SetProperty(ref _minHeight, value);
    }

    private int _maxHeight = 8;
    public int MaxHeight
    {
        get => _maxHeight;
        private set => SetProperty(ref _maxHeight, value);
    }

    private int _heightStep = 1;
    public int HeightStep
    {
        get => _heightStep;
        private set => SetProperty(ref _heightStep, value);
    }

    private double? _totalCost = null;
    public double? TotalCost
    {
        get => _totalCost;
        private set
        {
            if (SetProperty(ref _totalCost, value))
            {
                OnPropertyChanged(nameof(TotalCostText));
            }
        }
    }

    private int? _blockCount = null;
    public int? BlockCount
    {
        get => _blockCount;
        private set
        {
            if (SetProperty(ref _blockCount, value))
            {
                OnPropertyChanged(nameof(TotalBlockCountText));
            }
        }
    }

    private bool _includeBottomLayer = false;
    public bool IncludeBottomLayer
    {
        get => _includeBottomLayer;
        set
        {
            if (SetProperty(ref _includeBottomLayer, value))
            {
                OnPropertyChanged(nameof(TotalCostText));
                OnPropertyChanged(nameof(TotalBlockCountText));
                OnPropertyChanged(nameof(EffectiveHeightText));
                CalculateCostCommand.NotifyCanExecuteChanged();
                CalculateCostCommand.Execute(null);
            }
        }
    }

    private bool _showBottomLayerOption = false;
    public bool ShowBottomLayerOption
    {
        get => _showBottomLayerOption;
        private set => SetProperty(ref _showBottomLayerOption, value);
    }


    public string TotalCostText => TotalCost.HasValue
        ? $"Total Material Cost: {TotalCost.Value:F0}{(IncludeBottomLayer ? " (with bottom layer)" : "")}"
        : "Cost: N/A";

    public string TotalBlockCountText => BlockCount.HasValue
        ? $"Block Count: {BlockCount.Value}{(IncludeBottomLayer ? " (with bottom layer)" : "")}"
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

        // Check if additional layers are available
        _showBottomLayerOption = exportService.HasAdditionalLayers(solutionPlacements);

        // Calculate initial cost
        CalculateCost();
    }

    [RelayCommand]
    private void CalculateCost()
    {
        TotalCost = null;
        BlockCount = null;

        try
        {
            var (cost, blocks) = _exportService.CalculateExportMetricsWithLayers(_solutionPlacements, TargetHeight, IncludeBottomLayer);
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