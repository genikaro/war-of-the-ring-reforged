namespace Reforged.Presentation;

public sealed record StructureDetails(int Id,int Team,string Name,string Portrait,int Health,int MaxHealth,string Armor,
    int ProgressPercent,string Resource,int ResourceId)
{
    public bool UnderConstruction => ProgressPercent < 100;
}
