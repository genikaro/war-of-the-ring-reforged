namespace Reforged.Simulation;

public sealed partial class SimUnit
{
    public HeroEffect? Darkness { get; internal set; }
    public HeroEffect? SavageInfluence { get; internal set; }
    public int ParalyzeSuppressedUntil { get; internal set; } = -1;
    public int TechniqueTimeScalePpm { get; internal set; } = 1_000_000;
    public int ParalyzeRangeMm { get; internal set; }
    public int SavageDamagePpm => SavageInfluence?.Ability.BonusScale ?? 1_000_000;
    public AbilityDefinition? FlameArrow => EnabledAbilities.FirstOrDefault(a => a.Kind == AbilityKind.FlameArrows);
}

public sealed partial class SimWorld
{
    public bool CanAffordAbility(SimUnit unit,AbilityDefinition ability) => ability.ActivateFoodCost+ability.ActivateOreCost == 0
        || Economies.TryGetValue(unit.Team,out var bank) && bank.FoodMilli >= ability.ActivateFoodCost*1000L && bank.OreMilli >= ability.ActivateOreCost*1000L;

    private bool? ApplyEvilUpgradeCast(SimUnit source,AbilityDefinition ability,SimUnit target)
    {
        if (ability.Kind == AbilityKind.PoisonDart)
        {
            // The source overrides the shot's direct damage to zero; the weapon applies its poison on impact.
            if (!CanAffordAbility(source,ability)) return false;
            var bank = Economies[source.Team]; bank.FoodMilli -= ability.ActivateFoodCost*1000L; bank.OreMilli -= ability.ActivateOreCost*1000L;
            int y=source.Y+source.Definition.BodyHeightMm*2/3,ty=target.Y+target.Definition.BodyHeightMm/2;
            long distance=IntegerMotion.SqrtCeiling(SeparationSquared(source,target)+(long)(y-ty)*(y-ty));
            int travel=(int)Math.Max(1,(distance*TicksPerSecond+HeroProjectileSpeedMm-1)/HeroProjectileSpeedMm);
            Projectiles.Add(new(_projectileSequence++,source.Id,target.Id,-1,0,0,Tick,Tick,Tick+travel,source.X,y,source.Z,target.X,ty,target.Z,
                Style:"evil-poison-dart",Poison:ability));
            return true;
        }
        if (ability.Kind is not (AbilityKind.SavageInfluence or AbilityKind.Darkness or AbilityKind.SprayVenom or AbilityKind.SweepingAxe)) return null;
        if (ability.Kind == AbilityKind.SprayVenom) source.ParalyzeSuppressedUntil=Tick+ability.DelayTicks;
        foreach (var unit in NearUnits(source.X,source.Z,ability.AreaRadiusMm).Where(u => u.Present
            && SeparationSquared(source,u) <= (long)ability.AreaRadiusMm*ability.AreaRadiusMm).OrderBy(u=>u.Id).ToArray())
        {
            if (ability.Kind == AbilityKind.SavageInfluence)
            {
                if (unit.Id==source.Id || unit.Team!=source.Team) continue;
                unit.HealthMilli=Math.Max(0,unit.HealthMilli-ability.HealMilli);
                // Build-163: an ally the price kills is its side's loss, like any fallen unit (the observer's Stats check counted one
                // death more than losses once bots' last stands put wounded slaves beside the casters).
                if (unit.Health<=0) { CancelAbility(unit); RecordDeath(source.Team,unit); CancelStrike(unit); continue; }
                if (unit.Present) unit.SavageInfluence=new(source.Id,ability,Tick+ability.DurationTicks);
                continue;
            }
            if (unit.Team<0 || unit.Team==source.Team || Rules.Pair(source.Definition.Id,unit.Definition.Id).Invincible) continue;
            if (ability.Kind == AbilityKind.SweepingAxe)
                ApplyHit(source,unit,-1,ability.Slot,ability.HealMilli,Tick,spell:true,experiencePpm:ability.ExperiencePpm);
            else if (ability.Kind == AbilityKind.Darkness)
            {
                if ((unit.Immunities & StatusProtection.Slow)==0) unit.Darkness=new(source.Id,ability,Tick+ability.DurationTicks);
                if (ability.Blinds && (unit.Immunities & StatusProtection.Blind)==0)
                { unit.BlindUntil=Math.Max(unit.BlindUntil,Tick+ability.DurationTicks); CancelStrike(unit); }
            }
            else if ((unit.Immunities & StatusProtection.Stun)==0
                && Math.Abs(UnitFacing.Delta(source.Facing,UnitFacing.FromDirection(unit.X-source.X,unit.Z-source.Z)))<=ability.ArcDegrees*500)
                StunWithUpgrade(source,unit,ability);
        }
        AbilityEvents.Add(new(Tick,AbilityEventKind.EvilUpgrade,source.Id,source.Id,ability.Slot));
        return true;
    }

    private void StunWithUpgrade(SimUnit source,SimUnit target,AbilityDefinition ability)
    {
        Apply(new(Tick,_sequence++,target.Team,[target.Id],-1,Kind:OrderKind.Stop));
        target.Dazed=new(source.Id,ability,Tick+ability.DurationTicks);
        AbilityEvents.Add(new(Tick,AbilityEventKind.Dazed,source.Id,target.Id,ability.Slot));
    }

