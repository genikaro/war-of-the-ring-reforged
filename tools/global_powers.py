"""Global Fate powers of both sides, with the original GlobalSpells rows and spell graphs as provenance.

Timers are seconds at 20 simulation ticks/second. Blessed Wind uses its spell's
20-second timer, rather than the GlobalSpells row's otherwise unused 30 field.
Global recharge is adapted as a per-power 10-second cooldown from release.
"""
import math
from pathlib import PureWindowsPath
from elven_abilities import graph

ORDER = [4, 6, 5, 3, 1]  # Captured Good Fate menu: Q W E R, then A.
# The Evil menu follows the manual's hotkey table (Q Summon Bog, W Murder of Crows, E Vile Surge, R Summon Obelisk, A Summon Balrog).
EVIL_ORDER = [7, 8, 2, 9, 0]
EVIL_DESCRIPTIONS = {
    7: 'Raises a marsh in 10 m for 15 seconds. Enemy units standing in it move and attack at half speed.',
    8: 'A murder of crows harries the 12 m area for 22 seconds: enemy missile weapons there do 1% of their damage.',
    2: 'Heals friendly units in 12 m for 200 health and refreshes every ability they have.',
    9: 'Raises an Obelisk with 5000 health. Friendly units within 15 m take half of any damage; the Obelisk soaks the other half. '
       'It loses 1.5% of its health each second and falls when its health is gone.',
    0: 'Summons a controllable Balrog for 120 seconds. Requires clear ground and costs no population.',
}


def export(resolver):
    db = resolver.db
    result = []
    descriptions = {
        4: 'Blinds enemies in 10 m for 10 seconds. They can move but cannot attack.',
        6: 'Creates brambles in 8 m for 25 seconds. Moving enemies lose 35 HP per second; standing still avoids damage.',
        5: 'Permanently grants a friendly hero 35% more damage, +2 armor and 50% more maximum health. That hero cannot be summoned again after death.',
        3: 'Grants friendly missile troops in 10 m knockback on their projectiles for 20 seconds.',
        1: 'Summons a controllable Giant Ent for 120 seconds. Requires clear ground and costs no population.',
    }
    for faction, order in (('good', ORDER), ('evil', EVIL_ORDER)):
        descriptions = {**descriptions, **EVIL_DESCRIPTIONS} if faction == 'evil' else descriptions
        for slot, ident in enumerate(order):
            result.append(one(db, faction, slot, ident, descriptions))
    return result


