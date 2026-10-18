"""Resolve a War of the Ring model to the extracted files it needs.

Input is a Models.lqr symbolic name (MODEL_G_ARAGORN), a Units.lqr name
(UNIT_G_HERO_ARAGORN, uses its Model column), a numeric Models id, or an .lmd
path. Output lists the skeleton (.lsk), every mesh part (.lms) with its
weights (.lwt), diffuse and team colour mask textures (.dds), and the
attached models (resolved recursively, with skeleton joint names) and effects.

Database paths map onto the extracted tree like this:
    Models.lqr     Filename  ->  extracted/Models/<Filename>
    Meshes.lqr     Filename  ->  extracted/Meshes/<Filename>
    Weights.lqr    Filename  ->  extracted/Weights/<Filename>
    Skeletons.lqr  Filename  ->  extracted/Skeletons/<Filename>
    Textures.lqr   Filename  ->  extracted/Textures/<Filename> with .tga
                                 replaced by .dds (Conversion None keeps .tga)
    Effects.lqr    Filename  ->  extracted/Effects/<Filename>
Lookups are case insensitive.

Texture choice per mesh: texture slots that share state bits with the mesh
(see lmd.py); with a MATERIAL_TEAM_COLOR material, slots whose file name
contains "mask" are the team colour mask, the rest are diffuse. The first
diffuse slot on the lowest channel is the default, others are listed as
alternates (damaged / corrupted swaps, upgrade levels).

Usage:
    python resolve_model.py <MODEL_NAME|UNIT_NAME|id|model.lmd> [--json] [--no-recurse]
    python resolve_model.py --validate [--json] [--list-missing]
"""
import json
import os
import struct
import sys
from dataclasses import asdict

import lmd
import lqr
import lsk

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..'))
EXTRACTED = os.path.join(ROOT, 'extracted')
TEAM_COLOR = 'MATERIAL_TEAM_COLOR'
CLASS_NAMES = {0: 'object', 1: 'unit', 2: 'building', 3: 'ambient creature',
               4: 'interface', 5: 'static', 6: 'ambient'}
ANIM_STATE_TABLES = {0: 'ObjectAnimStates', 1: 'UnitAnimStates', 2: 'BuildingAnimStates',
                     3: 'AmbientCreatureAnimStates', 4: 'InterfaceAnimStates',
                     5: 'StaticAnimStates', 6: 'AmbientAnimStates'}


class FileIndex:
    """Case insensitive view of the extracted tree."""

    def __init__(self, root=EXTRACTED):
        self.root = root
        self.files = {}
        for base, _, names in os.walk(root):
            for n in names:
                full = os.path.join(base, n)
                self.files[os.path.relpath(full, root).replace('\\', '/').lower()] = full

    def find(self, *parts):
        rel = '/'.join(p.replace('\\', '/').strip('/') for p in parts)
        return self.files.get(rel.lower())

    def find_basename(self, folder, name):
        """The single file called <name> anywhere below <folder>, else None."""
        prefix, name = folder.lower() + '/', name.lower()
        hits = [f for k, f in self.files.items() if k.startswith(prefix) and k.rsplit('/', 1)[-1] == name]
        return hits[0] if len(hits) == 1 else None

    def show(self, path):
        return os.path.relpath(path, ROOT).replace('\\', '/') if path else None


def joint_names(path):
    """Joint names from the v1.0/v2.0 parser, or None for an invalid file."""
    try:
        with open(path, 'rb') as f:
            return [joint.name for joint in lsk.parse(f.read()).joints]
    except (OSError, ValueError, struct.error):
        return None


