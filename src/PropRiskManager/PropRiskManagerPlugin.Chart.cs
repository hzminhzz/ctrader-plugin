using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private void OnActiveFrameChanged(ActiveFrameChangedEventArgs args) => BindToActiveChart();

    private void BindToActiveChart()
    {
        var frame = ChartManager.ActiveFrame as ChartFrame
            ?? ChartManager.OfType<ChartFrame>().FirstOrDefault(f => f.Chart.IsActive)
            ?? ChartManager.OfType<ChartFrame>().FirstOrDefault(f => f.Chart.IsVisible)
            ?? ChartManager.OfType<ChartFrame>().FirstOrDefault();

        _chart = frame?.Chart;
        _symbol = frame?.Symbol;
    }

    private void UnbindChart()
    {
        _chart = null;
        _symbol = null;
    }

}
