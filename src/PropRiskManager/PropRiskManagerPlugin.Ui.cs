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
}
