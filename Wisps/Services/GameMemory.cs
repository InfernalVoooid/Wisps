using System.Drawing;
using GameHelper;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using GameHelper.RemoteObjects.UiElement;

namespace Wisps.Services;

internal static class GameMemory
{
    internal static Rectangle WindowArea => Core.Process.WindowArea;

    internal static LargeMapUiElement LargeMap => Core.States.InGameStateObject.GameUi.LargeMap;

    internal static bool IsWorldMapOpen => Core.States.InGameStateObject.GameUi.WorldMapPanel.IsVisible;

    internal static bool TryGetArea(out AreaInstance area)
    {
        area = null!;
        if (Core.States.GameCurrentState != GameStateTypes.InGameState) return false;

        var state = Core.States.InGameStateObject;
        if (state.Address == nint.Zero) return false;

        area = state.CurrentAreaInstance;
        return area.Address != nint.Zero && area.Player.Address != nint.Zero;
    }
}
