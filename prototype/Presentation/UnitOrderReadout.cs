using System.Globalization;
using Reforged.Simulation;

namespace Reforged.Presentation;

/// <summary>A unit's order as the console shows it (build-67), a testing aid in place of the old how-to hints: the order
/// and what it is aimed at, how far that is, what the unit is doing about it this tick, the orders queued after it, and
/// for a bot's unit the job the bot gave it, so an observer sees why. Units are named with the #number their portrait
/// tooltips use.</summary>
public sealed record UnitOrderReadout(string Order,string Distance,string Activity,string Queue,string Role = "")
{
    /// <summary>The order with its distance, as one line.</summary>
    public string Headline => Distance == "" ? Order : $"{Order} · {Distance}";

    public static string Name(OrderKind kind) => kind switch { OrderKind.AttackMove => "Attack-move",OrderKind.HoldGround => "Hold Position",
        OrderKind.ForceAttack => "Force Attack",OrderKind.UseAbility => "Ability",OrderKind.PlaceBuilding => "Build",OrderKind.AttackStructure => "Attack",
        _ => kind.ToString() };

    public static string Metres(long millimetres) => millimetres < 10_000
        ? (millimetres/1000m).ToString("0.#",CultureInfo.InvariantCulture)+" m" : $"{millimetres/1000} m";

    public static UnitOrderReadout From(SimWorld world,SimUnit unit,Func<int,string> name)
    {
        bool IsUnit(int id) => id >= 0 && id < world.Units.Count;
        string Unit(int id) => IsUnit(id) ? $"{name(id)} #{id+1}" : $"unit #{id+1}";
        string Away(int x,int z) => Metres((long)Math.Sqrt(SimWorld.DistanceSquared(unit.X,unit.Z,x,z)));
        string ToUnit(int id) => IsUnit(id) ? Away(world.Units[id].X,world.Units[id].Z) : "";
        string ToCell(int cell) { if (cell < 0) return ""; var (x,z) = world.Navigation.Center(cell); return Away(x,z); }
        string Site(int id) => world.ConstructionSites.TryGetValue(id,out var site) ? world.SiteRules(site).DisplayName : $"site {id}";
        string ToSite(int id) => world.ConstructionSites.TryGetValue(id,out var site) ? Away(site.X,site.Z) : "";
        int destination = unit.OrderDestination >= 0 ? unit.OrderDestination : unit.OrderCell;
        var (order,distance) = unit.Order switch
        {
            OrderKind.Move or OrderKind.AttackMove => (Name(unit.Order),ToCell(destination)),
            OrderKind.Patrol => ("Patrol → "+(unit.PatrolForward ? "far end" : "start"),ToCell(destination)),
            OrderKind.Attack or OrderKind.ForceAttack or OrderKind.Guard when IsUnit(unit.OrderTarget)
                => ($"{Name(unit.Order)} → {Unit(unit.OrderTarget)}",ToUnit(unit.OrderTarget)),
            OrderKind.Guard => ("Guard → post",ToCell(unit.GuardOrigin)),
            OrderKind.ForceAttack => ("Force Attack → ground",ToCell(destination)),
            OrderKind.UseAbility => ($"{unit.Ability(unit.AbilitySlot)?.Name ?? "Ability"} → {Unit(unit.OrderTarget)}",ToUnit(unit.OrderTarget)),
            OrderKind.AttackStructure or OrderKind.Build => ((unit.Order == OrderKind.Build && world.ConstructionSites.GetValueOrDefault(unit.OrderTarget)?.Complete == true
                ? "Repair" : Name(unit.Order))+" → "+Site(unit.OrderTarget),ToSite(unit.OrderTarget)),
            OrderKind.PlaceBuilding when unit.OrderTarget >= 0 && unit.OrderTarget < world.PlaceableBuildings.Count
                => ($"Build → new {world.BuildingRules[world.PlaceableBuildings[unit.OrderTarget]].DisplayName}",ToCell(unit.PlacementCell)),
            OrderKind.Gather when world.Resources.TryGetValue(unit.OrderTarget,out var source)
                => ($"Gather {source.Kind.ToString().ToLowerInvariant()} → "+(world.ConstructionSites.ContainsKey(source.Id) ? Site(source.Id)
                    : source.Kind == ResourceKind.Food ? "food well" : "ore pile"),Away(source.X,source.Z)),
            _ => (Name(unit.Order),"")
        };

        var doing = new List<string>();
        if (unit.HeldOrder is { } held) doing.Add($"{Name(held.Kind)} waits for this swing");
        if (unit.Casting) doing.Add("casting "+(unit.Ability(unit.AbilitySlot)?.Name ?? "an ability"));
        int siege = SimWorld.SiegeTarget(unit);
        if (world.IsBesieging(unit)) doing.Add((unit.StrikeActive ? "striking " : "attacking ")+Site(siege));
        else if (unit.Target >= 0)
            doing.Add((unit.StrikeActive ? "striking " : unit.Attacking ? "fighting " : unit.CombatWaiting ? "waiting for room at " : unit.Moving ? "closing on " : "facing ")
                + Unit(unit.Target)+(unit.AutoEngage ? " (acquired)" : ""));
        else if (siege >= 0 && unit.Order == OrderKind.AttackMove) doing.Add("heading for "+Site(siege));
        if (unit.Moving) doing.Add((unit.Walking ? "walking" : "running")+(unit.BlockedTicks > 0
            ? ", blocked "+(unit.BlockedTicks/(decimal)SimWorld.TicksPerSecond).ToString("0.#",CultureInfo.InvariantCulture)+" s" : ""));
        if (unit.YieldFor >= 0) doing.Add("making way for "+Unit(unit.YieldFor));
        if (unit.ReturningToRest) doing.Add("returning to its spot");
        if (unit.AttackMoveReturning) doing.Add("back to the march");
        if (unit.IdleGuardReturning) doing.Add("back to its post");
        if (unit.PlacementWaitTick >= 0) doing.Add($"waiting for the spot to clear, {(world.Tick-unit.PlacementWaitTick)/SimWorld.TicksPerSecond} s");
        if (unit.GatherPhase != GatherPhase.None) doing.Add(unit.GatherPhase switch { GatherPhase.Mining => "mining",GatherPhase.PickingUp => "picking up",
            GatherPhase.Inside => unit.InsideSite >= 0 ? "inside the "+Site(unit.InsideSite) : "inside",_ => "putting down" });
        if (unit.ReturningCargo) doing.Add("returning cargo");
        if (unit.CargoMilli > 0) doing.Add((unit.CargoMilli/1000m).ToString("0.#",CultureInfo.InvariantCulture)+" "+unit.CargoKind.ToString().ToLowerInvariant()+" carried");
        string activity = doing.Count == 0 ? "Standing" : char.ToUpperInvariant(doing[0][0])+string.Join(" · ",doing)[1..];

        var queued = unit.OrderQueue.Select(o => Name(o.Kind)).ToList();
        string queue = queued.Count == 0 ? "" : "Then "+string.Join(" → ",queued.Take(4))+(queued.Count > 4 ? $" +{queued.Count-4}" : "");
        return new(order,distance,activity,queue,BotRole(world,unit,Unit,Site));
    }

