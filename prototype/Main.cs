using Godot;
using Reforged.Simulation;
using System.Text.Json;
using System.Buffers.Binary;
using Reforged.Input;

public partial class Main : Node3D
{
    private SimWorld? _world;
    private NavigationPicker _picker = null!;
    private Camera3D _camera = null!;
    private Label _status = null!;
    private Label _selection = null!;
    private SandboxHud _hud = null!;
    private readonly HashSet<int> _selected = [];
    private readonly Dictionary<int, UnitView> _views = [];
    private readonly Dictionary<int, Node3D> _obstacleViews = [];
    private JsonElement _obstacleLayers;
    private int _pendingObstacle = -1;
    private bool _pendingObstacleActive;
    private Vector3 _focus;
    private float _distance = 48, _yaw = 0.25f;
    private Vector2 _dragStart;
    private bool _dragging, _panning, _paused;
    private string _cache = "", _capture = "";
    private bool _smoke;
    private bool _surfaceSmoke;
    private bool _surfacePicked;
    private Vector2 _surfaceSmokePoint;
    private bool _obstacleSmoke, _obstacleRemoved;
    private int _obstacleSmokeId = -1;
    private Vector2 _obstacleSmokePoint;
    private int _frames, _moveDestination = -1;
    private string _initialDigest = "";
    private int _movingSeen, _attackingSeen;
    private bool _inputSelectionPassed, _inputMovePassed, _inputAttackPassed;
    private bool _inputCameraPassed, _inputPausePassed;
    private int _pauseTick;
    private Vector3 _cameraBeforeInput;
    private sealed class UnitView(Node3D root, MeshInstance3D ring, AnimationPlayer? player, JsonElement model)
    {
        public Node3D Root = root;
        public MeshInstance3D Ring = ring;
        public AnimationPlayer? Player = player;
        public JsonElement Model = model;
        public string Clip = "";
        public bool Died;
    }

