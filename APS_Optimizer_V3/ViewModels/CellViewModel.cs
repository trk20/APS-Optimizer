// ViewModels/CellViewModel.cs
using Microsoft.UI;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers;
using APS_Optimizer_V3.Services;
using Microsoft.UI.Xaml.Shapes;

namespace APS_Optimizer_V3.ViewModels;
// Add ShapePreview state

public enum EditorCellState { Empty, Blocked }

public partial class CellViewModel : ViewModelBase
{
    // --- Static Colors/Brushes for Default Appearance ---
    private static readonly Brush DefaultEmptyBackground = new SolidColorBrush(Colors.White);
    private static readonly Brush DefaultShapeBackground = new SolidColorBrush(Colors.DarkCyan);
    private static readonly Brush DefaultBlockedBackground = new SolidColorBrush(Colors.Black);
    private static readonly Brush UnknownTypeBackground = new SolidColorBrush(Colors.Gray);
    // Icon colors (usually contrasting with DefaultShapeBackground)
    private static readonly Brush DefaultIconForeground = new SolidColorBrush(Colors.White);
    // Clip specific colors
    private static readonly Brush DefaultClipLineBrush = new SolidColorBrush(Colors.DarkGray) { };
    private static readonly Brush DefaultClipBorderBrush = new SolidColorBrush(Colors.DarkGray);
    // -----------------------------------------------------

    [ObservableProperty] private int _row;
    [ObservableProperty] private int _col;
    [ObservableProperty] private int? _displayNumber = null;
    private Action<CellViewModel>? _onClickAction;

    [ObservableProperty] private CellType _type = CellType.Empty;
    [ObservableProperty] private EditorCellState _editorState = EditorCellState.Empty;

    // Visual State Properties
    [ObservableProperty] private Brush _background = DefaultEmptyBackground; // Initialize with default
    [ObservableProperty] private Visibility _displayNumberVisible = Visibility.Collapsed;
    [ObservableProperty] private UIElement? _cellIconElement = null;

    // --- Constants ---
    private const double ResultCellSize = 15.0;


    // --- Constructors ---
    public CellViewModel(int row, int col, Action<CellViewModel>? onClickAction, EditorCellState initialState = EditorCellState.Empty)
    {
        Row = row; Col = col; _onClickAction = onClickAction;
        EditorState = initialState; Type = CellType.Empty;
        UpdateVisuals(); // Call combined update
    }
    public CellViewModel(int row, int col, Action<CellViewModel>? onClickAction, CellType initialType = CellType.Empty)
    {
        Row = row; Col = col; _onClickAction = onClickAction;
        EditorState = EditorCellState.Empty; // Assume empty state if not specified
        Type = initialType;
        UpdateVisuals(); // Call combined update
    }
    public CellViewModel(int row, int col, CellType initialType = CellType.Empty)
    {
        Row = row; Col = col; _onClickAction = null;
        EditorState = EditorCellState.Empty; Type = initialType;
    }

    // Called when Type changes (for Result/Preview/ShapeEditor)
    partial void OnTypeChanged(CellType value) => UpdateVisuals();
    // Called when EditorState changes (for Main Grid Editor)
    partial void OnEditorStateChanged(EditorCellState value) => UpdateVisuals();


    // Sets visuals based on CellType (Result/Preview/ShapeEditor)
    public void UpdateVisuals()
    {
        // Note: This method sets the *default* appearance based on Type/State.
        // SetResultPlacement will override the Background later.

        CellIconElement = null;
        DisplayNumberVisible = DisplayNumber.HasValue ? Visibility.Visible : Visibility.Collapsed;

        if (_onClickAction != null) // Main editor grid cell
        {
            Background = EditorState == EditorCellState.Blocked
               ? DefaultBlockedBackground
               : DefaultEmptyBackground;
        }
        else // Preview, Shape Editor, or Result cell
        {
            // Set default background - THIS will be overridden by SetResultPlacement
            Background = Type == CellType.Empty
                ? DefaultEmptyBackground
                : DefaultBlockedBackground;

            // Create icon element using DEFAULT colors initially
            CreateIconElement(ResultCellSize, DefaultShapeBackground); // Pass default BG for icons
        }
    }


