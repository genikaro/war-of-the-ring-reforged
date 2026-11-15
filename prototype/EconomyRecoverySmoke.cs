using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Wheel of Fate's six-bot late economy: exhausted home wells, living ore miners and one hero per side,
/// with the central well unclaimed. All construction, income and hero orders after setup use the ordinary AI.</summary>
public partial class Main
{
    private bool _economyRecoverySmoke,_recoveryFinished,_recoveryClaimed,_recoveryIncome,_recoveryHeroAttack;
    private int _recoveryStartTick,_recoveryCentral;
    private readonly Dictionary<int,(int X,int Z)> _recoveryHeroStarts = [];

    private void EconomyRecoverySmokeInput()
    {
        if (_recoveryFinished) return;
        var world = _world!;
        if (_frames == 2)
        {
            if (world.AiPlayers.Count != 6) throw new InvalidOperationException("Economy recovery smoke requires six bots");
            var bots = world.AiPlayers.Values.ToArray(); world.AiPlayers.Clear();
            var resources = _cacheFiles.Root.GetProperty("skirmish").GetProperty("resources").EnumerateArray().ToArray();
            var homeWells = resources.Where(r => r.GetProperty("kind").GetString() == "food"
                && r.GetProperty("start_team").ValueKind != JsonValueKind.Null).ToArray();
            _recoveryCentral = resources.Single(r => r.GetProperty("kind").GetString() == "food"
                && r.GetProperty("start_team").ValueKind == JsonValueKind.Null).GetProperty("id").GetInt32();
            foreach (var bot in bots)
            {
                world.Economies[bot.Team].FoodMilli = 1000000;
                world.Economies[bot.Team].OreMilli = 2000000;
                int well = homeWells.Single(r => r.GetProperty("start_team").GetInt32() == bot.Team).GetProperty("id").GetInt32();
                world.Enqueue(bot.Team,world.Units.Where(u => u.Team == bot.Team && world.Workers.ContainsKey(u.Definition.Name))
                    .Take(2).Select(u => u.Id),OrderKind.Build,target:well);
            }
            for (int tick=0;tick<2400 && homeWells.Any(r => !world.ConstructionSites[r.GetProperty("id").GetInt32()].Complete);tick++) world.Step();
            if (homeWells.Any(r => !world.ConstructionSites[r.GetProperty("id").GetInt32()].Complete))
                throw new InvalidOperationException("Could not prepare the six starting Mills");
            foreach (var well in homeWells) world.Resources[well.GetProperty("id").GetInt32()].RemainingMilli = 0;
            foreach (var worker in world.Units)
            {
                worker.CargoMilli=0; worker.ReturningCargo=false; worker.ReturnCargoSource=-1;
                world.Enqueue(worker.Team,[worker.Id],OrderKind.Stop);
            }
            world.Step(); // Leave the exhausted Mills before restoring the bots; no old food loads may count as recovery.
            foreach (var bot in bots)
            {
                world.AiPlayers.Add(bot.Team,bot);
                world.Economies[bot.Team].FoodMilli = 0; world.Economies[bot.Team].OreMilli = 2000000;
                world.Economies[bot.Team].GatheredFoodMilli = 0;
                bot.LastArmyTick = world.Tick;
                var start = world.StartStructures.Single(s => s.Team == bot.Team);
                var definition = world.Rules.Units["UNIT_G_HERO_GIMLI"];
                var nav = world.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
                SimUnit? hero = null;
                for (int i=0;i<64 && hero==null;i++)
                {
                    double angle = i*Math.Tau/16; int radius = 14000+i/16*4000;
                    int cell = nav.Nearest(nav.Cell(start.X+(int)(radius*Math.Cos(angle)),start.Z+(int)(radius*Math.Sin(angle))),
                        connectedTo:world.Units.First(u => u.Team == bot.Team).Node);
                    if (cell<0) continue;
                    try { hero = world.Spawn(bot.Team,cell,definition); }
                    catch (ArgumentException) { }
                }
                if (hero == null) throw new InvalidOperationException("No free hero spawn for team "+bot.Team);
                _recoveryHeroStarts.Add(hero.Id,(hero.X,hero.Z));
            }
            _recoveryStartTick = world.Tick;
            GD.Print("ECONOMY_RECOVERY_SETUP "+JsonSerializer.Serialize(new {world.Tick,central=_recoveryCentral,bots=bots.Select(b=>b.Personality.Id)}));
        }
        if (_frames <= 2) return;
        var central = world.ConstructionSites[_recoveryCentral];
        _recoveryClaimed |= central.Team >= 0 && central.Complete;
        _recoveryIncome |= central.Team >= 0 && world.Economies[central.Team].GatheredFoodMilli > 0;
        _recoveryHeroAttack |= _recoveryHeroStarts.Any(p => world.Units[p.Key] is {Present:true} hero
            && (hero.Order is OrderKind.AttackStructure or OrderKind.Attack || hero.Target>=0)
            && SimWorld.DistanceSquared(hero.X,hero.Z,p.Value.X,p.Value.Z) > 20000L*20000);
        bool passed = _recoveryClaimed && _recoveryIncome && _recoveryHeroAttack;
        if (!passed && world.Tick-_recoveryStartTick < 9*60*SimWorld.TicksPerSecond) return;
        var report = new {build=BuildInfo.Label,passed,ticks=world.Tick-_recoveryStartTick,
            central_food_claimed=_recoveryClaimed,food_delivered=_recoveryIncome,heroes_join_the_fight=_recoveryHeroAttack,
            teams=world.AiPlayers.Values.Select(a=>new {a.Team,a.Personality.Id,a.Status,
                food=world.Economies[a.Team].Food,ore=world.Economies[a.Team].Ore,
                gathered_food=world.Economies[a.Team].GatheredFoodMilli/1000,
                soldiers=world.ActiveUnits.Count(u=>u.Team==a.Team && u.Health>0 && !u.Definition.Hero && !world.Workers.ContainsKey(u.Definition.Name))}).ToArray()};
        System.IO.File.WriteAllText(GamePaths.Report("economy-recovery-smoke-report.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("ECONOMY_RECOVERY_SMOKE "+JsonSerializer.Serialize(report));
        _recoveryFinished=true; _=FinishCombatAudio(passed ? 0 : 1);
    }
}
