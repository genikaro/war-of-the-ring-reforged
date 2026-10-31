using Godot;
using Reforged.Presentation;
using Reforged.Simulation;

namespace Reforged.Rendering;

public sealed class ProjectileTrail
{
    public SimProjectile Arrow { get; }
    public MeshInstance3D Root { get; }
    public bool Ended { get; private set; }
    private readonly ImmediateMesh _mesh = new();
    private readonly ProjectileTrailPlayback _history;
    private readonly float _width,_endWidth,_gray;
    private readonly Color _start,_end;

    public ProjectileTrail(SimProjectile arrow,double lifetime,float width,float endWidth,Color start,Color end,float gray,Material material,Vector3? origin = null)
    {
        Arrow = arrow; _history = new(lifetime); _width = width; _endWidth = endWidth; _start = start; _end = end; _gray = gray;
        Root = new() {Name = "ArrowTrail_"+arrow.Id,Mesh = _mesh,MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off};
        Record(arrow.ReleaseTick/(double)SimWorld.TicksPerSecond,origin ?? new(-arrow.X/1000f,arrow.Y/1000f,arrow.Z/1000f));
    }
    public void Record(double time,Vector3 position) => _history.Record(new(time,position.X,position.Y,position.Z));
    public void End(Vector3 position)
    {
        if (Ended) return;
        Record(Arrow.ImpactTick/(double)SimWorld.TicksPerSecond,position); Ended = true;
    }
    public int Draw(double time,Camera3D camera)
    {
        _mesh.ClearSurfaces(); var points = _history.Sample(time);
        if (points.Length < 2) return points.Length;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        Vector3 Position(TrailPoint p) => new((float)p.X,(float)p.Y,(float)p.Z);
        float Age(TrailPoint p) => (float)Math.Clamp((time-p.Time)/_history.Lifetime,0,1);
        void Vertex(TrailPoint p,Vector3 side,float edge)
        {
            float age = Age(p); var color = _start.Lerp(_end,age);
            color.R *= _gray; color.G *= _gray; color.B *= _gray; color.A *= 1-age;
            _mesh.SurfaceSetColor(color); _mesh.SurfaceSetUV(new(edge,age));
            float width = Mathf.Lerp(_width,_endWidth,age);
            _mesh.SurfaceAddVertex(Position(p)+side*((edge-.5f)*width));
        }
        for (int i = 1; i < points.Length; i++)
        {
            var a = points[i-1]; var b = points[i]; var direction = Position(b)-Position(a);
            var side = direction.Cross(camera.GlobalPosition-(Position(a)+Position(b))*.5f);
            side = side.LengthSquared() > .000001f ? side.Normalized() : camera.GlobalBasis.X;
            Vertex(a,side,0); Vertex(b,side,0); Vertex(b,side,1);
            Vertex(a,side,0); Vertex(b,side,1); Vertex(a,side,1);
        }
        _mesh.SurfaceEnd(); return points.Length;
    }
    public void Free() { Root.Mesh = null; _mesh.Dispose(); Root.QueueFree(); }
}
