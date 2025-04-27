// ViewModels/CellViewModel.cs
using Microsoft.UI;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using Windows.Foundation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace APS_Optimizer_V3.ViewModels;

public partial class CellViewModel : ObservableObject
{
    // --- Static Colors/Brushes ---
    private static readonly Brush DefaultEmptyBackground = new SolidColorBrush(Colors.White);
    private static readonly Brush DefaultShapeBackground = new SolidColorBrush(Colors.DarkCyan);
    private static readonly Brush BlackBackground = new SolidColorBrush(Colors.Black);

    [ObservableProperty] private int _row;
    [ObservableProperty] private int _col;
    private Action<CellViewModel>? _onClickAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CellIconElement))]
    [NotifyPropertyChangedFor(nameof(Background))]
    private CellTypeInfo _displayedCellType = CellTypeInfo.EmptyCellType;

    [ObservableProperty] private Brush _background = DefaultEmptyBackground;
    [ObservableProperty] private UIElement? _cellIconElement = null;

    public CellViewModel(int row, int col, Action<CellViewModel>? onClickAction, CellTypeInfo? type)
    {
        Row = row;
        Col = col;
        _onClickAction = onClickAction;
        _displayedCellType = type ?? CellTypeInfo.EmptyCellType;
        UpdateVisuals();
    }
    public CellViewModel(int row, int col, CellTypeInfo? type) : this(row, col, null, type) { }

    // --- Visual Update Logic ---
    public void UpdateVisuals()
    {

        // Create Icon Element if applicable
        if (!string.IsNullOrEmpty(DisplayedCellType.IconPath) && DisplayedCellType != CellTypeInfo.BlockedCellType && !DisplayedCellType.IsEmpty)
        {
            CreateIconElement();
        }
        else if (string.IsNullOrEmpty(DisplayedCellType.IconPath))
        {
            Debug.WriteLine("CellVM: No icon path found, setting CellIconElement to null.");
            CellIconElement = null;
        }
        else
        {
            CellIconElement = null;
        }
        Background = DisplayedCellType.IsEmpty ? DefaultEmptyBackground : (DisplayedCellType.Name == CellTypeInfo.BlockedCellType.Name ? BlackBackground : DefaultShapeBackground);
    }

    // Creates the visual element for the icon
    private void CreateIconElement()
    {
        CellIconElement = null; // Reset
        if (string.IsNullOrEmpty(DisplayedCellType.IconPath))
        {
            Debug.WriteLine($"CellVM ({Row},{Col}): No icon path found for {DisplayedCellType.Name}. CellIconElement remains null.");
            return;
        }

        //Debug.WriteLine($"CellVM ({Row},{Col}): Creating icon for {DisplayedCellType.Name} using path '{DisplayedCellType.IconPath}'");
        try
        {
            var iconUri = new Uri($"ms-appx:///Assets/{DisplayedCellType.IconPath}");
            //Debug.WriteLine($"CellVM ({Row},{Col}): Icon URI: {iconUri}");

            var bitmapImage = new BitmapImage(iconUri);
            //bitmapImage.ImageOpened += (s, e) => Debug.WriteLine($"CellVM ({Row},{Col}): PNG Image opened for {iconUri}");
            bitmapImage.ImageFailed += (s, e) => Debug.WriteLine($"CellVM ({Row},{Col}): PNG Image FAILED to open for {iconUri}. Error: {e.ErrorMessage}");

            var image = new Image()
            {
                Source = bitmapImage,
                Stretch = Stretch.Uniform
            };

            // Apply rotation if needed
            if (DisplayedCellType.IsRotatable && DisplayedCellType.CurrentRotation != RotationDirection.North)
            {
                var rotateTransform = new RotateTransform { Angle = (int)DisplayedCellType.CurrentRotation * 90, CenterX = 0.5, CenterY = 0.5 };
                image.RenderTransform = rotateTransform;
                image.RenderTransformOrigin = new Point(0.5, 0.5);
                //Debug.WriteLine($"CellVM ({Row},{Col}): Applied rotation {rotateTransform.Angle} deg");
            }

            var viewbox = new Viewbox
            {
                Child = image,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            CellIconElement = viewbox;
            //Debug.WriteLine($"CellVM ({Row},{Col}): Successfully created and assigned CellIconElement (Viewbox).");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CellVM ({Row},{Col}): ***** ERROR creating icon Viewbox for {DisplayedCellType.Name} ({DisplayedCellType.IconPath}): {ex.Message}");
        }
    }


    [RelayCommand]
    private void CellClicked() { _onClickAction?.Invoke(this); }

    // --- Methods for Result Grid ---
    public void SetResultPlacement(Brush placementColor)
    {
        if (_onClickAction != null) return;
        Background = placementColor;
        // Re-create the icon element 
        if (!DisplayedCellType.IsEmpty && DisplayedCellType != CellTypeInfo.BlockedCellType && !string.IsNullOrEmpty(DisplayedCellType.IconPath))
        {
            CreateIconElement();
        }
        else
        {
            CellIconElement = null;
        }
    }

    public void SetBlocked() { DisplayedCellType = CellTypeInfo.BlockedCellType; }
    public void SetEmpty() { DisplayedCellType = CellTypeInfo.EmptyCellType; }

    partial void OnDisplayedCellTypeChanged(CellTypeInfo value)
    {
        UpdateVisuals(); // Logic moved here
    }
}
