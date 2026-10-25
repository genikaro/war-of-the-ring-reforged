using Godot;
using Reforged.Simulation;

public partial class BattleMinimap : Control
{
    public event Action<Vector2>? Chosen;
    /// <summary>A right-click on the map: the normalized point, whether the order is queued, and whether it marches.</summary>
    public event Action<Vector2,bool,bool>? Ordered;
    private SimWorld? _world;
    private ImageTexture? _terrain;
    private (Texture2D Icon,float X,float Z)[] _markers = [];
    /// <summary>Ground corners of the battlefield view in map millimetres.</summary>
    public Vector2[] View { get; set; } = [];
    public bool Dragging { get; private set; }
    /// <summary>Each player slot's colour; unit dots draw a lighter shade of it.</summary>
    public Color[] TeamColors { get; set; } = [];
    public IReadOnlyList<(Texture2D Icon,float X,float Z)> Markers => _markers;
    private const float MarkerSize = 12;

    /// <summary>Original minimap marker for a neutral object type, or null when the source has none.</summary>
    public static string? MarkerIcon(string type) => type switch {
        "OBJECT_RESOURCE_ORE_PILE" => "console_minimap_ore",
        "OBJECT_RESOURCE_FOOD_WELL" => "console_minimap_well",
        _ when type.StartsWith("OBJECT_PLACE_OF_POWER") => "console_minimap_place_of_power",
        _ => null };

    public override void _Ready() => ClipContents = true;

    public void Configure(SimWorld world)
    {
        _world = world; var nav = world.Navigation;
        using var image = Image.CreateEmpty(nav.Width,nav.Height,false,Image.Format.Rgba8);
        for (int z = 0; z < nav.Height; z++) for (int x = 0; x < nav.Width; x++)
        {
            int node = z*nav.Width+x; float shade = Math.Clamp(nav.NodeHeight(node)/30000f,0,.45f);
            image.SetPixel(x,nav.Height-1-z,nav.CanStand(node) ? new Color(.18f+shade,.25f+shade,.10f+shade) : new Color(.12f+shade,.14f+shade,.10f+shade));
        }
        _terrain = ImageTexture.CreateFromImage(image);
    }

    public void SetMarkers(IEnumerable<(Texture2D Icon,float X,float Z)> markers) => _markers = markers.ToArray();

    /// <summary>Minimap position of a map point in millimetres. North (source +y, the map's Z) is up, as on the
    /// original minimap and in the default camera view (build-75).</summary>
    public Vector2 MapPoint(float x,float z)
    {
        var nav = _world!.Navigation;
        return new(x/(nav.Width*nav.CellSize)*Size.X,(1-z/(nav.Height*nav.CellSize))*Size.Y);
    }

    /// <summary>Map point in millimetres of a normalized minimap position; the inverse of MapPoint.</summary>
    public Vector2 MapPosition(Vector2 point)
    {
        var nav = _world!.Navigation;
        return new(point.X*nav.Width*nav.CellSize,(1-point.Y)*nav.Height*nav.CellSize);
    }

    /// <summary>Every standing building and foundation as its footprint outline on the minimap, with its team.</summary>
    public IEnumerable<(int Team,bool Complete,Vector2[] Outline)> Buildings()
    {
        foreach (var site in _world!.ConstructionSites.Values.Where(s => s.Team >= 0 && s.Health > 0))
            foreach (var footprint in _world.Navigation.Footprints(site.Obstacle))
                yield return (site.Team,site.Complete,footprint.Polygon.Select(p => MapPoint(p.X,p.Z)).ToArray());
    }

    public override void _Draw()
    {
        if (_world == null || _terrain == null) return;
        DrawTextureRect(_terrain,new(Vector2.Zero,Size),false);
        foreach (var (icon,x,z) in _markers)
            DrawTextureRect(icon,new(MapPoint(x,z)-Vector2.One*MarkerSize/2,Vector2.One*MarkerSize),false);
        // A held place of power is ringed in its holder's colour (build-54).
        foreach (var place in _world.PlacesOfPower.Where(p => p.Owner >= 0 && p.Owner < TeamColors.Length))
            DrawArc(MapPoint(place.X,place.Z),MarkerSize/2+1.5f,0,MathF.Tau,24,TeamColors[place.Owner],2);
        // Buildings at their footprint's size, over the resource markers so a claimed well or pile shows its owner;
        // foundations are paler until complete.
        foreach (var (team,complete,outline) in Buildings())
        {
            var color = team < TeamColors.Length ? TeamColors[team] : new Color("ded2a5");
            DrawColoredPolygon(outline,complete ? color : color with { A = .5f });
            DrawPolyline([..outline,outline[0]],color.Darkened(.55f),1);
        }
        foreach (var unit in _world.Units.Where(u => u.Present))
            DrawRect(new Rect2(MapPoint(unit.X,unit.Z)-new Vector2(1.5f,1.5f),new(3,3)),unit.Team >= 0 && unit.Team < TeamColors.Length ? TeamColors[unit.Team].Lightened(.25f) : new Color("ded2a5"));
        if (View.Length > 2) DrawPolyline([..View.Select(p => MapPoint(p.X,p.Y)),MapPoint(View[0].X,View[0].Y)],Colors.White,1.5f);
        DrawRect(new(Vector2.Zero,Size),new Color("c4aa6e"),false,1);
    }

    public override void _GuiInput(InputEvent input)
    {
        // Press and drag scroll the camera; Godot keeps sending motion here until the release.
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            Dragging = mouse.Pressed;
            if (mouse.Pressed) Choose(mouse.Position);
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && Dragging) { Choose(motion.Position); AcceptEvent(); }
        // The original manual's minimap order: a right-click sends the selection, it does not move the camera.
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right,Pressed: true } order)
        {
            Ordered?.Invoke(new(Math.Clamp(order.Position.X/Size.X,0,1),Math.Clamp(order.Position.Y/Size.Y,0,1)),
                order.ShiftPressed,order.CtrlPressed);
            AcceptEvent();
        }
    }

    private void Choose(Vector2 position) => Chosen?.Invoke(new(Math.Clamp(position.X/Size.X,0,1),Math.Clamp(position.Y/Size.Y,0,1)));
}