    private void ApplyEvilWeaponHit(SimUnit source,SimUnit target,int attack,long damage)
    {
        if (damage<=0 || !target.Present || source.Team==target.Team || attack<0 || source.Definition.FiresProjectile(attack)) return;
        foreach (var ability in source.EnabledAbilities)
        {
            if (ability.Kind==AbilityKind.PoisonWeapon) InflictWeaponPoison(source.Id,-1,target,ability);
            if (ability.Kind==AbilityKind.Paralyze && Tick>=source.ParalyzeSuppressedUntil && (target.Immunities & StatusProtection.Stun)==0)
                StunWithUpgrade(source,target,ability);
        }
    }

    private void InflictWeaponPoison(int source,int site,SimUnit target,AbilityDefinition ability)
    {
        if (!target.Present || (target.Immunities & StatusProtection.Poison)!=0
            || source>=0 && Rules.Pair(Units[source].Definition.Id,target.Definition.Id).Invincible) return;
        target.Poisons.RemoveAll(p=>p.Ability.Id==ability.Id);
        target.Poisons.Add(new(source,ability,Tick+ability.DurationTicks,site));
        AbilityEvents.Add(new(Tick,AbilityEventKind.Poisoned,source,target.Id,ability.Slot));
    }

    private void UpdateEvilUpgrades()
    {
        foreach (var unit in ActiveUnits)
        {
            if (unit.ParalyzeSuppressedUntil<=Tick) unit.ParalyzeSuppressedUntil=-1;
            if (!unit.Present || unit.Darkness?.Until<=Tick) unit.Darkness=null;
            if (!unit.Present || unit.SavageInfluence?.Until<=Tick) unit.SavageInfluence=null;
        }
    }

    private string EvilUpgradesDigest() => ":evil-upgrades:"+string.Join(';',Units.Select(u=>
        $"{u.Id}/{u.Darkness?.Source}/{u.Darkness?.Ability.Id}/{u.Darkness?.Until}/{u.SavageInfluence?.Source}/{u.SavageInfluence?.Until}/{u.ParalyzeSuppressedUntil}/{u.TechniqueTimeScalePpm}"));

    private void AiEvilUnitAbilities(AiPlayer ai,SimUnit[] mine)
    {
        if (!ai.Personality.UsesSpecialAbilities || FactionOf(ai.Team)!=Evil) return;
        foreach (var unit in mine.Where(u=>!u.Definition.Hero && u.Present && !u.Casting && u.Order is not (OrderKind.UseAbility or OrderKind.Build or OrderKind.PlaceBuilding)))
            foreach (var ability in unit.EnabledAbilities.Where(a=>!a.Passive && unit.AbilityReadyAt[a.Slot]<=Tick))
            {
                var enemies=NearUnits(unit.X,unit.Z,Math.Max(ability.RangeMm,ability.AreaRadiusMm)).Where(e=>e.Present && e.Team>=0 && e.Team!=ai.Team
                    && CanSeeUnit(ai.Team,e) && !Rules.Pair(unit.Definition.Id,e.Definition.Id).Invincible).OrderBy(e=>e.Id).ToArray();
                if (ability.Kind==AbilityKind.PoisonDart && CanAffordAbility(unit,ability))
                {
                    var victim=enemies.Where(e=>e.Poisons.All(p=>p.Ability.Id!=ability.Id) && ValidAbilityTarget(unit,ability,e)
                        && (e.Immunities & StatusProtection.Poison)==0 && SeparationSquared(unit,e)<=(long)ability.RangeMm*ability.RangeMm)
                        .OrderByDescending(e=>e.HealthMilli).FirstOrDefault();
                    if (victim==null) continue;
                    Dispatch(new(Tick,_sequence++,ai.Team,[unit.Id],-1,victim.Id,OrderKind.UseAbility,AbilitySlot:ability.Slot)); break;
                }
                bool area=ability.Kind is AbilityKind.Darkness or AbilityKind.SprayVenom or AbilityKind.SweepingAxe;
                bool useful=area && enemies.Count(e=>SeparationSquared(unit,e)<=(long)ability.AreaRadiusMm*ability.AreaRadiusMm
                    && (ability.Kind!=AbilityKind.SprayVenom || Math.Abs(UnitFacing.Delta(unit.Facing,UnitFacing.FromDirection(e.X-unit.X,e.Z-unit.Z)))<=ability.ArcDegrees*500))>=2;
                if (ability.Kind==AbilityKind.SavageInfluence && enemies.Length>0)
                {
                    var allies=NearUnits(unit.X,unit.Z,ability.AreaRadiusMm).Where(a=>a.Present && a.Team==ai.Team && a.Id!=unit.Id
                        && SeparationSquared(unit,a)<=(long)ability.AreaRadiusMm*ability.AreaRadiusMm).ToArray();
                    useful=allies.Length>=3 && allies.All(a=>a.HealthMilli>ability.HealMilli+20000 && a.SavageInfluence==null);
                }
                if (!useful) continue;
                Dispatch(new(Tick,_sequence++,ai.Team,[unit.Id],-1,unit.Id,OrderKind.UseAbility,AbilitySlot:ability.Slot)); break;
            }
    }
}