class Resolver:
    def __init__(self, db_path=lqr.DEFAULT_DB, extracted=EXTRACTED):
        self.db = lqr.Database(db_path)
        self.index = FileIndex(extracted)
        self.models = self.db['Models']
        self.meshes = self.db['Meshes']
        self.weights = self.db['Weights']
        self.textures = self.db['Textures']
        self.skeletons = self.db['Skeletons']
        self.materials = self.db['Materials']
        self.effects = self.db['Effects']
        self.model_by_file = {r['Filename'].replace('\\', '/').lower(): r for r in self.models}
        self._joints = {}
        self._clips = {}

    # -- single records ------------------------------------------------
    def _entry(self, table, folder, rid, path=''):
        if rid is None or rid < 0:
            if not path:
                return None
            # raw path from the authoring machine, keep the part below <folder>
            low = path.replace('\\', '/')
            cut = low.lower().find('/%s/' % folder.lower())
            rel = low[cut + len(folder) + 2:] if cut >= 0 else os.path.basename(low)
            found = self.index.find(folder, rel)
            entry = {'name': None, 'id': None, 'source': path}
            if not found:                         # stale authoring folder: match the file name
                found = self.index.find_basename(folder, os.path.basename(rel))
                entry['matched_by_name'] = found is not None
            entry.update(file=self.index.show(found), exists=found is not None)
            return entry
        rec = table.get(rid)
        if rec is None:
            return {'name': None, 'id': rid, 'file': None, 'exists': False, 'error': 'no record'}
        fn = rec['Filename']
        found = None
        if table is self.textures:
            stem = os.path.splitext(fn)[0]
            order = [fn, stem + '.dds'] if rec['Conversion'] == 0 else [stem + '.dds', fn]
            for cand in order:
                found = self.index.find(folder, cand)
                if found:
                    break
            want = found or os.path.join(EXTRACTED, folder, order[0])
        else:
            found = self.index.find(folder, fn)
            want = found or os.path.join(EXTRACTED, folder, fn)
        return {'name': rec.name, 'id': rid, 'file': self.index.show(want), 'exists': found is not None}

    def texture(self, rid, path=''):
        return self._entry(self.textures, 'Textures', rid, path)

    def joints(self, skeleton_file):
        if skeleton_file not in self._joints:
            self._joints[skeleton_file] = joint_names(os.path.join(ROOT, skeleton_file))
        return self._joints[skeleton_file]

    def clips(self, skeleton_file):
        if skeleton_file not in self._clips:
            try:
                with open(os.path.join(ROOT, skeleton_file), 'rb') as f:
                    skeleton = lsk.parse(f.read())
                self._clips[skeleton_file] = [clip.name for clip in skeleton.clips]
            except (OSError, ValueError, struct.error):
                self._clips[skeleton_file] = []
        return self._clips[skeleton_file]

    def animation_states(self, model, skeleton_file):
        """Keep raw choices and keys; annotate state IDs and primary clip indices."""
        table_name, _default = self.animation_class(model.model_class)
        table = self.db[table_name] if table_name and table_name.lower() in self.db.files else None
        clips = self.clips(skeleton_file) if skeleton_file else []
        states = []
        for state in model.animation_states:
            entry = asdict(state)
            record = table.get(state.id) if table is not None else None
            entry['name'] = record.name if record else None
            for choice in entry['choices']:
                clip = choice['clip']
                choice['clip_name'] = clips[clip] if 0 <= clip < len(clips) else None
            states.append(entry)
        return states

    def event_names(self, event, joint_names, model):
        """Clip event plus `<field>_name` for its joint, attachment and table references."""
        out = dict(event)
        tables = {'sound': 'SoundEvents', 'speech': 'SpeechFXEventTypes',
                  'effect': 'Effects', 'projectile': 'Projectiles'}
        for field, table in tables.items():
            if field in event and table.lower() in self.db.files:
                record = self.db[table].get(event[field])
                out[field + '_name'] = record.name if record else None
        if 'joint' in event:
            j = event['joint']
            out['joint_name'] = joint_names[j] if joint_names and 0 <= j < len(joint_names) else None
        if 'attachment' in event:
            models = [a for a in model.attachments if a.kind == 'model']
            a = event['attachment']
            record = self.models.get(models[a].id) if 0 <= a < len(models) else None
            out['attachment_name'] = record.name if record else None
        return out

    def animation_class(self, model_class):
        """Use the shipped class metadata, including its declared default state."""
        if 'modelclasses' in self.db.files:
            record = self.db['ModelClasses'].get(model_class)
            if record is not None:
                return record['AnimationStateTable'], record['DefaultAnimState']
        return ANIM_STATE_TABLES.get(model_class), None

    # -- models --------------------------------------------------------
    def model_by_path(self, path):
        """Models record for an authoring path (.../Models/<Filename>), by file name as fallback."""
        rel = path.replace('\\', '/').lower().split('/models/', 1)[-1]
        rec = self.model_by_file.get(rel)
        if rec is None:
            name = rel.rsplit('/', 1)[-1]
            hits = [r for k, r in self.model_by_file.items() if k.rsplit('/', 1)[-1] == name]
            rec = hits[0] if len(hits) == 1 else None
        return rec

    def lookup(self, key):
        """Models record for a name, id, unit name or .lmd path (or None)."""
        if os.path.isfile(key):
            rel = os.path.relpath(os.path.abspath(key), os.path.join(EXTRACTED, 'Models'))
            return self.model_by_file.get(rel.replace('\\', '/').lower()), key
        if key.upper().startswith('UNIT_'):
            unit = self.db['Units'].get(key.upper())
            if unit is None:
                return None, None
            key = unit['Model']
        elif key[:1].isdigit():
            key = int(key, 0)
        rec = self.models.get(key if isinstance(key, int) else key.upper())
        return rec, None

    def resolve(self, key, recurse=True, _depth=0):
        rec, path = (key, None) if isinstance(key, lqr.Record) else self.lookup(key)
        if rec is None and path is None:
            raise KeyError('unknown model: %s' % key)
        if path is None:
            path = self.index.find('Models', rec['Filename'])
        out = {'model': rec.name if rec else None, 'id': rec.id if rec else None,
               'file': self.index.show(path) if path else
               'extracted/Models/' + rec['Filename'].replace('\\', '/'),
               'exists': bool(path)}
        if rec:
            out['scale'] = [round(rec[k], 4) for k in ('ScaleX', 'ScaleY', 'ScaleZ')]
        if not path:
            return out
        m = lmd.load(path)
        out['class'] = CLASS_NAMES.get(m.model_class, m.model_class)
        out['version'] = m.version
        out['skeletons'] = [self._entry(self.skeletons, 'Skeletons', s) for s in m.skeletons]
        out['materials'] = [(self.materials.get(r.id).name if self.materials.get(r.id) else r.id)
                            for r in m.materials]
        team = TEAM_COLOR in out['materials']

        tex_entries = []
        for slot in m.textures:
            e = self.texture(slot.texture.id, slot.texture.path)
            if e:
                e['channel'] = slot.channels[0] if slot.channels else None
                fname = (e['file'] or e.get('source') or '').lower()
                e['role'] = 'mask' if team and 'mask' in os.path.basename(fname) else 'diffuse'
            tex_entries.append(e)
        out['textures'] = tex_entries

        out['parts'] = []
        states = m.states()
        for i, slot in enumerate(m.meshes):
            part = {'mesh': self._entry(self.meshes, 'Meshes', slot.mesh.id, slot.mesh.path),
                    'weights': self._entry(self.weights, 'Weights', slot.weight, slot.weight_path),
                    'state_mask': slot.mesh.state_mask}
            cand = [j for j in m.textures_for_mesh(i) if tex_entries[j]]
            cand.sort(key=lambda j: (tex_entries[j]['channel'] or 0, j))
            diffuse = [j for j in cand if tex_entries[j]['role'] == 'diffuse']
            masks = [j for j in cand if tex_entries[j]['role'] == 'mask']
            part['texture'] = tex_entries[diffuse[0]] if diffuse else None
            part['mask'] = tex_entries[masks[0]] if masks else None
            if len(diffuse) > 1 or len(masks) > 1:
                part['alternates'] = [tex_entries[j]['name'] or tex_entries[j]['file']
                                      for j in diffuse[1:] + masks[1:]]
            mesh_states = [b for b, (ms, _) in states.items() if i in ms]
            if mesh_states and len(states) > 1:
                part['states'] = mesh_states
            out['parts'].append(part)
        if len(states) > 1:
            out['states'] = {b: {'meshes': ms, 'textures': ts} for b, (ms, ts) in states.items()}

        skel_file = out['skeletons'][0]['file'] if out['skeletons'] and out['skeletons'][0] else None
        out['animation_state_table'], out['default_animation_state'] = self.animation_class(m.model_class)
        out['animation_states'] = self.animation_states(m, skel_file)
        names = self.joints(skel_file) if skel_file else None
        out['attachments'] = []
        for a in m.attachments:
            item = {'kind': a.kind, 'joint': a.joint, 'offset': list(a.offset),
                    'shown': a.shown, 'state_mask': a.state_mask}
            if a.joint >= 0 and names and a.joint < len(names):
                item['joint_name'] = names[a.joint]
            if a.kind == 'effect':
                item['effect'] = self._entry(self.effects, 'Effects', a.id, a.path)
            else:
                sub = self.models.get(a.id) if a.id >= 0 else self.model_by_path(a.path)
                if sub is None:
                    item['model'] = {'model': None, 'id': a.id, 'source': a.path, 'exists': False}
                elif recurse and _depth < 4:
                    item['model'] = self.resolve(sub, True, _depth + 1)
                else:
                    found = self.index.find('Models', sub['Filename'])
                    item['model'] = {'model': sub.name, 'id': sub.id, 'exists': bool(found),
                                     'file': self.index.show(found) if found else
                                     'extracted/Models/' + sub['Filename'].replace('\\', '/')}
            out['attachments'].append(item)
        out['secondary_events'] = m.secondary_events
        out['events'] = None if m.events is None else [self.event_names(e, names, m) for e in m.events]
        if m.warnings:
            out['warnings'] = m.warnings
        return out


