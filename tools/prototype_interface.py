"""Extract a traced local HUD cache from original UVA atlas rectangles."""
import hashlib
import json
from pathlib import Path

from PIL import Image
from resolve_model import Resolver
from texture_animation import parse_uva

ROOT = Path(__file__).resolve().parent.parent


def crop_rect(rect, size):
    x, y, w, h = rect
    width, height = size
    box = tuple(round(v) for v in (x*width, y*height, (x+w)*width, (y+h)*height))
    if box[0] < 0 or box[1] < 0 or box[2] > width or box[3] > height or box[2] <= box[0] or box[3] <= box[1]:
        raise ValueError('UVA rectangle outside atlas')
    return box


def export(output, resolver=None):
    output = Path(output).resolve()
    if any(output.is_relative_to(ROOT / p) for p in ['extracted', 'TLOTR - War of the Ring']):
        raise ValueError('interface cache must be outside original and extracted data')
    resolver = resolver or Resolver()
    source = ROOT / 'extracted/UVAs/Interface'
    paths = list((source / 'Console/Main/Good').glob('*.uva'))
    paths += list((source / 'Console/ActionButtons/Good').glob('*.uva'))
    paths += [source / 'Console/Main/console_frameportrait.uva']
    paths += list((source / 'Console/Counter Markers').glob('*.uva'))
    paths += [source / ('Console/Unit Abilities/Borderless/' + name + '.uva') for name in
              ['BattleGear_Greater_Kingsfoil_Borderless','BattleGear_Andurils_Fury_Borderless','BattleGear_Immunity_Borderless',
               'BattleGear_OneRing_Borderless','BattleGear_Frodo_Sting_Borderless','BattleGear_Heroic_Aura_Borderless']]
    paths += [source / 'Console/UnitPortraits/Evil/Portrait_E_Uruk_Hai.uva']
    paths += list((source / 'Console/UnitPortraits/Good').glob('*.uva'))
    paths += list((source / 'Console/BuildingPortraits/Good').glob('*.uva'))
    paths += list((source / 'Console/Unit Abilities/Border').glob('Purchase_*.uva'))
    paths += list((source / 'Console/ObjectPortraits').glob('*.uva'))
    paths += [source / ('Console/Minimap/console_minimap_' + name + '.uva') for name in ['ore', 'well', 'place_of_power']]
    paths += [p for p in (source / 'Cursors/Good').glob('*.uva')
              if any(word in p.stem for word in ['attack', 'move', 'guard', 'patrol', 'greater_kingsfoil', 'negative'])]
    target = output / 'interface'; target.mkdir(parents=True, exist_ok=True)
    index = {'format_version': 1, 'assets': {}}
    for path in sorted(paths):
        raw = path.read_bytes(); uva = parse_uva(raw)
        if uva['frame_count'] != 1:
            continue  # Animated Palantir/alerts remain outside this static HUD slice.
        texture = resolver.texture(uva['texture'])
        texture_path = ROOT / texture['file']
        with Image.open(texture_path) as atlas:
            box = crop_rect(uva['rect'], atlas.size)
            cropped = atlas.crop(box).convert('RGBA')
            filename = path.stem + '.png'; cropped.save(target / filename)
        index['assets'][path.stem] = {'file': 'interface/' + filename, 'size': list(cropped.size),
            'uva': path.relative_to(ROOT).as_posix(), 'uva_sha256': hashlib.sha256(raw).hexdigest(),
            'texture': texture, 'texture_sha256': hashlib.sha256(texture_path.read_bytes()).hexdigest(), 'crop': box}
    (target / 'index.json').write_text(json.dumps(index, indent=2) + '\n')
    return index


def refresh(output):
    output = Path(output).resolve()
    manifest_path = output / 'manifest.json'
    manifest = json.loads(manifest_path.read_text())
    import content_pack
    content_pack.reject_slim_cache(manifest)
    index = export(output)
    manifest['interface'] = 'interface/index.json'
    for path in sorted((output / 'interface').glob('*')):
        manifest['files'][path.relative_to(output).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    return index
