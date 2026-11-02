using Godot;
using Reforged.Simulation;

public partial class BattleMinimap : Control
{
    public event Action<Vector2>? Chosen;
    /// <summary>A right-click on the map: the normalized point, whether the order is queued, and whether it marches.</summary>
    public event Action<Vector2,bool,bool>? Ordered;
    private SimWorld? _world;
    private ImageTexture? _terrain;
    /// <summary>Build-150: the terrain in colour and in grey, and the two mixed by the viewer's sight.</summary>
    private byte[] _colour = [],_grey = [],_mixed = [];
    private Image? _mixedImage;
    private ImageTexture? _fogged;
    private (int Team,int Updates) _foggedFor = (int.MinValue,-1);
    private (Texture2D Icon,float X,float Z)[] _markers = [];
    /// <summary>Ground corners of the battlefield view in map millimetres.</summary>
    public Vector2[] View { get; set; } = [];
    public bool Dragging { get; private set; }
    /// <summary>The viewing player; a negative value observes both teams.</summary>
    public int ViewerTeam { get; set; }
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
        // Outside sight the original's minimap shows its terrain in grey (local-023): the same shades, desaturated and dimmed.
        _colour = image.GetData(); _grey = new byte[_colour.Length]; _mixed = new byte[_colour.Length];
        for (int i = 0; i < _colour.Length; i += 4)
        {
            byte luma = (byte)Math.Min(255,(_colour[i]*30+_colour[i+1]*59+_colour[i+2]*11)*85/10000);
            _grey[i] = _grey[i+1] = _grey[i+2] = luma; _grey[i+3] = 255;
        }
        _fogged = null; _foggedFor = (int.MinValue,-1);
    }

    private bool Fogged => _world!.FogOfWar && ViewerTeam >= 0;

    /// <summary>The terrain in colour where the viewer sees and in grey elsewhere, remade when its sight changes.</summary>
    private Texture2D FoggedTerrain()
    {
        var world = _world!; var nav = world.Navigation;
        if (_fogged != null && _foggedFor == (ViewerTeam,world.VisionUpdates)) return _fogged;
        _foggedFor = (ViewerTeam,world.VisionUpdates);
        var visible = world.Vision(ViewerTeam).Visible;
        for (int z = 0; z < nav.Height; z++)
        {
            int row = Math.Min(world.VisionHeight-1,z*nav.CellSize/SimWorld.VisionCellMm)*world.VisionWidth;
            int line = (nav.Height-1-z)*nav.Width*4;
            for (int x = 0; x < nav.Width; x++)
            {
                var source = visible[row+Math.Min(world.VisionWidth-1,x*nav.CellSize/SimWorld.VisionCellMm)] != 0 ? _colour : _grey;
                Buffer.BlockCopy(source,line+x*4,_mixed,line+x*4,4);
            }
        }
        if (_mixedImage == null) { _mixedImage = Image.CreateFromData(nav.Width,nav.Height,false,Image.Format.Rgba8,_mixed); _fogged = ImageTexture.CreateFromImage(_mixedImage); }
        else { _mixedImage.SetData(nav.Width,nav.Height,false,Image.Format.Rgba8,_mixed); _fogged!.Update(_mixedImage); }
        return _fogged!;
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

    /// <summary>Whether the minimap shows the map point in colour (in the viewer's sight) rather than grey; for smokes.</summary>
    public bool ColouredAt(int x,int z)
    {
        var nav = _world!.Navigation;
        if (!Fogged) return true;
        FoggedTerrain();
        int i = ((nav.Height-1-Math.Clamp(z/nav.CellSize,0,nav.Height-1))*nav.Width+Math.Clamp(x/nav.CellSize,0,nav.Width-1))*4;
        return _mixed[i] != _mixed[i+1] || _mixed[i+1] != _mixed[i+2];
    }

    /// <summary>Every standing building and foundation as its footprint outline on the minimap, with its team. Under fog
    /// (build-150) the viewer's own, and the enemy buildings it has found, as it last saw them.</summary>
    public IEnumerable<(int Team,bool Complete,Vector2[] Outline)> Buildings()
    {
        var world = _world!;
        var shown = !Fogged ? world.ConstructionSites.Values.Where(s => s.Team >= 0 && s.Health > 0).Select(s => (s.Team,s.Complete,s.Obstacle))
            : world.ConstructionSites.Values.Where(s => s.Team == ViewerTeam && s.Health > 0).Select(s => (s.Team,s.Complete,s.Obstacle))
                .Concat(world.Vision(ViewerTeam).Sites.Values.Select(s => (s.Team,s.Complete,s.Obstacle)));
        foreach (var (team,complete,obstacle) in shown)
            foreach (var footprint in world.Navigation.Footprints(obstacle))
                yield return (team,complete,footprint.Polygon.Select(p => MapPoint(p.X,p.Z)).ToArray());
    }

    public override void _Draw()
    {
        if (_world == null || _terrain == null) return;
        DrawTextureRect(Fogged ? FoggedTerrain() : _terrain,new(Vector2.Zero,Size),false);
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
        foreach (var unit in VisibleUnits())
            DrawRect(new Rect2(MapPoint(unit.X,unit.Z)-new Vector2(1.5f,1.5f),new(3,3)),unit.Team >= 0 && unit.Team < TeamColors.Length ? TeamColors[unit.Team].Lightened(.25f) : new Color("ded2a5"));
        if (View.Length > 2) DrawPolyline([..View.Select(p => MapPoint(p.X,p.Y)),MapPoint(View[0].X,View[0].Y)],Colors.White,1.5f);
        DrawRect(new(Vector2.Zero,Size),new Color("c4aa6e"),false,1);
    }

    public IEnumerable<SimUnit> VisibleUnits() => _world!.Units.Where(u => u.Present && _world.CanSeeUnit(ViewerTeam,u));

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
