using Godot;

/// <summary>The player's choices on the in-game Settings screen (build-96), kept in user://settings.cfg. The Reforged
/// tab picks an edition: Purist switches every Reforged improvement off to play as close to the 2003 original as the
/// prototype allows, Reforged switches them all on, and Custom keeps the player's own choice for each one.</summary>
public sealed class GameSettings
{
    public enum Edition { Purist,Reforged,Custom }

    /// <summary>A modern improvement over the original that the Reforged tab can switch off. Each build lists its
    /// own here and asks <see cref="IsOn"/> before applying it.</summary>
    public sealed record ReforgedOption(string Key,string Name,string Description);
    public static readonly ReforgedOption[] ReforgedOptions = [
        new(Reforged.Presentation.BloodIntensity.Option,"Scaled blood",
            "Harder hits throw more blood, and units bleed more often the more they are wounded. Off: every hit bleeds, as in the original.")];

    public static readonly (Edition Edition,string Name,string Description)[] Editions = [
        (Edition.Purist,"Purist Edition","Every Reforged improvement off, for play as close to the original game as possible."),
        (Edition.Reforged,"Reforged Edition","Every Reforged improvement on."),
        (Edition.Custom,"Custom Edition","Choose each improvement yourself.")];

    public const string DefaultPath = "user://settings.cfg";
    public const int DefaultCursorSizePercent = 50;
    public static IReadOnlyList<int> CursorSizes { get; } = Array.AsReadOnly(new[] { 50,75,100 });
    private int _cursorSizePercent = DefaultCursorSizePercent;
    public int CursorSizePercent
    {
        get => _cursorSizePercent;
        set => _cursorSizePercent = CursorSizes.Contains(value) ? value : DefaultCursorSizePercent;
    }
    public bool PlayerColoredBars { get; set; } = true;
    public bool OverheadBars { get; set; } = true;
    /// <summary>Build-129: hit blood, death blood pools and bloody weapons, the effects and decals the source flags
    /// IsMature ("will not be seen if player turns off maturity in the options").</summary>
    public bool Blood { get; set; } = true;
    public bool Muted { get; set; }
    private double _masterVolume = 80,_musicVolume = 45,_effectsVolume = 80,_voiceVolume = 90;
    public double MasterVolume { get => _masterVolume; set => _masterVolume = Volume(value,80); }
    public double MusicVolume { get => _musicVolume; set => _musicVolume = Volume(value,45); }
    public double EffectsVolume { get => _effectsVolume; set => _effectsVolume = Volume(value,80); }
    public double VoiceVolume { get => _voiceVolume; set => _voiceVolume = Volume(value,90); }
    private static double Volume(double value,double fallback) => double.IsFinite(value) ? Math.Clamp(value,0,100) : fallback;
    public Edition ReforgedEdition { get; set; } = Edition.Reforged;
    /// <summary>The improvements switched on in the Custom edition; one a later build adds starts on.</summary>
    public HashSet<string> CustomOn { get; private set; } = [.. ReforgedOptions.Select(o => o.Key)];

    public bool IsOn(string key) => ReforgedEdition switch {
        Edition.Purist => false,Edition.Reforged => true,_ => CustomOn.Contains(key) };

    public GameSettings Copy() => new() { PlayerColoredBars = PlayerColoredBars,OverheadBars = OverheadBars,Blood = Blood,CursorSizePercent = CursorSizePercent,
        ReforgedEdition = ReforgedEdition,CustomOn = [.. CustomOn],Muted = Muted,
        MasterVolume = MasterVolume,MusicVolume = MusicVolume,EffectsVolume = EffectsVolume,VoiceVolume = VoiceVolume };

    public static GameSettings Load(string path = DefaultPath)
    {
        var settings = new GameSettings(); var config = new ConfigFile();
        if (config.Load(path) != Error.Ok) return settings;
        settings.PlayerColoredBars = config.GetValue("health_bars","player_colored",true).AsBool();
        settings.OverheadBars = config.GetValue("health_bars","overhead",true).AsBool();
        settings.Blood = config.GetValue("game","blood",true).AsBool();
        settings.CursorSizePercent = config.GetValue("interface","cursor_size_percent",DefaultCursorSizePercent).AsInt32();
        settings.Muted = config.GetValue("audio","muted",false).AsBool();
        settings.MasterVolume = config.GetValue("audio","master",80.0).AsDouble();
        settings.MusicVolume = config.GetValue("audio","music",45.0).AsDouble();
        settings.EffectsVolume = config.GetValue("audio","effects",80.0).AsDouble();
        settings.VoiceVolume = config.GetValue("audio","voices",90.0).AsDouble();
        if (Enum.TryParse<Edition>(config.GetValue("reforged","edition","").AsString(),out var edition)) settings.ReforgedEdition = edition;
        foreach (var option in ReforgedOptions.Where(o => config.HasSectionKey("reforged_custom",o.Key)))
            if (config.GetValue("reforged_custom",option.Key).AsBool()) settings.CustomOn.Add(option.Key); else settings.CustomOn.Remove(option.Key);
        return settings;
    }

    public void Save(string path = DefaultPath)
    {
        var config = new ConfigFile();
        config.SetValue("health_bars","player_colored",PlayerColoredBars);
        config.SetValue("health_bars","overhead",OverheadBars);
        config.SetValue("game","blood",Blood);
        config.SetValue("interface","cursor_size_percent",CursorSizePercent);
        config.SetValue("audio","muted",Muted);
        config.SetValue("audio","master",MasterVolume);
        config.SetValue("audio","music",MusicVolume);
        config.SetValue("audio","effects",EffectsVolume);
        config.SetValue("audio","voices",VoiceVolume);
        config.SetValue("reforged","edition",ReforgedEdition.ToString());
        foreach (var option in ReforgedOptions) config.SetValue("reforged_custom",option.Key,CustomOn.Contains(option.Key));
        config.Save(path);
    }
}
