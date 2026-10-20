using Godot;
using Reforged.Simulation;

public partial class CommandPanel
{
    public event Action<int>? TrainingChosen;
    public event Action<int>? TrainingCancelled;
    private readonly Button[] _trainButtons = new Button[4];
    private readonly Button[] _queueButtons = new Button[SimWorld.ProductionQueueLimit];
    private Control _training = null!,_trainingProgress = null!;
    private TextureRect _trainingBar = null!;
    public Button TrainButton(int slot) => _trainButtons[slot];
    public Button QueueButton(int index) => _queueButtons[index];
    public bool TrainingVisible => _training.IsVisibleInTree();
    public float TrainingProgressWidth => _trainingProgress.Size.X;
    public bool TrainingBarLoaded => _trainingBar.Texture != null;
    public bool PaletteOnlyTraining => _trainButtons.Any(b => b.Visible) && !_buttons.Values.Concat(_abilities).Concat(_constructionButtons)
        .Concat([_buildButton,_backButton,_cancelConstructionButton]).Any(b => b.Visible);

    private void CreateProductionControls()
    {
        for (int slot = 0; slot < _trainButtons.Length; slot++)
        {
            int chosen = slot;
            var button = new Button { Position = new(788+slot*56,34),Size = new(54,54),FocusMode = FocusModeEnum.None,ExpandIcon = true,Visible = false };
            Skin(button); button.Pressed += () => TrainingChosen?.Invoke(chosen);
            _trainButtons[slot] = button; _content.AddChild(button);
        }
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

    public void RefreshProduction(SimWorld world,ProductionBuilding? producer)
    {
        _training.Visible = producer is { Queue.Count: > 0 };
        for (int slot = 0; slot < _trainButtons.Length; slot++)
        {
            var button = _trainButtons[slot];
            button.Visible = producer != null && slot < producer.Units.Length;
            if (!button.Visible) continue;
            var rules = world.ProductionRules[producer!.Units[slot]];
            button.Disabled = !world.CanTrain(0,producer.Id,slot,out string reason);
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.DisplayName));
            button.TooltipText = $"Train {rules.DisplayName}\n{rules.FoodCost} food · {rules.OreCost} ore · population {rules.Population} · {rules.DurationTicks/20}s"
                + $"\nClick again to queue up to {SimWorld.ProductionQueueLimit}. Click a portrait in training or press Backspace to cancel for a full refund."
                + (button.Disabled ? "\n"+reason : "");
        }
        if (producer == null) return;
        string rally = producer.RallyCell >= 0 ? "Rally point set · right-click to move it" : "Right-click the battlefield to set a rally point";
        if (producer.Queue.Count == 0) { _order.Text = "Your structure · "+rally; return; }
        var active = world.ProductionRules[producer.Queue[0].Unit];
        int percent = producer.ProgressTicks*100/active.DurationTicks;
        _order.Text = $"Training {active.DisplayName} {percent}%"+(producer.Queue.Count > 1 ? $" · {producer.Queue.Count-1} queued" : "")+" · "+rally;
        _trainingBar.Texture ??= _art.GetValueOrDefault("console_g_progressbar");
        _trainingProgress.Size = new(185f*producer.ProgressTicks/active.DurationTicks,6);
        for (int index = 0; index < _queueButtons.Length; index++)
        {
            var button = _queueButtons[index]; button.Visible = index < producer.Queue.Count;
            if (!button.Visible) continue;
            var rules = world.ProductionRules[producer.Queue[index].Unit];
            button.Icon = _art.GetValueOrDefault(PortraitName(rules.DisplayName));
            button.TooltipText = (index == 0 ? $"Training {rules.DisplayName} · {percent}%" : $"Queued {rules.DisplayName}")
                + $"\nClick to cancel and receive {rules.FoodCost} food and {rules.OreCost} ore";
        }
    }
}
