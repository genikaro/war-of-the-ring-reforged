namespace Reforged.Simulation;

public sealed record SimProjectile(int Id,int Unit,int Target,int Attack,int Hit,long DamageMilli,
    int StartTick,int ReleaseTick,int ImpactTick,int X,int Y,int Z,int TargetX,int TargetY,int TargetZ,int Site = -1,int Source = -1,
    int ChainTargets = 0,int ChainRangeMm = 0,int ChainScalePpm = 1_000_000,long RawDamageMilli = 0,int ArmorDurationPpm = 0,
    int FirePpm = 0,string Style = "",int RotationStartTick = -1,AbilityDefinition? HeroAbility = null,AbilityDefinition? Wind = null,int SourceTeam = -1,bool Seeking = false,string Visited = "",AbilityDefinition? Poison = null);

public sealed partial class SimWorld
{
    public List<SimProjectile> Projectiles { get; } = [];
    private int _projectileSequence;

    private void ReleaseProjectile(SimUnit unit,SimUnit target,int hit,long damage)
    {
        int y = unit.Y+unit.Definition.BodyHeightMm*2/3,targetY = target.Y+target.Definition.BodyHeightMm/2;
        long squared = DistanceSquared(unit.X,unit.Z,target.X,target.Z)+(long)(y-targetY)*(y-targetY);
        long distance = IntegerMotion.SqrtCeiling(squared);
        int travel = (int)Math.Max(1,(distance*TicksPerSecond+unit.Definition.ProjectileSpeedMmPerSecond-1)/unit.Definition.ProjectileSpeedMmPerSecond);
        var axe = unit.AxeAbility;
        var blades = unit.BladesAbility;
        // Murder of Crows: the unit's missiles do a fraction of their damage while the crows are on it.
        if (unit.CrowsUntil > Tick && unit.CrowsPower != null) damage = damage*unit.CrowsPower.MissilePpm/1_000_000;
        int duration = unit.Definition.HitDurationPpm();
        long raw = PlaceDamage(unit,unit.Definition.HitDamageAtRate(unit.MeleeDamageRate),ranged: true,building: false);
        int armorDuration = (int)PlaceDamage(unit,duration,ranged: true,building: false);
        var chaining = blades ?? axe;
        int chain = chaining != null && squared <= (long)chaining.ChainStartRangeMm*chaining.ChainStartRangeMm ? chaining.ChainTargets : 0;
        Projectiles.Add(new(_projectileSequence++,unit.Id,target.Id,unit.AttackVariant,hit,damage,unit.AttackStartTick,
            Tick,Tick+travel,unit.X,y,unit.Z,target.X,targetY,target.Z,ChainTargets: chain,ChainRangeMm: chaining?.RangeMm ?? 0,
            ChainScalePpm: chaining?.BonusScale ?? 1_000_000,Seeking: blades != null,Visited: blades != null ? target.Id.ToString() : "",RawDamageMilli: raw,ArmorDurationPpm: armorDuration,Wind: unit.BlessedWindUntil > Tick ? unit.BlessedWindPower?.StatusAbility : null,
            Style:unit.FlameArrow!=null ? "evil-flame-arrows" : ""));
        CombatEvents.Add(new(Tick,CombatEventKind.Release,unit.Id,target.Id,unit.AttackVariant,hit,0,
            unit.AttackStartTick,unit.X,y,unit.Z));
    }

    private void AdvanceProjectiles()
    {
        foreach (var arrow in Projectiles.Where(p => p.ImpactTick <= Tick).ToArray())
        {
            // Released arrows survive replacement orders and the shooter's death.
            // Target following is a prototype policy; dead targets consume arrows.
            if (arrow.Site >= 0)
            {
                if (ConstructionSites.TryGetValue(arrow.Site,out var site) && site.Team >= 0 && site.Health > 0)
                {
                    if (arrow.Unit >= 0) Ignite(site,arrow);
                    DamageSite(arrow.Unit >= 0 ? Units[arrow.Unit] : null,site,arrow.Attack,arrow.Hit,arrow.DamageMilli,arrow.StartTick,
                        source:arrow.Source,sourceTeam:arrow.SourceTeam);
                }
            }
            else if (Units[arrow.Target] is { Present: true } victim)
            {
                // Guardian Wind (build-58): a warded unit takes its share of missile damage when the missile lands.
                ApplyHit(arrow.Unit >= 0 ? Units[arrow.Unit] : null,victim,arrow.Attack,arrow.Hit,
                    victim.MissileWardUntil > Tick ? arrow.DamageMilli*victim.MissileWardPpm/1_000_000 : arrow.DamageMilli,arrow.StartTick,arrow.Source,spell:arrow.HeroAbility != null || arrow.Poison?.Kind==AbilityKind.PoisonDart,
                    experiencePpm:arrow.HeroAbility?.ExperiencePpm);
                if (arrow.Poison is {} poison) InflictWeaponPoison(arrow.Unit,arrow.Source,victim,poison);
                if (arrow.HeroAbility is { } skill)
                {
                    ApplyHeroKnockback(Units[arrow.Unit],victim,skill);
                    AbilityEvents.Add(new(Tick,AbilityEventKind.Trueshot,arrow.Unit,victim.Id,skill.Slot,HealthMilli:arrow.DamageMilli));
                }
                else if (arrow.Seeking) RicochetBlades(arrow,victim);
                else ContinueAxe(arrow,victim);
                if (arrow.Wind is {} wind && arrow.Unit >= 0) ApplyHeroKnockback(Units[arrow.Unit],victim,wind);
            }
            Projectiles.Remove(arrow);
        }
    }

