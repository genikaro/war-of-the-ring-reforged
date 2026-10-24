import io
import struct
import unittest
import wave
from types import SimpleNamespace
from unittest.mock import Mock

from combat_rules import attack_choice, quantize
import combat_rules
import prototype_assets
from pcm_audio import decode_ms_adpcm


def adpcm(payload=b'\x17', recent=0, older=0, delta=16, coefficient=(256,0), total=4):
    block = struct.pack('<Bhhh',0,delta,recent,older)+payload
    fmt = struct.pack('<HHIIHHHHHhh',2,1,44100,0,len(block),4,8,2+len(payload)*2,1,*coefficient)
    def chunk(name,value):
        return name+struct.pack('<I',len(value))+value+b'\0'*(len(value)&1)
    body = b'WAVE'+chunk(b'fmt ',fmt)+chunk(b'fact',struct.pack('<I',total))+chunk(b'data',block)
    return b'RIFF'+struct.pack('<I',len(body))+body


class CombatAdapterTests(unittest.TestCase):
    def test_arrow_trail_source_width_lifetime_and_argb(self):
        profile = combat_rules.trail_profile({'duration_ms':250,'keys':[
            {'time':0,'words':[0,0,0,struct.unpack('<I',struct.pack('<f',.05))[0],0,0x3fffffff,0]},
            {'time':1,'words':[0,0,0,0,0,0x19ffffff,0]}]})
        self.assertEqual((profile['LifetimeMs'],profile['WidthMm'],profile['EndWidthMm']),(250,50,0))
        self.assertEqual((profile['StartColorArgb'],profile['EndColorArgb']),(0x3fffffff,0x19ffffff))
        with self.assertRaises(ValueError):
            combat_rules.trail_profile({'duration_ms':0,'keys':[]})

    def test_regeneration_reads_both_rate_columns_of_each_state(self):
        rows = {name: {'HealthRegenerationRate': 1.0,'HealthRegenerationRateHero': 1.0} for name in combat_rules.REGENERATION_STATES.values()}
        rows['UNIT_ANIM_STATE_RUN'] = {'HealthRegenerationRate': 0.0,'HealthRegenerationRateHero': 0.0}
        rows['UNIT_ANIM_STATE_ATTACK_MELEE_01'] = {'HealthRegenerationRate': 0.0,'HealthRegenerationRateHero': 0.16699999570846558}
        rows['UNIT_ANIM_STATE_REPAIR'] = {'HealthRegenerationRate': 2.0,'HealthRegenerationRateHero': 2.0}
        rates = combat_rules.regeneration(Mock(get=rows.get))
        self.assertEqual(len(rates),2*len(combat_rules.REGENERATION_STATES))
        self.assertEqual((rates['IdleMilli'],rates['RunMilli'],rates['AttackMilli'],rates['AttackHeroMilli'],rates['RepairMilli']),(1000,0,0,167,2000))

    def test_locomotion_ratio_uses_source_xy_and_state_choices(self):
        skeleton = SimpleNamespace(joints=[SimpleNamespace(name='J_Position ')], clips=[
            SimpleNamespace(name='walk',duration=2,tracks=[SimpleNamespace(positions=[(0,0,0),(0,4,99)])]),
            SimpleNamespace(name='run',duration=1,tracks=[SimpleNamespace(positions=[(0,0,0),(0,4,99)])])])
        model = {'animation_states': [
            {'name':'UNIT_ANIM_STATE_WALK','id':4,'choices':[{'weight':0,'clip_name':'unused'},{'weight':10,'clip_name':'walk'}]},
            {'name':'UNIT_ANIM_STATE_RUN','id':5,'choices':[{'weight':10,'clip_name':'run'}]}]}
        motion, scale = combat_rules.locomotion(model,skeleton)
        self.assertEqual(scale,500000)
        self.assertEqual(motion['Walk']['SourceSpeed'],2)
        self.assertEqual(motion['Run']['Clip'],'run')
        skeleton.clips[0].tracks[0].positions = [(0,0,0),(0,0,1)]
        with self.assertRaisesRegex(ValueError,'no forward movement'):
            combat_rules.locomotion(model,skeleton)
        skeleton.clips[0].tracks[0].positions = [(0,0,0),(0,40,0)]
        with self.assertRaisesRegex(ValueError,'ratio'):
            combat_rules.locomotion(model,skeleton)

    def test_run_scale_is_relative_to_the_reference_runner(self):
        self.assertEqual(combat_rules.run_scale({'Run': {'SourceSpeed': 7.215122699737549}},4.762779712677002),1514897)
        self.assertEqual(combat_rules.run_scale({'Run': {'SourceSpeed': 4.762779712677002}},4.762779712677002),1000000)
        with self.assertRaisesRegex(ValueError,'run speed'):
            combat_rules.run_scale({'Run': {'SourceSpeed': 20}},4)

    def test_type_matrix_resolves_source_ids_and_all_directions(self):
        table = Mock()
        table.get.side_effect = lambda name: SimpleNamespace(id={
            'DAMAGE_TYPE_CUTTING': 0,'DAMAGE_TYPE_PIERCING': 1,
            'DAMAGE_TYPE_BLUNT': 2,'DAMAGE_TYPE_HERO': 3}[name])
        matrix = combat_rules.type_matchups(table)
        self.assertEqual(len(matrix),16)
        self.assertEqual([[r['Scale'] for r in matrix if r['From'] == a] for a in range(4)], [
            [1000000,1250000,750000,1000000], [750000,1000000,1250000,1000000],
            [1250000,750000,1000000,1000000], [1000000,1000000,1000000,1000000]])

    def test_event_ticks_ceil_source_fraction(self):
        result = attack_choice({'clip_name':'swing','weight':1,'event_indices':[0,1]},[
            {'name':'melee_hit','time':.5,'damage_scale':1},
            {'name':'melee_hit','time':1,'damage_scale':.5}],1.53)
        self.assertEqual(result['DurationTicks'],31)
        self.assertEqual([(h['Tick'],h['Scale']) for h in result['Hits']],[(16,1000000),(30,500000)])

    def test_invalid_events_fail(self):
        for fraction in (-1,1.1,float('nan')):
            with self.assertRaises(ValueError):
                attack_choice({'clip_name':'x','weight':1,'event_indices':[0]},[
                    {'name':'melee_hit','time':fraction,'damage_scale':1}],1)
        with self.assertRaises(ValueError):
            attack_choice({'clip_name':'x','weight':1,'event_indices':[]},[],1)

    def test_missile_release_has_unit_weight_and_source_timing(self):
        result = attack_choice({'clip_name':'shoot','weight':1,'event_indices':[0,1]},[
            {'name':'missile_release','time':0.5333330035209656},
            {'name':'sound','time':0.23}],2.0333333015441895,'missile_release')
        self.assertEqual(result['DurationTicks'],41)
        self.assertEqual([(h['Tick'],h['Scale']) for h in result['Hits']],[(22,1000000)])

    def test_quantization_is_explicit(self):
        self.assertEqual(quantize(.0005),1)
        with self.assertRaises(ValueError):
            quantize(float('inf'))

    def test_protected_outputs_fail_before_reading_sources(self):
        for directory in ('extracted/cache','TLOTR - War of the Ring/cache'):
            with self.assertRaisesRegex(ValueError,'outside'):
                combat_rules.export(combat_rules.ROOT/directory)

    def test_equipment_restore_is_idempotent_and_preserves_bones(self):
        data = {'nodes':[{'name':'bone','children':[1]},{'name':'body','mesh':0},{'mesh':1},{'mesh':2}],
                'scenes':[{'nodes':[0]}], 'extras':{'wotrAppearance':{'hiddenNodes':[{'node':2,'parent':0},{'node':3,'parent':None}]}}}
        prototype_assets.restore_meshes(data); prototype_assets.restore_meshes(data)
        self.assertEqual(data['nodes'][0],{'name':'bone','children':[1,2]})
        self.assertEqual(data['scenes'][0]['nodes'],[0,3])
        self.assertEqual([n['name'] for n in data['nodes'][1:]],['WotrMesh_1','WotrMesh_2','WotrMesh_3'])

    def samples(self,source):
        with wave.open(io.BytesIO(decode_ms_adpcm(source))) as pcm:
            self.assertEqual((pcm.getnchannels(),pcm.getsampwidth(),pcm.getframerate()),(1,2,44100))
            payload = pcm.readframes(pcm.getnframes())
            return struct.unpack('<'+'h'*(len(payload)//2),payload)

    def test_adpcm_seed_order_signed_nibbles_and_adaptation(self):
        self.assertEqual(self.samples(adpcm(b'\x18',recent=10,older=-20)),(-20,10,26,-102))
        self.assertEqual(self.samples(adpcm(b'\x77')), (0,0,112,378))

    def test_adpcm_prediction_truncates_toward_zero(self):
        self.assertEqual(self.samples(adpcm(b'\x00',recent=-3,coefficient=(64,0))),(0,-3,0,0))

    def test_adpcm_clips_and_trims_fact_samples(self):
        self.assertEqual(self.samples(adpcm(b'\x77',recent=32760,total=3)),(0,32760,32767))

    def test_adpcm_rejects_truncation_and_bad_header(self):
        for source in (adpcm()[:-1],adpcm(total=100),adpcm(delta=-1),b'not a wav'):
            with self.assertRaises(ValueError):
                decode_ms_adpcm(source)


if __name__ == '__main__':
    unittest.main()
