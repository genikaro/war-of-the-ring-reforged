"""Hero progression from Units.lqr, rather than the obsolete five-row HeroExperiences table.

Rings.exe 0x43851e adds each next threshold to the previous boundary and preserves
the current HP fraction on level-up (0x43acd7, 0x446769). Runtime uses integer XP.
"""
from decimal import Decimal, ROUND_HALF_UP


def scaled(value, scale=1000):
    return int((Decimal(str(value))*scale).to_integral_value(rounding=ROUND_HALF_UP))


def export(unit):
    if not unit['IsHero'] or unit['MaxExperienceLevel'] < 2:
        return None
    levels = [dict(Level=1,ExperienceMilli=0,HitPoints=0,ArmorMilli=0,DamageBonusPpm=0)]
    total = 0
    for level in range(2,unit['MaxExperienceLevel']+1):
        total += scaled(unit['ExperienceLevel%dThreshold' % level])
        levels.append(dict(Level=level,ExperienceMilli=total,
                           HitPoints=unit['ExperienceLevel%dHitpointsModifier' % level],
                           ArmorMilli=unit['ExperienceLevel%dArmorModifier' % level]*1000,
                           DamageBonusPpm=scaled(unit['ExperienceLevel%dDamageModifier' % level],1_000_000)))
    return dict(UnitDamagePpm=scaled(unit['ExperiencePerUnitDamagePercentage'],1_000_000),
                BuildingDamagePpm=scaled(unit['ExperiencePerBuildingDamagePercentage'],1_000_000),Levels=levels)


SOURCE = dict(Table='Units',Thresholds='Incremental XP costs, accumulated for each next level.',
              Executable='Rings.exe',AddExperience='0x43851e',HealthFraction='0x43acd7',MaxHealth='0x446769',
              Progress='0x438944',BuildingDamage='0x4371ca',SpellDamage='0x54e66e',
              Policy='Actual hostile damage after shields, capped at remaining health; weapon/spell and EXPERIENCE place multipliers. A single award resolves every crossed level.')
