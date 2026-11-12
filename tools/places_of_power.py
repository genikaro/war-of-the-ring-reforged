"""Places of power for the content pack (build-54): every place-of-power object the maps place, the source places of power
it stands for, and each place's control radius, affected units and modifier.

Source: Objects (OBJECT_PLACE_OF_POWER_*, DefaultPlaceOfPower1-3), PlacesOfPower (radius, MustCapture, MustBeInRadius,
AllowRandomPlacement, AffectsAllUnitTypes, AffectsHeroesOnly, AffectedUnitType1-6, ModifierType and its parameters) and
PlaceOfPowerModifiers (the modifier names). The manual: "To gain control over a Place of Power, you must move your units
into the control zone AND be the only faction present within the zone"; "all units in that army gain a benefit".
"""
from combat_rules import quantize
from pathlib import PureWindowsPath


SKIRMISH_EXCLUDED_OBJECTS = (
    'OBJECT_PLACE_OF_POWER_STEED_OF_ROHAN',
    'OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY',
    'OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW',
)


def power_record(resolver, record):
    """The two usable landmark spells, with their source graphs."""
    from elven_abilities import graph
    db = resolver.db
    gear = db['BattleGear'].get(record['BattleGear1'])
    if not gear:
        return None
    spell = db['Spells'].get(gear['SpellType'])
    actions = graph(db, spell)
    typed = lambda suffix: [a for e, a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix)]
    timer = typed('_START_TIMER')[0]
    slow = typed('_APPLY_TIMESCALE_MULTIPLIER')
    cursor = db['Cursors'].get(spell['TargetCursor'])
    return {'Kind': 'Willow' if slow else 'Aviary',
            'Icon': PureWindowsPath(db['UVAs'].get(gear['PortraitBorderless'])['Filename']).stem,
            'Cursor': PureWindowsPath(cursor['Filename']).stem,
            'RangeMm': 30000 if slow else 0,
            'RadiusMm': round(spell['MaxAreaOfEffectRange']*1000),
            'DurationTicks': round(timer['Float_01']*20), 'RechargeTicks': round(spell['RechargeTime']*20),
            'TimeScalePpm': round(slow[0]['Float_01']*1_000_000) if slow else 1_000_000,
            'SoundEvents': sorted({a['SoundEventType'] for a in typed('_PLAY_SOUND_EVENT')}),
            'Source': {'Gear': gear.values, 'Spell': spell.values,
                       'Actions': [{'Event': e.name, 'Type': db['SpellActions'].get(a['SpellActionType']).name,
                                    'Values': a.values} for e, a in actions]}}


def place_record(record, modifiers):
    values = record.values
    modifier = modifiers.get(values['ModifierType']).name.removeprefix('PLACE_OF_POWER_MODIFIER_') if values['ModifierType'] >= 0 else 'NONE'
    return {'id': record.id, 'name': record.name, 'display_name': values['Name'], 'radius_mm': quantize(values['Radius']),
            'must_capture': bool(values['MustCapture']), 'must_be_in_radius': bool(values['MustBeInRadius']),
            'random': bool(values['AllowRandomPlacement']), 'all_units': bool(values['AffectsAllUnitTypes']),
            'heroes_only': bool(values['AffectsHeroesOnly']),
            'unit_types': [values['AffectedUnitType%d' % i] for i in range(1, 7) if values['AffectedUnitType%d' % i] >= 0],
            'modifier': modifier, 'float_ppm': quantize(values['ModifierFloatParam1'], 1_000_000), 'long': values['ModifierLongParam1']}


def export(resolver):
    """Place-of-power objects by type and the source places of power, ids as in PlacesOfPower."""
    modifiers = resolver.db['PlaceOfPowerModifiers']
    places = [place_record(r, modifiers) for r in resolver.db['PlacesOfPower'].records]
    # Owner's skirmish balance rule (build-132); preserve the imported armor rule as provenance.
    narsil = next(p for p in places if p['name'] == 'PLACE_OF_POWER_MONUMENT_OF_NARSIL')
    narsil['source_rule'] = dict(narsil)
    narsil.update(modifier='DAMAGE', float_ppm=1_250_000, long=0, all_units=False, heroes_only=True, unit_types=[])
    objects = {}
    for record in resolver.db['Objects'].records:
        if record.name.startswith('OBJECT_PLACE_OF_POWER_'):
            event = resolver.db['ModelEvents'].get(record['ModelEvent'])
            model = resolver.db['Models'].get(event['AssociatedModelResource1'])
            objects[record.name] = {'display_name': record['Name'].replace('Radaghast', 'Radagast'),
                                    'model': model.name, 'file': 'objects/places/' + model.name + '.glb',
                                    'power': power_record(resolver, record),
                                    'places': [record['DefaultPlaceOfPower%d' % i] for i in range(1, 4) if record['DefaultPlaceOfPower%d' % i] >= 0]}
    return {'format_version': 3, 'objects': objects, 'places': places,
            'skirmish_excluded_objects': list(SKIRMISH_EXCLUDED_OBJECTS),
            'policy': 'Source PlacesOfPower, PlaceOfPowerModifiers and the place-of-power Objects. A random place '
                      'takes a complete object whose default rules allow random placement, chosen from the match seed. '
                      'Models follow Objects.ModelEvent -> ModelEvents.AssociatedModelResource1; shared source models stay shared. '
                      'Skirmishes replace Steed of Rohan, Aviary and Willow with allowed random landmarks. '
                      'Owner balance override: Monument of Narsil grants heroes +25% damage; its original rule is preserved in source_rule. '
                      'See docs/PLACES-OF-POWER.md for spell adapters and source-only campaign entries.'}


def export_models(output, resolver, data):
    from lms2gltf import convert_model
    for model, filename in sorted({(o['model'], o['file']) for o in data['objects'].values()}):
        target = output / filename
        target.parent.mkdir(parents=True, exist_ok=True)
        convert_model(resolver, model, str(target))
