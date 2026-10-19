using Godot;
using Reforged.Simulation;

public partial class BattleMinimap : Control
{
    public event Action<Vector2>? Chosen;
    private SimWorld? _world;
    private ImageTexture? _terrain;
    public void Configure(SimWorld world)
    {
        _world = world; var nav = world.Navigation;
        using var image = Image.CreateEmpty(nav.Width,nav.Height,false,Image.Format.Rgba8);
        for (int z = 0; z < nav.Height; z++) for (int x = 0; x < nav.Width; x++)
        {
            int node = z*nav.Width+x; float shade = Math.Clamp(nav.NodeHeight(node)/30000f,0,.45f);
            image.SetPixel(nav.Width-1-x,z,nav.CanStand(node) ? new Color(.18f+shade,.25f+shade,.10f+shade) : new Color(.12f+shade,.14f+shade,.10f+shade));
        }
        _terrain = ImageTexture.CreateFromImage(image);
    }
    public override void _Draw()
    {
        if (_world == null || _terrain == null) return;
        DrawTextureRect(_terrain,new(Vector2.Zero,Size),false); var nav = _world.Navigation;
        foreach (var unit in _world.Units.Where(u => u.Health > 0))
        {
            var point = new Vector2((1-unit.X/(float)(nav.Width*nav.CellSize))*Size.X,unit.Z/(float)(nav.Height*nav.CellSize)*Size.Y);
            DrawRect(new Rect2(point-new Vector2(1.5f,1.5f),new(3,3)),unit.Team == 0 ? new Color("4a9ae7") : new Color("ed5446"));
        }
        DrawRect(new(Vector2.Zero,Size),new Color("c4aa6e"),false,1);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left,Pressed: true } mouse)
        { Chosen?.Invoke(new(Math.Clamp(mouse.Position.X/Size.X,0,1),Math.Clamp(mouse.Position.Y/Size.Y,0,1))); AcceptEvent(); }
    }
}
