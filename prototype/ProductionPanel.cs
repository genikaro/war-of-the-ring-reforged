using Godot;
using Reforged.Presentation;
using Reforged.Simulation;

public partial class CommandPanel
{
    public event Action<int>? TrainingChosen;
    public event Action<int>? TrainingCancelled;
    public event Action? UpgradeChosen;
    /// <summary>Bottom-left grid position, where the original shows the next Citadel level (local skirmish-011).</summary>
    public const int UpgradeSlot = 8;
    private Button _upgradeButton = null!;
    public Button UpgradeButton => _upgradeButton;
    // The Citadel trains six (Worker, Frodo and the four Fate heroes, source UnitProduced1-6); the upgrade keeps slot 8.
    private readonly Button[] _trainButtons = new Button[6];
    private readonly Button[] _queueButtons = new Button[SimWorld.ProductionQueueLimit];
    private Control _training = null!,_trainingProgress = null!;
    /// <summary>Build-224: the training row's top in the building panel, just under the health bar (StructurePanel.cs).</summary>
    private const float TrainingTop = 104;
    private TextureRect _trainingBar = null!;
    public Button TrainButton(int slot) => _trainButtons[slot];
    public Button QueueButton(int index) => _queueButtons[index];
    public bool TrainingVisible => _training.IsVisibleInTree();
    public float TrainingProgressWidth => _trainingProgress.Size.X;
    public bool TrainingBarLoaded => _trainingBar.Texture != null;
    /// <summary>Only building actions on show: training, plus the upgrade where the building has one.</summary>
    public bool PaletteOnlyTraining => _trainButtons.Any(b => b.Visible) && !_buttons.Values.Concat(_abilities).Concat(_constructionButtons)
        .Concat([_buildButton,_backButton,_cancelConstructionButton]).Any(b => b.Visible);

    private void CreateProductionControls()
    {
        for (int slot = 0; slot < _trainButtons.Length; slot++)
        {
            int chosen = slot;
            // Grid positions row by row (the Citadel's Legolas and Gandalf take A and S on the second row).
            var button = new Button { Position = new(788+slot%4*56,34+slot/4*56),Size = new(54,54),FocusMode = FocusModeEnum.None,ExpandIcon = true,Visible = false };
            Skin(button); AddHotkey(button,GridKeys[slot].ToString()); button.Pressed += () => TrainingChosen?.Invoke(chosen);
            _trainButtons[slot] = button; _content.AddChild(button);
        }
        _upgradeButton = ActionButton(UpgradeSlot); _upgradeButton.Pressed += () => UpgradeChosen?.Invoke();
        // Original selection panel: the unit in training with the purple bar, followed by the queue. Build-224: under the
        // building's health bar, where the console's bottom line used to repeat it in words.
        _training = new Control { Position = new(0,TrainingTop),Size = new(235,46),Visible = false,MouseFilter = MouseFilterEnum.Ignore };
        _structureDetails.AddChild(_training);
        for (int index = 0; index < _queueButtons.Length; index++)
        {
            int chosen = index;
            var button = new Button { Position = index == 0 ? new(0,0) : new(50+(index-1)*36,14),Size = index == 0 ? new(44,44) : new(32,32),
                FocusMode = FocusModeEnum.None,ExpandIcon = true };
            Skin(button); button.Pressed += () => TrainingCancelled?.Invoke(chosen);
            _queueButtons[index] = button; _training.AddChild(button);
        }
        _training.AddChild(new ColorRect { Position = new(50,1),Size = new(185,8),Color = new Color("282418"),MouseFilter = MouseFilterEnum.Ignore });
        _trainingProgress = new Control { Position = new(50,2),Size = new(0,6),ClipContents = true,MouseFilter = MouseFilterEnum.Ignore };
        _training.AddChild(_trainingProgress);
        _trainingBar = new TextureRect { Size = new(185,6),ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,MouseFilter = MouseFilterEnum.Ignore };
        _trainingProgress.AddChild(_trainingBar);
    }

