using Godot;
using Reforged.Rendering;
using System.Text.Json;

public partial class Main
{
    // An offscreen target gives exact 4K/ultrawide captures even when Windows
    // constrains a window to the attached monitor's work area.
    private async Task CaptureOffscreenFrame(bool quit,string destination)
    {
        var viewport = new SubViewport { Size = _captureResolution,World3D = GetViewport().World3D,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var camera = new Camera3D { Transform = _camera.GlobalTransform,Fov = _camera.Fov,Near = _camera.Near,Far = _camera.Far,
            VOffset = _camera.VOffset,Current = true }; viewport.AddChild(camera);
        var canvas = new CanvasLayer(); viewport.AddChild(canvas);
        var root = new Control { Size = _captureResolution,MouseFilter = Control.MouseFilterEnum.Ignore }; canvas.AddChild(root);
        // Waypoint lines were projected for the window; rescale them to the capture.
        var stretch = (Vector2)_captureResolution/GetViewport().GetVisibleRect().Size;
        var overlay = new SandboxHud { Size = _captureResolution,MouseFilter = Control.MouseFilterEnum.Ignore,Visible = _hud.Visible }; root.AddChild(overlay);
        overlay.Waypoints.AddRange(_hud.Waypoints.Select(w => (w.From*stretch,w.To*stretch,w.Color)));
        overlay.WaypointMarks.AddRange(_hud.WaypointMarks.Select(m => m*stretch));
        root.AddChild(new BuildOverlay { Visible = _hud.Visible });
        var hud = new CommandPanel { ManageCursor = false,BuildMenuOpen = _buildMenu,PlayerTeam = PlayerTeam }; root.AddChild(hud); hud.TeamColors = _teamColors; hud.LoadArtwork(_cache,_world!);
        hud.Visible = _hud.Visible;
        var heroes = new HeroBar { Visible = _hud.Visible }; root.AddChild(heroes); heroes.LoadArtwork(hud);
        heroes.Refresh(FriendlyHeroes(),hud.Art,hud.EffectiveScale);
        hud.SetMinimapMarkers(_minimapMarkers);
        hud.SetMinimapView(ViewFootprint(camera,viewport.GetVisibleRect(),hud.Visible ? hud.GetGlobalRect().Position.Y : viewport.GetVisibleRect().End.Y));
        if (_skirmish) ApplySkirmishHud(hud);
        var selected = _world!.Units.Where(u => _selected.Contains(u.Id) && u.Health > 0).ToList();
        hud.Refresh(selected,UnitName,_orders.Pending,_paused,Enumerable.Range(0,10).Select(slot => _controlGroups.Recall(slot,LivingFriendly)).ToArray(),
            _world.Units.Count(u => u.Team == PlayerTeam && u.Health > 0),_world.Units.Count(u => u.Team != PlayerTeam && u.Health > 0),_world.IsWalking(PlayerTeam),_world.Tick,_pendingAbilitySlot);
        if (_observing)
        {
            hud.RefreshMatch(_mapName,_world.Tick,Speeds,_speed,_paused,MatchState(_world),_actualSpeed);
            var overview = new ObserverHud { Visible = _hud.Visible }; root.AddChild(overview); overview.Mirror(_observerHud);
            overview.Refresh(_world,TeamColor,hud.Art,hud.EffectiveScale);
        }
        if (_skirmish)
        {
            hud.RefreshConstruction(_world,selected,_constructionTargeting,_constructionBuilding,_constructionChoices,SelectedStructureDetails());
            hud.RefreshStructure(SelectedStructureDetails());
            hud.RefreshProduction(_world,SelectedProducer());
        }
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image = viewport.GetTexture().GetImage();
        var error = image.SavePng(destination);
        var bounds = hud.GetGlobalRect();
        System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(destination,".json"),JsonSerializer.Serialize(new {
            width = image.GetWidth(),height = image.GetHeight(),offscreen = true,hud_scale_percent = hud.ScalePercent,
            hud_effective_scale = hud.EffectiveScale,hud_bounds = new[] { bounds.Position.X,bounds.Position.Y,bounds.Size.X,bounds.Size.Y },
            preview_lighting = PreviewLighting.Metadata,terrain_shading = _presentation.TerrainShading,
            original_game_fidelity_verified = false },new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("CAPTURE "+destination+" "+error); viewport.QueueFree();
        if (quit) GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