    // Creates the specific icon UIElement based on Type
    private void CreateIconElement(double cellSize, Brush? placementBrush = null)
    {
        // Use placementBrush if provided (for result grid), otherwise use default shape background
        Brush actualBackground = placementBrush ?? (Type == CellType.Empty ? DefaultEmptyBackground : DefaultShapeBackground);
        // Update the main Background property as well, so result grid starts with correct color before number is added
        Background = actualBackground;

        double innerSize = cellSize;
        double iconStrokeThickness = 1.0;

        switch (Type)
        {
            case CellType.Empty:
            case CellType.Generic:
                CellIconElement = null;
                break;

            case CellType.Loader:
                // Loader stroke remains constant (White)
                CellIconElement = new Ellipse
                {
                    Width = innerSize * 0.7,
                    Height = innerSize * 0.7,
                    Stroke = DefaultIconForeground,
                    StrokeThickness = iconStrokeThickness,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                break;

            case CellType.Cooler:
                var coolerGrid = new Grid();
                // Outer fill remains constant (White)
                var outerCooler = new Ellipse { Width = innerSize * 0.8, Height = innerSize * 0.8, Fill = DefaultIconForeground, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                // *** Inner dot uses the ACTUAL background color ***
                var innerCooler = new Ellipse { Width = innerSize * 0.4, Height = innerSize * 0.4, Fill = actualBackground, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                coolerGrid.Children.Add(outerCooler);
                coolerGrid.Children.Add(innerCooler);
                CellIconElement = coolerGrid;
                break;

            case CellType.ClipN:
            case CellType.ClipE:
            case CellType.ClipS:
            case CellType.ClipW:
                Background = DefaultEmptyBackground; // Let background be set by main logic
                // *** Pass the actual background brush to CreateClipVisual ***
                CellIconElement = CreateClipVisual(Type, cellSize, actualBackground);
                break;

            default:
                CellIconElement = null;
                Background = UnknownTypeBackground;
                break;
        }
    }


    // *** REVISED Clip creation method ***
    private UIElement CreateClipVisual(CellType clipType, double cellSize, Brush backgroundBrush)
    {
        double clipLengthRatio = 0.7;
        double clipWidthRatio = 0.6;
        double lineThickness = 1.0;
        int numberOfLines = 5;
        double linePaddingAmount = 1.5;

        double clipWidth, clipHeight;
        Orientation lineOrientation;
        VerticalAlignment vAlign = VerticalAlignment.Center;
        HorizontalAlignment hAlign = HorizontalAlignment.Center;

        if (clipType == CellType.ClipN || clipType == CellType.ClipS)
        {
            clipHeight = cellSize * clipLengthRatio; clipWidth = cellSize * clipWidthRatio;
            lineOrientation = Orientation.Horizontal;
            if (clipType == CellType.ClipN) vAlign = VerticalAlignment.Top; else vAlign = VerticalAlignment.Bottom;
        }
        else
        {
            clipWidth = cellSize * clipLengthRatio; clipHeight = cellSize * clipWidthRatio;
            lineOrientation = Orientation.Vertical;
            if (clipType == CellType.ClipE) hAlign = HorizontalAlignment.Right; else hAlign = HorizontalAlignment.Left;
        }

        var container = new Grid(); // Fills the cell

        var clipVisual = new Border // The visible clip rectangle background/border
        {
            Width = clipWidth,
            Height = clipHeight,
            Background = backgroundBrush, // Use passed brush
            BorderBrush = DefaultClipBorderBrush,   // Use defined brush
            BorderThickness = new Thickness(0.5),
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign
        };

        var lineGrid = new Grid // Grid for lines
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = GetLineGridMargin(clipType, linePaddingAmount) // Use margin helper
        };

        // Add Row/Column definitions
        if (lineOrientation == Orientation.Horizontal) { for (int i = 0; i < (numberOfLines * 2) - 1; i++) lineGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); }
        else { for (int i = 0; i < (numberOfLines * 2) - 1; i++) lineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); }

        // Add the lines
        for (int i = 0; i < numberOfLines; i++)
        {
            var line = new Rectangle { Fill = DefaultClipLineBrush }; // Use defined brush
            if (lineOrientation == Orientation.Horizontal)
            {
                line.Height = lineThickness; line.VerticalAlignment = VerticalAlignment.Center; line.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetRow(line, i * 2); Grid.SetColumn(line, 0);
            }
            else
            {
                line.Width = lineThickness; line.HorizontalAlignment = HorizontalAlignment.Center; line.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(line, i * 2); Grid.SetRow(line, 0);
            }
            lineGrid.Children.Add(line);
        }

        clipVisual.Child = lineGrid;
        container.Children.Add(clipVisual);
        return container;
    }


    private Thickness GetLineGridMargin(CellType clipType, double padding)
    {
        switch (clipType)
        {
            case CellType.ClipN: // Attached Top, pad Left, Right, Bottom
                return new Thickness(padding, 0, padding, padding);
            case CellType.ClipE: // Attached Right, pad Left, Top, Bottom
                return new Thickness(padding, padding, 0, padding);
            case CellType.ClipS: // Attached Bottom, pad Left, Top, Right
                return new Thickness(padding, padding, padding, 0);
            case CellType.ClipW: // Attached Left, pad Right, Top, Bottom
                return new Thickness(0, padding, padding, padding);
            default: // Should not happen for clips
                return new Thickness(padding);
        }
    }



    [RelayCommand]
    private void CellClicked() { if (_onClickAction != null) _onClickAction.Invoke(this); }

    // --- Methods for Result Grid ---
    public void SetResultPlacement(Brush placementColor, int number)
    {
        // Recreate the icon element using the specific placement color
        // This ensures elements like Cooler inner dot or Clip background get the right color
        CreateIconElement(ResultCellSize, placementColor); // Pass the placement color

        // Set the number display
        DisplayNumber = number;
        DisplayNumberVisible = Visibility.Visible;
    }


    public void SetBlocked()
    {
        Type = CellType.Empty; // Treat blocked as visually empty for type icons
        EditorState = EditorCellState.Blocked; // Set editor state if applicable (might not be used for result grid)
        Background = new SolidColorBrush(Colors.Black);
        DisplayNumber = null;
        DisplayNumberVisible = Visibility.Collapsed;
        // Ensure icons are hidden (UpdateVisuals called by Type setter handles this)
    }

    public void SetEmpty()
    {
        Type = CellType.Empty;
        EditorState = EditorCellState.Empty;
        Background = new SolidColorBrush(Colors.White);
        DisplayNumber = null;
        DisplayNumberVisible = Visibility.Collapsed;
        // Ensure icons are hidden (UpdateVisuals called by Type setter handles this)
    }
}