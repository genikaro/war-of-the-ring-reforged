using Godot;
using Reforged.Rendering;
using Reforged.Simulation;

public partial class Main
{
    private readonly Dictionary<int,int> _buildingDestructions = [];
    private readonly Dictionary<bool,HealingEffect.Layer[]> _buildingDamageLayers = [];
    private readonly List<(BuildingDamageEffect Effect,Vector3 Position,int Team,double Start)> _buildingDust = [];
    private readonly List<(BuildingVisual View,int Team,double Start,double ClipLength,string Clip)> _buildingCollapses = [];
    private double _buildingOutcomeTime;

    // DestroyedSites is reset at the start of each simulation tick, including during fast-forward.
    private void RecordBuildingDestructions()
    {
        foreach (int id in _world!.DestroyedSites)
        {
            if (!_constructionViews.TryGetValue(id,out var view) || !view.Root.Visible) continue;
            int team = _structureViews[view.Obstacle].Team;
            if (ShowsBuildingEffect(team,view.Root.Position,view.Radius)) _buildingDestructions.TryAdd(id,_world.Tick);
        }
    }

    private bool ShowsBuildingEffect(int team,Vector3 position,float radius) => team == ViewTeam
        || _world!.SiteInSight(ViewTeam,(int)Math.Round(-position.X*1000),(int)Math.Round(position.Z*1000),(int)Math.Round(radius*1000));

    private static string? BuildingAnimation(BuildingVisual view,string clip) => clip.Length == 0 ? null
        : view.Player?.GetAnimationList().FirstOrDefault(a => a == clip || a.EndsWith('/'+clip));

    private void StartBuildingCollapse(BuildingVisual view,int team,int tick)
    {
        view.Bar.Conceal(); view.Fire?.Refresh(0,tick);
        string clip = view.Stage is >= 0 and < 3 && view.Clips[7].Length > 0 ? view.Clips[7] : view.Clips[6];
        string? animation = BuildingAnimation(view,clip);
        double duration = 0;
        if (view.Player != null) view.Player.SpeedScale = 0;
        if (animation != null)
        {
            duration = view.Player!.GetAnimation(animation).Length;
            view.Player.Play(animation); view.Player.Advance(0);
        }
        if (clip.Length > 0) view.Appearance.Apply(clip,0);
        StartBuildingDust(view,team,tick,true);
        _buildingCollapses.Add((view,team,tick/(double)SimWorld.TicksPerSecond+_buildingOutcomeTime,duration,clip));
    }

    private void StartBuildingDust(BuildingVisual view,int team,int tick,bool collapse)
    {
        if (!_buildingDamageLayers.TryGetValue(EvilTeam(team),out var layers)) return;
        var visual = new BuildingDamageEffect(layers,view.Radius,collapse);
        AddChild(visual.Root);
        _buildingDust.Add((visual,view.Root.Position,team,tick/(double)SimWorld.TicksPerSecond+_buildingOutcomeTime));
    }

    private void PresentBuildingDamage()
    {
        // The outcome pauses the battle immediately. Let the final collapse finish behind its panel.
        if (_outcome.Visible && !_gameMenu.Visible) _buildingOutcomeTime += GetProcessDeltaTime();
        double time = _healingTime+_buildingOutcomeTime;
        for (int i = _buildingCollapses.Count-1; i >= 0; i--)
        {
            var (view,team,start,clipLength,clip) = _buildingCollapses[i];
            double age = Math.Max(0,time-start);
            double end = Math.Max(2.6,clipLength+.65);
            if (age >= end)
            { view.Root.QueueFree(); _buildingCollapses.RemoveAt(i); continue; }
            view.Root.Visible = ShowsBuildingEffect(team,view.Root.Position,view.Radius);
            float impact = Mathf.SmoothStep(.08f,.9f,(float)age);
            float settle = Mathf.SmoothStep((float)(end-.75),(float)end,(float)age);
            // Several source ruin clips swap meshes but have static bones. Settle the whole model into the dust.
            float sink = clip.Length == 0 ? impact : .4f*impact+.6f*settle;
            view.Model.Position = Vector3.Down*(sink*Math.Max(3,view.Radius*1.7f));
            view.Model.Rotation = view.Model.Rotation with { Z = impact*(clip.Length == 0 ? .28f : .15f) };
            if (clip.Length > 0)
            {
                view.Player?.Seek(Math.Min(age,clipLength),true);
                view.Appearance.Apply(clip,clipLength > 0 ? Math.Min(1,age/clipLength) : 0);
            }
        }
        for (int i = _buildingDust.Count-1; i >= 0; i--)
        {
            var (effect,position,team,start) = _buildingDust[i];
            if (time-start >= effect.Duration)
            { effect.Root.QueueFree(); _buildingDust.RemoveAt(i); continue; }
            effect.Root.Visible = ShowsBuildingEffect(team,position,effect.Radius);
            if (effect.Root.Visible) effect.Draw(Math.Max(0,time-start),position);
        }
    }
}
