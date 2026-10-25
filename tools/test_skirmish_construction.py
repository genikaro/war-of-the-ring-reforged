"""Source costs, prerequisites and deterministic resource-site masks."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import skirmish_construction
from resolve_model import Resolver
import wor
import prototype_assets


class ConstructionImportTests(unittest.TestCase):
    def test_scaffolds_use_source_texture_keys_and_refresh_is_idempotent(self):
        gltf = {'nodes': [{'mesh': 0}, {'mesh': 1}],
                'meshes': [{'primitives': [{'material': 0}]}, {'primitives': [{'material': 0}]}],
                'materials': [{'pbrMetallicRoughness': {'baseColorTexture': {'index': 0}}, 'emissiveTexture': {'index': 1}}],
                'extras': {'wotrAppearance': {'models': [{'parts': [0, 1], 'textures': [0, 1, 2, 3],
                    'states': [{'id': 19, 'choices': [{'weight': 1, 'meshes': [0, -1], 'textureKeys': [[0, [2, 3]]]}]}]}]}}}
        prototype_assets.construction_materials(gltf)
        material = gltf['materials'][gltf['meshes'][0]['primitives'][0]['material']]
        self.assertEqual(material['pbrMetallicRoughness']['baseColorTexture']['index'], 2)
        self.assertEqual(material['emissiveTexture']['index'], 3)
        self.assertEqual(gltf['meshes'][1]['primitives'][0]['material'], 0)
        prototype_assets.construction_materials(gltf)
        self.assertEqual(len(gltf['materials']), 2)

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Buildings.lqr').exists(), 'owner install not available')
    def test_original_rules_and_site_ids(self):
        with patch('skirmish_map.building_footprint', return_value=({'object_id': 20007}, None)) as footprint:
            data, masks = skirmish_construction.construction_data(Resolver(), [
                {'id': 7, 'kind': 'food', 'position': [1, 2, 3]}])
        mill, foundry = data['definitions']['food'], data['definitions']['ore']
        self.assertEqual((mill['ore_cost'], mill['duration_ticks'], mill['max_health']), (50, 500, 570))
        self.assertEqual((foundry['ore_cost'], foundry['duration_ticks'], foundry['max_health']), (80, 800, 750))
        self.assertEqual((mill['food_refund_ppm'], foundry['ore_refund_ppm']), (750000, 750000))
        self.assertEqual((mill['extra_worker_ppm'], mill['initial_health_ppm']), (100000, 50000))
        self.assertEqual(mill['prerequisites'], [])
        self.assertEqual(foundry['prerequisites'], ['BUILDING_G_TOWN_CENTER_LEVEL_1'])
        self.assertEqual(data['sites'], [{'resource': 7, 'kind': 'food', 'obstacle': 20007}])
        self.assertEqual(masks[0]['object_id'], 20007)
        self.assertEqual(footprint.call_args.args[1], 'BUILDING_G_FOOD_STRUCTURE')
        citadel = data['buildings']['BUILDING_G_TOWN_CENTER_LEVEL_1']
        self.assertEqual((citadel['max_health'], citadel['armor'], citadel['portrait']), (750, 4, 'Building_G_Keep_1'))
        self.assertEqual(data['buildings']['BUILDING_G_DWARF_BARRACKS']['portrait'], 'Building_G_Dwarvish_Hall')

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Buildings.lqr').exists(), 'owner install not available')
    def test_original_open_ground_placement(self):
        data = skirmish_construction.placement_data(Resolver())
        self.assertEqual(data['order'][:3], ['BUILDING_G_TOWN_CENTER_LEVEL_1', 'BUILDING_G_FOOD_STRUCTURE', 'BUILDING_G_BLACKSMITH'])
        self.assertEqual(sorted(data['placeable']), ['BUILDING_G_ARMORY', 'BUILDING_G_DWARF_BARRACKS', 'BUILDING_G_ELF_BARRACKS',
                         'BUILDING_G_GONDORIAN_BARRACKS', 'BUILDING_G_HOSPITAL', 'BUILDING_G_RANGER_BARRACKS', 'BUILDING_G_SKILLS_STRUCTURE',
                         'BUILDING_G_STABLES', 'BUILDING_G_TOWER', 'BUILDING_G_TOWN_CENTER_LEVEL_1', 'BUILDING_G_WORKER_STRUCTURE'])
        # The second row of the full worker palette (good-018): Forge, Ranger Post, Outpost, Nature's Haven, Elven Sanctuary, House of Lore.
        self.assertEqual(data['order'][7:], ['BUILDING_G_SKILLS_STRUCTURE', 'BUILDING_G_RANGER_BARRACKS', 'BUILDING_G_ARMORY',
                                             'BUILDING_G_STABLES', 'BUILDING_G_ELF_BARRACKS', 'BUILDING_G_HOSPITAL'])
        # Tier 3 (atlas): Nature's Haven 130/135, Elven Sanctuary 135/135, House of Lore 95/100, all at Citadel 3.
        self.assertEqual({name: (data['placeable'][name]['food_cost'], data['placeable'][name]['ore_cost'], data['placeable'][name]['max_health'],
                                 data['placeable'][name]['prerequisites']) for name in ['BUILDING_G_STABLES', 'BUILDING_G_ELF_BARRACKS', 'BUILDING_G_HOSPITAL']},
                         {'BUILDING_G_STABLES': (130, 135, 680, ['BUILDING_G_TOWN_CENTER_LEVEL_3']),
                          'BUILDING_G_ELF_BARRACKS': (135, 135, 650, ['BUILDING_G_TOWN_CENTER_LEVEL_3']),
                          'BUILDING_G_HOSPITAL': (95, 100, 625, ['BUILDING_G_TOWN_CENTER_LEVEL_3'])})
        # Tier 2 needs Citadel level 2; captured Forge 75/100 (skirmish-010) and Ranger Post 95/100 (skirmish-014).
        forge, post = data['placeable']['BUILDING_G_SKILLS_STRUCTURE'], data['placeable']['BUILDING_G_RANGER_BARRACKS']
        self.assertEqual((forge['food_cost'], forge['ore_cost'], forge['max_health'], forge['prerequisites']),
                         (75, 100, 590, ['BUILDING_G_TOWN_CENTER_LEVEL_2']))
        self.assertEqual((post['food_cost'], post['ore_cost'], post['max_health']), (95, 100, 675))
        self.assertEqual(sorted(data['upgrades']), ['BUILDING_G_TOWER', 'BUILDING_G_TOWN_CENTER_LEVEL_1', 'BUILDING_G_TOWN_CENTER_LEVEL_2',
                                                    'BUILDING_G_TOWN_CENTER_LEVEL_3', 'BUILDING_G_TOWN_CENTER_LEVEL_4', 'BUILDING_G_WORKER_STRUCTURE'])
        # Compendium: Citadel upgrades 115/105, 120/120, 125/135, 130/150, 50 s each; 1000, 1250, 1500, 1750 health.
        self.assertEqual([(data['upgrades'][name]['food_cost'], data['upgrades'][name]['ore_cost'], data['upgrades'][name]['duration_ticks'],
                           data['upgrades'][name]['max_health']) for name in ['BUILDING_G_TOWN_CENTER_LEVEL_%d' % level for level in range(1, 5)]],
                         [(115, 105, 1000, 1000), (120, 120, 1000, 1250), (125, 135, 1000, 1500), (130, 150, 1000, 1750)])
        ballista = data['upgrades']['BUILDING_G_TOWER']
        self.assertEqual((ballista['name'], ballista['food_cost'], ballista['ore_cost'], ballista['duration_ticks'], ballista['max_health'], ballista['prerequisites']),
                         ('BUILDING_G_TOWER_UPGRADE', 150, 150, 800, 500, ['BUILDING_G_TOWN_CENTER_LEVEL_4']))
        citadel2, war_camp = data['upgrades']['BUILDING_G_TOWN_CENTER_LEVEL_1'], data['upgrades']['BUILDING_G_WORKER_STRUCTURE']
        self.assertEqual((citadel2['name'], citadel2['display_name'], citadel2['food_cost'], citadel2['ore_cost'],
                          citadel2['duration_ticks'], citadel2['max_health']),
                         ('BUILDING_G_TOWN_CENTER_LEVEL_2', 'Citadel 2', 115, 105, 1000, 1000))
        self.assertEqual((war_camp['name'], war_camp['food_cost'], war_camp['ore_cost'], war_camp['duration_ticks'], war_camp['prerequisites']),
                         ('BUILDING_G_POPULATION_STRUCTURE_UPGRADE', 0, 55, 800, ['BUILDING_G_TOWN_CENTER_LEVEL_2']))
        camp, citadel = data['placeable']['BUILDING_G_WORKER_STRUCTURE'], data['placeable']['BUILDING_G_TOWN_CENTER_LEVEL_1']
        self.assertEqual((camp['ore_cost'], camp['duration_ticks'], camp['max_health'], camp['must_be_near'], camp['drop_off']),
                         (80, 600, 400, True, False))
        self.assertEqual(camp['prerequisites'], ['BUILDING_G_TOWN_CENTER_LEVEL_1'])
        self.assertEqual((citadel['display_name'], citadel['must_be_near'], citadel['drop_off']), ('Citadel 1', False, True))
        self.assertEqual(data['placeable']['BUILDING_G_TOWER']['footprint'],
                         {'polygon_mm': [[-1956, -1956], [1956, -1956], [1956, 1956], [-1956, 1956]], 'min_height_mm': -400, 'max_height_mm': 400})
        self.assertEqual((data['placement_radius_mm']['BUILDING_G_TOWN_CENTER_LEVEL_1'], data['placement_radius_mm']['BUILDING_G_FOOD_STRUCTURE']),
                         (25000, 15000))

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Buildings.lqr').exists(), 'owner install not available')
    def test_original_watchtower_defense(self):
        with tempfile.TemporaryDirectory(dir=wor.ROOT / 'converted') as output:
            data = skirmish_construction.defense_data(Path(output), Resolver())
            self.assertEqual(sorted(data), ['BUILDING_G_TOWER', 'BUILDING_G_TOWER_UPGRADE'])
            tower = data['BUILDING_G_TOWER'][0]
            # The Ballista Tower keeps the arrows (BuildingDefenses1) and adds the bolt (BuildingDefenses2): 35 DPS, 10-22 m, 0.1 a second.
            arrows, bolt = data['BUILDING_G_TOWER_UPGRADE']
            self.assertEqual((arrows['weapon'], bolt['weapon']), ('WEAPON_BUILDING_G_TOWER_MISSILE', 'WEAPON_BUILDING_G_SUPERTOWER_BOLT'))
            self.assertEqual((bolt['dps_milli'], bolt['minimum_range_mm'], bolt['range_mm'], bolt['rates_milli'][0]), (35000, 10000, 22000, 100))
            self.assertEqual(bolt['projectile']['Visual'], 'combat/projectile_tower_upgrade_ballista_arrow.glb')
            self.assertEqual((tower['defense'], tower['weapon']), ('BUILDING_DEFENSES_G_WATCHTOWER', 'WEAPON_BUILDING_G_TOWER_MISSILE'))
            self.assertEqual((tower['dps_milli'], tower['minimum_range_mm'], tower['range_mm'], tower['max_garrison']), (14000, 0, 19000, 4))
            self.assertEqual(tower['rates_milli'], [1000, 1200, 1400, 1600, 1750])
            # Build-76: tower arrows earn a quarter of the Fate (Weapons FateModifier 0.25), the bolt the full share.
            self.assertEqual((tower['fate_ppm'], arrows['fate_ppm'], bolt['fate_ppm']), (250_000, 250_000, 1_000_000))
            # J_Projectiles carries JointFlags bit 11 (Building Projectile), at the model's 0.8 scale.
            self.assertEqual(tower['origin_mm'], [94, 3434, 159])
            self.assertEqual(tower['projectile']['Visual'], 'combat/projectile_evil_tower_arrow.glb')
            self.assertTrue((Path(output) / tower['projectile']['Visual']).is_file())
            self.assertEqual({s['EventName'] for s in tower['projectile']['LaunchSounds']}, {'SOUND_EVENT_WEAPON_ARROW_LAUNCH'})


if __name__ == '__main__':
    unittest.main()
