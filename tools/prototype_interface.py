"""Extract a traced local HUD cache from original UVA atlas rectangles."""
import hashlib
import json
import math
import struct
from pathlib import Path, PureWindowsPath

from PIL import Image
from resolve_model import Resolver
from texture_animation import parse_uva

ROOT = Path(__file__).resolve().parent.parent


def cursor_hotspot(data, size):
    """LIQDCRSR v1: UVA id, normalized display width/height, then image-relative hotspot X/Y."""
    if len(data) != 32 or data[:8] != b'LIQDCRSR' or struct.unpack_from('<f', data, 8)[0] != 1:
        raise ValueError('unsupported cursor descriptor')
    width, height, x, y = struct.unpack_from('<4f', data, 16)
    if not all(math.isfinite(v) for v in (width, height, x, y)) or width <= 0 or height <= 0 or not 0 <= x <= 1 or not 0 <= y <= 1:
        raise ValueError('invalid cursor hotspot')
    return [min(size[0]-1,round(x*size[0])), min(size[1]-1,round(y*size[1]))]


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
    cursor_descriptors = {}
    for descriptor in (ROOT / 'extracted/Cursors').glob('*/*.lcr'):
        raw_cursor = descriptor.read_bytes()
        if len(raw_cursor) != 32 or raw_cursor[:8] != b'LIQDCRSR':
            raise ValueError('unsupported cursor descriptor: '+str(descriptor))
        cursor_uva = resolver.db['UVAs'].get(struct.unpack_from('<i',raw_cursor,12)[0])
        cursor_descriptors[PureWindowsPath(cursor_uva['Filename']).stem] = (descriptor,raw_cursor)
    source = ROOT / 'extracted/UVAs/Interface'
    paths = list((source / 'Console/Main/Good').glob('*.uva'))
    paths += list((source / 'Console/ActionButtons/Good').glob('*.uva'))
    paths += [source / 'Console/Main/console_frameportrait.uva']
    paths += list((source / 'Console/Counter Markers').glob('*.uva'))
    paths += list((source / 'Console/Unit Abilities/Borderless').glob('*.uva'))
    paths += [source / ('Console/Unit Abilities/Misc/' + name + '.uva')
              for name in ['Clickable_Border', 'Good_Purchase_Overlay', 'Evil_Purchase_Overlay']]
    paths += list((source / 'Console/SpellButtons/Good').glob('Global_Spell_G_*_Bordered.uva'))
    paths += [source / 'Console/UnitPortraits/Evil/Portrait_E_Uruk_Hai.uva']
    paths += list((source / 'Console/UnitPortraits/Good').glob('*.uva'))
    paths += list((source / 'Console/BuildingPortraits/Good').glob('*.uva'))
    paths += list((source / 'Console/Unit Abilities/Border').glob('Purchase_*.uva'))
    paths += [source / 'Console/Unit Abilities/Border/Default_Darkness_Border.uva']  # the Evil Hospital's Darkness technique
    paths += list((source / 'Console/ObjectPortraits').glob('*.uva'))
    paths += list((source / 'Console/Place of Power Abilities').glob('*_Bordered.uva'))
    paths += list((source / 'Cursors/Neutral').glob('*.uva'))
    paths += [source / ('Console/Minimap/console_minimap_' + name + '.uva') for name in ['ore', 'well', 'place_of_power']]
    # The parchment tooltip above the console's right section: backing, title band, cost cells and Description strip.
    paths += list((source / 'Console/Tooltips/Good').glob('console_tooltip_g_right_*.uva'))
    paths += [source / 'Console/Tooltips/Good/console_tooltip_global_icon_population.uva']
    paths += list((source / 'Console/Tooltips').glob('*/*_right_counter_graph.uva'))
    paths += [p for p in (source / 'Cursors/Good').glob('*.uva')
              if any(word in p.stem for word in ['attack', 'move', 'guard', 'patrol', 'kingsfoil', 'negative', 'protection',
                                               'negate', 'dispel', 'light_of_lothlorien', 'trueshot', 'flame_shield',
                                               'rain_of_fire', 'capture', 'blind', 'brambles', 'heroic_legacy', 'blessed_wind', 'willow', 'aviary', 'pointer'])]
    # The in-game menu (build-96): the Good menu frame (InGame_MainMenu), the Exit box (Quit_Acknowledgement), and the
    # gold-edged buttons, check boxes, radio buttons and slider pieces of the original Settings screen (InGame_Options).
    paths += [source / 'Menu/background_good_mainmenu.uva']
    paths += [source / 'Console/Dialogs/Quit Acknowledgement/Good/console_dialog_quit_acknowledgement_g_box.uva']
    paths += [source / ('Menu/control_' + name + '.uva') for name in [
        'yellowbox_regular', 'yellowbox_highlighted', 'yellowbox_pressed', 'checkbox_checked', 'checkbox_empty',
        'radiobutton_check1', 'radiobutton_empty', 'sliderball_regular', 'slider_end_cap_left', 'slider_end_cap_right',
        'golden_panel_thin']]
    # Build-133: the Evil side's console, action buttons, portraits, tooltip parchment, cursors and menu frame. The game
    # draws them in place of the Good ones for an Evil player (console_e_* for console_g_*).
    paths += list((source / 'Console/Main/Evil').glob('*.uva'))
    paths += list((source / 'Console/ActionButtons/Evil').glob('*.uva'))
    paths += list((source / 'Console/UnitPortraits/Evil').glob('*.uva'))
    paths += list((source / 'Console/BuildingPortraits/Evil').glob('*.uva'))
    paths += list((source / 'Console/SpellButtons/Evil').glob('Global_Spell_E_*_Bordered.uva'))
    paths += list((source / 'Console/Tooltips/Evil').glob('console_tooltip_e_right_*.uva'))
    paths += [p for p in (source / 'Cursors/Evil').glob('*.uva')
              if any(word in p.stem for word in ['attack', 'move', 'guard', 'patrol', 'negative', 'repair', 'mining', 'enter',
                                                 'cast', 'fate_power', 'extinguish', 'prison_of_ice', 'invisible_force', 'pestilence', 'twisting_influence',
                                                 'bind_shadow', 'blood_mark', 'dark_tracking', 'grishnakh', 'poison_trap', 'shadow_walk', 'pointer'])]
    paths += [source / 'Menu/background_evil_mainmenu.uva']
    target = output / 'interface'; target.mkdir(parents=True, exist_ok=True)
    index = {'format_version': 1, 'assets': {}}
    for path in sorted(set(paths)):
        raw = path.read_bytes(); uva = parse_uva(raw)
        animated_orb = path.stem in ('console_g_powerbutton', 'console_e_powerbutton')
        if uva['frame_count'] != 1 and not animated_orb:
            continue
        texture = resolver.texture(uva['texture'])
        texture_path = ROOT / texture['file']
        with Image.open(texture_path) as atlas:
            box = crop_rect(uva['rect'], atlas.size)
            cropped = atlas.crop(box).convert('RGBA')
            if animated_orb:
                x, y, width, height = uva['rect']
                tile_size = cropped.size
                cropped = Image.new('RGBA', (tile_size[0]*uva['frame_count'], tile_size[1]))
                for frame in range(uva['frame_count']):
                    rect = (x + frame % uva['frames_per_row']*width, y + frame // uva['frames_per_row']*height, width, height)
                    tile = atlas.crop(crop_rect(rect, atlas.size)).convert('RGBA')
                    cropped.paste(tile, (frame*tile_size[0], 0))
            filename = path.stem + '.png'; cropped.save(target / filename)
        index['assets'][path.stem] = {'file': 'interface/' + filename, 'size': list(cropped.size),
            'uva': path.relative_to(ROOT).as_posix(), 'uva_sha256': hashlib.sha256(raw).hexdigest(),
            'texture': texture, 'texture_sha256': hashlib.sha256(texture_path.read_bytes()).hexdigest(), 'crop': box}
        if animated_orb:
            index['assets'][path.stem].update(frame_count=uva['frame_count'], frame_ms=uva['frame_ms'])
        if path.stem in cursor_descriptors:
            descriptor,raw_cursor = cursor_descriptors[path.stem]
            index['assets'][path.stem]['cursor_hotspot'] = cursor_hotspot(raw_cursor, cropped.size)
            index['assets'][path.stem]['cursor_source'] = {'file': descriptor.relative_to(ROOT).as_posix(),
                                                         'sha256': hashlib.sha256(raw_cursor).hexdigest()}
    # The Settings frame has no UVA: InGame_Options draws the whole texture, cropped here to its opaque part.
    texture = resolver.texture(resolver.db['Textures'].get('TEXTURE_GAME_SETTINGS_SCREEN_G_BG').id)
    texture_path = ROOT / texture['file']
    with Image.open(texture_path) as image:
        image = image.convert('RGBA'); box = image.getchannel('A').getbbox()
        cropped = image.crop(box); filename = 'game_settings_screen_g_bg.png'; cropped.save(target / filename)
    index['assets']['game_settings_screen_g_bg'] = {'file': 'interface/' + filename, 'size': list(cropped.size),
        'texture': texture, 'texture_sha256': hashlib.sha256(texture_path.read_bytes()).hexdigest(), 'crop': box}
    (target / 'index.json').write_text(json.dumps(index, indent=2) + '\n')
    return index


def refresh_pack(pack):
    """Re-export the shared content pack's interface and re-record its hashes in content.json."""
    pack = Path(pack).resolve()
    content_path = pack / 'content.json'
    content = json.loads(content_path.read_text())
    index = export(pack)
    for path in sorted((pack / 'interface').glob('*')):
        content['files'][path.relative_to(pack).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    content_path.write_text(json.dumps(content, indent=2) + '\n')
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
