import tempfile
import unittest
from pathlib import Path

import hero_abilities
import lsk
import nature_abilities
import skirmish_production
import skirmish_research
import wor
from resolve_model import Resolver


@unittest.skipUnless((wor.ROOT/'TLOTR - War of the Ring/Database/Units.lqr').exists(),'owner install not available')
class NatureImportTests(unittest.TestCase):
    def test_forms_healing_and_source_clips(self):
        r=Resolver()
        def abilities(name):
            u=r.db['Units'].get(name);m=r.resolve(name,recurse=False)
            return hero_abilities.export(r,u,m,lsk.parse((wor.ROOT/m['skeletons'][0]['file']).read_bytes()))
        morph,herbs=abilities('UNIT_G_BEORNING');human,blow=abilities('UNIT_G_BEAR');tree,=abilities('UNIT_G_HUORN')
        self.assertEqual((morph['MorphUnit'],human['MorphUnit']),('UNIT_G_BEAR','UNIT_G_BEORNING'))
        self.assertEqual((herbs['HealMilli'],herbs['RangeMm'],herbs['ResponseRangeMm'],herbs['RechargeTicks'],herbs['EffectTick']),(75000,8000,15000,120,17))
        self.assertEqual((blow['HealMilli'],blow['ArcDegrees'],blow['AreaRadiusMm'],blow['KnockbackSpeedMm'],blow['EffectTick']),(75000,260,4000,7000,22))
        self.assertEqual((tree['DurationTicks'],tree['AreaRadiusMm'],tree['DpsMilli'],tree['RechargeTicks']),(160,10000,20000,500))
        self.assertEqual((tree['SoundEvents'],tree['EndSoundEvents']),([232],[186]))

    def test_research_and_bear_is_transformation_only(self):
        r=Resolver();data=skirmish_research.research_data(r,('good',))
        self.assertEqual(len(data['techniques']),31)
        self.assertEqual(list(data['unsimulated']),['TECHNIQUE_LOOKOUT_TOWER'])
        for name in ['BEAR_FORM','SAVAGE_BLOW','TREEFORM']:
            self.assertEqual(data['techniques']['TECHNIQUE_'+name]['building'],'BUILDING_G_HOSPITAL')
        production=skirmish_production.production_data(r,['UNIT_G_BEORNING','UNIT_G_BEAR','UNIT_G_HUORN'])
        self.assertEqual(production['producers']['BUILDING_G_STABLES']['units'],['UNIT_G_BEORNING','UNIT_G_HUORN'])
        self.assertNotIn('UNIT_G_BEAR',production['units'])

    def test_source_effects_include_root_mesh(self):
        with tempfile.TemporaryDirectory() as folder:
            data=nature_abilities.visuals(Path(folder),Resolver(),lambda _:None)
            self.assertTrue((Path(folder)/data['363']['RootModel']).is_file())
            self.assertEqual([l['Name'] for l in data['363']['Layers']],['burst'])
            for effect in data.values():
                self.assertTrue(effect['Layers'])
                self.assertTrue(all((Path(folder)/f).is_file() for layer in effect['Layers'] for f in layer['Frames']))


if __name__=='__main__':unittest.main()