    /// <summary>Whether the weapon behind a hit has the source CanCriticallyKill flag: a ranged unit's contact
    /// attacks use its melee weapon, its missiles and every other unit's attacks its main weapon, a tower its own.</summary>
    private bool CriticalBlow(SimUnit? unit,int attack,int source) => unit != null
        ? attack >= 0 && attack < unit.Definition.Attacks.Length && (unit.Definition.IsRanged && !unit.Definition.FiresProjectile(attack)
            ? unit.Definition.CriticalMeleeWeapon : unit.Definition.CriticalWeapon)
        : ConstructionSites.TryGetValue(source,out var tower) && DefenseWeapon(tower,attack)?.CriticalKill == true;

    /// <summary>A hit from a unit, or from a defending building (no unit, <paramref name="source"/> the site).</summary>
    private void ApplyHit(SimUnit? unit,SimUnit target,int attack,int hit,long damage,int startTick,int source = -1,bool spell = false,int? experiencePpm = null,int? fatePpm = null)
    {
        // Build-141: a sneaking Gollum takes the spell's damage multiplier, and his first blow starts the second that ends the sneak.
        if (target.Sneaking) damage = damage*target.DamageTakenPpm/1_000_000;
        if (unit is { Sneaking: true,SneakEndTick: < 0 } && !spell && damage > 0 && unit.SneakAbility is { } sneak) unit.SneakEndTick = Tick+sneak.DelayTicks;
        if (target.ProtectionUntil > Tick && target.ProtectionMilli > 0)
        {
            long absorbed = Math.Min(damage,target.ProtectionMilli);
            target.ProtectionMilli -= absorbed; damage -= absorbed;
        }
        damage = SoakByObelisk(target,damage);
        damage = Math.Min(damage,target.HealthMilli); target.HealthMilli -= damage;
        if (unit!=null) EarnHeroExperience(unit,target.Team,damage,building:false,experiencePpm ?? (spell ? 0 : WeaponExperiencePpm(unit,attack)));
        if (unit!=null && damage>0) RecordHostileDamage(unit,target.Team,target.X,target.Z,IsAiWorker(target));
        // Build-76: the damage earns the attacker's side Fate, measured where the unit or tower stands.
        if (unit != null) EarnFate(unit.Team,target.Team,unit.X,unit.Z,damage,fatePpm ?? 1_000_000);
        else if (ConstructionSites.TryGetValue(source,out var tower) && DefenseWeapon(tower,attack) is { } weapon)
            EarnFate(tower.Team,target.Team,tower.X,tower.Z,damage,fatePpm ?? weapon.FatePpm);
        // Build-130: a critical weapon's killing blow bursts a critically killable unit (presentation reads the flag).
        if (target.Health <= 0 && damage > 0 && !spell && target.Definition.CriticallyKillable && CriticalBlow(unit,attack,source))
            target.CriticalDeath = true;
        if (!spell) CombatEvents.Add(new(Tick,CombatEventKind.Hit,unit?.Id ?? -1,target.Id,attack,hit,damage,startTick,target.X,target.Y,target.Z,source));
        if (unit != null && !spell) { ApplyShieldBreak(unit,target,attack); ApplyEvilWeaponHit(unit,target,attack,damage); }
        if (target.Health <= 0)
        {
            CancelAbility(target);
            if (unit != null && damage > 0 && target.Team != unit.Team) unit.KillCount++;
            // A tower's arrow credits the tower's side, while the tower stands.
            if (damage > 0) RecordDeath(unit?.Team ?? (ConstructionSites.TryGetValue(source,out var tower) ? tower.Team : -1),target);
            CancelStrike(target);
        }
        if (unit == null) RetaliateAgainstSite(target,source);
        else if (unit.Present) Retaliate(target,unit);
    }
}
