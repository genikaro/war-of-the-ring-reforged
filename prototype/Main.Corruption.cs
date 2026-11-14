using Godot;
using Reforged.Rendering;
using Reforged.Simulation;

/// <summary>Corrupted ground on screen (build-133, terrain skin build-136): the source's dark ash tiles over the map's own terrain
/// under every finished War Post and every Hovel the map started an Evil team with, taken away when the post falls.</summary>
public partial class Main
{
    private readonly Dictionary<(int X,int Z,int Radius),MeshInstance3D> _corruptionPatches = [];
    private MeshInstance3D? _terrainMesh;
    private CorruptionGround? _corruptionGround;

    private CorruptionGround? CorruptionSkin()
    {
        if (_corruptionGround != null || _terrainMesh == null) return _corruptionGround;
        var tiles = new List<Texture2D>();
        for (int n = 1; n <= 3; n++)
            try
            {
                var image = InstallFiles.LoadImage(CachePath($"terrain/corruption_tile_0{n}.png"));
                if (image == null) continue;
                image.GenerateMipmaps(); tiles.Add(ImageTexture.CreateFromImage(image));
            }
            catch (Exception) { break; }
        return _corruptionGround = new CorruptionGround(_terrainMesh,tiles.Count > 0 ? [.. tiles] : [CorruptionGround.FallbackTile()]);
    }

    private void PresentCorruption()
    {
        var world = _world;
        if (world?.Corruption == null) return;
        var wanted = world.TeamFactions.Where(p => p.Value == SimWorld.Evil).SelectMany(p => world.CorruptedAreas(p.Key)).ToHashSet();
        foreach (var key in _corruptionPatches.Keys.Where(k => !wanted.Contains(k)).ToArray())
        { _corruptionPatches.Remove(key,out var gone); gone!.QueueFree(); }
        foreach (var key in wanted.Where(k => !_corruptionPatches.ContainsKey(k)))
        {
            if (CorruptionSkin() is not { } skin) return;
            var patch = skin.Create(PositionOf(key.X,key.Z),key.Radius/1000f);
            AddChild(patch); _corruptionPatches[key] = patch;
        }
    }
}
