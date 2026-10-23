"""Places of power for the content pack (build-54): every place-of-power object the maps place, the source places of power
it stands for, and each place's control radius, affected units and modifier.

Source: Objects (OBJECT_PLACE_OF_POWER_*, DefaultPlaceOfPower1-3), PlacesOfPower (radius, MustCapture, MustBeInRadius,
AllowRandomPlacement, AffectsAllUnitTypes, AffectsHeroesOnly, AffectedUnitType1-6, ModifierType and its parameters) and
PlaceOfPowerModifiers (the modifier names). The manual: "To gain control over a Place of Power, you must move your units
into the control zone AND be the only faction present within the zone"; "all units in that army gain a benefit".
"""
from combat_rules import quantize


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
    objects = {}
    for record in resolver.db['Objects'].records:
        if record.name.startswith('OBJECT_PLACE_OF_POWER_'):
            objects[record.name] = {'display_name': record['Name'],
                                    'places': [record['DefaultPlaceOfPower%d' % i] for i in range(1, 4) if record['DefaultPlaceOfPower%d' % i] >= 0]}
    return {'format_version': 1, 'objects': objects, 'places': places,
            'policy': 'Source PlacesOfPower, PlaceOfPowerModifiers and the place-of-power Objects. A random place '
                      '(OBJECT_PLACE_OF_POWER_RANDOM, no default places) takes one of the places with AllowRandomPlacement, '
                      'chosen from the match seed; capture and the simulated modifiers are described in prototype/SKIRMISH.md build-54.'}
