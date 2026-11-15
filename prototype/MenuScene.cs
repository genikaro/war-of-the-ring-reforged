using Godot;
using Reforged.Rendering;
using System.Text.Json;

/// <summary>The main menu's background (build-147): the original's own menu world, Levels/MenuScene.wor
/// ("ModifiedMenu2"), Barad-dur with the Eye on its tower behind an orc army of goblin spearmen, orc slashers,
/// Uruk-Hai and bonecleaver trolls among pounding flag spears, under the Mordor sky (SKYBOX_MORDOR_01) and its
/// volcanoes. The map export keeps the models' rest poses, so each soldier is swapped for the content pack's animated
/// model of the same name, idling from a random point of its clip in the Evil red. The camera stands by the scene's
/// single camera waypoint (CamWayPt 6, at the army's tail; the waypoint's own angles are not decoded), a few metres
/// further back and higher so the nearest spears clear the lens, and looks north over the army to the tower, swaying
/// slowly; the tower stands in the left part of the window, the right is the menu's. The 128 m map ends in a bare edge
/// that this low view keeps behind the volcanoes. Fire glows from the tower's fire pits and torches, the Eye and the
/// volcanoes. Light, haze and the camera's sway are the prototype's own.</summary>
public partial class MenuScene : Node3D
{
    private static readonly Color EvilRed = new("b90000");
    private Camera3D _camera = null!;
    private Vector3 _eye,_target;
    private double _time;
    private readonly List<(OmniLight3D Light,float Energy,float Phase)> _fires = [];
    public int AnimatedUnits { get; private set; }
    public int Fires => _fires.Count;
    public Camera3D Camera => _camera;

    public async Task Load(Func<string?,float?,Task> step)
    {
        var art = FrontEndArt.Shared;
        var presentation = new ScenePresentation();
        await step("Loading the menu scene",.05f);
        // Build-212: the install's menu scene, built off the main thread, when it gives it.
        byte[]? built = await Task.Run(() => InstallFiles.Glb(art.MenuScene!));
        var scene = built != null ? presentation.Load(built,"map/scene.glb") : presentation.Load(art.MenuScene!,"map/scene.glb"); AddChild(scene);
        await step("Placing the army",.4f);
        await AnimateArmy(scene,presentation,step);
        await step("Lighting the fires",.9f);
        AddChild(Environment(art.MenuSky));
        var sun = new DirectionalLight3D { LightColor = new Color("ffc29a"),LightEnergy = .95f,ShadowEnabled = true,
            RotationDegrees = new Vector3(-38,-128,0),DirectionalShadowMaxDistance = 160 };
        AddChild(sun);
        // The fire joints are bones of the skinned models: each glow rides a bone attachment.
        foreach (var skeleton in ScenePresentation.Descendants(scene).OfType<Skeleton3D>().ToArray())
            for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
            {
                string name = skeleton.GetBoneName(bone);
                (Color Color,float Energy,float Range)? fire = name.StartsWith("J_effect_Eye_Of_Sauron") ? (new Color("ff5a1e"),5,26)
                    : name.StartsWith("J_Baradur_Fire_Pit") ? (new Color("ff7a2a"),4,22)
                    : name.StartsWith("J_Fire_Torch") ? (new Color("ff9a40"),1.6f,7)
                    : name.StartsWith("J_Fire_Billow") || name.StartsWith("J_Attach_Model_Lava") ? (new Color("ff6020"),2.5f,16) : null;
                if (fire is not { } glow) continue;
                var attachment = new BoneAttachment3D { BoneName = name }; skeleton.AddChild(attachment);
                var light = new OmniLight3D { LightColor = glow.Color,LightEnergy = glow.Energy,OmniRange = glow.Range,ShadowEnabled = false };
                attachment.AddChild(light); _fires.Add((light,glow.Energy,(float)GD.RandRange(0,Mathf.Tau)));
            }
        // CamWayPt 6 is source (50.06, 1.80) at 10.4 m, Godot (-50.06, 10.43, 1.8); the eye stands 8 m behind it, 3.5 m up.
        _eye = new Vector3(-50f,14f,-6f);
        _target = new Vector3(-47.6f,12f,104f);
        _camera = new Camera3D { Fov = 46,Near = .2f,Far = 900,Current = true }; AddChild(_camera);
        PlaceCamera();
    }

