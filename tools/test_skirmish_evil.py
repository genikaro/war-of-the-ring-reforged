"""The Evil faction's imported rules (build-133): start building, worker palettes, corruption, population, tree and research."""
import unittest

import combat_rules
import lsk
import skirmish_construction
import skirmish_economy
import skirmish_map
import skirmish_production
import skirmish_research
import prepare_prototype
from resolve_model import Resolver
import wor

INSTALLED = (wor.ROOT / 'TLOTR - War of the Ring/Database/Buildings.lqr').exists()


class FactionTableTests(unittest.TestCase):
    def test_names_belong_to_their_side(self):
        self.assertEqual(skirmish_map.faction_of('BUILDING_E_TOWER'), 'evil')
        self.assertEqual(skirmish_map.faction_of('UNIT_E_GOBLIN_SLAVE'), 'evil')
        self.assertEqual(skirmish_map.faction_of('BUILDING_G_TOWER'), 'good')
        self.assertEqual(skirmish_map.faction_of('OBJECT_RESOURCE_ORE_PILE'), 'good')
        self.assertTrue(all(skirmish_map.faction_of(b) == 'evil' for b in skirmish_map.EVIL_BUILDINGS))
        self.assertTrue(all(skirmish_map.faction_of(u) == 'evil' for u in skirmish_map.EVIL_UNITS))
        self.assertFalse(set(skirmish_map.GOOD_BUILDINGS) & set(skirmish_map.EVIL_BUILDINGS))

    @unittest.skipUnless(INSTALLED, 'owner install not available')
    def test_start_building_is_the_source_town_center(self):
        resolver = Resolver()
        for faction in skirmish_map.FACTIONS.values():
            self.assertTrue(resolver.db['Buildings'].get(faction['town_center'])['IsTownCenter'])
            self.assertTrue(resolver.db['Buildings'].get(faction['town_center'])['IsResourceDropOffBuilding'])
        # The Fortress is not the Evil town centre: the Goblin Hovel is, and it trains the worker.
        self.assertFalse(resolver.db['Buildings'].get(skirmish_map.EVIL_FORTRESS)['IsTownCenter'])
        self.assertEqual(skirmish_map.FACTIONS['evil']['town_center'], 'BUILDING_E_WORKER_STRUCTURE')


