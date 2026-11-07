namespace Reforged.Presentation;

public sealed record StructureDetails(int Id,int Team,string Name,string Portrait,int Health,int MaxHealth,string Armor,
    int ProgressPercent,string Resource,int ResourceId,bool HasHealth = true,int SiteId = -1,string Place = "",string Attack = "",bool Burning = false,
    DefenseHudStats[]? Weapons = null,bool Obelisk = false)
{
    /// <summary>Build-172: the selection id of a team's standing Obelisk (structure ids are obstacle ids, never this low).</summary>
    public static int ObeliskId(int team) => int.MinValue+team;
    public bool UnderConstruction => ProgressPercent < 100;
}
