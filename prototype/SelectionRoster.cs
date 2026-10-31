using Godot;

public partial class CommandPanel
{
    public Rect2 SelectionRosterRect => _rosterScroll.GetGlobalRect();
    public int SelectionScrollOffset { get => _rosterScroll.ScrollVertical; set => _rosterScroll.ScrollVertical = value; }
    public bool SelectionScrollbarVisible => _rosterScroll.IsVisibleInTree() && _rosterScroll.GetVScrollBar().IsVisibleInTree();

    private void CreateSelectionRoster()
    {
        _rosterScroll = new ScrollContainer { Name = "SelectionRoster",Position = new(265,84),Size = new(505,104),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            MouseFilter = MouseFilterEnum.Stop,MouseForcePassScrollEvents = false,FocusMode = FocusModeEnum.None,DrawFocusBorder = false,
            ScrollVerticalCustomStep = 53 };
        _rosterScroll.AddThemeStyleboxOverride("panel",new StyleBoxEmpty()); _content.AddChild(_rosterScroll);
        var scrollbar = _rosterScroll.GetVScrollBar();
        scrollbar.FocusMode = FocusModeEnum.None; scrollbar.MouseForcePassScrollEvents = false;
        scrollbar.AddThemeStyleboxOverride("scroll",new StyleBoxFlat { BgColor = new Color("3e3828"),ContentMarginLeft = 4,ContentMarginRight = 4 });
        scrollbar.AddThemeStyleboxOverride("scroll_focus",new StyleBoxEmpty());
        foreach (var (state,color) in new[] {("grabber","92815f"),("grabber_highlight","b4a075"),("grabber_pressed","c7b899")})
            scrollbar.AddThemeStyleboxOverride(state,new StyleBoxFlat { BgColor = new Color(color),ContentMarginLeft = 4,ContentMarginRight = 4,
                ContentMarginTop = 6,ContentMarginBottom = 6 });
        using var pixel = Image.CreateEmpty(1,1,false,Image.Format.Rgba8); pixel.Fill(Colors.Transparent);
        var noArrow = ImageTexture.CreateFromImage(pixel);
        foreach (string icon in new[] {"increment","increment_highlight","increment_pressed","decrement","decrement_highlight","decrement_pressed"})
            scrollbar.AddThemeIconOverride(icon,noArrow);
        _roster = new Control { CustomMinimumSize = new(496,0),MouseFilter = MouseFilterEnum.Ignore };
        _rosterScroll.AddChild(_roster);
    }
}
