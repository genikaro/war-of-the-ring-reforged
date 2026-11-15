using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Three recruitment/death cycles verify corpse assets are freed and retained unit nodes grow only by
/// two lightweight lookup nodes per casualty. Run headless with the Sandbox cache and --performance-smoke.</summary>
public partial class Main
{
    private bool _performanceSmoke;
    private readonly Dictionary<string,bool> _performanceChecks = [];
    private readonly List<object> _performanceSnapshots = [];
    private int _performanceTemplateNodes;

    private void PerformanceSmokeInput()
    {
        var world = _world!;
        if (_frames == 2)
        {
            world.AiPlayers.Clear();
            foreach (var unit in world.Units) unit.Health = 0;
        }
        if (_frames == 3)
        {
            _performanceChecks["fresh_corpses_keep_their_models"] = _views.Values.All(v => !v.Buried && v.Player != null);
            AgeCorpses();
        }
        for (int wave = 0; wave < 3; wave++)
        {
            int start = 6+wave*8;
            if (_frames == start)
            {
                int cx = world.Navigation.Width*world.Navigation.CellSize/2,cz = world.Navigation.Height*world.Navigation.CellSize/2;
                var definition = world.Rules.Units["UNIT_G_GONDOR_SWORDSMAN"];
                var nav = world.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
                for (int i = 0; i < 64; i++)
                {
                    int cell = nav.Cell(cx-14000+i%8*3000,cz-14000+i/8*3000);
                    world.Spawn(0,nav.Nearest(cell),definition);
                }
            }
            if (_frames == start+2)
            {
                var live = world.Units.Where(u => u.Health > 0).ToArray();
                _performanceChecks[$"wave_{wave}_recruits_animate"] = live.Length == 64
                    && live.All(u => _views[u.Id] is {Player: not null,Appearance: not null,Buried:false} v
                        && Descendants(v.Root).OfType<Skeleton3D>().Any() && v.Player.IsPlaying());
                foreach (var unit in live) unit.Health = 0;
            }
            if (_frames == start+3) AgeCorpses();
            if (_frames == start+6)
            {
                int nodes = _views.Values.Sum(v => Descendants(v.Root).Count());
                int templates = _modelTemplateStore == null ? 0 : Descendants(_modelTemplateStore).Count();
                if (wave == 0) _performanceTemplateNodes = templates;
                _performanceChecks[$"wave_{wave}_corpse_assets_released"] = _views.Values.All(v => v.Buried
                    && v.Root.GetChildCount() == 1 && v.Ring.Mesh == null && v.Player == null && v.Appearance == null
                    && v.Health == null && v.HeroGlow == null && v.Ghost == null) && nodes == 2*world.Units.Count;
                _performanceChecks[$"wave_{wave}_templates_stay_bounded"] = templates == _performanceTemplateNodes
                    && _modelTemplates.Values.All(GodotObject.IsInstanceValid);
                _performanceSnapshots.Add(new {wave,units=world.Units.Count,unit_nodes=nodes,template_nodes=templates,
                    retained=_views.Where(p => p.Value.Root.GetChildCount() != 1 || !p.Value.Buried)
                        .Select(p => new {id=p.Key,world.Units[p.Key].Health,p.Value.Buried,p.Value.DeathTick,children=p.Value.Root.GetChildCount()}).ToArray()});
            }
        }
        if (_frames != 30) return;
        var report = new {build=BuildInfo.Label,checks=_performanceChecks,snapshots=_performanceSnapshots,
            passed=_performanceChecks.Count == 10 && _performanceChecks.Values.All(v => v)};
        System.IO.File.WriteAllText(GamePaths.Report("performance-smoke-report.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("PERFORMANCE_SMOKE "+JsonSerializer.Serialize(report));
        _ = FinishCombatAudio(report.passed ? 0 : 1);

        void AgeCorpses()
        {
            foreach (var view in _views.Values.Where(v => !v.Buried))
                view.DeathTick = world.Tick;
            for (int tick = 0; tick < CorpseTicks+CorpseSinkTicks+2; tick++) world.Step();
        }
    }
}
