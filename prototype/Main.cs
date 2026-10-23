using Godot;
using Reforged.Simulation;
using System.Text.Json;
using System.Buffers.Binary;
using Reforged.Input;
using Reforged.Presentation;
using Reforged.Rendering;

public partial class Main : Node3D
{
    private SimWorld? _world;
    private bool _loadFailed;
    private CombatRules _rules = CombatRules.Probe;
    private readonly Dictionary<string,JsonElement> _appearanceData = [];
    private NavigationPicker _picker = null!;
    private Camera3D _camera = null!;
    private Label _status = null!;
    private SandboxHud _hud = null!;
    private CommandPanel _commands = null!;
    private readonly OrderInput _orders = new();
    private readonly ControlGroups _controlGroups = new();
    private int _lastGroup = -1;
    private ulong _lastGroupTime;
    private readonly HashSet<int> _selected = [];
    private readonly Dictionary<int, UnitView> _views = [];
    private readonly Dictionary<string,JsonElement> _unitModels = [];
    private readonly Dictionary<(string File,int Team),Node3D> _modelTemplates = [];
    private readonly Dictionary<int, Node3D> _obstacleViews = [];
    private JsonElement _obstacleLayers;
    private int _pendingObstacle = -1;
    private bool _pendingObstacleActive;
    private Vector3 _focus;
    private Vector3 _squadFocus, _fortressFocus;
    private readonly ScenePresentation _presentation = new();
    private Color[] _teamColors = [new Color("025cd1"), new Color("b90000")];
    private string _view = "squad";
    private float _squadDistance = 48;
    private int _spawnCount;
    private readonly List<int[]> _friendlySquads = [];
    private readonly HashSet<int> _primarySquad = [];
    private bool _hideHud;
    private float _distance = 48, _yaw = 0.25f;
    private Vector2 _dragStart;
    private bool _dragging, _panning, _paused, _selectSameType;
    private string _cache = "", _capture = "";
    private Vector2I _captureResolution;
    private bool _smoke;
    private bool _motionSmoke;
    private bool _combatSmoke, _combatSpacingPassed = true, _squadKeysPassed = true;
    private readonly HashSet<int> _squadsFought = [];
    private int _turningSeen, _interpolatedSeen;
    private bool _surfaceSmoke;
    private bool _surfacePicked;
    private Vector2 _surfaceSmokePoint;
    private bool _obstacleSmoke, _obstacleRemoved;
    private int _obstacleSmokeId = -1;
    private Vector2 _obstacleSmokePoint;
    private int _frames, _moveDestination = -1;
    private string _initialDigest = "";
    private int _movingSeen, _attackingSeen, _freeMovementSeen;
    private bool _inputSelectionPassed, _inputMovePassed, _inputAttackPassed;
    private bool _inputCameraPassed, _inputPausePassed;
    private bool _fortressViewPassed, _overviewPassed, _squadViewPassed;
    private bool _cameraShortcutsRemoved;
    private int _pauseTick;
    private Vector3 _cameraBeforeInput;
    private sealed class UnitView(Node3D root, MeshInstance3D ring, AnimationPlayer? player, JsonElement model)
    {
        public Node3D Root = root;
        public MeshInstance3D Ring = ring;
        public HealthBar? Health;
        public AnimationPlayer? Player = player;
        public JsonElement Model = model;
        public string Clip = "";
        public string? State;
        public bool Died;
        /// <summary>The tick the presentation first saw this unit dead, and whether its body has left the field.</summary>
        public int DeathTick = -1;
        public bool Buried;
        public float DesiredYaw;
        public Vector3 RingPosition = new(float.PositiveInfinity, 0, 0);
        public float RingYaw;
        public int AttackSerial = -1;
        public int CastSerial = -1;
        public UnitAppearance? Appearance;
        public HeadingPlayback Heading = new();
        public bool Translucent;
        public (MeshInstance3D Mesh,int Surface,Material? Faded)[]? Ghost;
    }

