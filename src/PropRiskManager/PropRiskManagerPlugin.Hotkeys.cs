using System.Globalization;
using cAlgo.API;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private Chart? _cursorChart;
    private double? _cursorPrice;

    private void InitializeTradeHotkeys()
    {
        Hotkeys.TryAdd(Key.E, ModifierKeys.Shift, OnShiftEHotkey);
        ChartManager.ActiveFrameChanged += OnHotkeyActiveFrameChanged;
        BindCursorChart();
    }

    private void DisposeTradeHotkeys()
    {
        Hotkeys.Remove(Key.E, ModifierKeys.Shift);
        ChartManager.ActiveFrameChanged -= OnHotkeyActiveFrameChanged;
        UnbindCursorChart();
    }

    private void OnHotkeyActiveFrameChanged(ActiveFrameChangedEventArgs args)
    {
        BindCursorChart();
    }

    private void BindCursorChart()
    {
        UnbindCursorChart();

        if (ChartManager.ActiveFrame is not ChartFrame frame)
            return;

        _cursorChart = frame.Chart;
        _cursorPrice = null;
        _cursorChart.MouseMove += OnCursorMouseMove;
    }

    private void UnbindCursorChart()
    {
        if (_cursorChart != null)
            _cursorChart.MouseMove -= OnCursorMouseMove;

        _cursorChart = null;
        _cursorPrice = null;
    }

    private void OnCursorMouseMove(ChartMouseEventArgs args)
    {
        if (_cursorChart == null)
            return;

        _cursorPrice = _cursorChart.YToYValue(args.MouseY);
    }

    private void OnShiftEHotkey(HotkeyArgs args)
    {
        if (!_cursorPrice.HasValue || ChartManager.ActiveFrame is not ChartFrame frame)
            return;

        var price = _cursorPrice.Value;
        _useEntryPrice.IsChecked = true;
        _entryPrice.Text = price.ToString("F" + frame.Symbol.Digits, CultureInfo.InvariantCulture);

        if (_entryLine != null && ReferenceEquals(_chart, frame.Chart))
            _entryLine.Y = price;

        RecalculatePreview();
    }
}
