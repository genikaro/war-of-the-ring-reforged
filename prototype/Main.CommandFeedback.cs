using Godot;
using Reforged.Simulation;
using Reforged.Rendering;

public partial class Main
{
    private Vector3 _commandDestination;
    private MeshInstance3D? _groundCommandRing;
    private float _groundCommandTime;
    private bool _groundCommandOnTerrain;
    private const float GroundCommandDuration = .6f;
    private const float GroundCommandScale = .8f, GroundCommandOpacity = .8f;
    private const float CommandBlipOnTime = .16f, CommandBlipGap = .04f, CommandBlipRestore = .08f;
    private const float CommandBlipDuration = 2*CommandBlipOnTime+CommandBlipGap+CommandBlipRestore;
    private const float SelectedUnitRingLightening = .4f, HoverUnitRingLightening = .15f;
    private static readonly Color LegacyFriendlyUnitRing = new("ffaa00"), LegacyEnemyUnitRing = new("ff0000");
    private int _hoveredUnit = -1, _hoveredStructure = -1;
    private readonly Dictionary<(string Faction,bool Hero),Texture2D?> _unitRingTextures = [];

    private MeshInstance3D CreateUnitRing(UnitDefinition unit)
    {
        var key = (Faction: SimWorld.FactionOfName(unit.Name),unit.Hero);
        if (!_unitRingTextures.TryGetValue(key,out var texture))
        {
            string faction = key.Faction == SimWorld.Evil ? "Evil" : "Good";
            string variant = unit.Hero ? "Solid" : "Faded";
            string path = CachePath($"interface/Selection_Ring_Gold_{faction}_{variant}.png");
            // Older local caches still work with the plain ring.
            if (InstallFiles.Exists(path))
            {
                using var image = InstallFiles.LoadImage(path);
                image.GenerateMipmaps();
                texture = ImageTexture.CreateFromImage(image);
            }
            _unitRingTextures.Add(key,texture);
        }
        return new MeshInstance3D { Name = "SelectionRing",Mesh = new ArrayMesh(),Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoTexture = texture,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,TextureRepeat = false,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
    }

    private void ShowGroundCommandFeedback(Vector3 point,bool ground = true)
    {
        _commandDestination = point;
        _groundCommandOnTerrain = ground;
        _groundCommandTime = GroundCommandDuration;
        if (_groundCommandRing == null)
        {
            _groundCommandRing = new MeshInstance3D { Name = "GroundCommandRing",Mesh = ScenePresentation.SelectionRing(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(new Color("e9c22d"),GroundCommandOpacity),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha } };
            AddChild(_groundCommandRing);
        }
        _groundCommandRing.Position = point;
        PresentGroundCommandFeedback(0);
    }

    private void PresentGroundCommandFeedback(float dt)
    {
        _groundCommandTime = Math.Max(0,_groundCommandTime-dt);
        if (_groundCommandRing == null) return;
        _groundCommandRing.Visible = _groundCommandTime > 0;
        if (!_groundCommandRing.Visible) return;
        float t = 1-_groundCommandTime/GroundCommandDuration;
        float pulse = Mathf.Sin(t*Mathf.Tau);
        float radius = (.9f+.225f*pulse*pulse*(1-.3f*t))*GroundCommandScale;
        ((StandardMaterial3D)_groundCommandRing.MaterialOverride).AlbedoColor = new Color(new Color("e9c22d"),GroundCommandOpacity*(1-Mathf.SmoothStep(.75f,1,t)));
        // Project the destination marker onto the clicked ground or keep it on its elevated surface.
        ScenePresentation.SelectionRing(local => {
            var world = _commandDestination+local*(radius/.65f);
            world.Y = (_groundCommandOnTerrain ? _world!.Navigation.GroundHeight((int)(-world.X*1000),(int)(world.Z*1000))/1000f
                : _commandDestination.Y)+.06f;
            return world-_commandDestination;
        },(ArrayMesh)_groundCommandRing.Mesh!);
    }

    private void ShowUnitCommandFeedback(SimUnit unit)
    {
        var view = _views[unit.Id];
        view.CommandBlip = CommandBlipDuration;
    }

    private void ShowStructureCommandFeedback(int id)
    {
        if (_structureViews.TryGetValue(id,out var view) && StructureVisible(view)) view.CommandBlip = CommandBlipDuration;
    }

    private void UpdatePointerHover()
    {
        _hoveredUnit = _hoveredStructure = -1;
        if (!_pointerInWindow || !_windowFocused || _dragging || _panning
            || !GetViewport().GetVisibleRect().HasPoint(_pointerPosition) || GetViewport().GuiGetHoveredControl() != null) return;
        var structure = StructureAt(_pointerPosition);
        _hoveredUnit = UnitAt(_pointerPosition,structure?.Distance)?.Id ?? -1;
        if (_hoveredUnit < 0) _hoveredStructure = structure?.Id ?? -1;
    }

    private static Color CommandRingColor(float remaining,Color color,Color normal,bool normallyVisible)
    {
        if (remaining <= 0) return normal;
        var baseline = normallyVisible ? normal : new Color(color,0);
        float elapsed = CommandBlipDuration-remaining, half = CommandBlipOnTime/2;
        // Ease from the current hover/selection into the first flash, then fade each flash to transparent.
        if (elapsed < half) return baseline.Lerp(color,Mathf.SmoothStep(0,half,elapsed));
        if (elapsed < CommandBlipOnTime) return new Color(color,1-Mathf.SmoothStep(half,CommandBlipOnTime,elapsed));
        float secondStart = CommandBlipOnTime+CommandBlipGap;
        if (elapsed < secondStart) return new Color(color,0);
        float second = elapsed-secondStart;
        if (second < half) return new Color(color,Mathf.SmoothStep(0,half,second));
        if (second < CommandBlipOnTime) return new Color(color,1-Mathf.SmoothStep(half,CommandBlipOnTime,second));
        return new Color(normal,baseline.A*Mathf.SmoothStep(0,CommandBlipRestore,second-CommandBlipOnTime));
    }

    private static Color LegacyUnitRingColor(int owner,int viewpoint)
        => viewpoint >= 0 && owner >= 0 && owner != viewpoint ? LegacyEnemyUnitRing : LegacyFriendlyUnitRing;

    private void PresentCommandRing(SimUnit unit,UnitView view,float dt)
    {
        view.CommandBlip = Math.Max(0,view.CommandBlip-dt);
        bool flashing = view.CommandBlip > 0;
        bool selected = _selected.Contains(unit.Id), hovered = _hoveredUnit == unit.Id;
        bool hoverOnly = hovered && !selected, playerColors = _settings.PlayerColoredUnitSelectors;
        var tint = playerColors ? TeamColor(unit.Team) : LegacyUnitRingColor(unit.Team,_observing ? _visionTeam : PlayerTeam);
        var normal = playerColors ? tint.Lightened(hoverOnly ? HoverUnitRingLightening : SelectedUnitRingLightening) : tint;
        var color = CommandRingColor(view.CommandBlip,tint,new Color(normal,hoverOnly ? .5f : 1),selected || hovered);
        bool ring = unit.Present && (flashing ? color.A > .001f : selected || hovered);
        if (ring != view.RingShown) { view.Ring.Visible = ring; view.RingShown = ring; }
        if (!ring || !view.Drawn) return;
        var material = (StandardMaterial3D)view.Ring.MaterialOverride;
        if (view.RingColor != color)
        {
            material.AlbedoColor = color;
            view.RingColor = color;
        }
        // The texture stays aligned to the ground as the unit turns.
        if (view.RingYaw != view.Yaw)
        {
            view.Ring.Rotation = new(0,-view.Yaw,0);
            view.RingYaw = view.Yaw;
        }
        var position = view.Root.GlobalPosition;
        var navigation = _world!.Navigation;
        bool onGround = unit.Node < navigation.GroundCount;
        if (view.RingPosition == position && view.RingOnGround == onGround) return;
        view.RingPosition = position;
        view.RingOnGround = onGround;
        // Leave room for the textured band around the body. Some large units have a small source selection radius.
        float radiusMm = Math.Max(unit.Definition.SelectionRadiusMm,unit.Definition.BodyRadiusMm*1.25f);
        float scale = radiusMm / (1000f * ScenePresentation.SelectionRingRadius);
        Vector3 Project(Vector3 local)
        {
            local *= scale;
            var point = position + local;
            if (onGround) local.Y = navigation.GroundHeight((int)(-point.X * 1000),(int)(point.Z * 1000)) / 1000f - position.Y;
            local.Y += .06f;
            return local;
        }
        var mesh = (ArrayMesh)view.Ring.Mesh!;
        if (material.AlbedoTexture != null) ScenePresentation.TexturedSelectionRing(Project,mesh);
        else ScenePresentation.SelectionRing(Project,mesh);
    }
}
