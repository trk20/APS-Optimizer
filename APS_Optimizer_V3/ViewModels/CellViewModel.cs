// ViewModels/CellViewModel.cs
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System.Diagnostics;
using APS_Optimizer_V3.Helpers; // Correct namespace
using System;

namespace APS_Optimizer_V3.ViewModels;
// Add ShapePreview state
public enum CellState { Empty, Blocked, Placed, ShapePreview }

public partial class CellViewModel : ViewModelBase
{
    // ... (Existing properties: Row, Col, DisplayNumber, _onClickAction) ...
    private int _row;
    private int _col;
    private CellState _state = CellState.Empty;
    private Brush _background = new SolidColorBrush(Colors.White);
    private int? _displayNumber = null;
    private Action<CellViewModel>? _onClickAction;

    public int Row { get => _row; set => SetProperty(ref _row, value); }
    public int Col { get => _col; set => SetProperty(ref _col, value); }

    public int? DisplayNumber
    {
        get => _displayNumber;
        set => SetProperty(ref _displayNumber, value);
        // No color update needed here directly, relies on State
    }

    public CellState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                UpdateColor();
                if (_state != CellState.Placed) DisplayNumber = null;
            }
        }
    }

    public Brush Background
    {
        get => _background;
        set => SetProperty(ref _background, value);
    }

    // Constructor allows null action
    public CellViewModel(int row, int col, Action<CellViewModel>? onClickAction, CellState initialState = CellState.Empty)
    {
        _row = row;
        _col = col;
        _onClickAction = onClickAction;
        State = initialState;
    }

    // UpdateColor needs the new state
    public void UpdateColor()
    {
        switch (State)
        {
            case CellState.Empty:
                Background = new SolidColorBrush(Colors.White);
                break;
            case CellState.Blocked:
                Background = new SolidColorBrush(Colors.Black);
                break;
            case CellState.Placed:
                // Default placed color, Solve command will override
                Background = new SolidColorBrush(Colors.LightGray);
                break;
            case CellState.ShapePreview: // Color for shape cells in preview
                Background = new SolidColorBrush(Colors.DarkCyan); // Example color
                break;
            default:
                Background = new SolidColorBrush(Colors.Gray);
                break;
        }
    }

    [RelayCommand]
    private void CellClicked()
    {
        // Only invoke click action if it's assigned (prevents clicks on result/preview cells)
        if (_onClickAction != null)
        {
            Debug.WriteLine($"Cell Clicked: Row={Row}, Col={Col}, CurrentState={State}");
            _onClickAction.Invoke(this);
        }
        else
        {
            Debug.WriteLine($"Click ignored on cell: Row={Row}, Col={Col}, State={State}");
        }
    }
}