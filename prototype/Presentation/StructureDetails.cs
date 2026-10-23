namespace Reforged.Presentation;

public sealed record StructureDetails(int Id,int Team,string Name,string Portrait,int Health,int MaxHealth,string Armor,
    int ProgressPercent,string Resource,int ResourceId,bool HasHealth = true,int SiteId = -1,string Place = "")
{
    public bool UnderConstruction => ProgressPercent < 100;
}
