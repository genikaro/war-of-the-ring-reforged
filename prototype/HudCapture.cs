using Godot;
using System.Text.Json;

public partial class Main
{
    // An offscreen target gives exact 4K/ultrawide captures even when Windows
    // constrains a window to the attached monitor's work area.
    private async Task CaptureOffscreenFrame(bool quit)
    {
        var viewport = new SubViewport { Size = _captureResolution,World3D = GetViewport().World3D,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var camera = new Camera3D { Transform = _camera.GlobalTransform,Fov = _camera.Fov,Near = _camera.Near,Far = _camera.Far,
            VOffset = _camera.VOffset,Current = true }; viewport.AddChild(camera);
        var canvas = new CanvasLayer(); viewport.AddChild(canvas);
        var root = new Control { Size = _captureResolution,MouseFilter = Control.MouseFilterEnum.Ignore }; canvas.AddChild(root);
        root.AddChild(new BuildOverlay { Visible = _hud.Visible });
        var hud = new CommandPanel { ManageCursor = false }; root.AddChild(hud); hud.LoadArtwork(_cache,_world!);
        hud.Visible = _hud.Visible;
        if (_skirmish) ApplySkirmishHud(hud);
        var selected = _world!.Units.Where(u => _selected.Contains(u.Id) && u.Health > 0).ToList();
        hud.Refresh(selected,UnitName,_orders.Pending,_paused,Enumerable.Range(0,10).Select(slot => _controlGroups.Recall(slot,LivingFriendly)).ToArray(),
            _world.Units.Count(u => u.Team == 0 && u.Health > 0),_world.Units.Count(u => u.Team != 0 && u.Health > 0),_world.IsWalking(0),_world.Tick,_pendingAbilitySlot);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using var image = viewport.GetTexture().GetImage();
        var error = image.SavePng(_capture);
        var bounds = hud.GetGlobalRect();
        System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(_capture,".json"),JsonSerializer.Serialize(new {
            width = image.GetWidth(),height = image.GetHeight(),offscreen = true,hud_scale_percent = hud.ScalePercent,
            hud_effective_scale = hud.EffectiveScale,hud_bounds = new[] { bounds.Position.X,bounds.Position.Y,bounds.Size.X,bounds.Size.Y },
            original_game_fidelity_verified = false },new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("CAPTURE "+_capture+" "+error); viewport.QueueFree();
        if (quit) GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