    public override void _Ready()
    {
        CreateUi();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--cache=")) _cache = arg[8..];
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
            if (arg == "--smoke") _smoke = true;
            if (arg == "--surface-smoke") _surfaceSmoke = true;
            if (arg == "--obstacle-smoke") _obstacleSmoke = true;
        }
        if (_cache == "") _cache = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../converted/prototype"));
        try { LoadSandbox(); }
        catch (Exception error)
        {
            _status.Text = "Asset cache unavailable. Run tools/prepare_prototype.py, then restart.";
            GD.PushError(error.ToString());
            if (_smoke || _surfaceSmoke || _obstacleSmoke) GetTree().Quit(1);
        }
    }

    private JsonElement ReadJson(string relative)
        => JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(_cache, relative))).RootElement.Clone();

    private Node3D LoadGlb(string relative)
    {
        var document = new GltfDocument(); var state = new GltfState();
        var error = document.AppendFromFile(System.IO.Path.Combine(_cache, relative), state);
        if (error != Error.Ok) throw new InvalidOperationException($"Cannot load {relative}: {error}");
        return document.GenerateScene(state) as Node3D ?? throw new InvalidOperationException("glTF scene has no 3D root");
    }

    private void LoadSandbox()
    {
        var manifest = ReadJson("manifest.json");
        if (manifest.GetProperty("format_version").GetInt32() != 1) throw new InvalidOperationException("Unsupported cache format");
        foreach (var file in manifest.GetProperty("files").EnumerateObject())
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_cache, file.Name));
            if (!path.StartsWith(System.IO.Path.GetFullPath(_cache) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cache path escapes its folder");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
            if (!hash.Equals(file.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Stale or damaged cache: " + file.Name);
        }
        var nav = ReadJson("navigation.json");
        byte[] bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(_cache, "heights.bin"));
        int[] heights = new int[bytes.Length / 4];
        for (int i = 0; i < heights.Length; i++) heights[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4));
        bool dynamic = nav.TryGetProperty("runtime_obstacles", out _obstacleLayers);
        var navigation = new GridNavigation(nav.GetProperty("width").GetInt32(), nav.GetProperty("height").GetInt32(),
            nav.GetProperty("cell_size_mm").GetInt32(), Convert.FromBase64String(nav.GetProperty(dynamic ? "painted_allowed_base64" : "allowed_base64").GetString()!).Select(b => b != 0).ToArray(), heights,
            nav.TryGetProperty("surface_nodes", out var surfaces) ? surfaces.EnumerateArray().Select(s => new SurfaceCell(
                s.GetProperty("cell").GetInt32(), s.GetProperty("height_mm").GetInt32(),
                s.GetProperty("links").EnumerateArray().Select(n => n.GetInt32()).ToArray())).ToArray() : null,
            dynamic ? _obstacleLayers.EnumerateArray().Select(o => new NavigationObstacle(o.GetProperty("id").GetInt32(),
                o.GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
                o.GetProperty("edges").EnumerateArray().Select(e => new NavigationEdge(e[0].GetInt32(), e[1].GetInt32())).ToArray())).ToArray() : null);
        _world = new SimWorld(navigation);
        _picker = NavigationPicker.FromCache(navigation, nav);
        var map = LoadGlb(manifest.GetProperty("map").GetString()!); AddChild(map);
        if (dynamic)
            foreach (var obstacle in _obstacleLayers.EnumerateArray())
            {
                var view = Descendants(map).OfType<Node3D>().FirstOrDefault(n => n.Name.ToString() == obstacle.GetProperty("name").GetString());
                if (view == null) throw new InvalidOperationException("Obstacle visual missing: " + obstacle.GetProperty("name").GetString());
                _obstacleViews.Add(obstacle.GetProperty("id").GetInt32(), view);
            }
        var environment = new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("18222b"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("a5b8c9"), AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic } };
        AddChild(environment);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.3f, ShadowEnabled = true });
        _camera = new Camera3D { Current = true, Fov = 48, Near = 0.1f, Far = 1200 };
        AddChild(_camera);
        var (cx, cz) = navigation.Center(manifest.GetProperty("camera_cell").GetInt32());
        _focus = PositionOf(cx, cz);
        var surfaceSpawns = new HashSet<int>();
        int surfaceStart = _surfaceSmoke ? Enumerable.Range(navigation.GroundCount, navigation.NodeCount-navigation.GroundCount)
            .Where(navigation.CanStand).OrderByDescending(n => {
                var (x,z) = navigation.Center(n); return navigation.NodeHeight(n)-navigation.GroundHeight(x,z);
            }).First() : -1;
        foreach (var spawn in manifest.GetProperty("spawns").EnumerateArray())
        {
            int team = spawn.GetProperty("team").GetInt32(), cell = spawn.GetProperty("cell").GetInt32();
            if (_surfaceSmoke && team == 0) { cell = navigation.Nearest(surfaceStart, surfaceSpawns); surfaceSpawns.Add(cell); }
            var unit = _world.Spawn(team, cell);
            var model = manifest.GetProperty("models")[spawn.GetProperty("model").GetInt32()];
            var root = new Node3D { Name = "Unit_" + unit.Id };
            AddChild(root); root.Position = UnitPosition(unit);
            var visual = LoadGlb(model.GetProperty("file").GetString()!); root.AddChild(visual);
            var ring = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.65f, BottomRadius = 0.65f, Height = 0.025f },
                Position = new Vector3(0, 0.07f, 0), MaterialOverride = new StandardMaterial3D {
                    AlbedoColor = unit.Team == 0 ? new Color("43ceab") : new Color("e37466"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
            root.AddChild(ring);
            _views[unit.Id] = new UnitView(root, ring, Descendants(visual).OfType<AnimationPlayer>().FirstOrDefault(), model);
        }
        _status.Text = "Helm's Deep  /  20 Hz sandbox";
        _selection.Text = "Select your green units. Right-click terrain to move or red units to attack.";
        if (_surfaceSmoke) _focus = UnitPosition(_world.Units[0]);
        UpdateCamera();
        _initialDigest = _world.StateDigest();
        if (_smoke)
        {
            var first = _world.Units[0];
            _moveDestination = navigation.Nearest(navigation.Cell(first.X + 6000, first.Z + 6000));
        }
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren()) foreach (var node in Descendants(child)) yield return node;
    }

    private Vector3 PositionOf(int x, int z) => new(-x / 1000f, (_world?.Navigation.GroundHeight(x, z) ?? 0) / 1000f, z / 1000f);
    private static Vector3 UnitPosition(SimUnit unit) => new(-unit.X / 1000f, unit.Y / 1000f, unit.Z / 1000f);

    private void CreateUi()
    {
        var canvas = new CanvasLayer(); AddChild(canvas);
        _hud = new SandboxHud(); canvas.AddChild(_hud);
        _hud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        var top = new PanelContainer { Position = new Vector2(16, 16), CustomMinimumSize = new Vector2(660, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.035f, 0.055f, 0.07f, 0.94f), ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 12, ContentMarginBottom = 12, CornerRadiusTopLeft = 8, CornerRadiusBottomRight = 8 };
        top.AddThemeStyleboxOverride("panel", style); _hud.AddChild(top);
        var lines = new VBoxContainer(); top.AddChild(lines);
        var title = new Label { Text = "WAR OF THE RING  /  REFORGED", Modulate = new Color("d8b574") };
        title.AddThemeFontSizeOverride("font_size", 20); lines.AddChild(title);
        _status = new Label { Text = "Loading local assets…" }; lines.AddChild(_status);
        lines.AddChild(new Label { Text = "WASD pan · Q/E rotate · Wheel zoom · Middle-drag pan · Space pause · Esc clear" });
        lines.AddChild(new Label { Text = "Ctrl + right-click ground below bridges · Alt + right-click remove/restore obstacle" });
        _selection = new Label { Text = "" }; lines.AddChild(_selection);
        var reset = new Button { Text = "Restart sandbox", Position = new Vector2(18, 182) };
        _hud.AddChild(reset); reset.Pressed += () => GetTree().ReloadCurrentScene();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_world is null) return;
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed) _distance = Math.Max(12, _distance * 0.88f);
            if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed) _distance = Math.Min(450, _distance * 1.12f);
            if (mouse.ButtonIndex == MouseButton.Middle) _panning = mouse.Pressed;
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed) { _dragStart = mouse.Position; _dragging = true; }
                else if (_dragging) { SelectUnits(_dragStart, mouse.Position, mouse.ShiftPressed); _dragging = false; _hud.SelectionBox = null; }
            }
            if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
            {
                if (mouse.AltPressed) ToggleObstacle(mouse.Position); else Order(mouse.Position, mouse.CtrlPressed);
            }
        }
        if (input is InputEventMouseMotion motion)
        {
            if (_panning) Pan(-motion.Relative.X * _distance / 1100, -motion.Relative.Y * _distance / 1100);
            if (_dragging) _hud.SelectionBox = new Rect2(_dragStart, motion.Position - _dragStart).Abs();
        }
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space)
            {
                _paused = !_paused;
                foreach (var view in _views.Values) if (view.Player != null) view.Player.SpeedScale = _paused ? 0 : 1;
            }
            if (key.Keycode == Key.Escape) _selected.Clear();
        }
    }

    private void SelectUnits(Vector2 start, Vector2 end, bool append)
    {
        if (!append) _selected.Clear();
        var rect = new Rect2(start, end - start).Abs();
        foreach (var unit in _world!.Units.Where(u => u.Team == 0 && u.Health > 0))
        {
            var pos = UnitPosition(unit) + Vector3.Up;
            if (_camera.IsPositionBehind(pos)) continue;
            var screen = _camera.UnprojectPosition(pos);
            if (rect.Size.Length() < 7 ? screen.DistanceTo(end) < 22 : rect.HasPoint(screen)) _selected.Add(unit.Id);
        }
    }

    private void Order(Vector2 mouse, bool groundOnly = false)
    {
        var enemy = _world!.Units.Where(u => u.Team != 0 && u.Health > 0 && !_camera.IsPositionBehind(UnitPosition(u)))
            .OrderBy(u => _camera.UnprojectPosition(UnitPosition(u) + Vector3.Up).DistanceSquaredTo(mouse)).FirstOrDefault();
        if (!groundOnly && enemy != null && _camera.UnprojectPosition(UnitPosition(enemy) + Vector3.Up).DistanceTo(mouse) < 24)
        {
            _world.Enqueue(0, _selected, enemy.Node, enemy.Id);
            _status.Text = "Attack order issued"; return;
        }
        Vector3 origin = _camera.ProjectRayOrigin(mouse), direction = _camera.ProjectRayNormal(mouse);
        var hit = _picker.Pick(new(-origin.X, origin.Y, origin.Z), new(-direction.X, direction.Y, direction.Z), groundOnly);
        if (hit == null) return;
        int goal = _world.Navigation.Nearest(hit.Node);
        if (goal >= 0)
        {
            _world.Enqueue(0, _selected, goal);
            _status.Text = groundOnly ? "Ground move order issued" : "Move order issued";
            _hud.Destination = _camera.UnprojectPosition(new Vector3(-hit.Position.X, hit.Position.Y, hit.Position.Z));
            _hud.DestinationTime = 0.7f;
        }
        else _status.Text = "No walkable space at this point";
    }

    private void ToggleObstacle(Vector2 mouse)
    {
        int selected = PickObstacle(mouse);
        if (selected < 0) return;
        _pendingObstacle = selected;
        _pendingObstacleActive = !_world!.Navigation.IsObstacleActive(selected);
        _world.EnqueueObstacle(selected, _pendingObstacleActive);
        _status.Text = "Obstacle change queued; restore requires free space";
    }

    private int PickObstacle(Vector2 mouse)
    {
        if (_obstacleLayers.ValueKind != JsonValueKind.Array) return -1;
        Vector3 origin = _camera.ProjectRayOrigin(mouse), direction = _camera.ProjectRayNormal(mouse);
        // Test source footprints even after their visuals are hidden, so the
        // same click restores an obstacle. Convex half-space ray clipping.
        float nearest = float.PositiveInfinity; int selected = -1;
        foreach (var layer in _obstacleLayers.EnumerateArray())
        foreach (var box in layer.GetProperty("footprints").EnumerateArray())
        {
            float lo = 0, hi = 1500;
            bool Clip(float value, float velocity)
            {
                if (Mathf.Abs(velocity) < 0.000001f) return value >= 0;
                float t = -value/velocity;
                if (velocity > 0) lo = Math.Max(lo, t); else hi = Math.Min(hi, t);
                return lo <= hi;
            }
            if (!Clip(origin.Y-box.GetProperty("y_min").GetSingle(), direction.Y)
                || !Clip(box.GetProperty("y_max").GetSingle()-origin.Y, -direction.Y)) continue;
            var polygon = box.GetProperty("polygon").EnumerateArray().ToArray();
            bool hit = true;
            for (int i = 0; i < polygon.Length; i++)
            {
                var p = polygon[i]; var q = polygon[(i+1)%polygon.Length];
                float dx = q[0].GetSingle()-p[0].GetSingle(), dz = q[1].GetSingle()-p[1].GetSingle();
                if (!Clip(dx*(origin.Z-p[1].GetSingle())-dz*(-origin.X-p[0].GetSingle()), dx*direction.Z+dz*direction.X))
                { hit = false; break; }
            }
            if (hit && lo < nearest) { nearest = lo; selected = layer.GetProperty("id").GetInt32(); }
        }
        return selected;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_world is null || _paused) return;
        _world.Step();
        if (_pendingObstacle >= 0)
        {
            bool applied = _world.Navigation.IsObstacleActive(_pendingObstacle) == _pendingObstacleActive;
            _status.Text = applied ? (_pendingObstacleActive ? "Obstacle restored" : "Obstacle removed")
                : "Cannot restore here while a unit occupies the space";
            _pendingObstacle = -1;
        }
        foreach (var (id, view) in _obstacleViews) view.Visible = _world.Navigation.IsObstacleActive(id);
        foreach (var unit in _world.Units)
        {
            var view = _views[unit.Id];
            Vector3 old = view.Root.Position, position = UnitPosition(unit);
            view.Root.Position = position;
            var direction = position - old;
            if (direction.LengthSquared() > 0.00001f) view.Root.Rotation = new Vector3(0, Mathf.Atan2(direction.X, direction.Z), 0);
            view.Ring.Visible = unit.Health > 0;
            view.Ring.Scale = _selected.Contains(unit.Id) ? Vector3.One * 1.3f : Vector3.One;
            string state = unit.Health <= 0 ? "death" : unit.Attacking ? "attack" : unit.Moving ? "walk" : "idle";
            if (unit.Moving) _movingSeen++;
            if (unit.Attacking) _attackingSeen++;
            string clip = view.Model.GetProperty(state).GetString() ?? "";
            if (view.Player != null && clip != "" && clip != view.Clip)
            {
                string? animation = view.Player.GetAnimationList().FirstOrDefault(a => a == clip || a.EndsWith('/' + clip));
                if (animation != null)
                {
                    var resource = view.Player.GetAnimation(animation);
                    resource.LoopMode = state == "death" ? Animation.LoopModeEnum.None : Animation.LoopModeEnum.Linear;
                    view.Player.Play(animation, 0.12); view.Clip = clip;
                }
            }
            if (unit.Health <= 0) view.Died = true;
        }
    }

    public override void _Process(double delta)
    {
        if (_world is null) return;
        float dt = (float)Math.Min(delta, 0.1);
        float right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        float forward = (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0);
        Pan(right * dt * _distance * 0.6f, forward * dt * _distance * 0.6f);
        _yaw += ((Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0)) * dt;
        UpdateCamera();
        var units = _world.Units.Where(u => _selected.Contains(u.Id) && u.Health > 0).ToList();
        _selection.Text = $"{units.Count} selected · {units.Sum(u => u.Health)} HP · {_world.Units.Count(u => u.Team == 1 && u.Health > 0)} enemies" + (_paused ? "  /  PAUSED" : "");
        _hud.DestinationTime -= dt; _hud.QueueRedraw();
        _frames++;
        if (_obstacleSmoke) ObstacleSmokeInput();
        if (_surfaceSmoke && _frames == 5)
        {
            _selected.UnionWith(_world.Units.Where(u => u.Team == 0).Select(u => u.Id));
            var unit = _world.Units[0];
            _surfaceSmokePoint = _camera.UnprojectPosition(UnitPosition(unit));
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
                Position = _surfaceSmokePoint, GlobalPosition = _surfaceSmokePoint }, true);
        }
        if (_surfaceSmoke && _frames == 7)
        {
            _surfacePicked = _world.Commands.Any(c => c.Cell >= _world.Navigation.GroundCount && c.Units.Length == 6);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, CtrlPressed = true,
                Position = _surfaceSmokePoint, GlobalPosition = _surfaceSmokePoint }, true);
        }
        if (_surfaceSmoke && _frames == 9)
        {
            bool ground = _world.Commands.LastOrDefault() is { Target: -1 } command && command.Cell < _world.Navigation.GroundCount;
            GD.Print("SURFACE_PICK_SMOKE " + _surfacePicked); GD.Print("UNDERPASS_PICK_SMOKE " + ground);
            GetTree().Quit(_surfacePicked && ground ? 0 : 1);
        }
        if (_smoke) SmokeInput();
        if (_smoke && _capture != "" && _frames == 60) _ = CaptureFrame(false);
        if (_smoke && _world.Tick >= 420) FinishSmoke();
        else if (!_smoke && _capture != "" && _frames == 20) _ = CaptureFrame(true);
    }

    private void Pan(float right, float forward)
    {
        _focus += new Vector3(Mathf.Cos(_yaw) * right + Mathf.Sin(_yaw) * forward, 0,
            -Mathf.Sin(_yaw) * right + Mathf.Cos(_yaw) * forward);
        if (_world != null)
        {
            _focus.X = Math.Clamp(_focus.X, -_world.Navigation.Width * _world.Navigation.CellSize / 1000f, 0);
            _focus.Z = Math.Clamp(_focus.Z, 0, _world.Navigation.Height * _world.Navigation.CellSize / 1000f);
        }
    }

    private void UpdateCamera()
    {
        _focus.Y = _world!.Navigation.PickHeight((int)(-_focus.X * 1000), (int)(_focus.Z * 1000)) / 1000f;
        _camera.Position = _focus + new Vector3(Mathf.Sin(_yaw) * _distance * 0.65f, _distance * 0.8f, Mathf.Cos(_yaw) * _distance * 0.65f);
        _camera.LookAt(_focus);
    }

    private bool _finishing;
    private void ObstacleSmokeInput()
    {
        if (_frames == 5)
        {
            var box = _obstacleLayers[0].GetProperty("footprints")[0];
            var polygon = box.GetProperty("polygon").EnumerateArray().ToArray();
            var center = new Vector3(-polygon.Average(p => p[0].GetSingle()),
                (box.GetProperty("y_min").GetSingle()+box.GetProperty("y_max").GetSingle())/2,
                polygon.Average(p => p[1].GetSingle()));
            _focus = center; UpdateCamera();
            _obstacleSmokePoint = _camera.UnprojectPosition(center);
            _obstacleSmokeId = PickObstacle(_obstacleSmokePoint);
        }
        if ((_frames == 5 || _frames == 10) && _obstacleSmokeId >= 0)
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
                AltPressed = true, Position = _obstacleSmokePoint, GlobalPosition = _obstacleSmokePoint }, true);
        if (_frames == 7 && _obstacleSmokeId >= 0)
            _obstacleRemoved = !_world!.Navigation.IsObstacleActive(_obstacleSmokeId) && !_obstacleViews[_obstacleSmokeId].Visible;
        if (_frames == 12)
        {
            bool passed = _obstacleSmokeId >= 0 && _obstacleRemoved && _world!.Navigation.IsObstacleActive(_obstacleSmokeId)
                && _obstacleViews[_obstacleSmokeId].Visible && _world.ObstacleCommands.Count == 2;
            GD.Print("OBSTACLE_INPUT_SMOKE " + passed); GetTree().Quit(passed ? 0 : 1);
        }
    }

    private void SmokeInput()
    {
        // Exercise Godot's input dispatch, including drag selection and picking.
        var friendly = _world!.Units.Where(u => u.Team == 0).Select(u => _camera.UnprojectPosition(UnitPosition(u) + Vector3.Up)).ToArray();
        Vector2 min = new(friendly.Min(p => p.X) - 8, friendly.Min(p => p.Y) - 8);
        Vector2 max = new(friendly.Max(p => p.X) + 8, friendly.Max(p => p.Y) + 8);
        if (_frames == 5)
        {
            _cameraBeforeInput = _camera.Position;
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(1000, 700), GlobalPosition = new Vector2(1000, 700) }, true);
        }
        if (_frames == 7) _inputCameraPassed = _camera.Position.DistanceTo(_cameraBeforeInput) > 0.1f;
        if (_frames == 10)
        {
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true }, true);
            _pauseTick = _world.Tick;
        }
        if (_frames == 13)
        {
            _inputPausePassed = _paused && _world.Tick == _pauseTick;
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true }, true);
        }
        if (_frames == 20) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = min, GlobalPosition = min }, true);
        if (_frames == 21) GetViewport().PushInput(new InputEventMouseMotion { Position = max, GlobalPosition = max, Relative = max - min, ButtonMask = MouseButtonMask.Left }, true);
        if (_frames == 22) GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = max, GlobalPosition = max }, true);
        if (_frames == 24)
        {
            _inputSelectionPassed = _selected.Count == 6;
            var (x, z) = _world.Navigation.Center(_moveDestination);
            var screen = _camera.UnprojectPosition(PositionOf(x, z));
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = screen, GlobalPosition = screen }, true);
            _inputMovePassed = _world.Commands.Any(c => c.Target < 0 && c.Units.Length == 6);
        }
        if (_frames == 100)
        {
            var target = _world.Units.First(u => u.Team == 1 && u.Health > 0);
            var screen = _camera.UnprojectPosition(UnitPosition(target) + Vector3.Up);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = screen, GlobalPosition = screen }, true);
            _inputAttackPassed = _world.Commands.Any(c => c.Target >= 0 && c.Units.Length == 6);
        }
    }

    private void FinishSmoke()
    {
        if (_finishing) return;
        _finishing = true;
        var report = new { ticks = _world!.Tick, units = _world.Units.Count, moving_samples = _movingSeen,
            attack_samples = _attackingSeen, damage = _world.Units.Sum(u => 100-u.Health),
            animated_units = _views.Values.Count(v => v.Player != null && v.Clip != ""),
            input_selection = _inputSelectionPassed, input_move = _inputMovePassed, input_attack = _inputAttackPassed,
            input_camera = _inputCameraPassed, input_pause = _inputPausePassed,
            map_loaded = true, initial_digest = _initialDigest, final_digest = _world.StateDigest() };
        string path = System.IO.Path.Combine(_cache, "smoke-report.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        bool passed = report.units == 12 && report.moving_samples > 0 && report.attack_samples > 0 && report.damage > 0 && report.animated_units == 12
            && report.input_selection && report.input_move && report.input_attack && report.input_camera && report.input_pause;
        GD.Print("SANDBOX_SMOKE " + JsonSerializer.Serialize(report));
        GetTree().Quit(passed ? 0 : 1);
    }

    private async Task CaptureFrame(bool quit)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_capture); GD.Print("CAPTURE " + _capture + " " + error);
        if (quit) GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