    /// <summary>Each soldier's rest-pose model becomes the pack's animated one, idling from a random point.</summary>
    private async Task AnimateArmy(Node3D scene,ScenePresentation presentation,Func<string?,float?,Task> step)
    {
        string pack = GamePaths.Installed("content/good");
        var models = new Dictionary<string,(string File,string Idle)>();
        try
        {
            // Build-207: the install's models list (build-217: no pack's is read).
            using var content = JsonDocument.Parse(InstallFiles.Models()?.ToJsonString() ?? throw new InvalidOperationException("the install gives no models list"));
            var list = content.RootElement;
            foreach (var model in list.EnumerateArray())
            {
                string file = model.GetProperty("file").GetString()!;
                models.TryAdd(System.IO.Path.GetFileNameWithoutExtension(file),(file,model.GetProperty("idle").GetString()!));
            }
        }
        catch (Exception error) when (error is JsonException or IOException or KeyNotFoundException or InvalidOperationException) { GD.PushWarning("Menu army stays still: "+error.Message); return; }
        var templates = new Dictionary<string,Node3D?>();
        int fromInstall = 0;
        var random = new Random(147);
        foreach (var placement in scene.GetChildren().OfType<Node3D>().ToArray())
        {
            // Godot numbers repeated node names (MODEL_E_GOBLIN_SPEARMAN2, 3, ...): the model is the name without it.
            if (placement.GetChildCount() == 0 || placement.GetChild(0) is not Node3D still) continue;
            string name = still.Name;
            if (!models.TryGetValue(name,out var model) && !models.TryGetValue(name.TrimEnd("0123456789".ToCharArray()),out model)) continue;
            if (!templates.TryGetValue(model.File,out var template))
            {
                string path = System.IO.Path.Combine(pack,model.File);
                // Build-207: the install's model, built off the main thread.
                byte[]? glb = await Task.Run(() => InstallFiles.Glb(path));
                if (glb != null) fromInstall++;
                template = glb != null ? presentation.Load(glb,model.File,EvilRed) : null;
                templates[model.File] = template;
                await step(null,null);
            }
            if (template == null) continue;
            var visual = (Node3D)template.Duplicate();
            placement.AddChild(visual); still.Visible = false;
            if (ScenePresentation.Descendants(visual).OfType<AnimationPlayer>().FirstOrDefault() is { } player
                && player.GetAnimationList().FirstOrDefault(a => a == model.Idle || a.EndsWith('/'+model.Idle)) is { } clip)
            {
                player.Deterministic = true;
                player.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
                player.Play(clip); player.Seek(random.NextDouble()*player.CurrentAnimationLength,true);
                player.SpeedScale = (float)(.9+random.NextDouble()*.2);
            }
            AnimatedUnits++;
        }
        foreach (var template in templates.Values) template?.QueueFree();
        GD.Print($"Menu army: {AnimatedUnits} soldiers from {templates.Count} models, {fromInstall} built from the install");
    }

    private static WorldEnvironment Environment(string? sky)
    {
        var environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,BackgroundColor = new Color("2a160c"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,AmbientLightColor = new Color("8c6a5c"),AmbientLightEnergy = .5f,
            FogEnabled = true,FogLightColor = new Color("6e3a1e"),FogDensity = .0045f,FogSkyAffect = .35f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,TonemapExposure = 1.05f };
        if (sky != null && InstallFiles.Exists(sky))
        {
            using var image = InstallFiles.LoadImage(sky);
            environment.BackgroundMode = Godot.Environment.BGMode.Sky;
            environment.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = ImageTexture.CreateFromImage(image) } };
            environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        }
        return new WorldEnvironment { Environment = environment };
    }

    public override void _Process(double delta)
    {
        if (_camera == null) return;
        _time += delta;
        PlaceCamera();
        foreach (var (light,energy,phase) in _fires)
            light.LightEnergy = energy*(.82f+.12f*Mathf.Sin((float)_time*7.3f+phase)+.06f*Mathf.Sin((float)_time*17.9f+phase*2));
    }

    public void Frame(Vector3 eye,Vector3 target,float fov) { _eye = eye; _target = target; _camera.Fov = fov; PlaceCamera(); }

    /// <summary>A slow sway around the view, so the scene lives behind the menu; the camera turns right just enough
    /// for the tower to stand left of centre, clear of the menu column at the window's right.</summary>
    private void PlaceCamera()
    {
        float sway = Mathf.Sin((float)_time*.07f);
        var eye = _eye+new Vector3(sway*3f,Mathf.Sin((float)_time*.05f)*.6f,0);
        _camera.LookAtFromPosition(eye,_target+new Vector3(sway*-4f,0,0),Vector3.Up);
        var viewport = GetViewport()?.GetVisibleRect().Size ?? new Vector2(16,9);
        float halfWidth = Mathf.Tan(Mathf.DegToRad(_camera.Fov)/2)*viewport.X/Math.Max(1,viewport.Y);
        _camera.RotateY(-Mathf.Atan((.5f-TowerAcross)*2*halfWidth));
    }

    /// <summary>Where the tower stands across the window, from its left edge.</summary>
    private const float TowerAcross = .36f;
}
