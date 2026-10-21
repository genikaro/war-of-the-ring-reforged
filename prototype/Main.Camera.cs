using Godot;

/// <summary>Map navigation: screen-edge scrolling, minimap jumps and the minimap view outline.</summary>
public partial class Main
{
    // Viewport pixels at each window edge that scroll the map, as in the original manual ("pushing" on screen edges).
    private const float EdgePanMargin = 8;
    private bool _pointerInWindow, _windowFocused = true;
    private readonly List<(string Type,float X,float Z)> _minimapMarkers = [];

    // Rendered automation must not consume live desktop input between its scripted events.
    private bool BlocksDesktopInput => (_constructionSmoke || _structureSmoke || _productionSmoke || _placementSmoke || _dwarfHallSmoke || _minimapSmoke || _frodoSmoke)
        && !_injectingConstructionInput;

    private void WatchPointer()
    {
        var window = GetWindow();
        window.MouseExited += () => { if (!BlocksDesktopInput) _pointerInWindow = false; };
        window.FocusExited += () => { if (!BlocksDesktopInput) _windowFocused = false; };
        window.FocusEntered += () => _windowFocused = true;
    }

    /// <summary>Screen-relative scroll direction (+right, +toward the camera) while the cursor rests on a window edge.</summary>
    private Vector2 EdgePan()
    {
        if (!_pointerInWindow || !_windowFocused || _panning || _commands.Minimap.Dragging) return Vector2.Zero;
        var screen = GetViewport().GetVisibleRect(); var p = _pointerPosition;
        float right = p.X >= screen.End.X-EdgePanMargin ? 1 : p.X <= screen.Position.X+EdgePanMargin ? -1 : 0;
        float forward = p.Y >= screen.End.Y-EdgePanMargin ? 1 : p.Y <= screen.Position.Y+EdgePanMargin ? -1 : 0;
        return new(right,forward);
    }

    private Vector2[] ViewFootprint()
    {
        var screen = GetViewport().GetVisibleRect();
        return ViewFootprint(_camera,screen,_hud.Visible ? _commands.GetGlobalRect().Position.Y : screen.End.Y);
    }

    /// <summary>Ground corners of the battlefield seen above the command panel, in map millimetres.</summary>
    private Vector2[] ViewFootprint(Camera3D camera,Rect2 screen,float bottom)
    {
        Vector2[] corners = [screen.Position,new(screen.End.X,screen.Position.Y),new(screen.End.X,bottom),new(screen.Position.X,bottom)];
        return [..corners.Select(corner => {
            Vector3 origin = camera.ProjectRayOrigin(corner),direction = camera.ProjectRayNormal(corner);
            // The ground plane sits at the camera pivot; rays near the horizon stop at a fixed range.
            float range = _distance*8, reach = direction.Y < -.01f ? Math.Min((_focus.Y-origin.Y)/direction.Y,range) : range;
            var ground = origin+direction*reach;
            return new Vector2(-ground.X*1000,ground.Z*1000);
        })];
    }

    /// <summary>Moves the camera so the minimap view outline is centered on a normalized minimap point.</summary>
    private void CenterViewOn(Vector2 point)
    {
        var nav = _world!.Navigation; var view = ViewFootprint();
        var center = view.Aggregate(Vector2.Zero,(sum,p) => sum+p)/view.Length;
        var pivot = new Vector2(-_focus.X*1000,_focus.Z*1000);
        var target = new Vector2((1-point.X)*nav.Width*nav.CellSize,point.Y*nav.Height*nav.CellSize)-(center-pivot);
        _focus = new(-target.X/1000,0,target.Y/1000); ClampFocus(); UpdateCamera();
    }
}
