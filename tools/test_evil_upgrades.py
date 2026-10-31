import unittest

import evil_upgrades
import hero_abilities
import lsk
import skirmish_research
import wor
from resolve_model import Resolver


@unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Techniques.lqr').exists(), 'owner install unavailable')
class EvilUpgradeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.r = Resolver()
        cls.abilities = {}
        for name in evil_upgrades.TECHNIQUES:
            unit=cls.r.db['Units'].get(name)
            model=cls.r.resolve(name,recurse=False)
            skeleton=lsk.parse((wor.ROOT/model['skeletons'][0]['file']).read_bytes())
            cls.abilities.update({a['RequiredTechnique']:a for a in hero_abilities.export(cls.r,unit,model,skeleton)})

    def test_all_thirteen_research_entries_are_available(self):
        data=skirmish_research.research_data(self.r,('evil',))
        self.assertEqual(len(evil_upgrades.SUPPORTED),13)
        self.assertTrue(evil_upgrades.SUPPORTED<=data['techniques'].keys())
        self.assertFalse(data['unsimulated'])
        for name in evil_upgrades.SUPPORTED:
            self.assertTrue(data['techniques'][name]['description'])
            self.assertEqual(data['techniques'][name]['duration_ticks'],1800)

    def test_active_clips_have_real_release_ticks(self):
        for ability in self.abilities.values():
            if not ability['Passive']:
                self.assertTrue(ability['CastClip'])
                self.assertTrue(0<=ability['EffectTick']<ability['CastTicks'])
                self.assertGreater(ability['RechargeTicks'],0)

    def test_poison_dart_price_and_poison_are_source_values(self):
        a=self.abilities['TECHNIQUE_POISON_DART']
        self.assertEqual((a['RangeMm'],a['RechargeTicks'],a['ActivateFoodCost'],a['ActivateOreCost']),(10000,160,25,25))
        self.assertEqual((a['DpsMilli'],a['DurationTicks']),(5000,500))

    def test_spider_uses_weapon_stun_not_cosmetic_child_timer(self):
        a=self.abilities['TECHNIQUE_PARALYZE']
        self.assertEqual((a['DurationTicks'],a['RangeMm']),(40,500))
        spray=self.abilities['TECHNIQUE_SPRAY_VENOM']
        self.assertEqual((spray['DurationTicks'],spray['AreaRadiusMm'],spray['ArcDegrees'],spray['DelayTicks']),(80,10000,180,600))

    def test_darkness_replacement_and_sweeping_axe(self):
        base=self.abilities['TECHNIQUE_DARKNESS'];up=self.abilities['TECHNIQUE_PERPETUAL_DARKNESS']
        self.assertEqual((base['AreaRadiusMm'],base['Blinds'],up['AreaRadiusMm'],up['Blinds']),(5000,False,7000,True))
        self.assertEqual(base['ReplacedByTechnique'],up['RequiredTechnique'])
        self.assertEqual((base['Slot'],up['Slot'],base['DurationTicks'],up['BonusScale']),(0,0,200,500000))
        axe=self.abilities['TECHNIQUE_SWEEPING_AXE']
        self.assertEqual((axe['HealMilli'],axe['AreaRadiusMm'],axe['RechargeTicks']),(100000,5000,240))

    def test_frenzy_and_tower_upgrade_keep_their_source_targets(self):
        data=skirmish_research.research_data(self.r)['techniques']
        frenzy=data['TECHNIQUE_GOBLIN_FRENZY']['bonuses']
        self.assertEqual(set(frenzy),{'UNIT_E_GOBLIN_SPEARMAN','UNIT_E_GOBLIN_SLAVE_MASTER'})
        self.assertTrue(all(v['time_scale_ppm']==1300000 for v in frenzy.values()))
        tower=data['TECHNIQUE_POISON_ARROWS']['building_weapon']
        self.assertEqual((tower['Building'],tower['DpsMilli'],tower['Poison']['DpsMilli'],tower['Poison']['DurationTicks']),('BUILDING_E_TOWER',14000,2000,200))
        self.assertEqual(self.abilities['TECHNIQUE_ASSASSINS_CLOAK']['Kind'],18)
        self.assertIn('Adapter',self.abilities['TECHNIQUE_ASSASSINS_CLOAK']['Source'])
        blade=self.abilities['TECHNIQUE_MORGUL_BLADE']
        self.assertEqual((blade['DpsMilli'],blade['DurationTicks']),(3000,100))


if __name__=='__main__':
    unittest.main()
