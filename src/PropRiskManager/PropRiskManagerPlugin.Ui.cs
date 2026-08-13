using cAlgo.API;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private static void ConfigureAspBlock(AspBlock block, double height)
    {
        block.Height = height;
        block.IsDetachable = true;
        block.DetachedWindow.Width = 390;
        block.DetachedWindow.Height = System.Math.Min(760, height + 80);
    }

    private static TextBox AddInput(StackPanel root, string label, string initialValue)
    {
        var row = new Grid(1, 2) { Margin = new Thickness(0, 2, 0, 2) };
        row.AddChild(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        var box = new TextBox { Text = initialValue, Height = 24 };
        row.AddChild(box, 0, 1);
        root.AddChild(row);
        return box;
    }

    private static double ParsePositive(string? text, double fallback)
        => TryParsePositive(text, out var value) ? value : fallback;

    private static bool TryParsePositive(string? text, out double value)
        => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;

    private static double ParseNonNegative(string? text, double fallback)
        => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : fallback;
}