    public override void _Ready()
    {
        CreateUi(); WatchPointer();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--cache=")) _cache = arg[8..];
            if (arg.StartsWith("--capture=")) _capture = arg[10..];
            if (arg == "--no-ai") _noAi = true;
            if (arg == "--observe") _observing = true;
            if (arg == "--profile") SimProfiler.Enabled = true;
            if (arg.StartsWith("--bots=")) _bots = arg[7..].Split(',');
            if (arg.StartsWith("--speed=")) _speed = int.Parse(arg[8..]);
            if (arg.StartsWith("--ai-personality=")) _aiPersonality = arg[17..];
            if (arg == "--ai-smoke") _aiSmoke = true;
            if (arg == "--observer-smoke") _observerSmoke = true;
            if (arg == "--setup") _setupMode = true;
            if (arg == "--setup-smoke") _setupSmoke = _setupMode = true;
            if (arg.StartsWith("--capture-resolution="))
            {
                var size = arg[21..].Split('x'); _captureResolution = new(int.Parse(size[0]),int.Parse(size[1]));
                if (_captureResolution.X < 320 || _captureResolution.Y < 200 || _captureResolution.X > 8192 || _captureResolution.Y > 8192)
                    throw new ArgumentOutOfRangeException(nameof(_captureResolution));
            }
            if (arg.StartsWith("--view=")) _view = arg[7..];
            if (arg == "--hide-hud") _hideHud = true;
            if (arg == "--debug-menu") _openDebugMenu = true;
            if (arg == "--smoke") _smoke = true;
            if (arg == "--motion-smoke") _motionSmoke = true;
            if (arg == "--combat-smoke") { _combatSmoke = true; _smoke = true; }
            if (arg == "--surface-smoke") _surfaceSmoke = true;
            if (arg == "--obstacle-smoke") _obstacleSmoke = true;
            if (arg == "--command-smoke") _commandSmoke = true;
            if (arg == "--orders-smoke") _ordersSmoke = true;
            if (arg == "--ability-smoke") _abilitySmoke = true;
            if (arg == "--skirmish-smoke") _skirmishSmoke = true;
            if (arg == "--construction-smoke") _constructionSmoke = true;
            if (arg == "--structure-smoke") _structureSmoke = true;
            if (arg == "--production-smoke") _productionSmoke = true;
            if (arg == "--placement-smoke") _placementSmoke = true;
            if (arg == "--dwarf-hall-smoke") _dwarfHallSmoke = true;
            if (arg == "--minimap-smoke") _minimapSmoke = true;
            if (arg == "--frodo-smoke") _frodoSmoke = true;
            if (arg == "--tech-smoke") _techSmoke = true;
            if (arg == "--siege-smoke") _siegeSmoke = true;
            if (arg == "--defense-smoke") _defenseSmoke = true;
            if (arg == "--victory-smoke") _victorySmoke = true;
            if (arg == "--melee-smoke") { _meleeSmoke = _combatSmoke = _smoke = true; }
            if (arg == "--engagement-smoke") _engagementSmoke = true;
            if (arg == "--attack-move-engagement-smoke") _attackMoveEngagementSmoke=_engagementSmoke=true;
            if (arg == "--formation-smoke") _formationSmoke=_engagementSmoke=true;
        }
        // The setup screen chooses the match; once chosen, its choice replaces the launch arguments.
        if (_setupMode && _chosenSetup is { } setup) ApplySetup(setup);
        else if (_setupMode) { ShowSetupScreen(); return; }
        _setupButton.Visible = _outcomeSetup.Visible = _setupMode;
        _hud.Visible = !_hideHud;
        _commands.PlayerTeam = PlayerTeam;
        if (!Speeds.Contains(_speed)) throw new ArgumentOutOfRangeException(nameof(_speed),"Game speed is 1, 2, 4, 8 or 16");
        if (_cache == "") _cache = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../converted/prototype"));
        try { LoadSandbox(); if (_openDebugMenu) { _hud.Show(); _toolsPanel.Show(); RefreshDebugMenu(); } }
        catch (Exception error)
        {
            _loadFailed = true;
            _toolsPanel.Visible = true;
            RefreshDebugMenu();
            _status.Text = "Asset cache unavailable. Run tools/prepare_prototype.py, then restart.";
            GD.PushError(error.ToString());
            if (_smoke || _surfaceSmoke || _obstacleSmoke || _commandSmoke || _ordersSmoke || _abilitySmoke || _skirmishSmoke || _constructionSmoke || _structureSmoke || _productionSmoke || _placementSmoke || _dwarfHallSmoke || _minimapSmoke || _frodoSmoke || _techSmoke || _siegeSmoke || _defenseSmoke || _victorySmoke || _aiSmoke || _observerSmoke || _setupSmoke || _engagementSmoke || _capture != "") GetTree().Quit(1);
        }
    }

    private JsonElement ReadJson(string relative)
        => JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(_cache, relative))).RootElement.Clone();

    private Node3D LoadGlb(string relative, Color? teamColor = null)
        => _presentation.Load(System.IO.Path.Combine(_cache, relative), relative, teamColor);

    private void LoadSandbox()
    {
        var manifest = ReadJson("manifest.json");
        if (manifest.GetProperty("format_version").GetInt32() != 1) throw new InvalidOperationException("Unsupported cache format");
        _skirmish = manifest.TryGetProperty("mode",out var mode) && mode.GetString() == "skirmish";
        _mapName = manifest.GetProperty("name").GetString()!.Split(" — ")[0];
        foreach (var file in manifest.GetProperty("files").EnumerateObject())
        {
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_cache, file.Name));
            if (!path.StartsWith(System.IO.Path.GetFullPath(_cache) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cache path escapes its folder");
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path)));
            if (!hash.Equals(file.Value.GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Stale or damaged cache: " + file.Name);
        }
        if (manifest.TryGetProperty("team_colors", out var colors))
            _teamColors = colors.EnumerateArray().Select(c => new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle())).ToArray();
        string rulesPath = manifest.GetProperty("combat_rules").GetString()!;
        _rules = CombatRules.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(_cache,rulesPath)));
        LoadCombatAudio(ReadJson(rulesPath));
        var nav = ReadJson("navigation.json");
        byte[] bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(_cache, "heights.bin"));
        int[] heights = new int[bytes.Length / 4];
        for (int i = 0; i < heights.Length; i++) heights[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * 4, 4));
        bool dynamic = nav.TryGetProperty("runtime_obstacles", out _obstacleLayers);
        var navigation = new GridNavigation(nav.GetProperty("width").GetInt32(), nav.GetProperty("height").GetInt32(),
            nav.GetProperty("cell_size_mm").GetInt32(), Convert.FromBase64String(nav.GetProperty(dynamic ? "painted_allowed_base64" : "allowed_base64").GetString()!).Select(b => b != 0).ToArray(), heights,
            nav.TryGetProperty("surface_nodes", out var surfaces) ? surfaces.EnumerateArray().Select(s => new SurfaceCell(
                s.GetProperty("cell").GetInt32(), s.GetProperty("height_mm").GetInt32(),
                s.GetProperty("links").EnumerateArray().Select(n => n.GetInt32()).ToArray(),
                s.TryGetProperty("x_mm", out var x) ? x.GetInt32() : null, s.TryGetProperty("z_mm", out var z) ? z.GetInt32() : null)).ToArray() : null,
            dynamic ? _obstacleLayers.EnumerateArray().Select(NavigationObstacle.Parse).ToArray() : null);
        navigation.LoadBodyProfiles(nav);
        if (nav.TryGetProperty("inactive_obstacles",out var inactive))
            foreach (var id in inactive.EnumerateArray()) navigation.SetObstacle(id.GetInt32(),false);
        _world = new SimWorld(navigation,_rules);
        if (nav.TryGetProperty("building_allowed_base64",out var building))
            _world.BuildingGround = Convert.FromBase64String(building.GetString()!).Select(b => b != 0).ToArray();
        _picker = NavigationPicker.FromCache(navigation, nav);
        var map = LoadGlb(manifest.GetProperty("map").GetString()!); AddChild(map);
        var gate = Descendants(map).OfType<Node3D>().FirstOrDefault(n => n.Name.ToString() == "MAIN_GATE");
        _fortressFocus = gate?.GlobalPosition + new Vector3(17, 0, -4)
            ?? new Vector3(-navigation.Width * navigation.CellSize / 2000f, 0, navigation.Height * navigation.CellSize / 2000f);
        if (dynamic)
            foreach (var obstacle in _obstacleLayers.EnumerateArray())
            {
                var view = Descendants(map).OfType<Node3D>().FirstOrDefault(n => n.Name.ToString() == obstacle.GetProperty("name").GetString());
                // Skirmish start buildings are spawned after the map and bind their own obstacle view.
                if (view == null && _skirmish && obstacle.GetProperty("id").GetInt32() >= SkirmishObstacleBase) continue;
                if (view == null) throw new InvalidOperationException("Obstacle visual missing: " + obstacle.GetProperty("name").GetString());
                _obstacleViews.Add(obstacle.GetProperty("id").GetInt32(), view);
            }
        // Preview lighting, not a decoded original-game time-of-day model.
        AddChild(PreviewLighting.CreateEnvironment());
        AddChild(PreviewLighting.CreateSun());
        _camera = new Camera3D { Current = true, Fov = 48, Near = 0.1f, Far = 1200 };
        AddChild(_camera);
        var (cx, cz) = navigation.Center(manifest.GetProperty("camera_cell").GetInt32());
        _squadFocus = PositionOf(cx, cz);
        var surfaceSpawns = new HashSet<int>();
        var largestBody = _rules.Units.Values.MaxBy(u => u.BodyRadiusMm)!;
        var surfaceNavigation = navigation.ForBody(largestBody.BodyRadiusMm,largestBody.BodyHeightMm);
        int surfaceStart = _surfaceSmoke ? Enumerable.Range(navigation.GroundCount, navigation.NodeCount-navigation.GroundCount)
            .Where(surfaceNavigation.CanStand).OrderByDescending(n => {
                var (x,z) = navigation.Center(n); return navigation.NodeHeight(n)-navigation.GroundHeight(x,z);
            }).First() : -1;
        foreach (var model in manifest.GetProperty("models").EnumerateArray()) _unitModels[model.GetProperty("unit").GetString()!] = model;
        foreach (var spawn in manifest.GetProperty("spawns").EnumerateArray())
        {
            int team = spawn.GetProperty("team").GetInt32(), cell = spawn.GetProperty("cell").GetInt32();
            if (_skirmish && !SlotOpen(team)) continue;
            bool primary = team == PlayerTeam && _primarySquad.Count < 6;
            if (_surfaceSmoke && primary) { cell = surfaceNavigation.Nearest(surfaceStart, surfaceSpawns); surfaceSpawns.Add(cell); }
            var model = manifest.GetProperty("models")[spawn.GetProperty("model").GetInt32()];
            var definition = _rules.Units[model.GetProperty("unit").GetString()!];
            if (_skirmish) cell = SkirmishSpawnCell(team,definition,cell);
            var unit = _world.Spawn(team, cell,definition);
            if (primary) _primarySquad.Add(unit.Id);
            AddUnitView(unit);
        }
        _spawnCount = _world.Units.Count;
        _friendlySquads.AddRange(_world.Units.Where(u => u.Team == PlayerTeam).Select(u => u.Id).Chunk(6));
        for (int slot = 0; slot < _friendlySquads.Count; slot++) _controlGroups.Assign(slot,_friendlySquads[slot],LivingFriendly);
        _commands.TeamColors = _teamColors;
        _commands.LoadArtwork(_cache,_world);
        _heroBar.LoadArtwork(_commands);
        _hudScale.Value = _commands.ScalePercent;
        if (_skirmish) LoadSkirmishScene(manifest);
        var framed = _skirmish && !_observing ? _world.Units.Where(u => u.Team == PlayerTeam).ToList() : _world.Units;
        float spanX = (framed.Max(u => u.X)-framed.Min(u => u.X))/1000f;
        float spanZ = (framed.Max(u => u.Z)-framed.Min(u => u.Z))/1000f;
        _squadDistance = Math.Max(48,Math.Max(spanX,spanZ)*1.9f);
        // Build each spawned body's region cache during loading, before a
        // mouse command can trigger a full-map connectivity pass mid-frame.
        long warmStart=System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var type in _world.Units.GroupBy(u=>(u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm)))
            navigation.ForBody(type.Key.BodyRadiusMm,type.Key.BodyHeightMm).WarmConnectedRegion(type.First().Node);
        _navigationWarmMs=System.Diagnostics.Stopwatch.GetElapsedTime(warmStart).TotalMilliseconds;
        SetCameraView(_smoke || _surfaceSmoke || _obstacleSmoke ? "squad" : _view);
        if (_surfaceSmoke) _focus = UnitPosition(_world.Units[0]);
        UpdateCamera();
        _initialDigest = _world.StateDigest();
        if (_smoke)
        {
            var first = _world.Units[0];
            _moveDestination = navigation.Nearest(navigation.Cell(first.X + 6000, first.Z + 6000));
        }
    }

    /// <summary>Model, ring and appearance for a start unit or one trained during play.</summary>
    private void AddUnitView(SimUnit unit)
    {
        int team = unit.Team; var model = _unitModels[unit.Definition.Name];
        var root = new Node3D { Name = "Unit_" + unit.Id, PhysicsInterpolationMode = Node.PhysicsInterpolationModeEnum.Off };
        AddChild(root); root.Position = UnitPosition(unit);
        var modelKey = (model.GetProperty("file").GetString()!,team);
        Node3D visual;
        if (_modelTemplates.TryGetValue(modelKey,out var template)) visual = (Node3D)template.Duplicate();
        else { visual = LoadGlb(modelKey.Item1,_teamColors[team]); _modelTemplates.Add(modelKey,visual); }
        var modelSize = new Node3D { Name = "ModelSize", Scale = Vector3.One*(unit.Definition.ModelScalePpm/1_000_000f) };
        root.AddChild(modelSize); modelSize.AddChild(visual);
        var ring = new MeshInstance3D { Mesh = ScenePresentation.SelectionRing(), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D {
                AlbedoColor = _observing ? TeamColor(team).Lightened(.25f) : new Color(team == PlayerTeam ? "e9c22d" : "e65045"), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        root.AddChild(ring);
        var health = new HealthBar(Math.Max(1.1f,2.2f*unit.Definition.BodyRadiusMm/1000f),.16f) { Position = new(0,.12f,0) };
        root.AddChild(health);
        if (!_appearanceData.ContainsKey(modelKey.Item1)) _appearanceData[modelKey.Item1] = UnitAppearance.Read(System.IO.Path.Combine(_cache,modelKey.Item1));
        var appearance = new UnitAppearance(visual,_appearanceData[modelKey.Item1]);
        appearance.Apply(model.GetProperty("idle").GetString()!,0);
        var player = Descendants(visual).OfType<AnimationPlayer>().FirstOrDefault()
            ?? throw new InvalidOperationException("Missing animation player for "+unit.Definition.Name);
        foreach (string clip in unit.Definition.Attacks.Select(a => a.Clip).Append(unit.Definition.ReadyClip))
            if (!player.GetAnimationList().Any(a => a == clip || a.EndsWith('/'+clip)))
                throw new InvalidOperationException("Missing combat animation: "+clip);
        if (_meleeSmoke && unit.Definition.HasMelee)
            foreach (var attack in unit.Definition.Attacks.Where(a => a.Melee))
            {
                string clip = player.GetAnimationList().First(a => a == attack.Clip || a.EndsWith('/'+attack.Clip));
                player.Play(clip); player.Advance(0);
                player.Seek(player.CurrentAnimationLength*attack.Hits[0].Tick/attack.DurationTicks,true);
                appearance.Apply(attack.Clip,attack.Hits[0].Tick/(double)attack.DurationTicks);
                _archerMeleeClipSamples++;
            }
        if (_meleeSmoke && unit.Definition.Id == 16)
        {
            var idle = appearance.VisibleNodes;
            appearance.Apply(unit.Definition.ReadyClip,0); var ready = appearance.VisibleNodes;
            appearance.Apply(unit.Definition.Attacks[0].Clip,0); var attack = appearance.VisibleNodes;
            appearance.Apply(model.GetProperty("idle").GetString()!,0);
            _equipmentStatesVerified = !idle.SequenceEqual(ready) && ready.SequenceEqual(attack) && idle.SequenceEqual(appearance.VisibleNodes);
        }
        _views[unit.Id] = new UnitView(root,ring,player,model) { Appearance = appearance,Health = health };
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
        _hud.AddChild(new BuildOverlay());
        _heroBar = new HeroBar(); _hud.AddChild(_heroBar);
        _heroBar.HeroChosen += ChooseHero;
        CreateDebugMenu();
        _commands = new CommandPanel(); _hud.AddChild(_commands);
        CreateOutcomePanel();
        CreateObserverPanel();
        _commands.MenuChosen += ToggleDebugMenu;
        _commands.CommandChosen += ChooseCommand;
        _commands.WalkRunChosen += ToggleWalkRun;
        _commands.AbilityChosen += ChooseAbility;
        _commands.ConstructionChosen += ChooseConstruction;
        _commands.BuildMenuChosen += OpenBuildMenu;
        _commands.TrainingChosen += ChooseTraining;
        _commands.TrainingCancelled += CancelTraining;
        _commands.UpgradeChosen += ChooseUpgrade;
        _commands.StructurePortraitChosen += () => {
            if (_selectedStructure is int id && _structureViews.TryGetValue(id,out var structure))
            { _focus = structure.Root.GlobalPosition; UpdateCamera(); }
        };
        _commands.GroupChosen += slot => UseGroup(slot,Input.IsPhysicalKeyPressed(Key.Ctrl),Input.IsPhysicalKeyPressed(Key.Shift));
        _commands.UnitChosen += (id,append,sameType) => {
            _selectedStructure = null;
            ClearConstructionTargeting();
            _orders.Cancel();
            if (_world != null) UnitSelection.Click(_selected,_world.Units,LivingUnit(id) ? _world.Units[id] : null,append,sameType);
        };
        _commands.MapChosen += point => { if (_world != null) CenterViewOn(point); };
        _commands.MapOrdered += OrderFromMinimap;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_world is null || _loadFailed || _outcome.Visible) return;
        if (input is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed) _distance = Math.Max(12, _distance * 0.88f);
            if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed) _distance = Math.Min(450, _distance * 1.12f);
            if (mouse.ButtonIndex == MouseButton.Middle) _panning = mouse.Pressed;
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed && _constructionTargeting) TargetConstruction(mouse.Position,mouse.ShiftPressed);
                else if (mouse.Pressed && _orders.Pending != null) Order(mouse.Position,mouse.CtrlPressed,mouse.ShiftPressed);
                else if (mouse.Pressed) { _dragStart = mouse.Position; _dragging = true; _selectSameType = mouse.CtrlPressed || mouse.DoubleClick; }
                else if (_dragging) { SelectUnits(_dragStart, mouse.Position, mouse.ShiftPressed,_selectSameType); _dragging = false; _hud.SelectionBox = null; }
            }
            if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
            {
                if (_orders.Pending != null || _constructionTargeting) CancelTargeting();
                else if (mouse.AltPressed && !mouse.CtrlPressed) ToggleObstacle(mouse.Position);
                else if (!TrySetRallyPoint(mouse.Position))
                {
                    if (mouse.CtrlPressed && !mouse.AltPressed) _orders.Begin(OrderKind.AttackMove);
                    Order(mouse.Position,mouse.CtrlPressed && mouse.AltPressed,mouse.ShiftPressed);
                }
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
                SetPaused(!_paused);
            }
            if (key.Keycode is Key.Minus or Key.KpSubtract) StepSpeed(-1);
            if (key.Keycode is Key.Equal or Key.Plus or Key.KpAdd) StepSpeed(1);
            if (key.Keycode == Key.Escape)
            {
                if (_orders.Pending != null || _constructionTargeting) CancelTargeting();
                else if (_buildMenu) OpenBuildMenu(false);
                else { _selected.Clear(); _selectedStructure = null; }
                _dragging = false; _hud.SelectionBox = null;
            }
            if (_observing && ObserverHotkey(key)) return;
            // Grid hotkeys go by key position, so they follow QWER/ASDF/ZXCV on any keyboard layout.
            int gridSlot = CommandPanel.GridSlot(key.PhysicalKeycode);
            if (gridSlot >= 0 && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed && UseGridHotkey(gridSlot)) return;
            OrderKind? command = key.Keycode switch {
                Key.M => OrderKind.Move, Key.A => OrderKind.Attack,
                Key.S => OrderKind.Stop, Key.H => OrderKind.HoldGround, _ => null };
            command ??= key.Keycode switch { Key.G => OrderKind.Guard,Key.T => OrderKind.Patrol,_ => null };
            if (command != null && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) ChooseCommand(command.Value);
            int abilitySlot = key.Keycode switch { Key.Q => 0,Key.W => 1,Key.E => 2,Key.R => 3,_ => -1 };
            if (abilitySlot >= 0 && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) ChooseAbility(abilitySlot);
            int groupSlot = key.Keycode == Key.Key0 ? 9 : key.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)key.Keycode-(int)Key.Key1 : -1;
            if (groupSlot >= 0 && !key.AltPressed && !key.MetaPressed) UseGroup(groupSlot,key.CtrlPressed,key.ShiftPressed);
            if (key.Keycode == Key.Backspace) CancelTraining(-1);
            if (key.Keycode == Key.B && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed && !_buildMenu) OpenBuildMenu(true);
            if (key.Keycode == Key.F4) ToggleDebugMenu();
            if (key.Keycode == Key.F2 && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) ToggleWalkRun();
            if (key.Keycode == Key.F10) _hud.Visible = !_hud.Visible;
        }
    }

    private bool LivingFriendly(int id) => _world != null && id >= 0 && id < _world.Units.Count && _world.Units[id].Team == PlayerTeam && _world.Units[id].Health > 0;
    private bool LivingUnit(int id) => _world != null && id >= 0 && id < _world.Units.Count && _world.Units[id].Present;

    private void UseGroup(int slot,bool assign,bool append)
    {
        if (_world == null) return;
        ClearConstructionTargeting();
        _orders.Cancel(); _dragging = false; _hud.SelectionBox = null;
        if (assign || append)
        {
            if (_selectedStructure != null) return;
            if (_selected.Count > 0 && !_selected.Any(LivingFriendly)) return;
            _controlGroups.Assign(slot,_selected,LivingFriendly,append);
            _lastGroup = -1;
            _status.Text = $"Group {(slot == 9 ? 0 : slot+1)} updated"; return;
        }
        // Workers inside a Mill or Foundry stay in their group but cannot be selected until they come out.
        var ids = _controlGroups.Recall(slot,LivingFriendly).Where(id => _world.Units[id].Present).ToArray();
        if (ids.Length == 0) { _lastGroup = -1; return; }
        _selectedStructure = null;
        _selected.Clear(); _selected.UnionWith(ids);
        ulong now = Time.GetTicksMsec();
        if (_lastGroup == slot && now-_lastGroupTime <= 350)
        {
            _focus = UnitPosition(_world.Units[ids[0]]);
            UpdateCamera(); _lastGroup = -1;
        }
        else _lastGroup = slot;
        _lastGroupTime = now;
    }

    public override void _Input(InputEvent input)
    {
        if (BlocksDesktopInput) { GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventMouseMotion motion) { _pointerPosition = motion.Position; _pointerInWindow = true; }
        else if (input is InputEventMouseButton pointer) _pointerPosition = pointer.Position;
        // GUI controls consume their own releases. End any battlefield drag
        // here as well when the pointer is released over the panel.
        if (input is not InputEventMouseButton { Pressed: false } mouse) return;
        if (mouse.ButtonIndex == MouseButton.Middle) _panning = false;
        if (mouse.ButtonIndex == MouseButton.Left && _dragging && (_commands.IsVisibleInTree()
            && _commands.GetGlobalRect().HasPoint(mouse.Position) || _heroBar.Contains(mouse.Position)))
        {
            _dragging = false; _hud.SelectionBox = null;
        }
    }

    private void SelectUnits(Vector2 start, Vector2 end, bool append,bool sameType = false)
    {
        ClearConstructionTargeting();
        _orders.Cancel();
        var rect = new Rect2(start, end - start).Abs();
        if (rect.Size.Length() < 7)
        {
            var structure = StructureAt(end);
            var unit = UnitAt(end,structure?.Distance);
            if (unit == null && structure is { } hit) { SelectStructure(hit.Id,append); return; }
            _selectedStructure = null;
            UnitSelection.Click(_selected,_world!.Units,unit,append,sameType);
            return;
        }
        int? previousStructure = _selectedStructure;
        _selectedStructure = null;
        if (!append || !_selected.All(LivingFriendly)) _selected.Clear();
        foreach (var unit in _world!.Units.Where(u => (u.Team == PlayerTeam || _observing) && u.Present))
        {
            var pos = UnitPosition(unit) + Vector3.Up;
            if (_camera.IsPositionBehind(pos)) continue;
            var screen = _camera.UnprojectPosition(pos);
            if (rect.HasPoint(screen)) _selected.Add(unit.Id);
        }
        // Units in the box (or retained by Shift) take priority. Structures
        // remain a separate inspection selection when no units are selected.
        if (_selected.Count > 0) return;
        if (StructureInBox(rect) is int structureId) SelectStructure(structureId);
        else if (append) _selectedStructure = previousStructure;
    }

    private void Order(Vector2 mouse, bool groundOnly = false,bool queued = false)
    {
        if (_orders.Pending == OrderKind.UseAbility) { TargetAbility(mouse); return; }
        if (!_world!.Units.Any(u => u.Team == PlayerTeam && u.Health > 0 && _selected.Contains(u.Id))) return;
        var unit = groundOnly ? null : UnitAt(mouse);
        if (!groundOnly && unit == null && TrySiege(mouse,queued)) return;
        if (!groundOnly && unit == null && TryGather(mouse,queued)) return;
        if (unit != null)
        {
            if (_orders.Pending == OrderKind.Guard && unit.Team < 0) { _status.Text = "Guard requires a friendly unit or location"; return; }
            bool enemy = unit.Team >= 0 && unit.Team != PlayerTeam;
            if (_orders.Resolve(unit.Node,enemy ? unit.Id : -1,out var targeted,enemy ? -1 : unit.Id,queued)) SendOrder(targeted,queued);
            else _status.Text = "Guard requires a friendly unit or location";
            return;
        }
        Vector3 origin = _camera.ProjectRayOrigin(mouse), direction = _camera.ProjectRayNormal(mouse);
        var hit = _picker.Pick(new(-origin.X, origin.Y, origin.Z), new(-direction.X, direction.Y, direction.Z), groundOnly);
        if (hit == null) { _status.Text = "Choose a point on the battlefield"; return; }
        int goal = _world.Navigation.Nearest(hit.Node);
        if (goal >= 0)
        {
            if (!_orders.Resolve(goal,-1,out var order,keepTargeting: queued)) { _status.Text = "Attack requires an enemy target"; return; }
            SendOrder(order,queued);
            _hud.Destination = _camera.UnprojectPosition(new Vector3(-hit.Position.X, hit.Position.Y, hit.Position.Z));
            _hud.DestinationTime = 0.7f;
        }
        else _status.Text = "No walkable space at this point";
    }

    private void ChooseCommand(OrderKind kind)
    {
        if (_world == null || !_world.Units.Any(u => u.Team == PlayerTeam && u.Health > 0 && _selected.Contains(u.Id))) return;
        _dragging = false; _hud.SelectionBox = null;
        ClearConstructionTargeting(); _buildMenu = false;
        if (_orders.Begin(kind)) _status.Text = CommandPanel.OrderName(kind)+": choose a target";
        else SendOrder(new(kind));
    }

    private SimUnit? UnitAt(Vector2 mouse,float? limit = null)
    {
        var origin = _camera.ProjectRayOrigin(mouse); var direction = _camera.ProjectRayNormal(mouse);
        var rayOrigin = new System.Numerics.Vector3(-origin.X,origin.Y,origin.Z);
        var rayDirection = new System.Numerics.Vector3(-direction.X,direction.Y,direction.Z);
        var terrain = _picker.Pick(rayOrigin,rayDirection);
        return UnitPicker.Pick(_world!.Units,rayOrigin,rayDirection,Math.Min(limit ?? float.PositiveInfinity,terrain?.Distance+.03f ?? float.PositiveInfinity));
    }

    private void CancelTargeting()
    {
        ClearConstructionTargeting();
        _orders.Cancel(); _status.Text = "Command targeting cancelled";
    }

    private void ToggleWalkRun()
    {
        if (_world == null || _observing) return;
        _world.Enqueue(PlayerTeam,[],OrderKind.ToggleWalkRun);
        _status.Text = _paused ? "Global Walk / Run queued (paused)" : "Global Walk / Run changed";
    }

    private void SendOrder(PlayerOrder order,bool queued = false)
    {
        var living = _world!.Units.Where(u => u.Team == PlayerTeam && u.Health > 0 && _selected.Contains(u.Id)).Select(u => u.Id).ToArray();
        if (living.Length == 0) return;
        _world.Enqueue(PlayerTeam,living,order.Kind,order.Cell,order.Target,queued);
        _status.Text = CommandPanel.OrderName(order.Kind)+(_paused ? " queued (paused)" : " order issued");
    }

    /// <summary>A right-click on the minimap orders the selection to that point, as the original manual
    /// describes. Ctrl marches there instead, and Shift queues, matching a right-click on the battlefield.</summary>
    private void OrderFromMinimap(Vector2 point,bool queued,bool march)
    {
        if (_world == null || _loadFailed) return;
        ClearConstructionTargeting();
        if (!_world.Units.Any(u => u.Team == PlayerTeam && u.Health > 0 && _selected.Contains(u.Id)))
        { _status.Text = "Select your forces first"; return; }
        var nav = _world.Navigation;
        int x = Math.Clamp((int)((1-point.X)*nav.Width*nav.CellSize),0,nav.Width*nav.CellSize-1);
        int z = Math.Clamp((int)(point.Y*nav.Height*nav.CellSize),0,nav.Height*nav.CellSize-1);
        int goal = nav.Nearest(nav.Cell(x,z));
        if (goal < 0) { _status.Text = "No walkable space at this point"; return; }
        if (march) _orders.Begin(OrderKind.AttackMove);
        if (!_orders.Resolve(goal,-1,out var order,keepTargeting: queued)) { _status.Text = "Attack requires an enemy target"; return; }
        SendOrder(order,queued);
    }

    private void ToggleObstacle(Vector2 mouse)
    {
        int selected = PickObstacle(mouse);
        if (_world!.ConstructionSites.Values.Any(s => s.Obstacle == selected))
        { _status.Text = "Resource building footprints follow construction"; return; }
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
        if (_world is null || _loadFailed || (_paused && !_singleStepRequested)) return;
        _presentStep = _paused;
        _singleStepRequested = false;
        // Game speed: several fixed ticks per physics frame. A single step while paused stays a single step,
        // and the result panel pausing the game ends the burst. So does the frame budget: when ticks are too
        // slow for the chosen speed the game runs slower, not the frame rate, and every tick stays the same.
        using var physicsTime = SimProfiler.Time("godot physics frame");
        long burstStart = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int burst = _presentStep ? 1 : _speed, step = 0; step < burst && (step == 0 || !_paused
            && System.Diagnostics.Stopwatch.GetElapsedTime(burstStart).TotalMilliseconds < SpeedBudgetMs); step++)
        {
            long stepStart=_engagementSmoke?System.Diagnostics.Stopwatch.GetTimestamp():0;
            _world.Step();
            while (_views.Count < _world.Units.Count) AddUnitView(_world.Units[_views.Count]);
            if (_skirmish) ApplySkirmishHud(_commands);
            if (_engagementSmoke) RecordEngagementStep(System.Diagnostics.Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds);
            PresentCombatEvents();
            PresentAbilityEvents();
            PresentFoundationFailures();
            PresentOutcome();
        }
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
            if (view.Buried) continue;
            if (unit.Casting && unit.OrderTarget >= 0)
            {
                var target = _world.Units[unit.OrderTarget];
                view.DesiredYaw = Mathf.Atan2(-(target.X-unit.X),target.Z-unit.Z);
            }
            else if (unit.Definition.IsRanged) view.DesiredYaw = (float)UnitFacing.Sample(unit.Facing,unit.Facing,1);
            else if (unit.Attacking && unit.Target >= 0)
            {
                var target = _world.Units[unit.Target];
                view.Heading.Reset();
                view.DesiredYaw = Mathf.Atan2(-(target.X-unit.X),target.Z-unit.Z);
            }
            else if (_world.IsBesieging(unit))
            {
                // Ranged besiegers already turn in the simulation; a melee one faces the building here.
                var (siteX,siteZ) = _world.SitePosition(_world.ConstructionSites[SimWorld.SiegeTarget(unit)]);
                view.Heading.Reset();
                view.DesiredYaw = Mathf.Atan2(-(siteX-unit.X),siteZ-unit.Z);
            }
            else if (WorkFacing(unit) is float facing) view.DesiredYaw = facing;
            else if (unit.Movement.Count >= 2)
            {
                var previous = unit.Movement[0]; var next = unit.Movement[^1];
                int dx = next.X-previous.X, dz = next.Z-previous.Z;
                if (view.Heading.Update(dx,dz,unit.MovementBudget,unit.OrderSequence) is float heading) view.DesiredYaw=heading;
            }
            string state = unit.Health <= 0 ? "death" : unit.Casting ? "cast" : unit.StrikeActive ? "attack" : unit.Moving ? (unit.Walking ? "walk" : "run") : unit.Target >= 0 ? "ready" : "idle";
            if (unit.Moving) _movingSeen++;
            if (unit.Moving && unit.FreeMovement) _freeMovementSeen++;
            if (unit.Attacking) _attackingSeen++;
            if (unit.Attacking && unit.Team == PlayerTeam)
                for (int squad = 0; squad < _friendlySquads.Count; squad++)
                    if (_friendlySquads[squad].Contains(unit.Id)) _squadsFought.Add(squad);
            string clip = state == "cast" ? unit.Ability(unit.AbilitySlot)!.CastClip : state == "attack" ? unit.Definition.Attacks[unit.AttackVariant].Clip
                : state is "walk" or "run" ? (unit.Walking ? unit.Definition.WalkClip : unit.Definition.RunClip)
                : state == "ready" ? unit.Definition.ReadyClip : view.Model.GetProperty(state).GetString() ?? "";
            if ((state is "walk" or "run") && clip == "") clip = view.Model.GetProperty("walk").GetString() ?? "";
            var gather = WorkerAnimation(unit);
            if (gather != null) clip = gather.Value.Clip;
            bool timed = gather is { Ticks: > 0 };
            bool restartAttack = unit.StrikeActive && view.AttackSerial != unit.AttackSerial;
            bool restartCast = unit.Casting && view.CastSerial != unit.CastSerial;
            if (view.Player != null && clip != "" && (clip != view.Clip || restartAttack || restartCast))
            {
                string? animation = view.Player.GetAnimationList().FirstOrDefault(a => a == clip || a.EndsWith('/' + clip));
                if (animation != null)
                {
                    var resource = view.Player.GetAnimation(animation);
                    resource.LoopMode = state is "death" or "attack" or "cast" || timed ? Animation.LoopModeEnum.None : Animation.LoopModeEnum.Linear;
                    if (restartAttack || restartCast) view.Player.Stop();
                    view.Player.Play(animation,state is "attack" or "cast" || timed ? 0 : 0.12); view.Clip = clip;
                }
            }
            // The carrying clip is a walk; a running worker plays it faster so its steps keep pace.
            if (view.Player != null) view.Player.SpeedScale = _paused || unit.StrikeActive || unit.Casting || timed ? 0
                : gather != null && unit.Moving && !unit.Walking ? 1_000_000f/unit.Definition.WalkScale : 1;
            view.AttackSerial = unit.AttackSerial;
            view.CastSerial = unit.CastSerial;
            view.State = gather?.State;
            view.Root.Visible = unit.InsideSite < 0;
            if (!unit.StrikeActive && !unit.Casting) view.Appearance?.Apply(clip,0,view.State);
            if (unit.Health <= 0) view.Died = true;
        }
        if (_combatSmoke)
            foreach (var unit in _world.Units.Where(u => u.Health > 0))
                foreach (var other in _world.Units.Where(u => u.Health > 0 && u.Id > unit.Id))
                    _combatSpacingPassed &= !SimWorld.BodiesOverlap(unit,other);
    }

    public override void _Process(double delta)
    {
        if (_setupScreen != null) { _frames++; if (_setupSmoke) SetupSmokeInput(); return; }
        if (_world is null || _loadFailed) return;
        using var processTime = SimProfiler.Time("godot process frame");
        ReportProfile();
        RefreshObserverPanel(delta);
        float dt = (float)Math.Min(delta, 0.1);
        if (!_paused || _presentStep)
            foreach (var unit in _world.Units)
            {
                var view = _views[unit.Id];
                double fraction = _paused ? 1 : Engine.GetPhysicsInterpolationFraction();
                if (PresentCorpse(unit,view,fraction)) continue;
                if (unit.Movement.Count > 0)
                {
                    var position = MovementPlayback.Sample(unit.Movement,fraction);
                    float y = position.Terrain ? _world.Navigation.GroundHeight((int)Math.Round(position.X),(int)Math.Round(position.Z))/1000f
                        : (float)position.Y/1000f;
                    view.Root.Position = new(-(float)position.X/1000f,y,(float)position.Z/1000f);
                    if (fraction > .001 && fraction < .999 && unit.Moving && view.Root.Position.DistanceSquaredTo(UnitPosition(unit)) > .000001f)
                        _interpolatedSeen++;
                }
                float before = Mathf.Abs(Mathf.AngleDifference(view.Root.Rotation.Y,view.DesiredYaw));
                float yaw = unit.Definition.IsRanged ? (float)UnitFacing.Sample(unit.PreviousFacing,unit.Facing,fraction)
                    : Mathf.RotateToward(view.Root.Rotation.Y,view.DesiredYaw,dt*8);
                float after = Mathf.Abs(Mathf.AngleDifference(yaw,view.DesiredYaw));
                if (before > .01f && after < before && after > .001f) _turningSeen++;
                view.Root.Rotation = new(0,yaw,0);
                if (unit.Definition.IsRanged && unit.StrikeActive && _world.Tick-1 == unit.AttackStartTick)
                {
                    _rangedFacingSamples++;
                    _rangedFacingVerified &= Mathf.Abs(Mathf.AngleDifference(yaw,view.DesiredYaw))
                        <= 2*UnitFacing.AttackTolerance*Mathf.Pi/180000+.00001f;
                }
                SeekAttack(unit,view,fraction);
                SeekAbility(unit,view,fraction);
                if (!SeekGather(unit,view,fraction) && !unit.StrikeActive && !unit.Casting && view.Player != null && view.Player.CurrentAnimation.ToString().Length > 0 && view.Player.CurrentAnimationLength > 0)
                    view.Appearance?.Apply(view.Clip,view.Player.CurrentAnimationPosition/view.Player.CurrentAnimationLength,view.State);
            }
        foreach (var unit in _world.Units)
        {
            var view = _views[unit.Id];
            if (view.Buried) continue;
            PresentRing(unit,view);
            view.Health?.Refresh(unit.Present ? unit.Health : 0,unit.MaxHealth);
            view.Ring.Visible = unit.Health > 0 && _selected.Contains(unit.Id);
            if (!view.Ring.Visible || (view.RingPosition == view.Root.Position && view.RingYaw == view.Root.Rotation.Y)) continue;
            var transform = view.Root.GlobalTransform; var inverse = transform.AffineInverse();
            ScenePresentation.SelectionRing(local => {
                local *= unit.Definition.BodyRadiusMm/650f;
                var point = transform * local;
                if (unit.Node < _world.Navigation.GroundCount)
                    point.Y = _world.Navigation.GroundHeight((int)(-point.X * 1000), (int)(point.Z * 1000)) / 1000f;
                point.Y += .06f;
                return inverse * point;
            }, (ArrayMesh)view.Ring.Mesh!);
            view.RingPosition = view.Root.Position; view.RingYaw = view.Root.Rotation.Y;
        }
        PresentProjectiles();
        PresentHealingEffects();
        var edge = EdgePan();
        float right = Math.Clamp((Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0) + edge.X,-1,1);
        float forward = Math.Clamp((Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0) + edge.Y,-1,1);
        if (_constructionSmoke || _structureSmoke || _productionSmoke || _placementSmoke || _dwarfHallSmoke) right = forward = 0;
        Pan(right * dt * _distance * 0.6f, forward * dt * _distance * 0.6f);
        UpdateCamera();
        _commands.SetMinimapView(ViewFootprint());
        _selected.RemoveWhere(id => !LivingUnit(id));
        var units = _world.Units.Where(u => _selected.Contains(u.Id)).ToList();
        if (units.Count == 0) _orders.Cancel();
        if (_skirmish) SyncBuildMenu();
        _commands.Refresh(units,UnitName,_orders.Pending,_paused,
            Enumerable.Range(0,10).Select(slot => _controlGroups.Recall(slot,LivingFriendly)).ToArray(),
            _world.Units.Count(u => u.Team == PlayerTeam && u.Health > 0),_world.Units.Count(u => u.Team != PlayerTeam && u.Health > 0),_world.IsWalking(PlayerTeam),_world.Tick,_pendingAbilitySlot);
        UpdateAbilityCursor();
        _heroBar.Refresh(FriendlyHeroes(),_commands.Art,_commands.EffectiveScale);
        if (_skirmish)
        {
            if (!SelectedWorkers().Any()) ClearConstructionTargeting();
            _commands.RefreshConstruction(_world,units,_constructionTargeting,_constructionBuilding,_constructionChoices,SelectedStructureDetails());
            PresentConstruction();
            PresentFoundationGhosts();
            PresentStructureSelection();
            PresentProduction();
            UpdateConstructionPreview();
        }
        _presentStep = false;
        if (_toolsPanel.Visible) RefreshDebugMenu();
        PresentWaypoints();
        _hud.DestinationTime -= dt; _hud.QueueRedraw();
        _frames++;
        if (_commandSmoke) CommandSmokeInput();
        if (_ordersSmoke) OrdersSmokeInput();
        if (_abilitySmoke) AbilitySmokeInput();
        if (_skirmishSmoke) SkirmishSmokeInput();
        if (_constructionSmoke || _structureSmoke || _productionSmoke || _placementSmoke || _dwarfHallSmoke || _minimapSmoke || _frodoSmoke || _techSmoke || _siegeSmoke || _defenseSmoke || _victorySmoke || _aiSmoke || _observerSmoke)
        {
            _injectingConstructionInput = true;
            try { if (_minimapSmoke) MinimapSmokeInput(); else if (_siegeSmoke) SiegeSmokeInput(); else if (_defenseSmoke) DefenseSmokeInput(); else if (_victorySmoke) VictorySmokeInput(); else if (_aiSmoke) AiSmokeInput(); else if (_observerSmoke) ObserverSmokeInput(); else if (_frodoSmoke) FrodoSmokeInput(); else if (_techSmoke) TechSmokeInput(); else if (_structureSmoke) StructureSmokeInput(); else if (_productionSmoke) ProductionSmokeInput(); else if (_placementSmoke) PlacementSmokeInput(); else if (_dwarfHallSmoke) DwarfHallSmokeInput(); else ConstructionSmokeInput(); }
            finally { _injectingConstructionInput = false; }
        }
        if (_engagementSmoke) { if (_formationSmoke) FormationSmokeInput(); else EngagementSmokeInput(); }
        if (_setupSmoke) SetupSmokeInput();
        if (_obstacleSmoke) ObstacleSmokeInput();
        if (_surfaceSmoke && _frames == 5)
        {
            _selected.UnionWith(_primarySquad);
            var unit = _world.Units[0];
            _surfaceSmokePoint = _camera.UnprojectPosition(UnitPosition(unit));
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
                Position = _surfaceSmokePoint, GlobalPosition = _surfaceSmokePoint }, true);
        }
        if (_surfaceSmoke && _frames == 7)
        {
            _surfacePicked = _world.Commands.Any(c => c.Cell >= _world.Navigation.GroundCount && c.Units.Length == 6);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, CtrlPressed = true,AltPressed = true,
                Position = _surfaceSmokePoint, GlobalPosition = _surfaceSmokePoint }, true);
        }
        if (_surfaceSmoke && _frames == 9)
        {
            bool ground = _world.Commands.LastOrDefault() is { Target: -1 } command && command.Cell < _world.Navigation.GroundCount;
            GD.Print("SURFACE_PICK_SMOKE " + _surfacePicked); GD.Print("UNDERPASS_PICK_SMOKE " + ground);
            GetTree().Quit(_surfacePicked && ground ? 0 : 1);
        }
        if (_smoke) SmokeInput();
        if (_smoke && _capture != "" && _frames == (_combatSmoke ? 180 : 60)) _ = CaptureFrame(false);
        if (_smoke && _world.Tick >= 420) FinishSmoke();
        else if (!_smoke && !_commandSmoke && !_ordersSmoke && !_abilitySmoke && !_skirmishSmoke && !_constructionSmoke && !_structureSmoke && !_productionSmoke && !_placementSmoke && !_dwarfHallSmoke && !_minimapSmoke && !_frodoSmoke && !_techSmoke && !_siegeSmoke && !_defenseSmoke && !_victorySmoke && !_aiSmoke && !_observerSmoke && !_setupSmoke && !_engagementSmoke && _capture != "" && _frames == 20) _ = CaptureFrame(true);
    }

    private string UnitName(int id) => _world!.Units[id].Definition.DisplayName;

    private void Pan(float right, float forward)
    {
        _focus += new Vector3(Mathf.Cos(_yaw) * right + Mathf.Sin(_yaw) * forward, 0,
            -Mathf.Sin(_yaw) * right + Mathf.Cos(_yaw) * forward);
        ClampFocus();
    }

    private void ClampFocus()
    {
        if (_world == null) return;
        _focus.X = Math.Clamp(_focus.X, -_world.Navigation.Width * _world.Navigation.CellSize / 1000f, 0);
        _focus.Z = Math.Clamp(_focus.Z, 0, _world.Navigation.Height * _world.Navigation.CellSize / 1000f);
    }

    private void UpdateCamera()
    {
        _focus.Y = _world!.Navigation.PickHeight((int)(-_focus.X * 1000), (int)(_focus.Z * 1000)) / 1000f;
        _camera.Position = _focus + new Vector3(Mathf.Sin(_yaw) * _distance * 0.65f, _distance * 0.8f, Mathf.Cos(_yaw) * _distance * 0.65f);
        _camera.LookAt(_focus);
        // Keep the encounter above the command panel instead of framing units
        // in the part of the viewport reserved for controls.
        _camera.VOffset = _hud.Visible ? -_distance*0.09f : 0;
    }

    private void SetCameraView(string view)
    {
        if (_world == null) return;
        _view = view;
        switch (view)
        {
            case "squad":
                _focus = _squadFocus; _distance = _squadDistance; _yaw = .25f; break;
            case "fortress":
                _focus = _fortressFocus; _distance = 120; _yaw = -.45f; break;
            case "overview":
                _focus = new Vector3(-_world.Navigation.Width * _world.Navigation.CellSize / 2000f, 0,
                    _world.Navigation.Height * _world.Navigation.CellSize / 2000f);
                _distance = 340; _yaw = -.5f; break;
            default: throw new InvalidOperationException("Unknown camera view: " + view);
        }
        UpdateCamera();
        _status.Text = "Camera centered on "+(view == "squad" ? "all squads" : view == "fortress" ? "the fortress" : "the whole map");
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
        var friendly = _world!.Units.Where(u => _primarySquad.Contains(u.Id)).Select(u => _camera.UnprojectPosition(UnitPosition(u) + Vector3.Up)).ToArray();
        Vector2 min = new(friendly.Min(p => p.X) - 8, friendly.Min(p => p.Y) - 8);
        Vector2 max = new(friendly.Max(p => p.X) + 8, friendly.Max(p => p.Y) + 8);
        if (_frames == 5)
        {
            _cameraBeforeInput = _camera.Position;
            // Keep this battlefield zoom event outside the command panel.
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(1000, 400), GlobalPosition = new Vector2(1000, 400) }, true);
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
            _inputSelectionPassed = _selected.Count == 6 && _views.Values.Count(v => v.Ring.Visible) == 6;
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
        if (_combatSmoke && _frames is 110 or 120)
        {
            int squad = _frames == 110 ? 1 : 2;
            GetViewport().PushInput(new InputEventKey { Keycode = squad == 1 ? Key.Key2 : Key.Key3, Pressed = true },true);
            _squadKeysPassed &= _selected.SetEquals(_friendlySquads[squad].Where(id => _world.Units[id].Health > 0));
            var leader = _world.Units[_friendlySquads[squad][0]];
            var enemy = _world.Units.Where(u => u.Team == 1 && u.Health > 0)
                .OrderBy(u => SimWorld.SeparationSquared(leader,u)).ThenBy(u => u.Id).First();
            var screen = _camera.UnprojectPosition(UnitPosition(enemy)+Vector3.Up);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right,Pressed = true,
                Position = screen,GlobalPosition = screen },true);
        }
        if (_frames == 139)
        {
            var focus = _focus; var yaw = _yaw; var distance = _distance; var view = _view;
            _cameraShortcutsRemoved = true;
            foreach (var key in new[] { Key.F1,Key.F2,Key.F3,Key.Q,Key.E })
            {
                PushKey(key);
                _cameraShortcutsRemoved &= _focus == focus && _yaw == yaw && _distance == distance && _view == view;
                if (key == Key.F2) PushKey(key); // Restore pace after verifying the former camera binding.
            }
            ToggleDebugMenu();
        }
        if (_frames is 140 or 144 or 148)
            Click(_cameraButtons[_frames == 140 ? "fortress" : _frames == 144 ? "overview" : "squad"].GetGlobalRect().GetCenter());
        if (_frames == 142) _fortressViewPassed = _view == "fortress" && _focus.DistanceTo(_fortressFocus) < 1;
        if (_frames == 146) _overviewPassed = _view == "overview" && _distance == 340;
        if (_frames == 150)
        {
            _squadViewPassed = _view == "squad" && _focus.DistanceTo(_squadFocus) < 1;
            Click(_cameraButtons["rotate-left"].GetGlobalRect().GetCenter());
        }
        if (_frames == 152) _inputCameraPassed &= Mathf.IsEqualApprox(_yaw,0);
        if (_frames == 154) Click(_cameraButtons["rotate-right"].GetGlobalRect().GetCenter());
        if (_frames == 156)
        {
            _inputCameraPassed &= Mathf.IsEqualApprox(_yaw,.25f);
            _toolsPanel.Visible = false;
        }
    }

    private void FinishSmoke()
    {
        if (_finishing) return;
        _finishing = true;
        if (_meleeSmoke) System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"body-combat-state.json"),JsonSerializer.Serialize(
            _world!.Units.Select(u => new {u.Id,u.Team,u.X,u.Y,u.Z,u.Health,u.Target,u.Goal,u.Node,u.TransitNode,u.BlockedTicks,
                u.AttackNode,u.CombatWaiting,u.Attacking,u.FreeMovement,u.Waypoint,Path=u.Path,Radius=u.Definition.BodyRadiusMm}),
            new JsonSerializerOptions { WriteIndented = true }));
        var report = new { ticks = _world!.Tick, units = _world.Units.Count, moving_samples = _movingSeen,
            free_movement_samples = _freeMovementSeen,
            attack_samples = _attackingSeen, damage = _world.Units.Sum(u => u.MaxHealth-u.Health),
            strike_starts = _strikeStarts,hit_events = _strikeHits,cancelled_strikes = _strikeCancels,
            attack_seek_samples = _attackSeekSamples, impact_samples = _impactSamples,
            equipment_changes = _views.Values.Sum(v => v.Appearance?.Changes ?? 0), rules_sha256 = _rules.Hash,
            equipment_idle_ready_verified = _equipmentStatesVerified,
            source_units = _world.Units.Select(u => u.Definition.Id).Distinct().Order().ToArray(),
            source_health = _rules.Units.Values.Select(u => u.MaxHealth).Order().ToArray(),
            arrow_releases = _arrowReleases, arrow_hits = _arrowHits, arrow_visuals = _arrowVisuals,
            arrow_launch_samples = _launchSamples, arrow_trails_created = _trailCreated, arrow_trail_samples = _trailSamples,
            ranged_release_timing = _combatTrace.Where(e => e.Kind == CombatEventKind.Release).All(e =>
                e.Tick-e.StartTick == _world.Units[e.Unit].Definition.Attacks[e.Attack].Hits[e.Hit].Tick),
            archer_melee_hits = _combatTrace.Count(e => e.Kind == CombatEventKind.Hit
                && _world.Units[e.Unit].Definition.HasMelee && !_world.Units[e.Unit].Definition.FiresProjectile(e.Attack)),
            archer_melee_clip_samples = _archerMeleeClipSamples,
            ranged_facing_samples = _rangedFacingSamples, ranged_starts_aligned = _rangedFacingVerified,
            hit_timing = _combatTrace.Where(e => e.Kind == CombatEventKind.Hit && !_world.Units[e.Unit].Definition.FiresProjectile(e.Attack)).All(e =>
                e.Tick-e.StartTick == _world.Units[e.Unit].Definition.Attacks[e.Attack].Hits[e.Hit].Tick),
            animated_units = _views.Values.Count(v => v.Player != null && v.Clip != ""),
            input_selection = _inputSelectionPassed, input_move = _inputMovePassed, input_attack = _inputAttackPassed,
            input_camera = _inputCameraPassed, input_pause = _inputPausePassed,
            input_map_views = _fortressViewPassed && _overviewPassed && _squadViewPassed,
            map_view_checks = new { squad = _squadViewPassed, fortress = _fortressViewPassed, overview = _overviewPassed },
            camera_shortcuts_removed = _cameraShortcutsRemoved,
            presentation_import = _presentation.Imports.All(i => i.MissingMipmapsAfter == 0 && i.DisabledVertexColorsAfter == 0)
                && _presentation.Imports.Single(i => i.File == "map/scene.glb").VertexColorSurfaces > 0
                && _presentation.Imports.Where(i => i.File.StartsWith("units/")).All(i => i.TeamMaterials > 0),
            turning_samples = _turningSeen, interpolated_samples = _interpolatedSeen,
            friendly_squads_fought = _squadsFought.Count, combat_spacing = _combatSpacingPassed, input_squad_keys = _squadKeysPassed,
            body_sizes = _world.Units.Select(u => new {u.Definition.Name,u.Definition.BodyRadiusMm,u.Definition.BodyHeightMm,u.Definition.ModelScalePpm,u.Definition.FriendlyCompressionPpm}).Distinct().ToArray(),
            map_loaded = true, initial_digest = _initialDigest, final_digest = _world.StateDigest() };
        string path = System.IO.Path.Combine(_cache, _meleeSmoke ? "melee-smoke-report.json" : _combatSmoke ? "combat-smoke-report.json" : _motionSmoke ? "motion-smoke-report.json" : "smoke-report.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        string traceName = _meleeSmoke ? "melee-hit-trace.json" : _motionSmoke ? "motion-hit-trace.json" : "combat-hit-trace.json";
        System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,traceName),JsonSerializer.Serialize(_combatTrace));
        bool passed = report.units == _spawnCount && report.moving_samples > 0 && report.attack_samples > 0 && report.damage > 0 && report.animated_units == _spawnCount
            && report.input_selection && report.input_move && report.input_attack && report.input_camera && report.input_pause
            && report.input_map_views && report.camera_shortcuts_removed && report.presentation_import && report.free_movement_samples > 0;
        if (_motionSmoke) passed &= report.turning_samples > 0 && report.interpolated_samples > 0;
        if (_combatSmoke) passed &= report.friendly_squads_fought == 3 && report.combat_spacing && report.input_squad_keys;
        if (_meleeSmoke) passed &= report.source_units.SequenceEqual(new[] {2,3,16,34}) && report.source_health.SequenceEqual(new[] {180,240,350,485})
            && report.arrow_releases > 0 && report.arrow_hits > 0 && report.arrow_visuals > 0 && report.ranged_release_timing
            && report.arrow_launch_samples == report.arrow_releases && report.arrow_trails_created == report.arrow_visuals && report.arrow_trail_samples > 0
            && report.archer_melee_clip_samples == 18
            && report.ranged_facing_samples > 0 && report.ranged_starts_aligned
            && report.hit_timing && report.hit_events > 0 && report.strike_starts > 0 && report.attack_seek_samples > 0
            && report.impact_samples > 0 && report.equipment_changes > 0 && report.equipment_idle_ready_verified;
        GD.Print("SANDBOX_SMOKE " + JsonSerializer.Serialize(report));
        _ = FinishCombatAudio(passed ? 0 : 1);
    }

    private async Task CaptureFrame(bool quit,string? path = null)
    {
        string destination = path ?? _capture;
        if (_captureResolution != Vector2I.Zero) { await CaptureOffscreenFrame(quit,destination); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(destination); GD.Print("CAPTURE " + destination + " " + error);
        if (error == Error.Ok)
        {
            var report = new { view = _view, focus = new[] { _focus.X, _focus.Y, _focus.Z },
                camera = new[] { _camera.Position.X, _camera.Position.Y, _camera.Position.Z },
                yaw_radians = _yaw, distance = _distance, fov_degrees = _camera.Fov,
                camera_vertical_offset = _camera.VOffset,
                width = image.GetWidth(), height = image.GetHeight(), hud_visible = _hud.Visible,
                hud_scale_percent = _commands.ScalePercent, hud_effective_scale = _commands.EffectiveScale,
                hud_bounds = new[] { _commands.GetGlobalRect().Position.X,_commands.GetGlobalRect().Position.Y,
                    _commands.GetGlobalRect().Size.X,_commands.GetGlobalRect().Size.Y },
                map_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.IO.File.ReadAllBytes(System.IO.Path.Combine(_cache, "map/scene.glb")))).ToLowerInvariant(),
                preview_lighting = PreviewLighting.Metadata,terrain_shading = _presentation.TerrainShading,
                imports = _presentation.Imports, original_game_fidelity_verified = false };
            System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(destination, ".json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (quit) GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
}
