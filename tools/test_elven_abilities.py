import json
import tempfile
import unittest
from pathlib import Path

import elven_abilities
import hero_abilities
import lsk
import skirmish_research
import wor
from resolve_model import Resolver


@unittest.skipUnless((wor.ROOT/'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
class ElvenImportTests(unittest.TestCase):
    def test_source_gear_clips_toolscript_and_effect_parameters(self):
        r=Resolver(); u=r.db['Units'].get('UNIT_G_ELVEN_LIGHTBRINGER')
        model=r.resolve(u.name,recurse=False); skeleton=lsk.parse((wor.ROOT/model['skeletons'][0]['file']).read_bytes())
        shield,dispel,light=hero_abilities.export(r,u,model,skeleton)
        self.assertEqual([(a['Id'],a['Slot'],a['Kind']) for a in [shield,dispel,light]],[(24,0,19),(23,1,20),(9,2,21)])
        self.assertEqual((shield['ShieldMilli'],shield['DurationTicks'],shield['RechargeTicks'],shield['EffectTick']),(150000,1000,700,13))
        self.assertEqual((dispel['RangeMm'],dispel['AreaRadiusMm'],dispel['RechargeTicks']),(15000,7000,200))
        self.assertEqual((light['RangeMm'],light['AreaRadiusMm'],light['DpsMilli'],light['DurationTicks'],light['EffectTick']),(16000,5000,22000,100,3))
        self.assertEqual([a['SoundEvents'] for a in [shield,dispel,light]],[[101],[265],[267]])
        self.assertNotIn('RequiredTechnique',shield)
        self.assertEqual((dispel['RequiredTechnique'],light['RequiredTechnique']),('TECHNIQUE_NEGATE_MAGIC','TECHNIQUE_LIGHT_OF_LOTHLORIEN'))

    def test_cloak_is_visibility_technique_and_all_research_is_available(self):
        r=Resolver(); u=r.db['Units'].get('UNIT_G_ELVEN_ARCHER')
        cloak,fletching=hero_abilities.export(r,u,None,None)
        self.assertEqual((cloak['Slot'],cloak['Kind'],cloak['Passive']),(2,18,True))
        self.assertEqual(cloak['Source']['Technique']['IsVisible'],0)
        self.assertEqual((fletching['Slot'],fletching['Kind'],fletching['Passive'],fletching['BonusScale']),(3,22,True,1200000))
        self.assertEqual(fletching['RequiredTechnique'],'TECHNIQUE_EAGLE_FLETCHING')
        self.assertEqual(fletching['Icon'],'BattleGear_Eagle_Fletching_Borderless')
        data=skirmish_research.research_data(r)['techniques']
        for name,cost,building,level in [('ELVEN_CLOAKS',165,'ARMORY',3),('NEGATE_MAGIC',75,'HOSPITAL',4),('LIGHT_OF_LOTHLORIEN',125,'HOSPITAL',4)]:
            t=data['TECHNIQUE_'+name]
            self.assertEqual((t['food_cost'],t['ore_cost'],t['duration_ticks']),(cost,cost,1800))
            self.assertEqual(t['building'],'BUILDING_G_'+building)
            self.assertEqual(t['prerequisite'],'BUILDING_G_TOWN_CENTER_LEVEL_'+str(level))

    def test_only_bound_effect_layers_are_rendered(self):
        with tempfile.TemporaryDirectory() as folder:
            data=elven_abilities.visuals(Path(folder),Resolver(),lambda _:None)
            self.assertEqual([x['Name'] for x in data['75']['Layers']],['Loth_Light','Light_Drops'])
            self.assertEqual([x['Name'] for x in data['402']['Layers']],['Ground_Light'])
            self.assertEqual(data['350']['Source']['Id'],350)
            for effect in data.values():
                self.assertTrue(effect['Layers'])
                for layer in effect['Layers']:
                    self.assertTrue(all((Path(folder)/f).is_file() for f in layer['Frames']))


if __name__=='__main__':
    unittest.main()