    /// <summary>The bot's job for one of its units, from the groups SkirmishAi keeps: "" for a unit no bot commands.</summary>
    private static string BotRole(SimWorld world,SimUnit unit,Func<int,string> named,Func<int,string> site)
    {
        if (!world.AiPlayers.TryGetValue(unit.Team,out var ai)) return "";
        if (ai.Party.Contains(unit.Id))
            return "Bot assault"+(ai.AllIn ? " (all in)" : "")+(world.ConstructionSites.ContainsKey(ai.PartyTarget) ? " → "+site(ai.PartyTarget) : "")
                + (ai.Leg < ai.Legs.Count ? $", leg {ai.Leg+1}/{ai.Legs.Count}" : "")+(ai.Chase >= 0 ? ", chasing "+named(ai.Chase) : "");
        if (ai.Raid.Contains(unit.Id)) return "Bot raid"+(ai.RaidSite >= 0 ? " → "+site(ai.RaidSite) : " on miners");
        if (ai.PlaceSquad.Contains(unit.Id))
            return "Bot place squad"+(world.PlacesOfPower.FirstOrDefault(p => p.Id == ai.PlaceTarget) is { } place ? " → "+place.Name : "");
        foreach (var (citadel,post) in ai.Guards) if (post.Units.Contains(unit.Id)) return "Bot guard of "+site(citadel);
        if (ai.Recovering.Contains(unit.Id)) return "Bot recovering at home";
        if (world.Workers.ContainsKey(unit.Definition.Name))
            return unit.Order == OrderKind.Build && unit.OrderTarget == ai.Project ? "Bot builder" : "Bot worker";
        return "Bot home army"+(ai.Intruder >= 0 ? " vs "+named(ai.Intruder) : "");
    }
}