def one(db, faction, slot, ident, descriptions):
    if True:
        row = db['GlobalSpells'].get(ident)
        spell = db['Spells'].get(row['Spell']) if row['Spell'] >= 0 else None
        actions = graph(db, spell) if spell else []
        typed = lambda suffix: [a for e, a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix)]
        timers = typed('_START_TIMER')
        cursor = db['Cursors'].get(row['TargetCursor']) if row['TargetCursor'] >= 0 else None
        item = dict(Id=ident, Slot=slot, Name=row['Name'], Icon=PureWindowsPath(db['UVAs'].get(row['Portrait'])['Filename']).stem,
                    Cursor=PureWindowsPath(cursor['Filename']).stem if cursor else 'cursor_e_move' if faction == 'evil' else 'cursor_g_move',
                    Faction=faction,
                    FateCost=row['CostPower'], RadiusMm=round(row['AreaOfEffectRadius']*1000),
                    DurationTicks=round((timers[0]['Float_01'] if timers else row['Duration'])*20),
                    RechargeTicks=row['RechargeTime']*20, Description=descriptions[ident],
                    SoundEvents=sorted({a['SoundEventType'] for a in typed('_PLAY_SOUND_EVENT')}),
                    Source={'Global': row.values, 'Spell': spell.values if spell else None,
                            'Actions': [{'Event': e.name, 'Type': db['SpellActions'].get(a['SpellActionType']).name, 'Values': a.values}
                                        for e, a in actions]})
        if ident == 6:
            item['DpsMilli'] = round(-typed('_UPDATE_HEALTH')[0]['Float_01']*1000)
        if ident == 5:
            item.update(DamagePpm=round(typed('_APPLY_DAMAGE_MULTIPLIER')[0]['Float_01']*1_000_000),
                        HealthPpm=round(typed('_APPLY_HITPOINTS_MODIFIER')[0]['Float_01']*1_000_000),
                        ArmorMilli=round(typed('_SET_ARMOR')[0]['Float_01']*1000))
        if ident == 3:
            knock = typed('_SET_MISSILE_WEAPON_KNOCKBACK')[0]
            item.update(KnockbackSpeedMm=knock['KnockbackForce']*1000, KnockbackAngle=round(knock['KnockbackAngle']))
        if ident == 1:
            item['SummonUnit'] = 'UNIT_G_GIANT_ENT'
        if ident == 0:
            item['SummonUnit'] = 'UNIT_E_HERO_BALROG'
        if ident == 7:
            # Units that enter the 10 m radius get the child spell's timescale multiplier (0.5) until they leave or the 15 s timer ends.
            item.update(DurationTicks=round(typed('_START_TIMER')[0]['Float_01']*20),
                        TimeScalePpm=round(next(a for e, a in graph(db, db['Spells'].get(next(x for e, x in actions if db['SpellActions'].get(x['SpellActionType']).name.endswith('_SPAWN_CHILD_SPELL'))['SpellType']))
                                                if db['SpellActions'].get(a['SpellActionType']).name.endswith('_APPLY_TIMESCALE_MULTIPLIER'))['Float_01']*1_000_000))
        if ident == 8:
            child = db['Spells'].get(next(x for e, x in actions if db['SpellActions'].get(x['SpellActionType']).name.endswith('_SPAWN_CHILD_SPELL'))['SpellType'])
            multiplier = next(a for e, a in graph(db, child) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER'))
            item.update(DurationTicks=round(typed('_START_TIMER')[0]['Float_01']*20), MissilePpm=round(multiplier['Float_01']*1_000_000))
        if ident == 2:
            item.update(HealMilli=round(typed('_APPLY_HEALTH')[0]['Float_01']*1000), DurationTicks=0)
        if ident == 9:
            # Build-164: the row's own columns. ObjectType 561 (OBJECT_FATE_POWER_OBELISK) gives the 5000 health;
            # DegenerationRate is "Percent per second" (lqr schema comment); DamageUnit / DamageObelisk split a hit on a
            # friendly unit in AreaOfEffectRadius between the unit and the Obelisk. The row's Duration (6000) is no timer
            # the decay could reach, so the area lasts until the decay alone would empty the health.
            health = db['Objects'].get(row['ObjectType'])['MaxHealth']
            decay = round(row['DegenerationRate']*10_000)
            item.update(ObjectHealth=health, DegenerationPpm=decay,
                        DamageUnitPpm=round(row['DamageUnit']*1_000_000), DamageObeliskPpm=round(row['DamageObelisk']*1_000_000),
                        DurationTicks=math.ceil(1_000_000*20/decay))
            item['Source']['Object'] = db['Objects'].get(row['ObjectType']).values
        return item


def dropped_objects(output, resolver):
    """The Obelisk's model (ObjectType 561 -> ModelEvent 567 -> MODEL_OBELISK), drawn where the power is cast."""
    from lms2gltf import convert_model
    obj = resolver.db['Objects'].get(resolver.db['GlobalSpells'].get(9)['ObjectType'])
    event = resolver.db['ModelEvents'].get(obj['ModelEvent'])
    model = resolver.db['Models'].get(event['AssociatedModelResource1'])
    filename = 'objects/global-powers/' + model.name + '.glb'
    target = output / filename
    target.parent.mkdir(parents=True, exist_ok=True)
    convert_model(resolver, model.name, str(target))
    return {'obelisk': {'File': filename, 'Object': obj.id, 'Model': model.name, 'Invisible': bool(obj['IsInvisible'])}}


def visuals(output, resolver, trace):
    from healing_visual import export_sprite_effect
    result = {}
    for ident in [225, 226, 400, 285, 286, 287, 289, 291, 191, 254]:
        data = export_sprite_effect(output, resolver, trace, resolver.db['Effects'].get(ident).name, 'global-effects/'+str(ident), clip_atlas_edges=ident == 285)
        def norm(name):
            return name.lower().removeprefix('e_').removeprefix('e').replace('_','').replace(' ','')
        active = {norm(e['name']) for e in data['Source']['Data']['particle_emitters']}
        data['Layers'] = [layer for layer in data['Layers'] if norm(layer['Name']) in active or layer['Name'].lower().replace('_','').replace(' ','') in active or ident == 286 and layer['Name'] == 'Glow']
        if ident == 287:
            from lms2gltf import convert_model
            obj = resolver.db['Objects'].get(556)
            event = resolver.db['ModelEvents'].get(obj['ModelEvent'])
            model = resolver.db['Models'].get(event['AssociatedModelResource1'])
            filename = 'combat/global-effects/brambles.glb'
            convert_model(resolver, model.name, str(output/filename))
            data['AreaModel'] = filename
            data['Source']['Object'] = obj.values
            data['Source']['ModelEvent'] = event.values
            trace(resolver.db.files['objects']); trace(resolver.db.files['modelevents'])
        if data['Layers']:
            result[str(ident)] = data
    return result