def missing_files(res, effects=True):
    """Files a resolved model references that are not in the extracted tree."""
    out = []

    def check(e):
        if e and not e.get('exists'):
            out.append(e.get('file') or e.get('source') or '%s 0x%x' % (e.get('name'), e.get('id') or 0))

    check(res)
    for s in res.get('skeletons', []):
        check(s)
    for t in res.get('textures', []):
        check(t)
    for p in res.get('parts', []):
        check(p['mesh'])
        check(p['weights'])
    for a in res.get('attachments', []):
        if a['kind'] == 'model':
            out += missing_files(a['model'], effects)
        elif effects:
            check(a.get('effect'))
    return out


def _name(e):
    if not e:
        return '-'
    return '%s  %s%s' % (e.get('file') or e.get('source'), e.get('name') or '',
                         '' if e.get('exists') else '  [MISSING]')


def print_model(res, indent=''):
    print('%s%s  %s%s' % (indent, res.get('model'), res.get('file'), '' if res.get('exists') else '  [MISSING]'))
    if not res.get('exists') or 'class' not in res:
        return
    ind = indent + '  '
    print('%sclass %s, lmd v%.1f, scale %s' % (ind, res['class'], res['version'], res.get('scale')))
    for i, s in enumerate(res['skeletons']):
        print('%sskeleton%s %s' % (ind, '' if i == 0 else ' slot %d' % i, _name(s)))
    if res['materials']:
        print('%smaterials %s' % (ind, ', '.join(str(x) for x in res['materials'])))
    for i, p in enumerate(res['parts']):
        print('%spart %d' % (ind, i))
        print('%s  mesh     %s' % (ind, _name(p['mesh'])))
        if p['weights']:
            print('%s  weights  %s' % (ind, _name(p['weights'])))
        print('%s  texture  %s' % (ind, _name(p['texture'])))
        if p['mask']:
            print('%s  mask     %s' % (ind, _name(p['mask'])))
        if p.get('alternates'):
            print('%s  alternates %s' % (ind, ', '.join(p['alternates'])))
        if p.get('states'):
            print('%s  states   %s' % (ind, p['states']))
    for a in res['attachments']:
        if a['joint'] >= 0:
            where = 'joint %d %s' % (a['joint'], a.get('joint_name', ''))
        elif 'parts' not in a.get('model', {}):
            where = 'no joint'
        elif any(p['weights'] for p in a['model']['parts']):
            where = 'parent skeleton (skinned)'
        else:
            where = 'model origin'
        if a['kind'] == 'effect':
            print('%seffect at %s: %s' % (ind, where.strip(), _name(a['effect'])))
        else:
            print('%sattached at %s:' % (ind, where.strip()))
            print_model(a['model'], ind + '  ')
    for state in res.get('animation_states', []):
        print('%sanimation state %d %s:' % (ind, state['id'], state['name'] or ''))
        for choice in state['choices']:
            active_meshes = [i for i, selection in enumerate(choice['mesh_selections']) if selection >= 0]
            print('%s  %s, weight %d, meshes %s, models %s' % (
                ind, choice['clip_name'] or 'clip %d' % choice['clip'], choice['weight'],
                active_meshes, choice['models']['indices']))
    for w in res.get('warnings', []):
        print('%swarning: %s' % (ind, w))


