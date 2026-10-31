"""The five Evil heroes' battle gear (builds 135-137), decoded from the shipped spell graphs.

Every gear of Gollum, the Nazgul King, Saleme (Qamara), Grishnakh and Saruman is exported with its source name, icon, slot, cursor,
recharge, range and Fate price. Simulated gears carry the numbers their spell graph holds; the rest are listed as unsimulated with what
the graph does, so the hero panel shows the full set and nothing pretends to work. `graph` follows spawned child spells, `walk` also the
events a spell enables (Gollum's Sneak, Grishnakh's Torch chain their events that way).
"""
import math
from pathlib import PureWindowsPath

from elven_abilities import graph
from hero_abilities import linked

# Gear id -> the name the source gives it (BATTLE_GEAR_*), per hero; the manual and guide use the same names.
HEROES = {
    'UNIT_E_HERO_GOLLUM': {37: 'Sneak', 38: 'Surprise Attack', 20: 'Dark Tracking'},
    'UNIT_E_HERO_NAZGUL_KING': {8: 'Aura of Terror', 29: 'Shadow Walk', 36: 'Shadow Bind'},
    'UNIT_E_HERO_QAMARA': {39: 'Mystic Blades', 35: 'Poison Trap', 21: 'Blood Mark'},
    'UNIT_E_HERO_GRISHNAKH': {13: 'Speed of the Wolf', 34: 'Satchel Charge', 5: 'Torch'},
    'UNIT_E_HERO_SARUMAN': {46: 'Prison of Ice', 30: 'Invisible Force', 47: 'Pestilence', 18: 'Twisting Influence'},
}

# What each unsimulated gear does in the source graph (spell actions), in the words of the panel.
DESCRIPTIONS = {
    8: 'Enemies within 8 m lose 2 armor.',
    13: 'Grishnakh runs and strikes 50% faster for 10 seconds.',
}

# Kinds appended to AbilityKind in the game (Abilities.cs); Unsimulated is 8, ElvenSpeed 30, TerrorAura 35.
KIND_UNSIMULATED, KIND_ELVEN_SPEED, KIND_TERROR_AURA = 8, 30, 35
KIND_PRISON_OF_ICE, KIND_INVISIBLE_FORCE, KIND_PESTILENCE, KIND_TWISTING_INFLUENCE = 36, 37, 38, 39
KIND_SHADOW_BIND, KIND_SHADOW_WALK = 40, 41
KIND_MYSTIC_BLADES, KIND_BLOOD_MARK, KIND_POISON_TRAP = 42, 43, 44
KIND_TORCH, KIND_SATCHEL_CHARGE = 45, 46
KIND_SNEAK, KIND_SURPRISE_ATTACK, KIND_DARK_TRACKING = 47, 48, 49


