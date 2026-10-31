import struct
import unittest
from blood_effects import _emitter, _ground, _keys, _rests, weapon_effect_rows, weapon_effects


def bits(value):
    return struct.unpack('<I', struct.pack('<f', value))[0]


class Record(dict):
    def __init__(self, id, **values):
        super().__init__(values)
        self.id = id


class Table:
    def __init__(self, records):
        self.records = records

    def get(self, id):
        return next(r for r in self.records if r.id == id)


class Resolver:
    def __init__(self):
        effects = [Record(0, WeaponImpactType=3, CompositionType=1, Effect=11),
                   Record(1, WeaponImpactType=3, CompositionType=16, Effect=378),
                   Record(2, WeaponImpactType=3, CompositionType=2, Effect=-1)]
        self.db = {'Weapons': Table([Record(4, WeaponImpactType=3), Record(5, WeaponImpactType=7)]),
                   'WeaponEffects': Table(effects),
                   'WeaponImpactTypes': Table([Record(3, DefaultWeaponEffectSetting=2), Record(7, DefaultWeaponEffectSetting=0)])}


class BloodEffectsTests(unittest.TestCase):
    def key(self, time, width, variation, color):
        return {'time': time, 'words': [0, 0, 0, 0, bits(width), bits(width), 0, bits(variation), bits(variation), 0, color]}

    def test_keys_keep_size_variation_and_color_in_time_order(self):
        keys = _keys({'name': 'Splat', 'keys': [self.key(1, .2, .3, 0), self.key(0, .2, .3, 0xd85c0000)]})
        self.assertEqual([(k['Time'], k['WidthMm'], k['WidthVariationMm'], k['ColorArgb']) for k in keys],
                         [(0, 200, 300, 0xd85c0000), (1, 200, 300, 0)])

    def test_invalid_timeline_fails(self):
        with self.assertRaisesRegex(ValueError, 'invalid'):
            _keys({'name': 'Drops', 'keys': [self.key(.5, 0, 0, 0)]})
        with self.assertRaisesRegex(ValueError, 'empty'):
            _keys({'name': 'Drops', 'keys': []})

    def emitter(self, name, count, rate, speed, variation=0, cone=0):
        words, header = [0]*36, [0]*24
        words[3], words[4], words[5], words[15] = rate, bits(speed), bits(variation), bits(cone)
        header[20] = count
        return {'name': name, 'words': header, 'keys': [{'words': words}]}

    def test_emitter_reads_count_rate_speed_and_cone_of_the_named_track(self):
        effect = {'particle_emitters': [self.emitter('E_Drops', 14, 17, 2.24, 2.17, .5585)]}
        self.assertEqual(_emitter(effect, 'Drops'), {'Count': 14, 'RatePerSecond': 17, 'SpeedMm': 2240, 'SpeedVariationMm': 2170,
                                                     'ConeDegrees': 32.0, 'Name': 'E_Drops'})
        self.assertIsNone(_emitter(effect, 'Splat'))
        # One skull per burst, although its emitter's rate is 343 per second.
        self.assertEqual(_emitter({'particle_emitters': [self.emitter('Eskull', 1, 343, 4.95)]}, 'skull')['Count'], 1)

    def track(self, name, duration, parameter=0, aligned=0, color=0xffffffff, width=.4):
        return {'name': name, 'duration_ms': duration, 'parameter': parameter, 'track_flags': [0, 0, aligned],
                'keys': [self.key(0, width, 0, color)]}

    def test_ground_marks_and_resting_gore(self):
        launched = {'SpeedMm': 5000}
        # Explosion blood, scorch and blood mark lie on the ground; so does a hit's still splat.
        self.assertTrue(_ground(self.track('scorch', 7000, parameter=1, width=3), {'SpeedMm': 0}))
        self.assertTrue(_ground(self.track('Splat', 2270, aligned=1), None))
        # A ground-aligned bone that is launched flies first, then rests; mists hover.
        self.assertFalse(_ground(self.track('bone', 4000, aligned=1), launched))
        self.assertTrue(_rests(self.track('bone', 4000, aligned=1), launched))
        self.assertTrue(_rests(self.track('flesh2', 4000), None))
        self.assertFalse(_rests(self.track('diffusion', 4000, color=0xffae0000), None))
        self.assertFalse(_rests(self.track('Drops', 1000, width=.2), {'SpeedMm': 2240}))

    def test_weapon_effects_pair_impact_with_composition_or_default(self):
        resolver = Resolver()
        # A cutting weapon flashes on flesh and splashes orc blood; stone falls back to the default row (no effect).
        self.assertEqual(weapon_effects(resolver, 4, {1, 2, 16}), {'1': 11, '16': 378})
        # An impact type without rows uses its default setting for every composition.
        self.assertEqual(weapon_effects(resolver, 5, {1, 16}), {'1': 11, '16': 11})
        self.assertEqual(weapon_effects(resolver, -1, {1}), {})

    def test_unlisted_pairings_take_the_default_row_for_sounds_too(self):
        # Build-131: an arrow (impact type 7, no rows) striking wood (0) or stone (2) sounds like its default row.
        rows = weapon_effect_rows(Resolver(), 7, [0, 1, 2])
        self.assertEqual({c: r.id for c, r in rows.items()}, {0: 0, 1: 0, 2: 0})
        self.assertEqual({c: r.id for c, r in weapon_effect_rows(Resolver(), 3, [1, 2, 5]).items()}, {1: 0, 2: 2, 5: 2})


if __name__ == '__main__':
    unittest.main()
