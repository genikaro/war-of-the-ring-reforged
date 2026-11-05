using Godot;
using Reforged.Simulation;

public partial class ObserverHud
{
    public Func<string,string?> BuildingPortrait { get; set; } = _ => null;
    public Func<string,string?> TechniquePortrait { get; set; } = _ => null;
    private VBoxContainer _buildings = null!,_upgrades = null!;
    private ScrollContainer _buildingScroll = null!,_upgradeScroll = null!;
    private sealed record AssetRow(Control Row,ColorRect Swatch,Label Name,HFlowContainer Icons,Label Empty);
    private sealed record AssetIcon(string? Portrait,string Badge,string Tip,bool Pending = false);
    private readonly List<AssetRow> _buildingRows = [],_upgradeRows = [];

    private void CreateAssetPages(VBoxContainer root)
    {
        foreach (var page in new[] {Page.Buildings,Page.Upgrades})
        {
            var scroll = new ScrollContainer { CustomMinimumSize = new(BodyWidth,0),HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
            root.AddChild(scroll);
            var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            body.AddThemeConstantOverride("separation",2); scroll.AddChild(body);
            if (page == Page.Buildings) { _buildings = body; _buildingScroll = scroll; }
            else { _upgrades = body; _upgradeScroll = scroll; }
        }
    }

    private void LayoutAssetPages(float scale,float top,float consoleTop)
    {
        float available = Math.Max(32,(consoleTop-top)/scale-40);
        foreach (var (scroll,body) in new[] {(_buildingScroll,_buildings),(_upgradeScroll,_upgrades)})
        {
            scroll.Visible = body.Visible;
            // Leave a little room around the content so a short page does not acquire a scrollbar from rounding.
            if (body.Visible) scroll.CustomMinimumSize = new(BodyWidth,Math.Min(available,body.GetCombinedMinimumSize().Y+4));
        }
    }

    private AssetIcon[] BuildingIcons(SimWorld world,int team)
        => world.ConstructionSites.Values.Where(s => s.Team == team && s.Health > 0)
            .GroupBy(s => world.SiteRules(s).Name).OrderBy(g => g.Key,StringComparer.Ordinal).Select(group => {
                var definition = world.SiteRules(group.First());
                int complete = group.Count(s => s.Complete),foundations = group.Count()-complete;
                string tip = $"{group.Count()} {definition.DisplayName}\n{complete} complete";
                if (foundations > 0) tip += $" · {foundations} under construction";
                int upgrading = group.Count(s => s.Upgrading);
                if (upgrading > 0) tip += $"\n{upgrading} upgrading";
                return new AssetIcon(BuildingPortrait(definition.Name),group.Count().ToString(),tip,complete == 0);
            }).ToArray();

    private AssetIcon[] UpgradeIcons(SimWorld world,int team)
    {
        var icons = new List<AssetIcon>();
        foreach (var technique in world.Techniques.Where(t => world.HasResearched(team,t.Name)))
            icons.Add(new(TechniquePortrait(technique.Name),"",technique.DisplayName+" · Complete\n"+technique.Description));
        foreach (var site in world.ConstructionSites.Values.Where(s => s.Team == team && s.Health > 0))
        {
            if (site.Research is { } name && world.Techniques.FirstOrDefault(t => t.Name == name) is { } technique)
            {
                string progress = Percent(site.ResearchTicks,technique.DurationTicks);
                icons.Add(new(TechniquePortrait(name),progress,$"{technique.DisplayName} · Researching {progress}\n{world.SiteRules(site).DisplayName}",true));
            }
            if (site.Upgrading && world.UpgradeRules(site) is { } upgrade)
            {
                string progress = Percent(site.UpgradeTicks,upgrade.DurationTicks);
                icons.Add(new(BuildingPortrait(upgrade.Name),progress,$"{upgrade.DisplayName} · Upgrading {progress}\n{world.SiteRules(site).DisplayName}",true));
            }
        }
        return icons.ToArray();
    }

    private static string Percent(int ticks,int duration) => $"{Math.Clamp(100L*ticks/Math.Max(1,duration),0,100)}%";

    private void RefreshAssets(SimWorld world,Func<int,Color> color,Func<string,Texture2D?> art,Page page)
    {
        bool buildings = page == Page.Buildings;
        var rows = buildings ? _buildingRows : _upgradeRows;
        var body = buildings ? _buildings : _upgrades;
        while (rows.Count < _teams.Count)
        {
            var row = Row(); row.CustomMinimumSize = new(0,30); body.AddChild(row);
            var (swatch,name) = PlayerCell(row,rows.Count);
            var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,CustomMinimumSize = new(280,0) }; row.AddChild(content);
            var icons = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,MouseFilter = MouseFilterEnum.Ignore };
            icons.AddThemeConstantOverride("h_separation",3); icons.AddThemeConstantOverride("v_separation",3); content.AddChild(icons);
            var empty = Cell(buildings ? "No buildings" : "No upgrades",0,12,HorizontalAlignment.Left);
            empty.Modulate = new Color(1,1,1,.6f); content.AddChild(empty);
            rows.Add(new(row,swatch,name,icons,empty));
        }
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; row.Row.Visible = index < _teams.Count;
            if (!row.Row.Visible) continue;
            int team = _teams[index];
            row.Swatch.Color = color(team); row.Name.Text = PlayerName(world,team); row.Name.TooltipText = PlayerTip(world,team);
            row.Row.Modulate = world.DefeatedTeams.Contains(team) ? new Color(1,1,1,.4f) : Colors.White;
            var items = buildings ? BuildingIcons(world,team) : UpgradeIcons(world,team);
            row.Empty.Visible = items.Length == 0;
            while (row.Icons.GetChildCount() < items.Length)
            {
                var icon = new TextureRect { CustomMinimumSize = new(32,32),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter = MouseFilterEnum.Stop };
                var badge = Cell("",0,11,HorizontalAlignment.Right); badge.Position = new(0,14); badge.Size = new(32,18);
                badge.AddThemeConstantOverride("outline_size",4); badge.AddThemeColorOverride("font_outline_color",new Color("201b12"));
                icon.AddChild(badge); row.Icons.AddChild(icon);
            }
            for (int slot = 0; slot < row.Icons.GetChildCount(); slot++)
            {
                var icon = row.Icons.GetChild<TextureRect>(slot); icon.Visible = slot < items.Length;
                if (!icon.Visible) continue;
                var item = items[slot]; icon.Texture = item.Portrait is { } portrait ? art(portrait) : null;
                icon.TooltipText = item.Tip;
                icon.Modulate = item.Pending ? new Color("f7d16a") : Colors.White;
                icon.GetChild<Label>(0).Text = item.Badge;
            }
        }
    }

    public int[] DisplayedBuildingCounts(int index) => _buildingRows[index].Icons.GetChildren().OfType<TextureRect>().Where(p => p.Visible)
        .Select(p => int.Parse(p.GetChild<Label>(0).Text)).ToArray();
    public string[] DisplayedUpgradeTips(int index) => _upgradeRows[index].Icons.GetChildren().OfType<TextureRect>().Where(p => p.Visible)
        .Select(p => p.TooltipText).ToArray();
}