    /// <summary>Training buttons, the upgrade button and the queue of the player's selected building. An upgrade in
    /// progress heads the queue, as in the original (local skirmish-003), and its portrait cancels it.</summary>
    public void RefreshProduction(SimWorld world,ProductionBuilding? producer,ConstructionSite? site = null,string? upgradePortrait = null,
        string? researchPortrait = null)
    {
        var upgrade = site is { Complete: true } && site.Team == PlayerTeam ? world.UpgradeRules(site) : null;
        bool upgrading = upgrade != null && site!.Upgrading;
        for (int slot = 0; slot < _trainButtons.Length; slot++)
        {
            var button = _trainButtons[slot];
            button.Visible = producer != null && slot < producer.Units.Length;
            if (!button.Visible) continue;
            var rules = world.TrainingCost(PlayerTeam,world.ProductionRules[producer!.Units[slot]]);
            button.Disabled = !world.CanTrain(PlayerTeam,producer.Id,slot,out string reason);
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.Unit));
            // Build-184: as the original's recruitment tooltip, the unit's own name, cost cells and description (campaign-012).
            // Build-185: no hero-limit line; a hero over the limit is refused with its own reason.
            button.TooltipText = HudTip.Make(HudTip.Keyed(rules.DisplayName,GridKeys[slot].ToString()),
                HudTip.Costs(rules.FoodCost,rules.OreCost,rules.FateCost,rules.Population),
                Seconds("tip.time",rules.DurationTicks),GameText.Record(rules.Unit,"description"),button.Disabled ? reason : null);
        }
        _upgradeButton.Visible = upgrade != null;
        if (upgrade != null)
        {
            _upgradeButton.Disabled = !world.CanUpgrade(PlayerTeam,site!.Id,out string reason);
            _upgradeButton.Icon = upgradePortrait == null ? null : _art.GetValueOrDefault(upgradePortrait);
            var opens = world.BuildingRules.Values.Where(r => r.Prerequisites.Contains(upgrade.Name)).Select(r => r.DisplayName)
                .Concat(world.ProductionRules.Values.Where(r => r.Prerequisites?.Contains(upgrade.Name) == true).Select(r => r.DisplayName)).ToArray();
            int population = world.PopulationProvided.GetValueOrDefault(upgrade.Name)-world.PopulationProvided.GetValueOrDefault(site.Building!);
            _upgradeButton.TooltipText = HudTip.Make(HudTip.Keyed(upgrade.DisplayName,GridKeys[UpgradeSlot].ToString()),HudTip.Costs(upgrade.FoodCost,upgrade.OreCost),
                Seconds("tip.time",upgrade.DurationTicks),GameText.Labeled("stat.health",upgrade.MaxHealth),
                population > 0 ? GameText.Labeled("resource.population","+"+population) : null,
                opens.Length > 0 ? GameText.Labeled("tip.opens",string.Join(", ",opens)) : null,
                GameText.Record(upgrade.Name,"description"),_upgradeButton.Disabled ? reason : null);
        }
        // An upgrade or a research in progress heads the queue (local skirmish-003): its own portrait, progress and refund.
        var research = site is { Research: { } name } && site.Team == PlayerTeam ? world.Techniques.First(t => t.Name == name) : null;
        (string Label,string? Portrait,int Done,int Duration,int Food,int Ore)? head = upgrading
            ? (GameText.T("queue.upgrading",upgrade!.DisplayName),upgradePortrait,site!.UpgradeTicks,upgrade.DurationTicks,
                upgrade.FoodCost*upgrade.FoodRefundPpm/1_000_000,upgrade.OreCost*upgrade.OreRefundPpm/1_000_000)
            : research != null ? (GameText.T("queue.researching",research.DisplayName),researchPortrait,site!.ResearchTicks,research.DurationTicks,research.FoodCost,research.OreCost) : null;
        _training.Visible = producer is { Queue.Count: > 0 } || head != null;
        // Build-224: the queue speaks for itself (its portraits' tooltips); the console's bottom line no longer spells out
        // the training, the queue length or the rally point.
        var queue = producer?.Queue ?? [];
        if (head == null && queue.Count == 0) return;
        int done = head?.Done ?? producer!.ProgressTicks;
        int duration = head?.Duration ?? world.ProductionRules[queue[0].Unit].DurationTicks;
        int percent = done*100/duration;
        _trainingBar.Texture ??= _art.GetValueOrDefault("console_g_progressbar");
        _trainingProgress.Size = new(185f*done/duration,6);
        int offset = head != null ? 1 : 0;
        for (int index = 0; index < _queueButtons.Length; index++)
        {
            var button = _queueButtons[index]; button.Visible = index < queue.Count+offset;
            if (!button.Visible) continue;
            if (index < offset)
            {
                button.Icon = head!.Value.Portrait == null ? null : _art.GetValueOrDefault(head.Value.Portrait);
                button.TooltipText = HudTip.Make($"{head.Value.Label} · {percent}%",null,GameText.T("queue.refund",head.Value.Food,head.Value.Ore));
                continue;
            }
            var rules = world.ProductionRules[queue[index-offset].Unit];
            var paid = queue[index-offset];
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.Unit));
            int food = paid.FoodPaid < 0 ? rules.FoodCost : paid.FoodPaid,ore = paid.OrePaid < 0 ? rules.OreCost : paid.OrePaid;
            button.TooltipText = HudTip.Make(index == 0 ? GameText.T("queue.training",rules.DisplayName)+$" · {percent}%" : GameText.T("queue.queued",rules.DisplayName),null,
                (paid.FatePaid < 0 ? rules.FateCost : paid.FatePaid) is > 0 and var fate ? GameText.T("queue.refund_fate",food,ore,fate) : GameText.T("queue.refund",food,ore));
        }
    }
}