def walk(db, spell, seen=None, events=None):
    """Like `graph`, and also the events that ENABLE_SPELL_AND_EVENT_TYPE actions switch on (they are not in the spell's own list)."""
    seen = set() if seen is None else seen
    events = set() if events is None else events
    result = []

    def visit(event):
        if event.id in events:
            return
        events.add(event.id)
        for action in linked(db['SpellAndEventAndActions'], event['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction'):
            result.append((event, action))
            if action['SpellType'] >= 0 and action['SpellType'] not in seen:
                seen.add(action['SpellType'])
                for e in linked(db['SpellAndEvents'], db['Spells'].get(action['SpellType'])['FirstSpellAndEvent'], 'NextSpellAndEvent'):
                    visit(e)
            if db['SpellActions'].get(action['SpellActionType']).name.endswith('_ENABLE_SPELL_AND_EVENT_TYPE') and action['SpellAndEventType'] >= 0:
                visit(db['SpellAndEvents'].get(action['SpellAndEventType']))
    seen.add(spell.id)
    for event in linked(db['SpellAndEvents'], spell['FirstSpellAndEvent'], 'NextSpellAndEvent'):
        visit(event)
    return result


def cast_clip(db, actions, model, skeleton):
    """The cast clip of the spell's PLAY_ANIMATION, its length in ticks and the tick of the toolscript event that applies the spell."""
    state = next(s for s in model['animation_states'] if s['id'] == next(
        a['UnitAnimType'] for e, a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith('_PLAY_ANIMATION')))
    choice = next(c for c in state['choices'] if c['weight'] > 0)
    clip = next(c for c in skeleton.clips if c.name == choice['clip_name'])
    tools = {e['ToolscriptType'] for e, a in actions if e['ToolscriptType'] >= 0}
    events = [model['events'][i] for i in choice['event_indices']]
    # A spell that fires a missile (Grishnakh's Torch) has no toolscript event: the clip's own missile release is its moment.
    release = next((e for e in events if e['type'] in tools), None) or next(e for e in events if e.get('name') == 'missile_release')
    ticks = math.ceil(clip.duration * 20)
    sounds = [a['SoundEventType'] for e, a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith('_PLAY_SOUND_EVENT')
              and a['SoundEventType'] >= 0]
    sounds += [model['events'][i]['sound'] for i in choice['event_indices'] if model['events'][i].get('name') == 'sound']
    return dict(CastClip=clip.name, CastTicks=ticks, EffectTick=min(ticks - 1, math.ceil(clip.duration * release['time'] * 20)),
                SoundEvents=sorted(set(sounds)))


def export(resolver, unit, model=None, skeleton=None):
    db = resolver.db
    abilities = []
    for gear_id in [unit['BattleGear%d' % i] for i in range(1, 5)]:
        if gear_id < 0:
            continue
        gear = db['BattleGear'].get(gear_id)
        spell = db['Spells'].get(gear['SpellType'])
        target = db['SpellValidTargets'].get(spell['ValidTargetType'])
        actions = graph(db, spell)

        def typed(suffix, _actions=actions):
            return [a for e, a in _actions if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix)]

        def excluded(_target=target):
            return [db['Units'].get(_target['ExcludeUnitType%d' % i]).name for i in (1, 2) if _target['ExcludeUnitType%d' % i] >= 0]
        # Usage 0 is a passive, 1 an active spell, 2 a toggle or instant one.
        active = spell['UsageType'] != 0
        cursor = db['Cursors'].get(spell['TargetCursor']) if spell['TargetCursor'] >= 0 else None
        ability = dict(Id=gear.id, Slot=gear['SpecificIndex'], Name=HEROES[unit.name][gear.id],
                       Icon=PureWindowsPath(db['UVAs'].get(gear['PortraitBorderless'])['Filename']).stem,
                       Passive=not active, Kind=KIND_UNSIMULATED, Description=DESCRIPTIONS.get(gear.id, ''),
                       RechargeTicks=math.ceil(spell['RechargeTime'] * 20), RangeMm=round(target['MaxSpecifiedRange'] * 1000) if target else 0,
                       Cursor=PureWindowsPath(cursor['Filename']).stem if cursor else '',
                       Purchasable=bool(gear['IsPurchasable']), PurchaseFateCost=gear['PurchaseCostPower'] if gear['IsPurchasable'] else 0,
                       ExperiencePpm=round(spell['ExperienceModifier'] * 1_000_000),
                       Source={'Gear': gear.values, 'Spell': spell.values, 'Target': target.values if target else None,
                               'Actions': [{'Event': e.name, 'Type': db['SpellActions'].get(a['SpellActionType']).name, 'Values': a.values} for e, a in actions]})
        if gear.id == 13:
            # APPLY_TIMESCALE_MULTIPLIER 1.5 for the 10 second timer, on the caster: the Elven Speed mechanism with a radius of its own body.
            ability.update(Kind=KIND_ELVEN_SPEED, Passive=False, BonusScale=round(typed('_APPLY_TIMESCALE_MULTIPLIER')[0]['Float_01'] * 1_000_000),
                           DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01'] * 20), AreaRadiusMm=500, RangeMm=0)
        elif gear.id == 8:
            # The child spell applies SET_ARMOR -2 to every enemy that enters the 8 m radius.
            child = db['Spells'].get(next(a for a in typed('_SPAWN_CHILD_SPELL'))['SpellType'])
            armor = next(a for e, a in graph(db, child) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_SET_ARMOR'))
            ability.update(Kind=KIND_TERROR_AURA, Passive=True, ArmorReductionMilli=round(-armor['Float_01'] * 1000), RangeMm=round(target['MaxSpecifiedRange'] * 1000))
        elif gear.id == 46:
            # SET_FROZEN 15 s on the target at the cast's toolscript event, and APPLY_DAMAGE 25; the Giant Ent and the Balrog cannot be targeted.
            ability.update(Kind=KIND_PRISON_OF_ICE, Passive=False, ExcludedUnits=excluded(), **cast_clip(db, actions, model, skeleton),
                           DurationTicks=math.ceil(typed('_SET_FROZEN')[0]['Float_01'] * 20), HealMilli=round(typed('_APPLY_DAMAGE')[0]['DamageAmount'] * 1000),
                           Description='Freezes an enemy within 15 m solid for 15 seconds and hurts it for 25.')
        elif gear.id == 30:
            # The child spell knocks back (force 10) and hurts (45) every non-hero enemy within 10 m of the point.
            child = db['Spells'].get(typed('_SPAWN_CHILD_SPELL')[0]['SpellType'])
            blast = [a for e, a in graph(db, child)]
            kinds = {db['SpellActions'].get(a['SpellActionType']).name: a for a in blast}
            knock, hurt = kinds['SPELL_ACTION_APPLY_KNOCKBACK'], kinds['SPELL_ACTION_APPLY_DAMAGE']
            ability.update(Kind=KIND_INVISIBLE_FORCE, Passive=False, **cast_clip(db, actions, model, skeleton),
                           AreaRadiusMm=round(knock['OccuranceRadius'] * 1000), KnockbackSpeedMm=round(knock['KnockbackForce'] * 1000),
                           KnockbackAngle=round(knock['KnockbackAngle']), HealMilli=round(hurt['DamageAmount'] * 1000),
                           Description='Hurls an unseen force at a spot within 15 m: enemy units (not heroes) within 10 m of it take 45 damage and are thrown back.')
        elif gear.id == 47:
            # Every enemy within 7.5 m gets the child spell: UPDATE_HEALTH -3 a second for 40 seconds, which never takes the last point.
            child = db['Spells'].get(next(a for a in typed('_SPAWN_CHILD_SPELL') if a['OccuranceRadius'] > 0)['SpellType'])
            drain = next(a for e, a in graph(db, child) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_UPDATE_HEALTH'))
            timer = next(a for e, a in graph(db, child) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_START_TIMER'))
            radius = next(a for a in typed('_SPAWN_CHILD_SPELL') if a['OccuranceRadius'] > 0)['OccuranceRadius']
            ability.update(Kind=KIND_PESTILENCE, Passive=False, **cast_clip(db, actions, model, skeleton), AreaRadiusMm=round(radius * 1000),
                           DurationTicks=math.ceil(timer['Float_01'] * 20), DpsMilli=round(-drain['Float_01'] * 1000),
                           Description='Sickens every enemy within 7.5 m of a spot within 15 m: 3 damage a second for 40 seconds, which never kills.')
        elif gear.id == 18:
            # CONVERT_UNIT_TEAM on the target at the toolscript event: any enemy but a hero, a Giant Ent or a Balrog joins Saruman for good.
            ability.update(Kind=KIND_TWISTING_INFLUENCE, Passive=False, ExcludedUnits=excluded(), **cast_clip(db, actions, model, skeleton),
                           Description='Bends the will of an enemy unit within 10 m (not a hero, a Giant Ent or a Balrog): it joins your army.')
        elif gear.id == 36:
            # Costs 1 Fate to use (ActivateCostPower). At the cast's toolscript event the child spell APPLY_MORPHs a Wraith (the only valid
            # target) into the Black Rider; at most eight Black Riders may stand at once (MaxUnitsOfType).
            child = db['Spells'].get(typed('_SPAWN_CHILD_SPELL')[0]['SpellType'])
            morph = next(a for e, a in graph(db, child) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_APPLY_MORPH'))
            ability.update(Kind=KIND_SHADOW_BIND, Passive=False, **cast_clip(db, actions, model, skeleton),
                           BonusTargets=[target['SpecificUnitType%d' % i] for i in range(1, 5) if target['SpecificUnitType%d' % i] >= 0],
                           MorphUnit=db['Units'].get(morph['MorphUnitType']).name, Limit=target['MaxUnitsOfType'], ActivateFateCost=gear['ActivateCostPower'],
                           Description='Turns one of your Wraiths within 5 m into a Black Rider for 1 Fate. At most eight Black Riders may stand at once.')
        elif gear.id == 29:
            # Instant (no cast clip): the Nazgul King and the Wraiths and Black Riders within 15 m of him (the affected list's radius) vanish, and
            # half a second later (START_TIMER 0.5) appear around a spot within 10 m of one of his War Posts (the 10 m decal).
            affected = next(a for a in typed('_ADD_TARGETS_TO_AFFECTED_UNITS_LIST'))
            carried = db['SpellValidTargets'].get(affected['ValidTargetTypeForUnitsInRadius'])
            sounds = [a['SoundEventType'] for a in typed('_PLAY_SOUND_EVENT')]
            ability.update(Kind=KIND_SHADOW_WALK, Passive=False, AreaRadiusMm=round(spell['MaxAreaOfEffectRange'] * 1000),
                           CarryRadiusMm=round(affected['OccuranceRadius'] * 1000), DelayTicks=math.ceil(typed('_START_TIMER')[0]['Float_01'] * 20),
                           BonusTargets=[carried['SpecificUnitType%d' % i] for i in range(1, 5) if carried['SpecificUnitType%d' % i] >= 0],
                           SoundEvents=sounds[:1], EndSoundEvents=sounds[1:],
                           Description='Instantly carries the Nazgul King and the Wraiths and Black Riders within 15 m of him to a spot within 10 m of one of your War Posts.')
        elif gear.id == 39:
            # Innate: SWITCH_MISSILE_WEAPON 64 on activation (combat_rules.INNATE_MISSILE_WEAPONS gives her unit that weapon). Its blades ricochet:
            # up to MaxChainTargets more enemies, each within MaxChainTargetDistance of the one before.
            weapon = db['Weapons'].get(typed('_SWITCH_MISSILE_WEAPON')[0]['WeaponType'])
            ability.update(Kind=KIND_MYSTIC_BLADES, Passive=True, ChainTargets=weapon['MaxChainTargets'], RangeMm=round(weapon['MaxChainTargetDistance'] * 1000),
                           ChainStartRangeMm=round(weapon['MaxChainStartDistance'] * 1000), BonusScale=round(weapon['ChainTargetDamageReduction'] * 1_000_000),
                           DpsMilli=round(weapon['CuttingDamagePerSecond'] * 1000), Purchasable=False, PurchaseFateCost=0,
                           Description='Saleme throws mystic blades for 20 damage up to 14 m. Each blade ricochets into up to five more enemies, each within 10 m of the one before, for 30% of the hit.')
            ability['Source']['Weapon'] = weapon.values
        elif gear.id == 21:
            # On a unit: the child spell's UPDATE_HEALTH +13 a second for 12 s. On a War Post (SpecificBuildingType1 1): the child spell's 15 s field of
            # 12 m heals every ally inside by 5 a second (its own child spell, UPDATE_HEALTH +5, lives while the ally stands in the radius).
            found = [(e, a) for e, a in walk(db, spell)]
            updates = [a for e, a in found if db['SpellActions'].get(a['SpellActionType']).name.endswith('_UPDATE_HEALTH')]
            timers = [a for e, a in found if db['SpellActions'].get(a['SpellActionType']).name.endswith('_START_TIMER')]
            post = db['SpellValidTargets'].get(22)
            ability.update(Kind=KIND_BLOOD_MARK, Passive=False, **cast_clip(db, actions, model, skeleton),
                           DpsMilli=round(updates[0]['Float_01'] * 1000), DurationTicks=math.ceil(timers[0]['Float_01'] * 20),
                           FieldDpsMilli=round(updates[1]['Float_01'] * 1000), FieldDurationTicks=math.ceil(timers[1]['Float_01'] * 20),
                           AreaRadiusMm=round(post['MaxSpecifiedRange'] * 1000),
                           Description='Heals an ally within 5 m by 13 a second for 12 seconds; on a War Post it heals the allies within 12 m by 5 a second for 15 seconds.')
        elif gear.id == 35:
            # Instant: the toolscript event of the drop clip lays the trap (DROP_OBJECT 530, at most three) at her feet. An enemy within 4 m springs it
            # and every enemy within 10 m takes the poison of the weapon the child spell applies (Poisons: 6 a second for 25 s).
            trap = db['Spells'].get(db['Objects'].get(typed('_DROP_OBJECT')[0]['Object'])['DefaultSpellObject'])
            inside = walk(db, trap)
            cloud = next(a for e, a in inside if a['OccuranceRadius'] > 0 and db['SpellActions'].get(a['SpellActionType']).name.endswith('_SPAWN_CHILD_SPELL'))
            hit = next(a for e, a in inside if db['SpellActions'].get(a['SpellActionType']).name.endswith('_APPLY_DAMAGE'))
            weapon = db['Weapons'].get(hit['WeaponType'])
            poison = db['Poisons'].get(weapon['PoisonType'])
            sprung = next(e for e, a in inside if e.name.endswith('_ONUNITENTEREDRADIUS'))
            ability.update(Kind=KIND_POISON_TRAP, Passive=False, **cast_clip(db, actions, model, skeleton),
                           ResponseRangeMm=round(db['SpellValidTargets'].get(sprung['ValidTargetForUnitInRadius'])['MaxSpecifiedRange'] * 1000),
                           AreaRadiusMm=round(cloud['OccuranceRadius'] * 1000), DpsMilli=round(poison['DamagePerSecond'] * 1000),
                           DurationTicks=math.ceil(poison['Duration'] / 50), Limit=db['Objects'].get(typed('_DROP_OBJECT')[0]['Object'])['MaxObjectsPerPlayer'],
                           Description='Lays a hidden trap at her feet (three at most). An enemy within 4 m springs it: every enemy within 10 m is poisoned for 6 a second for 25 seconds.')
            ability['Source']['Weapon'] = weapon.values
            ability['Source']['Poison'] = poison.values
        elif gear.id == 5:
            # SWITCH_MISSILE_WEAPON 54 and a missile attack on the target building: the torch flies (weapon 54, projectile 31) and sets it alight
            # (FireDamagePerSecond 30, far above anything a building resists) for its 100 cutting damage a second as one blow.
            weapon = db['Weapons'].get(typed('_SWITCH_MISSILE_WEAPON')[0]['WeaponType'])
            ability.update(Kind=KIND_TORCH, Passive=False, **cast_clip(db, actions, model, skeleton), RangeMm=round(weapon['MaximumRange'] * 1000),
                           HealMilli=round(weapon['CuttingDamagePerSecond'] * 1000), FirePpmPerSecond=round(weapon['FireDamagePerSecond'] * 1_000_000),
                           Description='Hurls a torch at an enemy building within 15 m, setting it alight at once.')
            ability['Source']['Weapon'] = weapon.values
        elif gear.id == 34:
            # Instant: the drop clip's toolscript event lays the satchel (DROP_OBJECT 538, two at most) at his feet. It explodes when its life ends
            # (5 s, EndLifeSpanSpell) or when the player sets it off (CanBeUserActivated): 70 to every enemy unit and 35 to every enemy building within 4 m,
            # knocking the units back (force 10).
            obj = db['Objects'].get(typed('_DROP_OBJECT')[0]['Object'])
            blast = [a for e, a in walk(db, db['Spells'].get(obj['ActivationSpellObject']))]
            kinds = {}
            for a in blast:
                kinds.setdefault(db['SpellActions'].get(a['SpellActionType']).name, []).append(a)
            units_hit, buildings_hit = kinds['SPELL_ACTION_APPLY_DAMAGE']
            knock = kinds['SPELL_ACTION_APPLY_KNOCKBACK'][0]
            ability.update(Kind=KIND_SATCHEL_CHARGE, Passive=False, **cast_clip(db, actions, model, skeleton), AreaRadiusMm=round(units_hit['OccuranceRadius'] * 1000),
                           HealMilli=round(units_hit['DamageAmount'] * 1000), BuildingDamageMilli=round(buildings_hit['DamageAmount'] * 1000),
                           KnockbackSpeedMm=round(knock['KnockbackForce'] * 1000), KnockbackAngle=round(knock['KnockbackAngle']),
                           DelayTicks=round(obj['Lifespan'] * 20), Limit=obj['MaxObjectsPerPlayer'],
                           Description='Drops a satchel charge at his feet (two at most). It blows up after 5 seconds, or when he presses the button again: enemies within 4 m take 70 and are thrown back, enemy buildings take 35.')
        elif gear.id == 37:
            # A toggle (UsageType 2) over the walked graph (the ENABLE_SPELL_AND_EVENT_TYPE chain): once the sneak animation ends he is SET_VISIBLE false and
            # SET_PASSIVE true, moves at TIMESCALE 0.5 and takes the DAMAGE_MULTIPLIER 2.0 (read as damage taken: a drawback, like the Guardian Wind's
            # missile multiplier; the manual gives Sneak no damage bonus). Moving does not reveal him; one second after a weapon hit completes the
            # spell deactivates and the 7 s recharge starts.
            found = [a for e, a in walk(db, spell)]

            def of(suffix, _found=found):
                return next(a for a in _found if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix))
            ability.update(Kind=KIND_SNEAK, Passive=False, BonusScale=round(of('_APPLY_TIMESCALE_MULTIPLIER')['Float_01'] * 1_000_000),
                           DamageTakenPpm=round(of('_APPLY_DAMAGE_MULTIPLIER')['Float_01'] * 1_000_000), AlphaPpm=500_000,
                           DelayTicks=math.ceil(of('_START_TIMER')['Float_01'] * 20),
                           Description='Gollum slips out of sight: invisible to enemies without detection, he does not fight on his own, creeps at half speed and takes double damage. Moving does not give him away; a second after his first blow he is seen again.')
        elif gear.id == 38:
            # Passive, bought for 1 Fate: while he sneaks, Sneak's ONSPELLENABLED chain SWITCH_MELEE_WEAPON 61 (45 damage a second against his 15).
            sneak = db['Spells'].get(db['BattleGear'].get(37)['SpellType'])
            weapon = db['Weapons'].get(next(a for e, a in walk(db, sneak) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_SWITCH_MELEE_WEAPON'))['WeaponType'])
            ability.update(Kind=KIND_SURPRISE_ATTACK, Passive=True, DpsMilli=round(weapon['CuttingDamagePerSecond'] * 1000),
                           Description='While he sneaks, Gollum strikes with a surprise weapon for 45 damage a second instead of 15.')
            ability['Source']['Weapon'] = weapon.values
        elif gear.id == 20:
            # Innate. At the toolscript event the child spell (START_REVEAL_FOW, 200 s or until the target dies) marks the enemy within 15 m;
            # no more than MaxNumAffectedGlobally (3) at once. The game has no fog of war, so the mark shows the target to its owner's team even when hidden.
            child = db['Spells'].get(typed('_SPAWN_CHILD_SPELL')[0]['SpellType'])
            timer = next(a for e, a in graph(db, child) if db['SpellActions'].get(a['SpellActionType']).name.endswith('_START_TIMER'))
            ability.update(Kind=KIND_DARK_TRACKING, Passive=False, **cast_clip(db, actions, model, skeleton), DurationTicks=math.ceil(timer['Float_01'] * 20),
                           Limit=child['MaxNumAffectedGlobally'],
                           Description='Marks an enemy within 15 m for up to 200 seconds (three at most): Gollum, and his side, see it wherever it goes, hidden or not.')
        if ability['Kind'] == KIND_UNSIMULATED:
            # Nothing to unlock: a gear the game cannot carry out is not sold for Fate.
            ability.update(Purchasable=False, PurchaseFateCost=0)
        abilities.append(ability)
    return sorted(abilities, key=lambda a: a['Slot'])


# The source effects the Evil heroes' spells play (Effects table ids): Prison of Ice, Invisible Force (AOE, cast), Pestilence (target),
# Twisting Influence, Black Rider formation, Shadow Walk (arrive, depart), Blood Mark (target, cast, area), Poison Trap (burst, target),
# Satchel Charge, Surprise Attack, Dark Tracking, Torch. Pestilence's own cast effects (198, 199) have atlas rectangles the exporter rejects.
EFFECTS = [356, 197, 202, 200, 196, 355, 365, 364, 169, 170, 183, 184, 163, 164, 403, 130, 159, 158, 252]


def dropped_objects(output, resolver):
    """The objects dropped by Saleme and Grishnakh, through their source ModelEvents."""
    from lms2gltf import convert_model
    result = {}
    for kind, object_id in [('trap', 530), ('satchel', 538)]:
        obj = resolver.db['Objects'].get(object_id)
        event = resolver.db['ModelEvents'].get(obj['ModelEvent'])
        model = resolver.db['Models'].get(event['AssociatedModelResource1'])
        filename = 'objects/hero-drops/' + model.name + '.glb'
        target = output / filename
        target.parent.mkdir(parents=True, exist_ok=True)
        convert_model(resolver, model.name, str(target))
        result[kind] = {'File': filename, 'Object': object_id, 'Model': model.name,
                        'Invisible': bool(obj['IsInvisible'])}
    return result


def visuals(output, resolver, trace):
    from healing_visual import export_sprite_effect
    result = {}
    for i in EFFECTS:
        data = export_sprite_effect(output, resolver, trace, resolver.db['Effects'].get(i).name, 'hero-effects/'+str(i))

        def normalized(name):
            return name.lower().replace(' ', '').replace('_', '')
        active = {normalized(e['name'][1:]) for e in data['Source']['Data']['particle_emitters']}
        data['Layers'] = [layer for layer in data['Layers'] if normalized(layer['Name']) in active or normalized(layer['Name']).removeprefix('p') in active]
        if data['Layers']:
            data['DurationMs'] = max(data['DurationMs'], max(layer['DurationMs'] for layer in data['Layers']))
            result[str(i)] = data
    return result
