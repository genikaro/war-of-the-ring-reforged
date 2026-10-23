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
    private readonly Button[] _trainButtons = new Button[4];
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
            var button = new Button { Position = new(788+slot*56,34),Size = new(54,54),FocusMode = FocusModeEnum.None,ExpandIcon = true,Visible = false };
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
    public void RefreshProduction(SimWorld world,ProductionBuilding? producer,ConstructionSite? site = null,string? upgradePortrait = null)
    {
        var upgrade = site is { Complete: true } && site.Team == PlayerTeam ? world.UpgradeRules(site) : null;
        bool upgrading = upgrade != null && site!.Upgrading;
        _training.Visible = producer is { Queue.Count: > 0 } || upgrading;
        for (int slot = 0; slot < _trainButtons.Length; slot++)
        {
            var button = _trainButtons[slot];
            button.Visible = producer != null && slot < producer.Units.Length;
            if (!button.Visible) continue;
            var rules = world.ProductionRules[producer!.Units[slot]];
            button.Disabled = !world.CanTrain(PlayerTeam,producer.Id,slot,out string reason);
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.DisplayName));
            button.TooltipText = $"Train {rules.DisplayName} [{GridKeys[slot]}]\n{rules.FoodCost} food · {rules.OreCost} ore · population {rules.Population} · {rules.DurationTicks/20}s"
                + $"\nClick again to queue up to {SimWorld.ProductionQueueLimit}. Click a portrait in training or press Backspace to cancel for a full refund."
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
                + $"\nTraining waits while the upgrade runs. Click its portrait to cancel for a {upgrade.FoodRefundPpm/10000}% refund."
                + (_upgradeButton.Disabled ? "\n"+reason : "");
        }
        if (producer == null && !upgrading) return;
        string rally = producer == null ? "" : " · "+(producer.RallyResource >= 0 ? $"New workers gather {world.Resources[producer.RallyResource].Kind.ToString().ToLowerInvariant()} · right-click to move the rally point"
            : producer.RallyCell >= 0 ? "Rally point set · right-click to move it" : "Right-click the battlefield, or a Mill or Foundry, to set a rally point");
        var queue = producer?.Queue ?? [];
        if (!upgrading && queue.Count == 0) { _order.Text = "Your structure"+rally; return; }
        int done = upgrading ? site!.UpgradeTicks : producer!.ProgressTicks;
        int duration = upgrading ? upgrade!.DurationTicks : world.ProductionRules[queue[0].Unit].DurationTicks;
        int percent = done*100/duration,waiting = queue.Count-(upgrading ? 0 : 1);
        _order.Text = (upgrading ? $"Upgrading to {upgrade!.DisplayName} {percent}%" : $"Training {world.ProductionRules[queue[0].Unit].DisplayName} {percent}%")
            + (waiting > 0 ? $" · {waiting} queued" : "")+rally;
        _trainingBar.Texture ??= _art.GetValueOrDefault("console_g_progressbar");
        _trainingProgress.Size = new(185f*done/duration,6);
        int offset = upgrading ? 1 : 0;
        for (int index = 0; index < _queueButtons.Length; index++)
        {
            var button = _queueButtons[index]; button.Visible = index < queue.Count+offset;
            if (!button.Visible) continue;
            if (index < offset)
            {
                button.Icon = upgradePortrait == null ? null : _art.GetValueOrDefault(upgradePortrait);
                button.TooltipText = $"Upgrading to {upgrade!.DisplayName} · {percent}%\nClick to cancel and receive "
                    + $"{upgrade.FoodCost*upgrade.FoodRefundPpm/1_000_000} food and {upgrade.OreCost*upgrade.OreRefundPpm/1_000_000} ore";
                continue;
            }
            var rules = world.ProductionRules[queue[index-offset].Unit];
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.DisplayName));
            button.TooltipText = (index == 0 ? $"Training {rules.DisplayName} · {percent}%" : $"Queued {rules.DisplayName}")
                + $"\nClick to cancel and receive {rules.FoodCost} food and {rules.OreCost} ore";
        }
    }
}
