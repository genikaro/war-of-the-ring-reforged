using Godot;
using Reforged.Simulation;

public partial class Main
{
    private static float UnitBarHeight(SimUnit unit) => .25f+unit.Definition.BodyHeightMm/1000f
        *(unit.Definition.Passive ? Math.Max(1,unit.Definition.ModelScalePpm/1_000_000f) : 1);

    private Vector3 BuildingBarPosition(StructureVisual structure,BuildingVisual view)
    {
        if (view.BarVertices.Length == 0)
        {
            var points = new HashSet<Vector3>();
            var inverse = structure.Root.GlobalTransform.AffineInverse();
            foreach (var (mesh,faces) in structure.Meshes)
            {
                string name = mesh.Mesh?.ResourceName ?? "";
                // Imported node names are numeric; the mesh resource keeps the source part name.
                // Flags, banners and ground shadows should not raise or shift a building's roof anchor.
                if (!mesh.IsVisibleInTree() || name.Contains("shadow",StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("CLOTH_",StringComparison.OrdinalIgnoreCase)) continue;
                var transform = inverse*mesh.GlobalTransform;
                foreach (var vertex in faces) points.Add(transform*vertex);
            }
            view.BarVertices = points.ToArray(); view.BarAnchorValid = false;
        }
        var camera = _camera.GetCameraTransform(); var projection = _camera.GetCameraProjection();
        var viewport = GetViewport().GetVisibleRect().Size;
        if (view.BarAnchorValid && view.BarCameraTransform == camera && view.BarCameraProjection == projection && view.BarViewport == viewport)
            return view.BarPosition;

        // Project the actual visible roof geometry, rather than lifting a bar over the model's off-center origin.
        var toCamera = camera.AffineInverse()*structure.Root.GlobalTransform;
        float left = float.PositiveInfinity,right = float.NegativeInfinity,top = float.PositiveInfinity;
        foreach (var vertex in view.BarVertices)
        {
            var point = toCamera*vertex;
            var clip = projection*new Vector4(point.X,point.Y,point.Z,1);
            if (clip.W <= 0) continue;
            float x = (clip.X/clip.W*.5f+.5f)*viewport.X,y = (.5f-clip.Y/clip.W*.5f)*viewport.Y;
            left = Math.Min(left,x); right = Math.Max(right,x); top = Math.Min(top,y);
        }
        var root = structure.Root.GlobalPosition;
        if (!float.IsFinite(top)) return new(0,.5f,0);
        float depth = Math.Max(_camera.Near,-camera.Basis.Z.Dot(root-camera.Origin));
        float halfHeight = _camera.UnprojectPosition(root+camera.Basis.Y*view.Bar.Size.Y/2).DistanceTo(_camera.UnprojectPosition(root));
        float gap = 4*Math.Clamp(viewport.Y/800f,.75f,3);
        var anchor = _camera.ProjectPosition(new((left+right)/2,top-gap-halfHeight),depth);
        view.BarPosition = structure.Root.ToLocal(anchor);
        view.BarCameraTransform = camera; view.BarCameraProjection = projection; view.BarViewport = viewport; view.BarAnchorValid = true;
        return view.BarPosition;
    }
}
