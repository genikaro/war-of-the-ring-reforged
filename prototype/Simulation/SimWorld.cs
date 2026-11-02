namespace Reforged.Simulation;

public readonly record struct MovementSample(long Elapsed, int X, int Y, int Z, bool Terrain = false);

public sealed partial class SimUnit(int id, int team, int x, int z)
{
    public int Id { get; } = id;
    /// <summary>Build-137: Saruman's Twisting Influence is the one thing that changes a living unit's team.</summary>
    public int Team { get; internal set; } = team;
    private int _x = x,_z = z;
    private UnitDefinition _definition = UnitDefinition.Probe;
    /// <summary>The spatial index this unit is filed in; position and definition changes keep it current.</summary>
    internal UnitGrid? Grid;
    internal UnitRoster? Roster;
    internal bool ActiveInRoster;
    internal int DeathCleanupTick = -1;
    internal int GridBucket = -1;
    public int X { get => _x; internal set { _x = value; Grid?.Moved(this); } }
    public int Z { get => _z; internal set { _z = value; Grid?.Moved(this); } }
    public int Y { get; internal set; }
    public int Node { get; internal set; }
    private int _transitNode = -1;
    public int TransitNode { get => _transitNode; internal set { _transitNode=value; Grid?.TransitChanged(this); } }
    public long TravelProgress { get; internal set; }
    public List<MovementSample> Movement { get; } = [];
    private long _healthMilli = 100_000;
    public int Health { get => (int)((_healthMilli+999)/1000); internal set => HealthMilli = Math.Max(0,(long)value*1000); }
    public long HealthMilli
    {
        get => _healthMilli;
        internal set
        {
            bool alive = _healthMilli > 0;
            _healthMilli = Math.Max(0,value);
            if (alive != (_healthMilli > 0)) { Grid?.HealthChanged(this); Roster?.HealthChanged(this); }
        }
    }
    public UnitDefinition Definition { get => _definition; internal set { _definition = value; Grid?.Measure(value); } }
    public int MaxHealth => (int)((long)(Definition.MaxHealth+ExperienceHitPoints)*PlaceMaxHealthPpm/1_000_000*GlobalHealthPpm/1_000_000);
    public int KillCount { get; internal set; }
    /// <summary>Researched bonuses (source TechniqueUnitModifiers) added to weapon DPS and armor.</summary>
    public int TechniqueDpsMilli { get; internal set; }
    public int TechniqueArmorMilli { get; internal set; }
    /// <summary>Researched missile range multiplier (Eagle Fletching) and the change a replaced missile weapon makes to
    /// the missile DPS (Razored Edge, Flaming Axe).</summary>
    public int TechniqueRangePpm { get; internal set; } = 1_000_000;
    public int TechniqueMissileDpsMilli { get; internal set; }
    /// <summary>Attack reach: a ranged unit's missile range with its research.</summary>
    public int ReachMm => Definition.IsRanged ? (int)((long)Definition.ReachMm*TechniqueRangePpm/1_000_000)
        : Definition.ReachMm+(ParalyzeSuppressedUntil<0 && Negated==null ? ParalyzeRangeMm : 0);
    /// <summary>The strongest Siege Aura over the unit this tick (Gimli: x1.5 damage against buildings within 10 m).</summary>
    public int SiegeAuraPpm { get; internal set; } = 1_000_000;
    /// <summary>Guardian Wind: missiles hitting the unit deal this share of their damage until the tick (Legolas: 1% for 12 s).</summary>
    public int MissileWardPpm { get; internal set; } = 1_000_000;
    public int MissileWardUntil { get; internal set; } = -1;
    /// <summary>The bonuses of the places of power its team controls (build-54), recomputed when one changes hands.</summary>
    public int PlaceDpsMilli { get; internal set; }
    public int PlaceArmorMilli { get; internal set; }
    public int PlaceRegenMilli { get; internal set; }
    public int PlaceMaxHealthPpm { get; internal set; } = 1_000_000;
    public int PlaceDamagePpm { get; internal set; } = 1_000_000;
    public int PlaceMeleePpm { get; internal set; } = 1_000_000;
    public int PlaceRangedPpm { get; internal set; } = 1_000_000;
    public int PlaceSiegePpm { get; internal set; } = 1_000_000;
    public int PlaceCooldownPpm { get; internal set; } = 1_000_000;
    public int PlaceSpeedPpm { get; internal set; } = 1_000_000;
    public int PlaceSightPpm { get; internal set; } = 1_000_000;
    /// <summary>Armor a Sandbox training dummy's own buttons add or clear (build-59).</summary>
    public int BonusArmorMilli { get; internal set; }
    /// <summary>Build-135: the armour an enemy Aura of Terror (Nazgul King) takes off this unit this tick.</summary>
    public int TerrorArmorMilli { get; internal set; }
    public int ArmorMilli => Definition.ArmorMilli+ExperienceArmorMilli+TechniqueArmorMilli+PlaceArmorMilli+BonusArmorMilli+(HeroicLegacy?.ArmorMilli ?? 0)-ShieldBreakArmorMilli-TerrorArmorMilli;
    public HashSet<string> AbilityResearch { get; } = [];
    public bool AbilityEnabled(AbilityDefinition ability) => Negated == null && AbilityPurchased(ability)
        && (ability.RequiredTechnique == null || AbilityResearch.Contains(ability.RequiredTechnique))
        && (ability.ReplacedByTechnique == null || !AbilityResearch.Contains(ability.ReplacedByTechnique));
    public IEnumerable<AbilityDefinition> EnabledAbilities => Definition.Abilities is {Length: > 0} abilities
        ? EnumerateEnabledAbilities(abilities) : Array.Empty<AbilityDefinition>();
    private IEnumerable<AbilityDefinition> EnumerateEnabledAbilities(AbilityDefinition[] abilities)
    {
        foreach (var ability in abilities) if (AbilityEnabled(ability)) yield return ability;
    }
    public bool Walking { get; internal set; }
    public int[] AbilityReadyAt { get; } = new int[4];
    /// <summary>Thousandths of a tick of extra recharge gained under a recharge aura.</summary>
    public int[] AbilityRechargeCarry { get; } = new int[4];
    public bool RingActive { get; internal set; }
    public int AbilitySlot { get; internal set; } = -1;
    public int CastStartTick { get; internal set; } = -1;
    public int CastSerial { get; internal set; }
    public bool CastApplied { get; internal set; }
    public bool Casting => CastStartTick >= 0;
    public AbilityDefinition? Ability(int slot)
    {
        foreach (var ability in EnabledAbilities) if (ability.Slot == slot) return ability;
        foreach (var ability in Definition.Abilities ?? []) if (ability.Slot == slot) return ability;
        return null;
    }
    public StatusProtection Immunities
    {
        get
        {
            var flags = Definition.NativeImmunities;
            foreach (var ability in Definition.Abilities ?? [])
                if (ability.Passive && AbilityEnabled(ability)) flags |= ability.Immunities;
            return flags;
        }
    }
    public int MeleeDamageRate
    {
        get
        {
            int dps = Definition.DpsMilli;
            foreach (var ability in Definition.Abilities ?? [])
                if (ability.Passive && ability.Kind == AbilityKind.Fury && AbilityEnabled(ability)) { dps = ability.DpsMilli; break; }
            // Build-141: Gollum's Surprise Attack swaps his weapon while he sneaks.
            if (Sneaking && EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.SurpriseAttack) is { } surprise) dps = surprise.DpsMilli;
            return (int)((long)ExperienceDamage(dps+TechniqueDpsMilli+PlaceDpsMilli+(Definition.IsRanged ? TechniqueMissileDpsMilli : 0))*SavageDamagePpm/1_000_000);
        }
    }
    public int AbilityDamageScale(int targetType)
    {
        foreach (var ability in Definition.Abilities ?? [])
            if (ability.Passive && ability.Kind is AbilityKind.Fury or AbilityKind.Bane
                && ability.BonusTargets?.Contains(targetType) == true && AbilityEnabled(ability)) return ability.BonusScale;
        return 1_000_000;
    }
    public int Facing { get; internal set; }
    public int PreviousFacing { get; internal set; }
    /// <summary>The last unit aim sample, so tracking compares the previous heading with the previous aim.</summary>
    public int AttackAimTarget { get; internal set; } = -1;
    public int AttackAimDirection { get; internal set; }
    public int AttackAimTick { get; internal set; } = -1;
    /// <summary>Micrometres run per tick: the source ratio to the Gondor Swordsman, who runs the accepted 3 m/s (build-60).</summary>
    public long RunBudget => Math.Max(1,SimWorld.MovementTickSpan*Definition.RunScale/1_000_000*PlaceSpeedPpm/1_000_000);
    public long MovementBudget => Math.Max(1,(Walking ? Math.Max(1,RunBudget*Definition.WalkScale/1_000_000) : RunBudget)*TimeScalePpm/1_000_000);
    public int AttackStartTick { get; internal set; } = -1;
    public int AttackVictim { get; internal set; } = -1;
    public int AttackVariant { get; internal set; } = -1;
    public int AttackSerial { get; internal set; }
    public int NextHit { get; internal set; }
    public bool StrikeActive => AttackStartTick >= 0;
    private int _target = -1;
    public int Target { get => _target; internal set { int previous=_target; _target=value; Grid?.Retargeted(this,previous); } }
    public int Cooldown { get; internal set; }
    public int LastHostileDamageTick { get; internal set; } = -1;
    public int LastHostileDamageTeam { get; internal set; } = -1;
    public int LastHostileDamageX { get; internal set; }
    public int LastHostileDamageZ { get; internal set; }
    public bool LastHostileDamageAsset { get; internal set; }
    public List<int> Path { get; internal set; } = [];
    public int Waypoint { get; internal set; }
    public bool Moving { get; internal set; }
    public bool Attacking { get; internal set; }
    public int Goal { get; internal set; } = -1;
    public int BlockedTicks { get; internal set; }
    public int YieldFor { get; internal set; } = -1;
    public int YieldGoal { get; internal set; } = -1;
    public int OrderSequence { get; internal set; } = -1;
    public bool ReturningToRest { get; internal set; }
    public bool FreeMovement { get; internal set; }
    private bool _groundSegment;
    public bool GroundSegment { get => _groundSegment; internal set { _groundSegment=value; Grid?.TransitChanged(this); } }
    public int GroundStartX { get; internal set; }
    public int GroundStartY { get; internal set; }
    public int GroundStartZ { get; internal set; }
    public int MovementTick { get; internal set; } = -1;
    public int AvoidanceTicks { get; internal set; }
    public int AvoidanceSide { get; internal set; }
    public int AvoidanceLockTicks { get; internal set; }
    private int _attackNode = -1;
    internal int AttackBucket = -1;
    public int AttackNode { get => _attackNode; internal set { _attackNode=value; Grid?.AttackPositionChanged(this); } }
    public int CombatTarget { get; internal set; } = -1;
    public int CombatPlanTick { get; internal set; } = -1;
    public int CombatRequestTick { get; internal set; } = -1;
    public int CombatRequestRenewedTick { get; internal set; } = -1;
    public int CombatSearchTick { get; internal set; } = -1;
    public int CombatAlternativeAttempt { get; internal set; }
    public int CombatMotionTick { get; internal set; } = -1;
    public int CombatMotionX { get; internal set; }
    public int CombatMotionZ { get; internal set; }
    public long CombatMotionTravel { get; internal set; }
    public bool CombatTrafficQueued { get; internal set; }
    public int CombatFlankTarget { get; internal set; } = -1;
    public int CombatFlankUntil { get; internal set; } = -1;
    public bool CombatFlankTried { get; internal set; }
    public int CombatTrafficPauseUntil { get; internal set; } = -1;
    public bool CombatWaiting { get; internal set; }
    public bool AutoEngage { get; internal set; }
    public int IdleGuardOrigin { get; internal set; } = -1;
    public bool IdleGuardReturning { get; internal set; }
    public int LastStopTick { get; internal set; } = -1;
    public OrderKind Order { get; internal set; }
    public int OrderDestination { get; internal set; } = -1;
    public int OrderTarget { get; internal set; } = -1;
    public int AttackMoveOrigin { get; internal set; } = -1;
    public int AttackMoveForward { get; internal set; } = -1;
    public int AttackMoveRouteStart { get; internal set; } = -1;
    public bool AttackMoveReturning { get; internal set; }
    public int AttackMoveIgnoredTarget { get; internal set; } = -1;
    public int AttackMoveIgnoreUntil { get; internal set; }
    /// <summary>An enemy building acquired by attack-move or idle guarding, or -1.</summary>
    public int SiteTarget { get; internal set; } = -1;
    /// <summary>The bot party's committed building, including members temporarily waiting without an attack spot.</summary>
    public int AiSiegeObjective { get; internal set; } = -1;
    public int AttackMoveIgnoredSite { get; internal set; } = -1;
    /// <summary>Where a worker walking to lay a foundation puts it, and since when it has waited there for units to clear.</summary>
    public int PlacementCell { get; internal set; } = -1;
    public int PlacementWaitTick { get; internal set; } = -1;
    public List<PendingOrder> OrderQueue { get; } = [];
    /// <summary>An order waiting for a cast to finish or a patient bot order waiting for swing damage, and the cell the
    /// order in force was given for (its own destination may be a formation slot beside it).</summary>
    public PendingOrder? HeldOrder { get; internal set; }
    public int OrderCell { get; internal set; } = -1;
    public int PatrolStart { get; internal set; } = -1;
    public int PatrolEnd { get; internal set; } = -1;
    public bool PatrolForward { get; internal set; }
    public int GuardPlanTick { get; internal set; } = -1;
    /// <summary>Throttles approach planning while besieging a building.</summary>
    public int SiegePlanTick { get; internal set; } = -1;
    /// <summary>The player aimed this siege deliberately, so it may hit a building of any team.</summary>
    public bool SiegeForced { get; internal set; }
    public int GuardOrigin { get; internal set; } = -1;
    public long CargoMilli { get; internal set; }
    public ResourceKind CargoKind { get; internal set; }
    public bool ReturningCargo { get; internal set; }
    /// <summary>The gather source to resume after an explicit delivery, or -1.</summary>
    public int ReturnCargoSource { get; internal set; } = -1;
    public int GatherPlanTick { get; internal set; } = -1;
    public GatherPhase GatherPhase { get; internal set; }
    public int GatherPhaseTick { get; internal set; } = -1;
    /// <summary>The Mill or Foundry site a worker is inside, or -1. Inside, the body neither collides nor can be targeted.</summary>
    public int InsideSite { get; internal set; } = -1;
    public bool Present => Health > 0 && InsideSite < 0;
}

