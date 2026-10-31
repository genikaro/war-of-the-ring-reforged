using Godot;
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
        // Original selection panel: the unit in training with the purple bar, followed by the queue.
        _training = new Control { Position = new(270,58),Size = new(235,46),Visible = false,MouseFilter = MouseFilterEnum.Ignore };
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
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.DisplayName));
            button.TooltipText = $"Train {rules.DisplayName} [{GridKeys[slot]}]\n{rules.FoodCost} food · {rules.OreCost} ore"
                + (rules.FateCost > 0 ? $" · {rules.FateCost} Fate" : "")+$" · population {rules.Population} · {rules.DurationTicks/20}s"
                + (rules.UniqueGroup >= 0 ? world.FactionOf(PlayerTeam) == SimWorld.Evil
                    ? $"\nHero: your Fortress holds {world.UniqueGroupLimit(PlayerTeam,rules.UniqueGroup)} of the Nazgul King, Saleme and Grishnakh at a time"
                    : $"\nHero: your Citadel holds {world.UniqueGroupLimit(PlayerTeam,rules.UniqueGroup)} of Gimli, Aragorn and Legolas at a time" : "")
                + (button.Disabled ? "\n"+reason : "");
        }
        _upgradeButton.Visible = upgrade != null;
        if (upgrade != null)
        {
            _upgradeButton.Disabled = !world.CanUpgrade(PlayerTeam,site!.Id,out string reason);
            _upgradeButton.Icon = upgradePortrait == null ? null : _art.GetValueOrDefault(upgradePortrait);
            var opens = world.BuildingRules.Values.Where(r => r.Prerequisites.Contains(upgrade.Name)).Select(r => r.DisplayName)
                .Concat(world.ProductionRules.Values.Where(r => r.Prerequisites?.Contains(upgrade.Name) == true).Select(r => r.DisplayName)).ToArray();
            int population = world.PopulationProvided.GetValueOrDefault(upgrade.Name)-world.PopulationProvided.GetValueOrDefault(site.Building!);
            _upgradeButton.TooltipText = $"Upgrade to {upgrade.DisplayName} [{GridKeys[UpgradeSlot]}]\n{upgrade.FoodCost} food · {upgrade.OreCost} ore · {upgrade.DurationTicks/20}s · {upgrade.MaxHealth} health"
                + (population > 0 ? $" · +{population} population" : "")
                + (opens.Length > 0 ? "\nOpens "+string.Join(", ",opens) : "")
                + (_upgradeButton.Disabled ? "\n"+reason : "");
        }
        // An upgrade or a research in progress heads the queue (local skirmish-003): its own portrait, progress and refund.
        var research = site is { Research: { } name } && site.Team == PlayerTeam ? world.Techniques.First(t => t.Name == name) : null;
        (string Label,string? Portrait,int Done,int Duration,int Food,int Ore)? head = upgrading
            ? ($"Upgrading to {upgrade!.DisplayName}",upgradePortrait,site!.UpgradeTicks,upgrade.DurationTicks,
                upgrade.FoodCost*upgrade.FoodRefundPpm/1_000_000,upgrade.OreCost*upgrade.OreRefundPpm/1_000_000)
            : research != null ? ($"Researching {research.DisplayName}",researchPortrait,site!.ResearchTicks,research.DurationTicks,research.FoodCost,research.OreCost) : null;
        _training.Visible = producer is { Queue.Count: > 0 } || head != null;
        if (producer == null && head == null) return;
        string rally = producer == null ? "" : producer.RallyResource >= 0 ? $"Rally: gather {world.Resources[producer.RallyResource].Kind.ToString().ToLowerInvariant()}"
            : producer.RallyCell >= 0 ? "Rally point set" : "No rally point";
        var queue = producer?.Queue ?? [];
        if (head == null && queue.Count == 0) { _order.Text = rally; return; }
        int done = head?.Done ?? producer!.ProgressTicks;
        int duration = head?.Duration ?? world.ProductionRules[queue[0].Unit].DurationTicks;
        int percent = done*100/duration,waiting = queue.Count-(head != null ? 0 : 1);
        _order.Text = (head != null ? $"{head.Value.Label} {percent}%" : $"Training {world.ProductionRules[queue[0].Unit].DisplayName} {percent}%")
            + (waiting > 0 ? $" · {waiting} queued" : "")+(rally != "" ? " · "+rally : "");
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
                button.TooltipText = $"{head.Value.Label} · {percent}%\nCancel refunds {head.Value.Food} food and {head.Value.Ore} ore";
                continue;
            }
            var rules = world.ProductionRules[queue[index-offset].Unit];
            var paid = queue[index-offset];
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.DisplayName));
            button.TooltipText = (index == 0 ? $"Training {rules.DisplayName} · {percent}%" : $"Queued {rules.DisplayName}")
                + $"\nCancel refunds {(paid.FoodPaid < 0 ? rules.FoodCost : paid.FoodPaid)} food and {(paid.OrePaid < 0 ? rules.OreCost : paid.OrePaid)} ore"
                + ((paid.FatePaid < 0 ? rules.FateCost : paid.FatePaid) is > 0 and var fate ? $" and {fate} Fate" : "");
        }
    }
}
