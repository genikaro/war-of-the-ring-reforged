using Godot;
using Reforged.Presentation;
using System.Buffers.Binary;
using System.Text.Json;

namespace Reforged.Rendering;

/// <summary>Visibility-only subset of extras.wotrAppearance. Never hides bones or moves units. Build-158: the data is
/// compiled once per model (Presentation/AppearanceTable.cs) and a call whose answer cannot have changed (same clip,
/// state and weapon blood, clip time still short of the next key) returns at once; shown pieces are remembered here,
/// so Godot is only told about a piece that changes.</summary>
public sealed class UnitAppearance
{
    private readonly AppearanceTable _table;
    private readonly Dictionary<int,MeshInstance3D> _meshes = [];
    /// <summary>Distinct meshes the table writes, which of them each write goes to, and what each shows now.</summary>
    private readonly MeshInstance3D[] _targets;
    private readonly int[] _targetOf;
    private readonly bool[] _shown,_visible;
    private bool _evaluated,_evaluatedBlood;
    private string _evaluatedClip = "";
    private string? _evaluatedState;
    private double _validFrom,_validUntil;
    public int BoundMeshes => _meshes.Count;
    public int[] VisibleNodes => _meshes.Where(p => p.Value.Visible).Select(p => p.Key).Order().ToArray();
    public int Changes { get; private set; }
    /// <summary>ModelFlags ids 0 and 1, MODEL_FLAG_WEAPON_BLOOD_RIGHT and _LEFT: the blood-smeared blade overlays.</summary>
    public const long WeaponBloodMask = AppearanceTable.WeaponBloodMask;
    /// <summary>Build-129: show the weapon blood overlays once the unit's weapon has drawn blood.</summary>
    public bool WeaponBlood { get; set; }
    public bool HasWeaponBlood => _table.HasWeaponBlood;
    public bool WeaponBloodVisible { get; private set; }

    public static JsonElement Read(string path) => ReadGlb(System.IO.File.ReadAllBytes(path));

    /// <summary>Build-198: the appearance of a GLB held in memory.</summary>
    public static JsonElement ReadGlb(byte[] bytes)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12,4));
        using var document = JsonDocument.Parse(bytes.AsMemory(20,length));
        return document.RootElement.GetProperty("extras").GetProperty("wotrAppearance").Clone();
    }

    public UnitAppearance(Node3D root,JsonElement data) : this(root,new AppearanceTable(data)) { }

    public UnitAppearance(Node3D root,AppearanceTable table)
    {
        _table = table;
        foreach (var mesh in ScenePresentation.Descendants(root).OfType<MeshInstance3D>())
        {
            string name = mesh.Name.ToString();
            if (name.StartsWith("WotrMesh_") && int.TryParse(name[9..],out int id)) _meshes.Add(id,mesh);
        }
        var distinct = new List<int>(); _targetOf = new int[table.WriteNodes.Length];
        for (int i = 0; i < table.WriteNodes.Length; i++)
        {
            int node = table.WriteNodes[i];
            if (!_meshes.ContainsKey(node)) throw new InvalidOperationException("Imported appearance mesh missing: "+node);
            int index = distinct.IndexOf(node);
            if (index < 0) { index = distinct.Count; distinct.Add(node); }
            _targetOf[i] = index;
        }
        _targets = distinct.Select(n => _meshes[n]).ToArray();
        _shown = _targets.Select(m => m.Visible).ToArray();
        _visible = new bool[table.WriteNodes.Length];
    }

    public void Apply(string clip,double time,string? state = null)
    {
        if (_evaluated && _table.DistinctWrites && WeaponBlood == _evaluatedBlood && time >= _validFrom && time < _validUntil
            && state == _evaluatedState && clip == _evaluatedClip) return;
        var (weaponBlood,from,until) = _table.Evaluate(clip,time,state,WeaponBlood,_visible);
        for (int i = 0; i < _visible.Length; i++)
        {
            int target = _targetOf[i]; bool visible = _visible[i];
            if (_shown[target] != visible) { _targets[target].Visible = visible; _shown[target] = visible; Changes++; }
        }
        WeaponBloodVisible = weaponBlood;
        _evaluated = true; _evaluatedBlood = WeaponBlood; _evaluatedClip = clip; _evaluatedState = state;
        _validFrom = from; _validUntil = until;
    }
}