@unittest.skipUnless(INSTALLED, 'owner install not available')
class EvilImportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.resolver = Resolver()

    def test_palettes_and_corruption(self):
        data = skirmish_construction.placement_data(self.resolver, ('evil',))
        self.assertEqual(sorted(data['palettes']), ['UNIT_E_GOBLIN_SLAVE', 'UNIT_E_GOBLIN_SLAVE_MASTER'])
        self.assertEqual(data['palettes']['UNIT_E_GOBLIN_SLAVE_MASTER'], ['BUILDING_E_TERRITORY_MARKER'])
        self.assertNotIn('BUILDING_E_TERRITORY_MARKER', data['palettes']['UNIT_E_GOBLIN_SLAVE'])
        self.assertEqual(data['order'][0], 'BUILDING_E_WORKER_STRUCTURE')
        post = data['placeable']['BUILDING_E_TERRITORY_MARKER']
        self.assertEqual((post['ore_cost'], post['food_cost'], post['max_health']), (65, 0, 500))
        self.assertTrue(post['creates_corruption'])
        self.assertGreaterEqual(post['duration_ticks'], 1)
        hovel = data['placeable']['BUILDING_E_WORKER_STRUCTURE']
        self.assertEqual((hovel['food_cost'], hovel['ore_cost'], hovel['max_health'], hovel['drop_off']), (225, 225, 700, True))
        corruption = data['corruption']
        # Source flags: the War Post creates corruption, every building but the well and ore buildings and the Post needs it.
        self.assertEqual(sorted(corruption['creators']), ['BUILDING_E_TERRITORY_MARKER'])
        self.assertEqual(sorted(corruption['start_creators']), ['BUILDING_E_WORKER_STRUCTURE'])
        self.assertIn('BUILDING_E_TOWN_CENTER_LEVEL_1', corruption['only'])
        self.assertNotIn('BUILDING_E_FOOD_STRUCTURE', corruption['only'])
        self.assertNotIn('BUILDING_E_BLACKSMITH', corruption['only'])
        self.assertEqual([name for name in data['upgrades'] if name.startswith('BUILDING_E_')],
                         ['BUILDING_E_TOWN_CENTER_LEVEL_%d' % level for level in range(1, 5)])
        self.assertEqual(data['upgrades']['BUILDING_E_TOWN_CENTER_LEVEL_1']['display_name'], 'Fortress 2')

    def test_resource_buildings_per_side(self):
        data, _ = skirmish_construction.construction_data(self.resolver, [])
        evil = data['faction_definitions']['evil']
        self.assertEqual((evil['food']['name'], evil['food']['ore_cost'], evil['food']['max_health']), ('BUILDING_E_FOOD_STRUCTURE', 50, 530))
        self.assertEqual((evil['ore']['name'], evil['ore']['ore_cost'], evil['ore']['max_health']), ('BUILDING_E_BLACKSMITH', 80, 725))
        self.assertEqual(data['faction_definitions']['good']['food']['name'], data['definitions']['food']['name'])

    def test_production_tree(self):
        data = skirmish_production.production_data(self.resolver, prepare_prototype.SKIRMISH_ROSTER, ('evil',))
        producers = {name: record['units'] for name, record in data['producers'].items()}
        self.assertEqual(producers['BUILDING_E_WORKER_STRUCTURE'], ['UNIT_E_GOBLIN_SLAVE', 'UNIT_E_GOBLIN_SPEARMAN', 'UNIT_E_GOBLIN_SLAVE_MASTER'])
        self.assertEqual(producers['BUILDING_E_ORC_BARRACKS'], ['UNIT_E_ORC_BOWMAN', 'UNIT_E_ORC_SLASHER', 'UNIT_E_URUK_HAI'])
        self.assertEqual(producers['BUILDING_E_STABLES'], ['UNIT_E_WARG_RIDER', 'UNIT_E_GIANT_SPIDER'])
        self.assertEqual(producers['BUILDING_E_HARADRIM_BARRACKS'], ['UNIT_E_WRAITH', 'UNIT_E_HARADRIM_ASSASSIN'])
        self.assertEqual(producers['BUILDING_E_TROLL_BARRACKS'], ['UNIT_E_TROLL_STONEHURLER', 'UNIT_E_TROLL_BONECLEAVER'])
        self.assertEqual(producers['BUILDING_E_TOWN_CENTER_LEVEL_1'], ['UNIT_E_HERO_GOLLUM', 'UNIT_E_HERO_NAZGUL_KING', 'UNIT_E_HERO_QAMARA',
                                                                       'UNIT_E_HERO_GRISHNAKH', 'UNIT_E_HERO_SARUMAN'])
        self.assertEqual(data['population_by_unit'], {'UNIT_E_GOBLIN_SLAVE_MASTER': {'added': 12, 'max': 15}})
        self.assertEqual(data['fate']['town_centers'], ['BUILDING_E_WORKER_STRUCTURE'])
        # Source requirements: Uruk-Hai and the Bonecleaver wait for Fortress 4, Saruman for Fortress 5; Gollum costs no Fate.
        units = data['units']
        self.assertEqual(units['UNIT_E_URUK_HAI']['prerequisites'], ['BUILDING_E_TOWN_CENTER_LEVEL_4'])
        self.assertEqual(units['UNIT_E_HERO_SARUMAN']['prerequisites'], ['BUILDING_E_TOWN_CENTER_LEVEL_5'])
        self.assertEqual((units['UNIT_E_HERO_GOLLUM']['fate_cost'], units['UNIT_E_HERO_SARUMAN']['fate_cost']), (0, 4))
        self.assertEqual((units['UNIT_E_GOBLIN_SLAVE']['food_cost'], units['UNIT_E_GOBLIN_SLAVE_MASTER']['ore_cost']), (35, 95))
        self.assertEqual(units['UNIT_E_GOBLIN_SLAVE_MASTER']['population'], 0)
        # Evil heroes share unique group 1, Good ones group 0.
        self.assertEqual({v['group'] for v in data['unique_group_capacity'].values()}, {1})

    def test_research_names_follow_the_manual(self):
        data = skirmish_research.research_data(self.resolver, ('evil',))
        names = {key: value['display_name'] for key, value in data['techniques'].items()}
        self.assertEqual(names['TECHNIQUE_FORGED_FITTINGS'], 'Iron Fittings')
        self.assertEqual(names['TECHNIQUE_IRON_FITTINGS'], 'Forged Fittings')
        self.assertEqual(names['TECHNIQUE_STEEL_FITTINGS'], 'Steel Fittings')
        self.assertEqual(names['TECHNIQUE_CURVED_BLADES'], 'Curved Blades')
        self.assertTrue(all(t['building'].startswith('BUILDING_E_') for t in data['techniques'].values()))
        blades = data['techniques']['TECHNIQUE_CURVED_BLADES']
        self.assertEqual((blades['food_cost'], blades['ore_cost'], blades['prerequisite']), (75, 75, 'BUILDING_E_TOWN_CENTER_LEVEL_1'))
        self.assertEqual(blades['bonuses']['UNIT_E_ORC_SLASHER']['damage_milli'], 1000)
        self.assertEqual(data['techniques']['TECHNIQUE_WICKED_BLADES']['prerequisite'], 'BUILDING_E_TOWN_CENTER_LEVEL_5')
        self.assertEqual(data['techniques']['TECHNIQUE_POISON_ARROWS']['building'], 'BUILDING_E_ARMORY')

    def test_goblin_worker_economy(self):
        data = skirmish_economy.economy_data(self.resolver, ('evil',))
        worker = data['workers']['UNIT_E_GOBLIN_SLAVE']
        self.assertEqual((worker['food_capacity'], worker['ore_capacity']), (10, 9))
        self.assertEqual(worker['animations']['mine']['clip'], 'E_Orc_Peasant_Gather_Resource')
        self.assertGreater(worker['animations']['mine']['hit_tick'], 0)

    def test_hero_abilities(self):
        import evil_abilities
        units = self.resolver.db['Units']
        import lsk
        from wor import ROOT

        def exported(hero):
            unit = units.get(hero); model = self.resolver.resolve(unit.name, recurse=False)
            skeleton = lsk.parse((ROOT / model['skeletons'][0]['file']).read_bytes())
            return evil_abilities.export(self.resolver, unit, model, skeleton)
        found = {hero: exported(hero) for hero in evil_abilities.HEROES}
        names = {hero: [a['Name'] for a in abilities] for hero, abilities in found.items()}
        self.assertEqual(names['UNIT_E_HERO_SARUMAN'], ['Prison of Ice', 'Invisible Force', 'Pestilence', 'Twisting Influence'])
        self.assertEqual(names['UNIT_E_HERO_GRISHNAKH'], ['Satchel Charge', 'Speed of the Wolf', 'Torch'])
        self.assertTrue(all(0 <= a['Slot'] <= 3 for abilities in found.values() for a in abilities))
        wolf = next(a for a in found['UNIT_E_HERO_GRISHNAKH'] if a['Name'] == 'Speed of the Wolf')
        self.assertEqual((wolf['Kind'], wolf['BonusScale'], wolf['DurationTicks'], wolf['RechargeTicks']), (evil_abilities.KIND_ELVEN_SPEED, 1_500_000, 200, 360))
        terror = next(a for a in found['UNIT_E_HERO_NAZGUL_KING'] if a['Name'] == 'Aura of Terror')
        self.assertEqual((terror['Kind'], terror['ArmorReductionMilli'], terror['RangeMm'], terror['Passive']), (evil_abilities.KIND_TERROR_AURA, 2000, 8000, True))
        prison, force, pestilence, twisting = found['UNIT_E_HERO_SARUMAN']
        self.assertEqual((prison['Kind'], prison['DurationTicks'], prison['HealMilli'], prison['RangeMm'], prison['RechargeTicks'], prison['Purchasable']),
                         (evil_abilities.KIND_PRISON_OF_ICE, 300, 25000, 15000, 500, False))
        self.assertEqual(prison['ExcludedUnits'], ['UNIT_G_GIANT_ENT', 'UNIT_E_HERO_BALROG'])
        self.assertEqual((force['Kind'], force['AreaRadiusMm'], force['KnockbackSpeedMm'], force['KnockbackAngle'], force['HealMilli'], force['PurchaseFateCost']),
                         (evil_abilities.KIND_INVISIBLE_FORCE, 10000, 10000, 40, 45000, 2))
        self.assertEqual((pestilence['Kind'], pestilence['AreaRadiusMm'], pestilence['DurationTicks'], pestilence['DpsMilli'], pestilence['PurchaseFateCost']),
                         (evil_abilities.KIND_PESTILENCE, 7500, 800, 3000, 3))
        self.assertEqual((twisting['Kind'], twisting['RangeMm'], twisting['RechargeTicks'], twisting['PurchaseFateCost']),
                         (evil_abilities.KIND_TWISTING_INFLUENCE, 10000, 1800, 3))
        self.assertTrue(all(a['CastClip'] and 0 <= a['EffectTick'] < a['CastTicks'] for a in (prison, force, pestilence, twisting)))
        bind, walk, _terror = found['UNIT_E_HERO_NAZGUL_KING']
        self.assertEqual((bind['Kind'], bind['RangeMm'], bind['RechargeTicks'], bind['BonusTargets'], bind['MorphUnit'], bind['Limit'], bind['ActivateFateCost'], bind['PurchaseFateCost']),
                         (evil_abilities.KIND_SHADOW_BIND, 5000, 1000, [52], 'UNIT_E_BONUS_BLACK_RIDER', 8, 1, 2))
        self.assertEqual((walk['Kind'], walk['RangeMm'], walk['AreaRadiusMm'], walk['CarryRadiusMm'], walk['DelayTicks'], walk['BonusTargets'], walk['RechargeTicks']),
                         (evil_abilities.KIND_SHADOW_WALK, 10000, 10000, 15000, 10, [33, 52], 800))
        self.assertTrue(bind['CastClip'] and 0 <= bind['EffectTick'] < bind['CastTicks'] and 'CastClip' not in walk)
        trap, mark, blades = found['UNIT_E_HERO_QAMARA']
        self.assertEqual((trap['Kind'], trap['ResponseRangeMm'], trap['AreaRadiusMm'], trap['DpsMilli'], trap['DurationTicks'], trap['Limit'], trap['RechargeTicks'], trap['PurchaseFateCost']),
                         (evil_abilities.KIND_POISON_TRAP, 4000, 10000, 6000, 500, 3, 300, 2))
        self.assertEqual((mark['Kind'], mark['DpsMilli'], mark['DurationTicks'], mark['FieldDpsMilli'], mark['FieldDurationTicks'], mark['AreaRadiusMm'], mark['PurchaseFateCost']),
                         (evil_abilities.KIND_BLOOD_MARK, 13000, 240, 5000, 300, 12000, 3))
        self.assertEqual((blades['Kind'], blades['Passive'], blades['ChainTargets'], blades['RangeMm'], blades['BonusScale'], blades['Purchasable']),
                         (evil_abilities.KIND_MYSTIC_BLADES, True, 5, 10000, 300000, False))
        satchel, _wolf, torch = found['UNIT_E_HERO_GRISHNAKH']
        self.assertEqual((satchel['Kind'], satchel['AreaRadiusMm'], satchel['HealMilli'], satchel['BuildingDamageMilli'], satchel['KnockbackSpeedMm'], satchel['DelayTicks'], satchel['Limit'], satchel['RechargeTicks'], satchel['PurchaseFateCost']),
                         (evil_abilities.KIND_SATCHEL_CHARGE, 4000, 70000, 35000, 10000, 100, 2, 400, 2))
        self.assertEqual((torch['Kind'], torch['RangeMm'], torch['HealMilli'], torch['FirePpmPerSecond'], torch['RechargeTicks'], torch['PurchaseFateCost']),
                         (evil_abilities.KIND_TORCH, 15000, 100000, 30_000_000, 160, 1))
        self.assertTrue(all(a['CastClip'] and 0 <= a['EffectTick'] < a['CastTicks'] for a in (satchel, torch)))
        sneak, surprise, tracking = found['UNIT_E_HERO_GOLLUM']
        self.assertEqual((sneak['Kind'], sneak['BonusScale'], sneak['DamageTakenPpm'], sneak['DelayTicks'], sneak['RechargeTicks'], sneak['PurchaseFateCost']),
                         (evil_abilities.KIND_SNEAK, 500_000, 2_000_000, 20, 140, 1))
        self.assertEqual((surprise['Kind'], surprise['Passive'], surprise['DpsMilli'], surprise['PurchaseFateCost']), (evil_abilities.KIND_SURPRISE_ATTACK, True, 45000, 1))
        self.assertEqual((tracking['Kind'], tracking['RangeMm'], tracking['DurationTicks'], tracking['Limit'], tracking['RechargeTicks'], tracking['Purchasable']),
                         (evil_abilities.KIND_DARK_TRACKING, 15000, 4000, 3, 200, False))
        self.assertTrue(tracking['CastClip'] and 0 <= tracking['EffectTick'] < tracking['CastTicks'])
        # Every gear of every Evil hero is carried out now.
        self.assertTrue(all(a['Kind'] != evil_abilities.KIND_UNSIMULATED for abilities in found.values() for a in abilities))
        # What the game cannot carry out is not sold for Fate.
        self.assertTrue(all(not a['Purchasable'] for abilities in found.values() for a in abilities if a['Kind'] == evil_abilities.KIND_UNSIMULATED))

    def test_saleme_throws_mystic_blades(self):
        import combat_rules
        self.assertEqual(combat_rules.missile_weapon_id(self.resolver, 'UNIT_E_HERO_QAMARA'), 64)
        self.assertEqual(combat_rules.missile_weapon_id(self.resolver, 'UNIT_E_ORC_BOWMAN'), self.resolver.db['Units'].get('UNIT_E_ORC_BOWMAN')['MissileWeapon'])
        self.assertEqual(self.resolver.db['Weapons'].get(64)['CuttingDamagePerSecond'], 20.0)

    def test_global_powers_of_both_sides(self):
        import global_powers
        powers = global_powers.export(self.resolver)
        evil = [p for p in powers if p['Faction'] == 'evil']
        good = [p for p in powers if p['Faction'] == 'good']
        self.assertEqual([p['Id'] for p in good], [4, 6, 5, 3, 1])
        self.assertEqual([p['Name'] for p in evil], ['Summon Bog', 'Murder of Crows', 'Vile Surge', 'Obelisk', 'Summon Balrog'])
        self.assertEqual([p['FateCost'] for p in evil], [2, 3, 3, 4, 7])
        self.assertEqual((evil[0]['TimeScalePpm'], evil[0]['DurationTicks']), (500_000, 300))
        self.assertEqual((evil[1]['MissilePpm'], evil[1]['DurationTicks']), (10_000, 440))
        self.assertEqual(evil[2]['HealMilli'], 200_000)
        self.assertEqual(evil[4]['SummonUnit'], 'UNIT_E_HERO_BALROG')
        # Build-164: the Obelisk from its own row: Object 561's 5000 health, 1.5% per second decay, a 50/50 damage split.
        obelisk = evil[3]
        self.assertEqual((obelisk['ObjectHealth'], obelisk['DegenerationPpm'], obelisk['DamageUnitPpm'], obelisk['DamageObeliskPpm']),
                         (5000, 15_000, 500_000, 500_000))
        self.assertEqual((obelisk['RadiusMm'], obelisk['DurationTicks']), (15_000, 1334))

    def test_fate_powers_keep_out_of_fog_and_buildings(self):
        # Build-171: CanCastInFOW is 0 on every row; CanCastOverBuildings 0 on the Balrog, the Giant Ent and the Obelisk.
        import global_powers
        powers = {p['Id']: p for p in global_powers.export(self.resolver)}
        self.assertTrue(all(p['BlockedInFog'] for p in powers.values()))
        self.assertEqual(sorted(i for i, p in powers.items() if p['BlockedOverBuildings']), [0, 1, 9])

    def test_researched_missile_weapons_bring_their_effects(self):
        # Build-171: Knockback Arrows (weapon 52) always throws its target; Unstoppable Force (weapon 73) splashes 5 m
        # (AreaOfEffectDamage 0.05) and throws projectile 35, the larger boulder.
        data = skirmish_research.research_data(self.resolver, ('evil',))['techniques']
        uruk = data['TECHNIQUE_KNOCKBACK_ARROWS']['bonuses']['UNIT_E_URUK_HAI']
        self.assertEqual((uruk['missile_knockback_mm'], uruk['missile_knockback_angle'], uruk['missile_dps_milli']), (6000, 25, 14000))
        self.assertNotIn('missile_projectile', uruk)
        troll = data['TECHNIQUE_UNSTOPPABLE_FORCE']['bonuses']['UNIT_E_TROLL_STONEHURLER']
        self.assertEqual((troll['missile_splash_radius_mm'], troll['missile_splash_dps_milli'], troll['missile_projectile']), (5000, 50, 35))
        self.assertNotIn('missile_knockback_mm', troll)

    def test_firesword_splashes_and_burns(self):
        # Build-171: the Balrog's Firesword (weapon 119) splashes 25 a second within 6 m and puts 2 fire a second into buildings;
        # the Giant Ent's boulder (weapon 10) splashes 15 within 5 m.
        weapons = self.resolver.db['Weapons']
        firesword = weapons.get(self.resolver.db['Units'].get('UNIT_E_HERO_BALROG')['MeleeWeapon'])
        self.assertEqual((firesword['AreaOfEffectRange'], firesword['AreaOfEffectDamage'], firesword['FireDamagePerSecond']), (6.0, 25.0, 2.0))
        boulder = weapons.get(combat_rules.missile_weapon_id(self.resolver, 'UNIT_G_GIANT_ENT'))
        self.assertEqual((boulder['AreaOfEffectRange'], boulder['AreaOfEffectDamage']), (5.0, 15.0))

    def test_obelisk_model_comes_from_its_object(self):
        import tempfile
        from pathlib import Path
        import global_powers
        with tempfile.TemporaryDirectory() as folder:
            drop = global_powers.dropped_objects(Path(folder), self.resolver)['obelisk']
            self.assertEqual((drop['Object'], drop['Model'], drop['Invisible']), (561, 'MODEL_OBELISK', False))
            self.assertTrue((Path(folder) / drop['File']).stat().st_size > 0)

    def test_ringsight_detectors_see_the_frodo_invisibility_type(self):
        # Build-164: the Ring is INVISIBILITY_TYPE_FRODO (bit 1); Gollum, the Nazgul King and the Black Rider detect it.
        units = self.resolver.db['Units']
        frodo_type = self.resolver.db['InvisibilityTypes'].get('INVISIBILITY_TYPE_FRODO')
        self.assertEqual(units.get('UNIT_G_HERO_FRODO')['InvisibilityType1'], frodo_type.id)
        for name in ['UNIT_E_HERO_GOLLUM', 'UNIT_E_HERO_NAZGUL_KING', 'UNIT_E_BONUS_BLACK_RIDER']:
            self.assertEqual(units.get(name)['DetectorType1'], frodo_type.id, name)
        self.assertNotEqual(units.get('UNIT_E_WRAITH')['DetectorType1'], frodo_type.id)

    def test_troll_root_joint_is_accepted(self):
        # The trolls name their root J_Positional, which the importer used to reject (their bodies were left out of the pack).
        model = self.resolver.resolve('UNIT_E_TROLL_BONECLEAVER', recurse=False)
        skeleton = lsk.parse((wor.ROOT / model['skeletons'][0]['file']).read_bytes())
        motion, walk_scale = combat_rules.locomotion(model, skeleton)
        self.assertEqual(skeleton.joints[0].name, 'J_Positional')
        self.assertGreater(motion['Run']['SourceSpeed'], 0)
        self.assertTrue(0 < walk_scale <= 1_000_000)


class CorruptionTileTests(unittest.TestCase):
    def test_tiles_export_as_dark_ground_textures(self):
        # The source paints corruption into the terrain with three 128x128 cracked-ash tiles (build-136 skins the terrain with them).
        import tempfile
        from pathlib import Path
        from PIL import Image
        import content_pack
        with tempfile.TemporaryDirectory() as folder:
            content_pack.export_corruption_tiles(folder)
            tiles = sorted((Path(folder) / 'terrain').glob('corruption_tile_*.png'))
            self.assertEqual([t.name for t in tiles], ['corruption_tile_01.png', 'corruption_tile_02.png', 'corruption_tile_03.png'])
            for tile in tiles:
                with Image.open(tile) as image:
                    self.assertEqual(image.size, (128, 128))
                    pixels = list(image.convert('L').getdata())
                    self.assertLess(sum(pixels) / len(pixels), 60)  # blackened ground, not grass


if __name__ == '__main__':
    unittest.main()