/// <summary>An order. A patient one (build-50) waits for each unit's swing to deal its damage before it applies.</summary>
public sealed record SimCommand(int Tick, int Sequence, int Team, int[] Units, int Cell, int Target = -1,
    OrderKind Kind = OrderKind.Move, bool Queued = false,int AbilitySlot = 0,bool Patient = false,int? TargetX = null,int? TargetZ = null);
public sealed record SimObstacleCommand(int Tick, int Sequence, int Obstacle, bool Active);

/// <summary>20 Hz integer simulation. No renderer, wall clock, random state or Godot APIs.</summary>
public sealed partial class SimWorld(GridNavigation navigation,CombatRules? rules = null)
{
    public const int TicksPerSecond = 20;
    public const long MovementTickSpan = 150_000;
    public GridNavigation Navigation { get; } = navigation;
    public CombatRules Rules { get; } = rules ?? CombatRules.Probe;
    public List<SimUnit> Units { get; } = [];
    private readonly UnitRoster _roster = new();
    /// <summary>Living units and casualties completing death cleanup, in stable id order. The returned snapshot
    /// is safe to iterate while damage or recruitment changes membership; check Present before acting.</summary>
    public IReadOnlyList<SimUnit> ActiveUnits
    {
        get { if (_roster.Count != Units.Count) _roster.Rebuild(Units); return _roster.Active; }
    }
    public List<SimCommand> Commands { get; } = [];
    public List<SimObstacleCommand> ObstacleCommands { get; } = [];
    public int Tick { get; private set; }
    private int _sequence;
    private readonly Dictionary<int,bool> _walkingTeams = [];
    public bool IsWalking(int team) => _walkingTeams.GetValueOrDefault(team);
    private int _navigationRevision = navigation.Revision;
    private readonly UnitGrid _grid = new(navigation);
    // Spawn files new units; tests that edit Units directly get a full refile on the next query.
    private UnitGrid Grid { get { if (_grid.Count != Units.Count) _grid.Rebuild(Units); return _grid; } }
    /// <summary>A superset of the living units within this horizontal radius of the point.</summary>
    private IEnumerable<SimUnit> NearUnits(int x,int z,int radius) => Grid.Nearby(x,z,radius);

