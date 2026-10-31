import json
from pathlib import Path
import unittest
from tempfile import TemporaryDirectory

import hero_abilities
import lsk
import prototype_interface
import wor
from resolve_model import Resolver


@unittest.skipUnless((wor.ROOT/'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
class HeroSkillImportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        r=Resolver(); cls.skills={}
        for name in ['GIMLI','LEGOLAS','GANDALF']:
            unit=r.db['Units'].get('UNIT_G_HERO_'+name); model=r.resolve(unit.name,recurse=False)
            skeleton=lsk.parse((wor.ROOT/model['skeletons'][0]['file']).read_bytes())
            cls.skills.update({a['Id']:a for a in hero_abilities.export(r,unit,model,skeleton)})

    def test_source_slots_casts_and_release_events(self):
        for gear,slot,kind,cast,release in [(27,0,27,42,20),(0,0,29,14,7),(15,0,31,21,8),
                                             (42,1,33,21,10),(40,2,32,53,25),(43,3,34,28,5)]:
            a=self.skills[gear]
            self.assertEqual((a['Slot'],a['Kind'],a['CastTicks'],a['EffectTick']),(slot,kind,cast,release))
            self.assertTrue(a['CastClip'])
            self.assertNotIn('RequiredTechnique',a)

    def test_source_effect_strengths_and_timers(self):
        speed=self.skills[6];flame=self.skills[42];rain=self.skills[43]
        self.assertEqual((speed['AreaRadiusMm'],speed['BonusScale'],speed['DurationTicks']),(12000,1200000,300))
        self.assertEqual((flame['AreaRadiusMm'],flame['DpsMilli'],flame['DurationTicks']),(3000,9000,500))
        self.assertEqual((rain['AreaRadiusMm'],rain['DelayTicks'],rain['DpsMilli'],rain['DurationTicks']),(10000,30,18000,110))
        self.assertEqual(rain['FirePpmPerSecond'],25000000)
        self.assertNotIn('HealMilli',rain)  # Weapon 67 targets buildings; units get the separate burn spell.

    def test_capture_uses_exact_source_building_whitelist(self):
        a=self.skills[45]
        self.assertEqual(a['CaptureBuildings'],['BUILDING_E_TOWER','BUILDING_G_TOWER','BUILDING_G_TOWER_UPGRADE'])
        self.assertEqual((a['Slot'],a['RechargeTicks'],a['SoundEvents']),(1,800,[263]))
        self.assertNotIn('CastClip',a)  # Capture is an approach/garrison order, not an invented cast animation.

    def test_targeted_hero_cursors_export_with_original_hotspots(self):
        with TemporaryDirectory() as output:
            assets = prototype_interface.export(output)['assets']
            for gear in [0, 40, 42, 43, 45]:
                cursor = self.skills[gear]['Cursor']
                with self.subTest(ability=self.skills[gear]['Name'], cursor=cursor):
                    self.assertTrue(cursor in assets, 'Missing hero cursor '+cursor)
                    self.assertEqual(assets[cursor]['cursor_hotspot'], [1, 3])
                    self.assertTrue((Path(output)/assets[cursor]['file']).is_file())

    def test_pack_contains_visible_original_hero_effects(self):
        path=wor.ROOT/'converted/content/good/combat/rules.json'
        if not path.exists():self.skipTest('content pack not built')
        visuals=json.loads(path.read_text())['HeroVisuals']
        for i in [145,253,127,328,142,143,185,186,178,182,176]:
            effect=visuals[str(i)]
            self.assertTrue(effect['Layers'])
            self.assertEqual(effect['Source']['Id'],i)
            for layer in effect['Layers']:
                self.assertTrue(all((wor.ROOT/'converted/content/good'/p).is_file() for p in layer['Frames']))


if __name__=='__main__':unittest.main()
