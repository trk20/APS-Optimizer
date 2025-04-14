// ViewModels/CellViewModel.cs
using Microsoft.UI;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.Services;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace APS_Optimizer_V3.ViewModels;
// Add ShapePreview state


public partial class CellViewModel : ViewModelBase
{
    // --- Static Colors/Brushes for Default Appearance ---
    private static readonly Brush DefaultEmptyBackground = new SolidColorBrush(Colors.White);
    private static readonly Brush DefaultShapeBackground = new SolidColorBrush(Colors.DarkCyan); // Used for previews/shapes
    private static readonly Brush DefaultBlockedBackground = new SolidColorBrush(Colors.DarkGray); // Slightly lighter than black
    private static readonly Brush BlackBackground = new SolidColorBrush(Colors.Black); // For explicitly blocked cells in result
    [ObservableProperty] private int _row;
    [ObservableProperty] private int _col;
    private Action<CellViewModel>? _onClickAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellIconElement))] // Recalculate Icon when Type changes
    [NotifyPropertyChangedFor(nameof(Background))]      // Recalculate Background when Type changes
    private CellTypeInfo _displayedCellType = CellTypeInfo.EmptyCellType;

    // Visual State Properties
    [ObservableProperty] private Brush _background = DefaultEmptyBackground; // Initialize with default
    [ObservableProperty] private UIElement? _cellIconElement = null;


    // --- Constructors ---
    public CellViewModel(int row, int col, Action<CellViewModel>? onClickAction, CellTypeInfo? type)
    {
        Row = row;
        Col = col;
        _onClickAction = onClickAction;
        // Use provided type or default to Empty. Ensure type is not null.
        _displayedCellType = type ?? CellTypeInfo.EmptyCellType;
        UpdateVisuals(); // Set initial appearance
    }

    public CellViewModel(int row, int col, CellTypeInfo? type) : this(row, col, null, type) { }
    // Called when EditorState changes (for Main Grid Editor)

    public void UpdateVisuals()
    {
        // 1. Determine Background based on Type and Context
        if (_onClickAction != null) // Main editor grid cell (Clickable)
        {
            // Editor cells are just Empty or Blocked visually
            Background = DisplayedCellType == CellTypeInfo.BlockedCellType
               ? DefaultBlockedBackground // Visually distinct blocked state for editor
               : DefaultEmptyBackground;
            CellIconElement = null; // Editor cells don't show icons
        }
        else // Preview, Shape Editor, or Result cell (Not clickable via this VM)
        {
            // Result/Preview cells show specific backgrounds or placement colors
            // Start with a default, SetResultPlacement will override for result grid
            Background = DisplayedCellType.IsEmpty
                ? DefaultEmptyBackground // Empty cells in result/preview are white
                : (DisplayedCellType == CellTypeInfo.BlockedCellType
                    ? BlackBackground      // Blocked cells in result/preview are black
                    : DefaultShapeBackground); // Default for non-empty/non-blocked (e.g., previews)

            // 2. Create Icon Element if needed
            CreateIconElement();
        }
    }



    // [incomplete] Creates the specific icon UIElement based on Type
    private void CreateIconElement()
    {
        try
        {
            var iconUri = new Uri($"ms-appx:///Assets/{DisplayedCellType.IconPath}");

            var image = new Image()
            {
                Source = iconUri,
                Stretch = Stretch.Uniform // Scales the SVG while maintaining aspect ratio
            };

            var cellBorder = new Border
            {
                //Background = PreviewShapeBackground,
                Width = 15,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Apply rotation if the type is rotatable and not facing North
            if (DisplayedCellType.IsRotatable && DisplayedCellType.CurrentRotation != RotationDirection.North)
            {
                var rotateTransform = new RotateTransform
                {
                    Angle = (int)DisplayedCellType.CurrentRotation * 90, // 0, 90, 180, 270
                };
                image.RenderTransform = rotateTransform;
                // Important: Set the origin for the transform to be the center
                image.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            cellBorder.Add(image); // Add image to the rectangle


            CellIconElement = cellBorder; // Set the property to the viewbox containing the image
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating icon for {DisplayedCellType.Name} ({DisplayedCellType.IconPath}): {ex.Message}");
            CellIconElement = null; // Fallback to no icon on error
        }
    }




    [RelayCommand]
    private void CellClicked() { if (_onClickAction != null) _onClickAction.Invoke(this); }

    // --- Methods for Result Grid ---
    public void SetResultPlacement(Brush placementColor)
    {
        if (_onClickAction != null) return; // Should not be called on editor cells

        Background = placementColor; // Override background with placement color
                                     // Re-create the icon element (it will be placed on top of the new background)
                                     // Ensure the type itself isn't empty/blocked before creating icon
        if (!DisplayedCellType.IsEmpty && DisplayedCellType != CellTypeInfo.BlockedCellType && !string.IsNullOrEmpty(DisplayedCellType.IconPath))
        {
            CreateIconElement();
        }
        else
        {
            CellIconElement = null;
        }
    }


    public void SetBlocked()
    {
        DisplayedCellType = CellTypeInfo.BlockedCellType;
        Background = new SolidColorBrush(Colors.Black);
    }

    public void SetEmpty()
    {
        DisplayedCellType = CellTypeInfo.EmptyCellType;
        Background = new SolidColorBrush(Colors.White);
    }


    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(DisplayedCellType))
        {
            // When the type changes programmatically, refresh visuals
            UpdateVisuals();
        }
    }
}