    public SimUnit Spawn(int team, int cell,UnitDefinition? definition = null)
    {
        definition ??= UnitDefinition.Probe; definition.Validate();
        if (!Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm).CanStand(cell)) throw new ArgumentException("Spawn cell is blocked for this body");
        var candidate = SpawnCandidate(team,cell,definition);
        if (!CanPlace(candidate)) throw new ArgumentException("Spawn cell occupied");
        RestoreHeroExperience(candidate);
        RestoreHeroAbilities(candidate);
        Units.Add(candidate);
        if (_roster.Count == Units.Count-1) _roster.Add(candidate);
        if (_grid.Count == Units.Count-1) _grid.Add(candidate);
        ApplyTechniques(candidate);
        ApplyPlaceBonuses(candidate);
        return candidate;
    }

    private SimUnit SpawnCandidate(int team,int cell,UnitDefinition definition)
    {
        var (x, z) = Navigation.Center(cell);
        return new SimUnit(Units.Count,team,x,z) { Node = cell,Y = Navigation.NodeHeight(cell),Definition = definition,Walking = IsWalking(team),
            HealthMilli = (long)definition.MaxHealth*1000*definition.StartingHealthPercent/100 };
    }

    private bool CanPlace(SimUnit candidate) => CanReserveMovement(candidate,candidate.Node) && !OccupiedNodes(-1).Contains(candidate.Node)
        && !ActiveUnits.Any(u => u.Present && BodiesOverlap(candidate,u));

    public void Enqueue(int team, IEnumerable<int> units, int cell, int target = -1)
        => Enqueue(team, units, target >= 0 ? OrderKind.Attack : OrderKind.Move, cell, target);

    public void Enqueue(int team, IEnumerable<int> units, OrderKind kind, int cell = -1, int target = -1, bool queued = false, bool patient = false)
    {
        if (kind is OrderKind.Idle || !Enum.IsDefined(kind)) throw new ArgumentException("Not a player order", nameof(kind));
        Commands.Add(new(Tick, _sequence++, team, units.Distinct().Order().ToArray(), cell, target, kind, queued, Patient: patient));
    }

    public void EnqueueObstacle(int id, bool active)
    {
        if (!Navigation.ObstacleIds.Contains(id)) throw new ArgumentException("Unknown obstacle");
        ObstacleCommands.Add(new(Tick, _sequence++, id, active));
    }

    private void ApplyObstacle(SimObstacleCommand command)
    {
        if (command.Active)
        {
            foreach (var unit in ActiveUnits.Where(u => u.Present))
            {
                if (Navigation.ObstacleCovers(command.Obstacle, unit.Node)) return;
                if (unit.Node < Navigation.GroundCount && Nav(unit).ObstacleCoversGroundBody(command.Obstacle,unit.X,unit.Z)) return;
                if (!unit.GroundSegment && unit.TransitNode >= 0 && Nav(unit).ObstacleCoversMovement(
                    command.Obstacle, unit.Node, unit.TransitNode)) return;
            }
        }
        Navigation.SetObstacle(command.Obstacle, command.Active);
    }

    private void Apply(SimCommand command, bool activating = false)
    {
        if (command.Kind == OrderKind.Build) { BeginConstruction(command,activating); return; }
        if (command.Kind == OrderKind.PlaceBuilding) { PlaceBuilding(command,activating); return; }
        if (command.Kind == OrderKind.CancelConstruction) { CancelConstruction(command); return; }
        if (command.Kind == OrderKind.Train) { BeginTraining(command); return; }
        if (command.Kind == OrderKind.CancelTraining) { CancelTraining(command); return; }
        if (command.Kind == OrderKind.Upgrade) { BeginUpgrade(command); return; }
        if (command.Kind == OrderKind.CancelUpgrade) { CancelUpgrade(command); return; }
        if (command.Kind == OrderKind.Research) { BeginResearch(command); return; }
        if (command.Kind == OrderKind.CancelResearch) { CancelResearch(command); return; }
        if (command.Kind is OrderKind.SetRallyPoint or OrderKind.SetRallyResource) { SetRallyPoint(command); return; }
        if (command.Kind == OrderKind.Gather) { BeginGather(command,activating); return; }
        if (command.Kind is OrderKind.ApproachStructure or OrderKind.ReturnCargo) { BeginStructureInteraction(command,activating); return; }
        if (command.Kind == OrderKind.AttackStructure) { BeginSiege(command,activating); return; }
        if (command.Kind == OrderKind.UseAbility) { BeginAbility(command,activating); return; }
        if (command.Kind == OrderKind.UseGlobalPower) { UseGlobalPower(command); return; }
        if (command.Kind == OrderKind.UsePlacePower) { UsePlacePower(command); return; }
        if (command.Kind == OrderKind.PurchaseAbility) { PurchaseAbility(command); return; }
        if (command.Kind == OrderKind.SetHerbsTargeting) { SetHerbsTargeting(command); return; }
        if (command.Kind == OrderKind.ToggleWalkRun)
        {
            bool walk = !IsWalking(command.Team);
            _walkingTeams[command.Team] = walk;
            foreach (var unit in ActiveUnits.Where(u => u.Team == command.Team && u.Health > 0)) unit.Walking = walk;
            return;
        }
        var reserved = new HashSet<int>();
        if (activating)
        {
            // Members of a mixed selection may finish casting on different ticks.
            // Keep destinations already assigned to the rest of that same order.
            var ids = command.Units.ToHashSet();
            reserved.UnionWith(ActiveUnits.Where(u => u.Present && u.OrderSequence == command.Sequence && !ids.Contains(u.Id))
                .Select(u => u.Goal >= 0 ? u.Goal : u.Node));
        }
        bool travels = command.Kind is OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol or OrderKind.Guard
            || (command.Kind == OrderKind.ForceAttack && command.Target < 0);
        var members = command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Present).ToArray();
        var formationNav = members.Length > 0 ? Nav(members.MaxBy(u => u.Definition.BodyRadiusMm)!) : Navigation;
        int anchor = travels ? formationNav.Nearest(command.Cell) : -1;
        if (command.Kind == OrderKind.Guard && command.Target >= 0 && command.Target < Units.Count) reserved.Add(Units[command.Target].Node);
        var ordered = command.Units.Where(id => id >= 0 && id < Units.Count).Select(id => Units[id])
            .Where(u => u.Team == command.Team && u.Present && anchor >= 0 && CanReachDestination(u,anchor)).ToArray();
        bool free = travels && ordered.All(u => PathFromCurrentPosition(u,anchor).All(FreeGround));
        var slots = new List<int>();
        foreach (var unit in ordered)
        {
            int slot = slots.Count == 0 && !reserved.Contains(anchor) ? anchor : formationNav.FormationSlot(anchor,reserved);
            if (slot < 0) break;
            slots.Add(slot); reserved.Add(slot);
        }
        free &= slots.All(FreeGround);
        var assignments = free ? AssignFormation(ordered,slots)
            : ordered.Zip(slots).ToDictionary(pair => pair.First.Id,pair => pair.Second);
        foreach (int id in command.Units)
        {
            if (id < 0 || id >= Units.Count) continue;
            var unit = Units[id];
            if (unit.Team != command.Team || !unit.Present) continue;
            // An invalid explicit attack cannot silently become a move or erase
            // a valid order (for example a friendly/dead target in a mixed selection).
            if (command.Kind == OrderKind.Attack && (command.Target < 0 || command.Target >= Units.Count
                || Units[command.Target].Team == unit.Team || !Units[command.Target].Present || !CanSeeUnit(unit.Team,Units[command.Target]))) continue;
            if (command.Kind == OrderKind.ForceAttack && command.Target >= 0 && (command.Target >= Units.Count
                || command.Target == id || !Units[command.Target].Present
                || !CanSeeUnit(unit.Team,Units[command.Target]))) continue;
            // A Ring-bearer is passive: explicit attacks are refused rather than silently becoming moves.
            if (unit.RingActive && command.Kind is OrderKind.Attack or OrderKind.ForceAttack) continue;
            if (command.Kind == OrderKind.Guard && command.Target >= 0 && (command.Target >= Units.Count
                || command.Target == id || Units[command.Target].Team != unit.Team || !Units[command.Target].Present)) continue;
            if (!activating && command.Queued && command.Kind is not (OrderKind.Stop or OrderKind.HoldGround)
                && unit.Order is not (OrderKind.Idle or OrderKind.Stop or OrderKind.HoldGround))
            {
                int queuedCell = travels ? assignments.GetValueOrDefault(id,-1) : command.Cell;
                if (!travels || queuedCell >= 0) unit.OrderQueue.Add(new(command.Kind,queuedCell,command.Target,command.Sequence));
                continue;
            }
            if (!activating) unit.OrderQueue.Clear();
            unit.GatherPhase = GatherPhase.None;
            if (unit.Order == OrderKind.ReturnCargo) unit.ReturningCargo = false;
            unit.ReturnCargoSource = -1;
            CancelAbility(unit);
            CancelStrike(unit);
            if (free && unit.TransitNode >= 0 && unit.TransitNode < Navigation.GroundCount
                && FreeGround(unit.Node) && FreeGround(unit.TransitNode)) unit.GroundSegment = true;
            if (command.Kind is OrderKind.Stop or OrderKind.HoldGround) RebaseCombatGround(unit);
            else RebaseGround(unit);
            unit.FreeMovement = free && unit.TransitNode < 0;
            unit.AvoidanceTicks = 0;
            unit.AvoidanceSide = unit.AvoidanceLockTicks = 0;
            unit.Order = command.Kind==OrderKind.Stop ? OrderKind.Idle : command.Kind; unit.OrderDestination = -1; unit.OrderCell = command.Cell;
            unit.LastStopTick=command.Kind==OrderKind.Stop ? Tick : -1;
            unit.IdleGuardOrigin=-1; unit.IdleGuardReturning=false;
            unit.AttackMoveOrigin=unit.AttackMoveForward=unit.AttackMoveRouteStart=-1; unit.AttackMoveReturning=false;
            unit.AttackMoveIgnoredTarget=-1; unit.AttackMoveIgnoreUntil=0;
            unit.SiteTarget=unit.AttackMoveIgnoredSite=unit.PlacementCell=unit.PlacementWaitTick=-1;
            unit.OrderTarget = command.Kind is OrderKind.Attack or OrderKind.ForceAttack or OrderKind.Guard ? command.Target : -1;
            unit.Target = command.Kind == OrderKind.Guard ? -1 : unit.OrderTarget;
            unit.PatrolStart = command.Kind == OrderKind.Patrol ? unit.Node : -1;
            unit.PatrolEnd = -1; unit.PatrolForward = true; unit.GuardPlanTick = -1;
            unit.GuardOrigin = command.Kind == OrderKind.Guard ? anchor : -1;
            unit.AutoEngage = unit.Target >= 0;
            unit.AttackNode = unit.CombatTarget = unit.CombatPlanTick = unit.CombatRequestTick = unit.CombatRequestRenewedTick = -1; unit.CombatWaiting = false;
            unit.CombatAlternativeAttempt=0;
            ResetCombatTraffic(unit);
            unit.CombatFlankTarget=unit.CombatFlankUntil=-1;
            unit.CombatFlankTried=false;
            unit.Path.Clear(); unit.Goal = -1; unit.BlockedTicks = 0;
            unit.Waypoint = 0; unit.Moving = unit.Attacking = false;
            unit.OrderSequence = command.Sequence;
            unit.ReturningToRest = false;
            unit.YieldFor = unit.YieldGoal = -1;
            foreach (var yielding in ActiveUnits.Where(u => u.YieldFor == id))
            {
                yielding.YieldFor = yielding.YieldGoal = -1;
                yielding.Path.Clear(); yielding.Waypoint = 0; yielding.BlockedTicks = 10;
            }
            if (!travels) continue;
            if (anchor < 0 || !CanReachDestination(unit,anchor)) { CompleteTravelOrder(unit); continue; }
            int goal = assignments.GetValueOrDefault(id,-1);
            if (goal < 0) { CompleteTravelOrder(unit); continue; }
            reserved.Add(goal);
            unit.Path = PathFromCurrentPosition(unit,goal);
            unit.FreeMovement &= unit.Path.All(FreeGround);
            unit.Goal = unit.Path.Count > 0 ? goal : -1;
            unit.OrderDestination = unit.Goal;
            if (command.Kind==OrderKind.AttackMove && unit.FreeMovement) unit.AttackMoveRouteStart=unit.Node;
            if (command.Kind == OrderKind.Patrol) unit.PatrolEnd = unit.Goal;
            if (unit.Goal < 0) CompleteTravelOrder(unit);
            unit.Waypoint = 0;
        }
    }

    private void RequestYield(SimUnit waiting)
    {
        if (waiting.Target >= 0) return;
        // A yielding unit can be blocked on its way to the refuge; it asks
        // for room on that route, never from a unit it is yielding for.
        int destination = waiting.YieldGoal >= 0 ? waiting.YieldGoal : waiting.Goal;
        var priorities = new HashSet<int>();
        for (int id = waiting.YieldFor; id >= 0 && priorities.Add(id); id = Units[id].YieldFor) { }
        var route = RouteCells(waiting,waiting.Node, destination);
        // A squad's early arrivals can seal its remaining formation slots.
        // Exchange slots only within the same explicit order, and only when
        // the parked unit has a clear route to the unfinished slot.
        foreach (var parked in ActiveUnits.Where(u => waiting.YieldFor < 0 && u.Id != waiting.Id && u.Team == waiting.Team && u.Present
            && u.Goal < 0 && u.Target < 0 && u.SiteTarget < 0 && u.YieldFor < 0 && u.OrderSequence == waiting.OrderSequence
            && u.OrderSequence >= 0 && route.Contains(u.Node) && !ActiveUnits.Any(o => o.Goal == u.Node)))
        {
            var occupied = OccupiedNodes(parked.Id);
            var onward = Nav(parked).FindPath(parked.Node, waiting.Goal, occupied);
            if (onward.Count == 0) continue;
            parked.Goal = waiting.Goal; waiting.Goal = parked.Node;
            RebaseGround(parked); RebaseGround(waiting);
            parked.FreeMovement = waiting.FreeMovement = false;
            parked.Path = onward; parked.Waypoint = 0; parked.BlockedTicks = 0;
            waiting.Path = Nav(waiting).FindPath(waiting.Node, waiting.Goal); waiting.Waypoint = 0;
            return;
        }
        // A unit heading the same way usually clears on its own. At a
        // junction, routes can still meet head-on although goals do not.
        bool Blocking(SimUnit u)
        {
            if (u.YieldFor >= 0 || u.Goal < 0) return true;
            int resume = u.Goal;
            var (wx, wz) = Navigation.Center(destination); var (ox, oz) = Navigation.Center(resume);
            if ((long)(wx - waiting.X) * (ox - u.X) + (long)(wz - waiting.Z) * (oz - u.Z) < 0) return true;
            var ahead = Nav(u).FindPath(u.Node, resume);
            return ahead.Contains(waiting.Node) || (!waiting.GroundSegment && ahead.Contains(waiting.TransitNode));
        }
        bool Eligible(SimUnit u) => u.Id != waiting.Id && u.Team == waiting.Team && !priorities.Contains(u.Id)
            && u.Order is not (OrderKind.Stop or OrderKind.HoldGround)
            && u.Present && u.Target < 0 && (u.YieldFor < 0 || u.YieldFor == waiting.Id);
        // A refuge must let its unit return to its goal without crossing the destination. A start that a failed full
        // search already reached cannot return either, so it is skipped without searching again: the refuge chosen is
        // the same, and a unit whose way back is cut off no longer floods its whole region once for every cell of it.
        // On MP04_River_Raid that stalled a worker's tick for minutes at 11:07 (build-57).
        var failedReturns = new List<(GridNavigation Nav,int Resume,int Flood)>();
        bool CanReturn(SimUnit mover,int from,int resume)
        {
            var nav = Nav(mover);
            if (failedReturns.Any(f => f.Nav == nav && f.Resume == resume && nav.FloodReached(f.Flood,from))) return false;
            if (nav.FindPath(from, resume, new HashSet<int> { destination }).Count > 0) return true;
            if (nav.LastFullFlood != 0) failedReturns.Add((nav,resume,nav.LastFullFlood));
            return false;
        }
        var occupants = new Dictionary<int, SimUnit>();
        foreach (var u in ActiveUnits.Where(u => u.Present))
        {
            occupants.TryAdd(u.Node, u);
            if (u.TransitNode >= 0 && !u.GroundSegment) occupants.TryAdd(u.TransitNode, u);
        }
        foreach (var other in ActiveUnits.Where(Eligible))
        {
            // Assigned refuges stay stable while reachable. One that units
            // now block (an inherited or since occupied refuge) is replaced.
            if (other.YieldFor >= 0 && (other.TransitNode >= 0 || (other.Node != other.YieldGoal
                && Nav(other).FindPath(other.Node, other.YieldGoal, OccupiedNodes(other.Id)).Count > 0))) continue;
            int cell = other.Node;
            if (!route.Contains(cell) || !Blocking(other)) continue;
            var reserved = ActiveUnits.Where(u => u.YieldGoal >= 0).Select(u => u.YieldGoal)
                .Concat(ActiveUnits.Where(u => u.Goal >= 0).Select(u => u.Goal)).ToHashSet();
            // The nearest refuge may lie past other standing friendly units
            // that could also give way. The one closest to it moves first; the
            // chain unwinds on later requests.
            var queue = new Queue<(int Node, SimUnit Mover)>(); queue.Enqueue((cell, other));
            var seen = new HashSet<int> { cell };
            while (queue.TryDequeue(out var item))
            {
                var (candidate, mover) = item;
                int resume = mover.Goal >= 0 ? mover.Goal : mover.Node;
                // The priority unit will park at its goal. Reject refuges that
                // would trap an ordered unit behind that final position; an
                // idle unit settles in the refuge instead.
                bool free = !occupants.ContainsKey(candidate) && !route.Contains(candidate) && !reserved.Contains(candidate);
                bool canReturn = free && CanReturn(mover,candidate,resume);
                if (free && (canReturn || mover.Goal < 0 || mover.ReturningToRest))
                {
                    bool giveUpRest = !canReturn;
                    var occupied = OccupiedNodes(mover.Id);
                    // Fill a single-entry refuge from the back, keeping its
                    // mouth available for the rest of the oncoming squad.
                    var pockets = new Queue<(int Node,int Depth)>(); pockets.Enqueue((candidate,0));
                    var pocketSeen = new HashSet<int> { candidate };
                    int refuge = candidate, bestDepth = 0;
                    while (pockets.TryDequeue(out var pocket))
                    {
                        if (pocket.Depth > bestDepth || (pocket.Depth == bestDepth && pocket.Node < refuge))
                        { refuge = pocket.Node; bestDepth = pocket.Depth; }
                        if (pocket.Depth >= 8) continue;
                        foreach (int next in Nav(mover).Neighbors(pocket.Node))
                            if (Nav(mover).CanStand(next) && !route.Contains(next) && !occupied.Contains(next)
                                && !reserved.Contains(next) && pocketSeen.Add(next)) pockets.Enqueue((next,pocket.Depth+1));
                    }
                    mover.ReturningToRest |= mover.Goal < 0;
                    mover.Goal = giveUpRest ? refuge : resume;
                    // Keep existing refuges held for the new priority unit,
                    // instead of letting a dependent pin this blocker in place.
                    foreach (var dependent in ActiveUnits.Where(u => u.YieldFor == mover.Id)) dependent.YieldFor = waiting.Id;
                    mover.YieldFor = waiting.Id; mover.YieldGoal = refuge;
                    RebaseGround(mover); mover.FreeMovement = false;
                    mover.Path = Nav(mover).FindPath(mover.Node, refuge, occupied);
                    mover.Waypoint = 0; mover.BlockedTicks = 0;
                    return;
                }
                foreach (int next in Nav(mover).Neighbors(candidate))
                {
                    if (!Nav(mover).CanStand(next) || !seen.Add(next)) continue;
                    if (!occupants.TryGetValue(next, out var unit)) queue.Enqueue((next, mover));
                    else if (unit.TransitNode < 0 && Eligible(unit) && Blocking(unit)) queue.Enqueue((next, unit));
                }
            }
        }
    }

    // A held refuge is released once the priority unit's remaining route no
    // longer crosses this unit's way back. Checked every half second.
    private bool Passed(SimUnit unit, SimUnit priority)
    {
        if (unit.TransitNode >= 0 || unit.Node != unit.YieldGoal || priority.YieldFor >= 0 || (Tick + unit.Id) % 10 != 0) return false;
        var ahead = RouteCells(priority,priority.TransitNode >= 0 && !priority.GroundSegment ? priority.TransitNode : priority.Node, priority.Goal);
        ahead.Add(priority.Node);
        return !RouteCells(unit,unit.Node, unit.Goal).Overlaps(ahead);
    }

    private HashSet<int> RouteCells(SimUnit unit,int from, int to)
    {
        var path = Nav(unit).FindPath(from, to);
        var cells = path.ToHashSet();
        foreach (var edge in path.Zip(path.Skip(1))) cells.UnionWith(Nav(unit).MovementNodes(edge.First, edge.Second));
        return cells;
    }

    // Arrival slots belong to the squad order. Uncrossing moving pairs and
    // advancing early arrivals keeps a dense perimeter from sealing its center.
    private bool AdvanceWideFormation(SimUnit waiting)
    {
        if (waiting.Order is not (OrderKind.Move or OrderKind.AttackMove) || waiting.Target >= 0 || waiting.SiteTarget >= 0 || waiting.AttackMoveReturning || waiting.Goal < 0 || waiting.YieldFor >= 0) return false;
        var (gx,gz) = Navigation.Center(waiting.Goal);
        long remaining = DistanceSquared(waiting.X,waiting.Z,gx,gz);
        foreach (var other in ActiveUnits.Where(u => u.Id != waiting.Id && u.Team == waiting.Team && u.Present
            && u.Goal >= 0 && u.Target < 0 && u.SiteTarget < 0 && !u.AttackMoveReturning && u.Order == waiting.Order && u.FreeMovement && u.YieldFor < 0
            && u.OrderSequence == waiting.OrderSequence).OrderBy(u => u.Id))
        {
            var (ox,oz) = Navigation.Center(other.Goal);
            if (DistanceSquared(waiting.X,waiting.Z,ox,oz) >= remaining
                || DistanceSquared(other.X,other.Z,gx,gz) >= DistanceSquared(other.X,other.Z,ox,oz)) continue;
            (waiting.Goal,other.Goal) = (other.Goal,waiting.Goal);
            foreach (var member in new[] {waiting,other})
            {
                member.OrderDestination = member.Goal; RebaseGround(member);
                member.Path = Nav(member).FindPath(member.Node,member.Goal); member.Waypoint = 0;
                member.BlockedTicks = member.AvoidanceTicks = 0;
            }
            return true;
        }
        foreach (var parked in ActiveUnits.Where(u => u.Id != waiting.Id && u.Team == waiting.Team && u.Present
            && u.Goal < 0 && u.Target < 0 && u.SiteTarget < 0 && !u.AttackMoveReturning && u.OrderSequence == waiting.OrderSequence
            && u.OrderSequence >= 0 && u.TransitNode < 0).OrderBy(u => DistanceSquared(waiting.X,waiting.Z,u.X,u.Z)).ThenBy(u => u.Id))
        {
            if (!FreeGround(parked.Node) || DistanceSquared(parked.X,parked.Z,gx,gz) >= remaining
                || !VerticalOverlap(waiting.Y,waiting.Definition.BodyHeightMm,parked.Y,parked.Definition.BodyHeightMm)
                || !IntegerMotion.SegmentsNear(waiting.X,waiting.Z,gx,gz,parked.X,parked.Z,parked.X,parked.Z,BodySeparation(waiting,parked))) continue;
            var onward = Nav(parked).FindPath(parked.Node,waiting.Goal);
            if (onward.Count == 0 || !onward.All(FreeGround)) continue;
            parked.Goal = waiting.Goal; waiting.Goal = parked.Node;
            parked.Order = waiting.Order;
            if (parked.Order==OrderKind.AttackMove) parked.AttackMoveRouteStart=parked.Node;
            parked.OrderDestination = parked.Goal; waiting.OrderDestination = waiting.Goal;
            RebaseGround(parked); RebaseGround(waiting);
            parked.FreeMovement = waiting.FreeMovement = true;
            parked.Path = onward; parked.Waypoint = 0; parked.BlockedTicks = parked.AvoidanceTicks = 0;
            waiting.Path = Nav(waiting).FindPath(waiting.Node,waiting.Goal); waiting.Waypoint = 0;
            waiting.BlockedTicks = waiting.AvoidanceTicks = 0;
            return true;
        }
        return false;
    }

    /// <summary>Nodes a stuck unit's body-avoiding replan may close before it gives up and yields (see FindPath);
    /// a retry after a failed one gets the smaller budget. Measured in build-37: on Bilbo's Bluffs every successful
    /// replan closed at most 16,384 nodes and a failed one flooded its whole region (under 16,384); on MP08_Rift
    /// 98% of successes stay under 32,768, and a failure costs 12-17 ms instead of a 76 ms flood of the map.</summary>
    private const int BlockedReplanBudget = 32768,RetryReplanBudget = 8192;
    /// <summary>A local repair rejoins the route within this many nodes ahead, closing at most RepairBudget nodes.</summary>
    private const int RepairReach = 24,RepairBudget = 1024;

    private HashSet<int> OccupiedNodes(int except,IEnumerable<SimUnit>? candidates = null,bool combatSpacing = false)
    {
        using var time = SimProfiler.Time("OccupiedNodes");
        var self = except >= 0 && except < Units.Count ? Units[except] : null;
        int radius = self?.Definition.BodyRadiusMm ?? 350, height = self?.Definition.BodyHeightMm ?? 700;
        bool uniform = radius == 350;
        if (uniform) foreach (var u in Units) if (u.Definition.BodyRadiusMm != 350) { uniform = false; break; }
        // Plain loops in the order the set was always filled: callers may enumerate it.
        var nodes = new HashSet<int>();
        foreach (var other in candidates ?? ActiveUnits)
        {
            if (other.Id == except || !other.Present) continue;
            nodes.Add(other.Node);
            if (other.TransitNode >= 0 && !other.GroundSegment) nodes.Add(other.TransitNode);
            if (uniform) continue;
            // Combat routes and shortcuts must leave the same room as local
            // approaches; ordinary movement keeps its compressed passing space.
            int separation = self != null ? (combatSpacing ? PositionSpacing(self,other) : BodySeparation(self,other))
                : radius+other.Definition.BodyRadiusMm;
            Navigation.AddBodyNodes(nodes,other.X,other.Y,other.Z,other.Definition.BodyHeightMm,separation,height);
        }
        return nodes;
    }

    private bool CanReserveMovement(SimUnit unit, int to)
    {
        using var time = SimProfiler.Time("CanReserveMovement");
        var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(to);
        var heights = Navigation.MovementHeights(unit.Node,to);
        foreach (var other in ActiveUnits.Where(u => u.Id != unit.Id && u.Present))
        {
            var (cx,cz) = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.Center(other.Node) : (other.X,other.Z);
            var (dx,dz) = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.Center(other.TransitNode) : (other.X,other.Z);
            var otherHeights = other.TransitNode >= 0 && !other.GroundSegment ? Navigation.MovementHeights(other.Node,other.TransitNode) : (other.Y,other.Y);
            if (heights.Low >= otherHeights.Item2+other.Definition.BodyHeightMm || otherHeights.Item1 >= heights.High+unit.Definition.BodyHeightMm) continue;
            if (IntegerMotion.SegmentsNear(ax,az,bx,bz,cx,cz,dx,dz,BodySeparation(unit,other))) return false;
        }
        return true;
    }

    private void SmoothWaypoint(SimUnit unit)
    {
        using var time = SimProfiler.Time("SmoothWaypoint");
        if (unit.Target >= 0 || unit.Node >= Navigation.GroundCount || unit.Waypoint >= unit.Path.Count) return;
        var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(unit.Path[unit.Waypoint]);
        int firstX = bx-ax, firstZ = bz-az, best = unit.Waypoint;
        var occupied = OccupiedNodes(unit.Id);
        bool bends = false;
        for (int i = unit.Waypoint+1; i < Math.Min(unit.Path.Count,unit.Waypoint+6); i++)
        {
            int candidate = unit.Path[i];
            if (candidate >= Navigation.GroundCount) break;
            var (px,pz) = Navigation.Center(unit.Path[i-1]); var (x,z) = Navigation.Center(candidate);
            bends |= (long)firstX*(z-pz) != (long)firstZ*(x-px);
            if (bends && Nav(unit).CanTraverseGround(unit.Node,candidate,occupied) && CanReserveMovement(unit,candidate)) best = i;
        }
        unit.Waypoint = best;
    }

    public void Step()
    {
        using var stepTime = SimProfiler.Time("step");
        CombatEvents.Clear();
        AbilityEvents.Clear();
        HeroLevelEvents.Clear();
        GlobalPowerEvents.Clear();
        ExpireGlobalPowers();
        UpdatePlacePowers();
        DestroyedSites.Clear();
        FoundationFailures.Clear();
        NewlyDefeated.Clear();
        PlaceCaptures.Clear();
        PlacePowerEvents.Clear();
        ExpireHeroEffects();
        var actions = Commands.Where(c => c.Tick == Tick).Select(c => (c.Sequence, Action: (Action)(() => Dispatch(c))))
            .Concat(ObstacleCommands.Where(c => c.Tick == Tick)
                .Select(c => (c.Sequence, Action: (Action)(() => ApplyObstacle(c)))));
        foreach (var action in actions.OrderBy(a => a.Sequence))
        {
            _sequence = Math.Max(_sequence,action.Sequence+1);
            action.Action();
        }
        using (SimProfiler.Time("queued orders")) { ReleaseHeldOrders(); ActivateQueuedOrders(); }
        using (SimProfiler.Time("ai")) UpdateAi();
        using (SimProfiler.Time("construction")) { UpdateConstruction(); UpdateRepairs(); }
        using (SimProfiler.Time("defenses")) UpdateDefenses();
        using (SimProfiler.Time("production")) { UpdateUpgrades(); UpdateResearch(); UpdateProduction(); UpdateWarpostFate(); }
        UpdateHeroAbilities();
        UpdateElvenEffects();
        UpdateEvilEffects();
        UpdateHeroSkills();
        using (SimProfiler.Time("route rebase")) if (_navigationRevision != Navigation.Revision)
        {
            using var hit = SimProfiler.Time("route rebase hit");
            // Rebuild the routes a change can touch at a tick boundary. Starting from the last reached node also
            // safely brings an in-flight unit back to its cell center. Routes elsewhere stay as they are: they
            // may miss a shortcut a removed building opened, but a building going up on one side of a large map
            // no longer re-plans every moving unit on the other.
            var areas = Navigation.ChangedAreas.ToArray(); Navigation.ChangedAreas.Clear();
            foreach (var unit in ActiveUnits.Where(u => u.Present && u.Goal >= 0 && RouteMeetsChange(u,areas)))
            {
                RebaseGround(unit);
                unit.YieldFor = unit.YieldGoal = -1;
                unit.Path = PathFromCurrentPosition(unit,unit.Goal);
                unit.FreeMovement &= unit.Path.All(FreeGround);
                if (unit.Path.Count == 0) unit.Path.Add(unit.Node);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
            }
            _navigationRevision = Navigation.Revision;
        }
        foreach (var unit in ActiveUnits)
        {
            unit.PreviousFacing = unit.Facing;
            unit.Movement.Clear(); unit.Movement.Add(new(0,unit.X,unit.Y,unit.Z)); unit.MovementTick = Tick;
        }
        PrepareCombatPlanning();
        foreach (var unit in ActiveUnits)
        {
            unit.Moving = unit.Attacking = false;
            if (unit.AvoidanceLockTicks > 0) unit.AvoidanceLockTicks--;
            if (unit.Health <= 0)
            {
                if (unit.DeathCleanupTick < 0) unit.DeathCleanupTick = Tick;
                RememberHeroExperience(unit);
                RememberHeroAbilities(unit);
                unit.HerbsTargeting = false;
                unit.Dazed = null; unit.TreeformAbility = null; unit.TreeformUntil = unit.TreeformExitUntil = unit.KnockbackUntil = unit.KnockbackRecoverUntil = -1;
                EndCamouflage(unit);
                unit.CargoMilli = 0; unit.ReturningCargo = false; unit.ReturnCargoSource = -1;
                unit.GatherPhase = GatherPhase.None; unit.InsideSite = -1;
                CancelAbility(unit);
                CancelStrike(unit);
                unit.Order = OrderKind.Idle; unit.OrderDestination = unit.OrderTarget = -1;
                unit.OrderQueue.Clear(); unit.HeldOrder = null;
                unit.Target = unit.Goal = unit.AttackNode = unit.CombatTarget = unit.SiteTarget = unit.PlacementCell = -1;
                unit.AutoEngage = false; unit.Path.Clear(); continue;
            }
            if (unit.InsideSite >= 0) { UpdateGather(unit); continue; }
            if (unit.YieldFor >= 0)
            {
                var priority = Units[unit.YieldFor];
                if (!priority.Present || priority.Goal < 0 || priority.Target >= 0 || unit.Target >= 0 || Passed(unit, priority))
                {
                    unit.YieldFor = unit.YieldGoal = -1;
                    unit.Path = Nav(unit).FindPath(unit.Node, unit.Goal);
                    unit.Waypoint = 0; unit.BlockedTicks = 0;
                }
            }
            if (unit.ReturningToRest && unit.YieldFor < 0 && unit.Goal >= 0 && unit.Target < 0
                && (!Nav(unit).CanStand(unit.Goal) || ActiveUnits.Any(u => u.Id != unit.Id && u.Present
                    && (u.Goal == unit.Goal || (u.Goal < 0 && u.Node == unit.Goal)))))
            {
                // An idle blocker has no explicit destination to defend. If a
                // convoy parks in its old spot, settle in nearby free space.
                var reserved = OccupiedNodes(unit.Id);
                reserved.UnionWith(ActiveUnits.Where(u => u.Id != unit.Id && u.Present && u.Goal >= 0).Select(u => u.Goal));
                int rest = unit.TransitNode < 0 && !reserved.Contains(unit.Node) ? unit.Node
                    : Nav(unit).Nearest(unit.Goal,reserved,connectedTo: unit.Node);
                unit.Goal = rest >= 0 ? rest : unit.Node;
                unit.Path = Nav(unit).FindPath(unit.Node,unit.Goal); unit.Waypoint = 0;
            }
            if (unit.Cooldown > 0) unit.Cooldown--;
            if (UpdateNatureControl(unit)) continue;
            AutoHealHerbs(unit);
            if (UpdateAbility(unit)) continue;
            if (FollowThrough(unit)) continue;
            using (SimProfiler.Time("unit gather")) UpdateGather(unit);
            UpdateStructureInteraction(unit);
            using (SimProfiler.Time("unit construction")) ApproachConstruction(unit);
            using (SimProfiler.Time("unit orders")) UpdatePersistentOrder(unit);
            bool spent;
            using (SimProfiler.Time("unit siege")) spent = UpdateSiege(unit);
            if (spent) continue;
            using (SimProfiler.Time("unit combat")) spent = UpdateCombat(unit);
            if (spent) continue;
            using var movementTime = SimProfiler.Time("unit movement");
            if (unit.Target < 0 && unit.FreeMovement && unit.Definition.BodyRadiusMm > 350 && Tick%10 == 0)
                AdvanceWideFormation(unit);
            // Retry on a fixed cadence. Occupied cells are temporary obstacles;
            // retain the order when no detour exists so a moving blocker can clear.
            if (unit.Target < 0 && unit.FreeMovement && unit.AvoidanceTicks >= 20)
            {
                // Larger bodies may fit at their current position but not at
                // the cell center. Keep continuous body checks on wide ground.
                if (unit.Definition.BodyRadiusMm > 350)
                { AdvanceWideFormation(unit); unit.AvoidanceTicks = 0; unit.BlockedTicks = 3; }
                else { unit.FreeMovement = false; RebaseGround(unit); unit.BlockedTicks = 10; }
            }
            if (unit.Target < 0 && unit.Goal >= 0 && unit.BlockedTicks >= (unit.FreeMovement ? 3 : 10)
                && !(unit.Definition.BodyRadiusMm>350 && !unit.Definition.IsRanged && SiegeTarget(unit)>=0))
            {
                using var replanTime = SimProfiler.Time("blocked replan");
                RebaseGround(unit);
                int start = unit.Node;
                bool avoid = !(unit.FreeMovement && unit.Definition.BodyRadiusMm > 350);
                // Wide-ground followers avoid bodies continuously. Their replan does not consume an occupancy
                // map; building one here needlessly rasterized every body for every blocked unit's retry.
                var blocked = avoid ? OccupiedNodes(unit.Id) : null;
                blocked?.Remove(start);
                // A unit whose last replan already failed keeps no route to repair; it retries on a smaller budget.
                bool routed = unit.Path.Count > 0 && unit.Path[^1] == unit.Goal;
                unit.Path = (avoid && unit.YieldGoal < 0 ? RepairRoute(unit,start,blocked!) : null)
                    ?? PathFromCurrentPosition(unit,unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal,avoid ? blocked : null,
                        routed ? BlockedReplanBudget : RetryReplanBudget);
                unit.Waypoint = 0; unit.BlockedTicks = 0;
                // A blocked idle unit has no destination worth displacing
                // others for; it rests where it stands instead.
                if (unit.Path.Count == 0 && unit.ReturningToRest && unit.YieldFor < 0)
                { unit.Goal = -1; unit.ReturningToRest = false; }
                else if (unit.Path.Count == 0) RequestYield(unit);
            }
            // An unreachable replacement order can discard the old route in
            // mid-step. Finish retreating to the last center before stopping.
            if (unit.TransitNode >= 0 && !unit.GroundSegment && unit.Path.Count == 0)
            {
                unit.Path.Add(unit.Node); unit.Waypoint = 0;
            }
            long budget = unit.MovementBudget;
            while (budget > 0 && unit.Waypoint < unit.Path.Count)
            {
                int nextNode = unit.Path[unit.Waypoint];
                // A physical-position connector is the first ground leg even
                // when the remaining route uses constrained cells or a deck.
                bool connector = !unit.FreeMovement && unit.Waypoint == 0 && nextNode != unit.Node
                    && unit.Node < Navigation.GroundCount && nextNode < Navigation.GroundCount
                    && (unit.GroundSegment ? unit.TransitNode == nextNode : !Nav(unit).IsConnected(unit.Node,nextNode));
                if (connector)
                {
                    if (!MoveGround(unit,nextNode,ref budget)) break;
                    unit.Waypoint++; continue;
                }
                bool align = unit.Node < Navigation.GroundCount && unit.TransitNode < 0
                    && (unit.X,unit.Z) != Navigation.Center(unit.Node);
                if (unit.FreeMovement || unit.GroundSegment || align)
                {
                    if (!unit.GroundSegment && unit.FreeMovement) SmoothGroundRoute(unit);
                    int groundTarget = unit.FreeMovement ? unit.Path[unit.Waypoint] : unit.Node;
                    if (!MoveGround(unit,groundTarget,ref budget)) break;
                    if (unit.FreeMovement) unit.Waypoint++;
                    continue;
                }
                if (unit.TransitNode < 0 && nextNode == unit.Node) { unit.Waypoint++; continue; }
                if (unit.TransitNode < 0) { SmoothWaypoint(unit); nextNode = unit.Path[unit.Waypoint]; }
                bool returning = nextNode == unit.Node;
                int transit = unit.TransitNode >= 0 ? unit.TransitNode : nextNode;
                if (!returning && unit.TransitNode >= 0 && nextNode != transit)
                    throw new InvalidOperationException("A changed route must first return to its reserved source");
                if (unit.TransitNode < 0 && (OccupiedNodes(unit.Id).Contains(transit) || !CanReserveMovement(unit,transit))) break;
                var (ax,az) = Navigation.Center(unit.Node); var (bx,bz) = Navigation.Center(transit);
                int ay = Navigation.NodeHeight(unit.Node), by = Navigation.NodeHeight(transit);
                long length = IntegerMotion.Length(bx-ax,by-ay,bz-az);
                long distance = returning ? unit.TravelProgress : length-unit.TravelProgress;
                if (distance == 0)
                {
                    unit.Node = nextNode; unit.TransitNode = -1; unit.TravelProgress = 0;
                    unit.Y = Navigation.NodeHeight(nextNode); unit.Waypoint++; continue;
                }
                long movement = Math.Min(distance,budget);
                long progress = unit.TravelProgress+(returning ? -movement : movement);
                int nx = IntegerMotion.Interpolate(ax,bx,progress,length), nz = IntegerMotion.Interpolate(az,bz,progress,length);
                int ny = IntegerMotion.Interpolate(ay,by,progress,length);
                if (unit.Node < Navigation.GroundCount && transit < Navigation.GroundCount)
                    ny = Navigation.GroundHeight(nx, nz);
                if (!ClearCombatStep(unit,nx,ny,nz)) break;
                using (SimProfiler.Time("body overlap")) if (ActiveUnits.Any(other => other.Id != unit.Id && other.Present
                    && BodyOverlapAt(unit,nx,ny,nz,other))) break;
                unit.TransitNode = transit; unit.TravelProgress = progress;
                unit.X = nx; unit.Z = nz; unit.Y = ny; unit.Moving = true; budget -= movement;
                unit.Movement.Add(new(MovementElapsed(unit,budget),nx,ny,nz,unit.Node < Navigation.GroundCount && transit < Navigation.GroundCount));
                if (movement == distance)
                {
                    unit.Node = nextNode; unit.TransitNode = -1; unit.TravelProgress = 0;
                    unit.Y = Navigation.NodeHeight(nextNode); unit.Waypoint++;
                }
            }
            if (unit.Goal >= 0)
            {
                var (gx, gz) = Navigation.Center(unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal);
                if (unit.Node == (unit.YieldGoal >= 0 ? unit.YieldGoal : unit.Goal)
                    && unit.X == gx && unit.Z == gz)
                {
                    if (unit.YieldFor < 0)
                    {
                        unit.Goal = -1; unit.ReturningToRest = false;
                        if (unit.Target < 0) TravelArrived(unit);
                    }
                    unit.BlockedTicks = 0;
                }
                else unit.BlockedTicks = unit.Moving ? 0 : unit.BlockedTicks + 1;
            }
        }
        foreach (var unit in ActiveUnits.Where(u => u.Camouflaged && u.Moving)) EndCamouflage(unit);
        MonitorCombatTraffic();
        using (SimProfiler.Time("facing")) FaceMovement();
        using (SimProfiler.Time("projectiles")) AdvanceProjectiles();
        UpdateDwarfEffects();
        UpdateGlobalPowers();
        using (SimProfiler.Time("places of power")) UpdatePlacesOfPower();
        using (SimProfiler.Time("regeneration")) UpdateRegeneration();
        using (SimProfiler.Time("outcome")) UpdateOutcome();
        Tick++;
        foreach (var unit in ActiveUnits) unit.ShieldBreaks.RemoveAll(s => s.Until <= Tick || !unit.Present);
        _roster.RetireSettledCorpses(Tick-1);
        if (Tick%VisionTicks == 0) UpdateVision();
    }

    public static long DistanceSquared(int ax, int az, int bx, int bz)
        => (long)(ax-bx)*(ax-bx) + (long)(az-bz)*(az-bz);

    public static long SeparationSquared(SimUnit a, SimUnit b)
        => DistanceSquared(a.X, a.Z, b.X, b.Z) + (long)(a.Y-b.Y)*(a.Y-b.Y);

    public string StateDigest()
    {
        var text = Rules.Hash + ":" + Tick + ":" + Navigation.Revision + ":" + string.Join(',', Navigation.ObstacleIds.Where(Navigation.IsObstacleActive))
            + ":" + string.Join(";", Units.Select(u => $"{u.Id},{u.Team},{u.X},{u.Y},{u.Z},{u.Node},{u.TransitNode},{u.TravelProgress},{u.Health},{u.Target},{u.Cooldown},{u.Waypoint},{u.Goal},{u.BlockedTicks},{u.YieldFor},{u.YieldGoal},{u.OrderSequence},{u.ReturningToRest},{u.FreeMovement},{u.GroundSegment},{u.GroundStartX},{u.GroundStartY},{u.GroundStartZ},{u.AvoidanceTicks},{u.AttackNode},{u.CombatTarget},{u.CombatPlanTick},{u.CombatWaiting},{u.AutoEngage},{u.Order},{u.OrderDestination},{u.OrderTarget},{u.PatrolStart},{u.PatrolEnd},{u.PatrolForward},{u.GuardPlanTick},{u.GuardOrigin}:{string.Join(',', u.Path)}:{string.Join('|',u.OrderQueue.Select(o => $"{o.Kind},{o.Cell},{o.Target},{o.Sequence}"))}:{u.HeldOrder?.Kind},{u.HeldOrder?.Cell},{u.HeldOrder?.Target},{u.HeldOrder?.Sequence},{u.OrderCell}"));
        text += ":"+string.Join(';',Units.Select(u => $"{u.Definition.Id},{u.HealthMilli},{u.KillCount},{u.Walking},{u.AttackStartTick},{u.AttackVictim},{u.AttackVariant},{u.AttackSerial},{u.NextHit},{u.AvoidanceSide},{u.AvoidanceLockTicks},{u.AttackMoveOrigin},{u.AttackMoveForward},{u.AttackMoveRouteStart},{u.AttackMoveReturning},{u.AttackMoveIgnoredTarget},{u.AttackMoveIgnoreUntil},{u.IdleGuardOrigin},{u.IdleGuardReturning},{u.LastStopTick}"));
        text += ":"+string.Join(';',Units.Select(u => $"{u.Facing},{u.PreviousFacing},{u.AttackAimTarget},{u.AttackAimDirection},{u.AttackAimTick}"));
        text += ":combat-planning:"+string.Join(';',Units.Select(u => $"{u.CombatRequestTick},{u.CombatRequestRenewedTick},{u.CombatSearchTick},{u.CombatAlternativeAttempt}"));
        text += ":combat-traffic:"+string.Join(';',Units.Select(u => $"{u.CombatMotionTick},{u.CombatMotionX},{u.CombatMotionZ},{u.CombatMotionTravel},{u.CombatTrafficQueued},{u.CombatTrafficPauseUntil}"));
        text += ":combat-flank:"+string.Join(';',Units.Select(u=>$"{u.CombatFlankTarget},{u.CombatFlankUntil},{u.CombatFlankTried}"));
        text += ":hostile-damage:"+string.Join(';',Units.Select(u=>$"{u.LastHostileDamageTick},{u.LastHostileDamageTeam},{u.LastHostileDamageX},{u.LastHostileDamageZ},{u.LastHostileDamageAsset}"));
        text += ":siege-commitment:"+string.Join(',',Units.Select(u=>u.AiSiegeObjective));
        text += ":pace:"+string.Join(';',_walkingTeams.OrderBy(p => p.Key).Select(p => $"{p.Key},{p.Value}"));
        text += ":abilities:"+string.Join(';',Units.Select(u => $"{u.AbilitySlot},{u.CastStartTick},{u.CastSerial},{u.CastApplied}:{string.Join(',',u.AbilityReadyAt)}"));
        if (Units.Any(u => u.HeldOrder?.Kind == OrderKind.UseAbility || u.OrderQueue.Any(o => o.Kind == OrderKind.UseAbility)))
            text += ":pending-abilities:"+string.Join(';',Units.Select(u =>
                $"{u.HeldOrder?.AbilitySlot},{u.HeldOrder?.TargetX},{u.HeldOrder?.TargetZ}:"
                +string.Join('|',u.OrderQueue.Select(o => $"{o.AbilitySlot},{o.TargetX},{o.TargetZ}"))));
        text += ":ring:"+string.Join(';',Units.Select(u => $"{u.RingActive}:{string.Join(',',u.AbilityRechargeCarry)}"));
        text += ":rangers:"+string.Join(';',Units.Select(u => $"{u.Camouflaged}:{u.TechniqueSightPpm}"));
        text += ElvenDigest();
        text += NatureDigest();
        text += HeroSkillsDigest();
        text += EvilDigest();
        text += HeroExperienceDigest();
        text += HeroAbilityPurchasesDigest();
        if (Units.Any(u => u.MissileWardUntil >= 0)) text += ":ward:"+string.Join(';',Units.Select(u => $"{u.MissileWardPpm},{u.MissileWardUntil}"));
        if (Units.Any(u => u.BonusArmorMilli != 0)) text += ":bonus-armor:"+string.Join(';',Units.Select(u => u.BonusArmorMilli));
        if (Units.Any(u => u.TerrorArmorMilli != 0)) text += ":terror:"+string.Join(';',Units.Select(u => u.TerrorArmorMilli));
        text += EconomyDigest();
        text += FateDigest();
        text += GlobalPowersDigest();
        text += FactionDigest();
        text += ConstructionDigest();
        text += ProductionDigest();
        text += PlacementDigest();
        text += UpgradeDigest();
        text += ResearchDigest();
        text += DwarfEffectsDigest();
        text += PlacesDigest();
        text += SiegeDigest();
        text += DefenseDigest();
        text += OutcomeDigest();
        text += AiDigest();
        text += VisionDigest();
        text += EvilUpgradesDigest();
        text += ":"+_projectileSequence+":"+string.Join(';',Projectiles);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