def validate(resolver, as_json=False, list_missing=False):
    """Resolve every .lmd under extracted/Models and count the complete ones."""
    files = sorted(f for k, f in resolver.index.files.items() if k.startswith('models/') and k.endswith('.lmd'))
    stats = {'models': len(files), 'parse_errors': 0, 'not_in_database': 0,
             'complete': 0, 'complete_with_effects': 0, 'incomplete': 0,
             'parts': 0, 'parts_with_texture': 0, 'parts_with_mask': 0,
             'skinned_parts': 0, 'with_skeleton': 0, 'with_attachments': 0}
    missing = {}
    for f in files:
        try:
            res = resolver.resolve(f, recurse=True)
        except Exception as e:                    # report, keep going
            stats['parse_errors'] += 1
            missing[resolver.index.show(f)] = ['parse error: %s' % e]
            continue
        if res['model'] is None:
            stats['not_in_database'] += 1
        stats['parts'] += len(res['parts'])
        stats['parts_with_texture'] += sum(1 for p in res['parts'] if p['texture'])
        stats['parts_with_mask'] += sum(1 for p in res['parts'] if p['mask'])
        stats['skinned_parts'] += sum(1 for p in res['parts'] if p['weights'])
        stats['with_skeleton'] += bool(res['skeletons'])
        stats['with_attachments'] += bool(res['attachments'])
        geo = missing_files(res, effects=False)
        allm = missing_files(res, effects=True)
        stats['complete'] += not geo
        stats['complete_with_effects'] += not allm
        if allm:
            stats['incomplete'] += 1
            missing[res['file']] = sorted(set(allm))
    if as_json:
        print(json.dumps({'stats': stats, 'missing': missing if list_missing else len(missing)}, indent=1))
        return 0
    for k, v in stats.items():
        print('%-24s %d' % (k, v))
    if list_missing:
        for f, lst in sorted(missing.items()):
            print(f)
            for x in lst:
                print('    missing', x)
    return 0


def main(argv):
    args = [a for a in argv[1:] if not a.startswith('--')]
    opts = {a for a in argv[1:] if a.startswith('--')}
    resolver = Resolver()
    if '--validate' in opts:
        return validate(resolver, '--json' in opts, '--list-missing' in opts)
    if not args:
        print(__doc__)
        return 1
    for key in args:
        try:
            res = resolver.resolve(key, recurse='--no-recurse' not in opts)
        except KeyError as e:
            print(e.args[0])
            return 1
        if '--json' in opts:
            print(json.dumps(res, indent=1))
        else:
            print_model(res)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
