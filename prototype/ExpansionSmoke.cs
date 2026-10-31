using Godot;
using Reforged.Simulation;
using System.Text.Json;

/// <summary>Imported Bilbo's Bluffs terrain and footprints: let a funded bot build and expand using normal worker
/// commands, then inspect its local Mill, Foundry, Citadel and Watchtower as one layout.</summary>
public partial class Main
{
    private bool _expansionFinished;
    private int _expansionCaptureFrame=-1;
    private void ExpansionSmokeInput()
    {
        var w=_world!;
        if (_frames==2)
        {
            w.AiPlayers.Clear();
            // Keep the fixture focused on expansion construction; a full standing army at home would add an
            // unrelated crowd-traffic load to the layout and resource checks.
            foreach (var name in w.AiBuildings.Where(b=>b.Value.Class is "MILITARY" or "UPGRADE").Select(b=>b.Key).ToArray()) w.AiBuildings.Remove(name);
            var ai=new AiPlayer(AiTeam,new("Default","Default",1,50,50,50,50,false,false),AiStyle.Balanced);
            ai.Profile=ai.Profile with {ExpansionTicks=0,ExpansionIntervalTicks=int.MaxValue/2,MaxCitadels=2,
                AssaultFromTicks=int.MaxValue,UpgradeTicks=int.MaxValue,ResearchFromTicks=int.MaxValue,Workers=12};
            w.AiPlayers.Add(AiTeam,ai);w.GrantResources(AiTeam,50000);
        }
        if (_frames<3 || _expansionFinished) return;
        var start=w.StartStructures.First(s=>s.Team==AiTeam);
        var keep=w.ConstructionSites.Values.FirstOrDefault(s=>s.Team==AiTeam && s.Health>0 && s.Complete
            && s.Building==start.Building && !s.Prebuilt);
        var local=keep==null?[]:w.Resources.Values.Where(r=>SimWorld.DistanceSquared(r.X,r.Z,keep.X,keep.Z)<=24000L*24000).ToArray();
        var tower=keep==null?null:w.ConstructionSites.Values.FirstOrDefault(s=>s.Team==AiTeam && s.Complete && s.Health>0
            && w.SiteDefense(s) is { } d && SimWorld.DistanceSquared(s.X,s.Z,keep.X,keep.Z)<=(long)(d.RangeMm-3000)*(d.RangeMm-3000));
        var checks=new Dictionary<string,bool>
        {
            ["citadel_built_on_imported_terrain"]=keep!=null,
            ["compact_food_and_ore_cluster"]=local.Select(r=>r.Kind).Distinct().Count()==2,
            ["local_mill_and_foundry_complete"]=local.Length>=2 && local.All(r=>w.ConstructionSites[r.Id] is {Team:AiTeam,Complete:true}),
            ["tower_covers_citadel"]=tower!=null,
            ["tower_covers_resource_working_areas"]=tower!=null && local.All(r=>
                {int reach=w.SiteDefense(tower)!.RangeMm-r.RadiusMm-w.WorkReachMm;
                    return reach>0 && SimWorld.DistanceSquared(tower.X,tower.Z,r.X,r.Z)<=(long)reach*reach;})
        };
        bool passed=checks.Values.All(v=>v);
        if (!passed && w.Tick<24000) return;
        if (_expansionCaptureFrame<0)
        {
            _paused=true;
            if (keep!=null)
            {
                int x=(keep.X+local.Sum(r=>r.X))/(local.Length+1),z=(keep.Z+local.Sum(r=>r.Z))/(local.Length+1);
                _focus=PositionOf(x,z);_distance=48;UpdateCamera();
                _selected.Clear();_selectedStructure=keep.Id;
            }
            _expansionCaptureFrame=_frames;
            return;
        }
        if (_frames-_expansionCaptureFrame==2 && _capture!="") _=CaptureFrame(false);
        if (_frames-_expansionCaptureFrame<8) return;
        var report=new {build=BuildInfo.Label,passed,checks,ticks=w.Tick,ai=w.AiPlayers[AiTeam].Status,
            citadel=keep==null?null:new {keep.Id,keep.X,keep.Z},tower=tower==null?null:new {tower.Id,tower.X,tower.Z},
            resources=local.Select(r=>new {r.Id,kind=r.Kind.ToString(),r.X,r.Z}),
            defenses=w.ConstructionSites.Values.Where(s=>s.Team==AiTeam && w.SiteDefense(s)!=null)
                .Select(s=>new {s.Id,s.X,s.Z,s.Complete,s.ProgressMicroTicks,s.Health}),
            workers=w.Units.Where(u=>u.Team==AiTeam && w.Workers.ContainsKey(u.Definition.Name))
                .Select(u=>new {u.Id,u.X,u.Z,order=u.Order.ToString(),u.OrderTarget,u.Goal,u.PlacementCell})};
        System.IO.File.WriteAllText(System.IO.Path.Combine(_cache,"expansion-smoke-report.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("EXPANSION_SMOKE "+JsonSerializer.Serialize(report));_expansionFinished=true;_=FinishCombatAudio(passed?0:1);
    }
}
