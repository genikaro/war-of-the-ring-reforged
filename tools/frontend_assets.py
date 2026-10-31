"""Front-end assets (build-147): what the main menu, the setup screens and the loading screen draw.

Everything comes from the owner's install, as the rest of the caches do:

- the original menu art cut from its UVA rectangles (`interface/`): the main menu's logo, torn-parchment buttons and
  gold bullets (Screens/Menu_Main.lsr), the loading screen's parchment and its two rings, the plain one filling into
  the glowing one (Menu_Loading_Screen.lsr), and the stone game-creation frame of the skirmish screen
  (MENU_SKIRMISH.lsr) with the long gold box and arrow buttons of its drop-downs;
- the Immortal menu font (`fonts/`): Fonts/Immortal.lfn holds four sizes (12, 16, 20 and 24 px) of white glyphs on
  alpha. Each size is a header (size, 0, space width, letter spacing, page width, page height, layers), BGRA pixels
  for every layer, then 160 glyphs of (code, advance, u0, v0, u1, v1); a code above 0xFFFF names its layer in the
  high word. Each size is written as one PNG with its layers stacked, plus the glyph rectangles in pixels;
- the main menu's 3D background (`menu/`): Levels/MenuScene.wor, Barad-dur behind an orc army under volcanoes,
  exported like any map, and a sky panorama made from the Mordor skybox (SKYBOX_MORDOR_01) the scene's terrain set
  belongs to.

python tools/frontend_assets.py            (writes converted/frontend)
"""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

import numpy as np
from PIL import Image

import wor2gltf
from prototype_interface import crop_rect
from resolve_model import Resolver
from texture_animation import parse_uva

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT = ROOT / 'converted/frontend'
MENU_UVAS = ROOT / 'extracted/UVAs/Interface/Menu'
ART = [
    # Main menu (Menu_Main.lsr): logo, the five parchment buttons' art and the bullets beside the hovered one.
    'lotr_wotr_logo', 'parchment_button_01', 'parchment_button_02', 'parchment_button_03', 'parchment_button_04',
    'menu_bullet_left', 'menu_bullet_right',
    # Loading screen (Menu_Loading_Screen.lsr): parchment, plain ring (bar background), glowing ring (bar fill).
    'menu_loading_screen_background', 'menu_loading_screen_bar_background', 'menu_loading_screen_bar_fill',
    # Skirmish screen frame (MENU_SKIRMISH.lsr) and its controls.
    'Game_Creation_BG', 'Game_Creation_BG_Top_Mask', 'Game_Creation_BG_Bottom_Mask',
    'control_yellowbox_regular', 'control_yellowbox_highlighted', 'control_yellowbox_pressed',
    'control_yellowbox_long', 'control_yellowbox_long_highlighted', 'control_yellowbox_long_pressed',
    'control_arrowbutton_normal', 'control_arrowbutton_highlighted', 'control_arrowbutton_pressed',
    'control_black_panel', 'control_brownbox', 'control_brownbox_highlighted',
    'control_checkbox_checked', 'control_checkbox_empty', 'control_radiobutton_check1', 'control_radiobutton_empty',
    'control_golden_panel_thin', 'control_golden_panel_thick', 'control_golden_panel_thin_vertical',
    'control_scrollbar_backing_vertical', 'control_scrollbar_up', 'control_scrollbar_down',
    'control_scrollbar_highlighted_up', 'control_scrollbar_highlighted_down', 'control_sliderball_regular',
    'control_slider_end_cap_left', 'control_slider_end_cap_right',
    # The Exit question (Menu_Quit.lsr reuses the in-game acknowledgement box) and the menu logo fade.
    'menu_logo_fade_screen',
]
QUIT_BOX = ROOT / 'extracted/UVAs/Interface/Console/Dialogs/Quit Acknowledgement/Good/console_dialog_quit_acknowledgement_g_box.uva'
MENU_SCENE = ROOT / 'extracted/Levels/MenuScene.wor'
FONT = ROOT / 'extracted/Fonts/Immortal.lfn'
SKYBOX = 'SKYBOX_MORDOR_01'


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def export_art(target, resolver):
    target.mkdir(parents=True, exist_ok=True)
    assets = {}
    for path in [MENU_UVAS / (name + '.uva') for name in ART] + [QUIT_BOX]:
        raw = path.read_bytes(); uva = parse_uva(raw)
        texture = resolver.texture(uva['texture'])
        with Image.open(ROOT / texture['file']) as atlas:
            box = crop_rect(uva['rect'], atlas.size)
            cropped = atlas.crop(box).convert('RGBA')
        name = path.stem.lower(); cropped.save(target / (name + '.png'))
        assets[name] = {'file': 'interface/' + name + '.png', 'size': list(cropped.size),
                        'uva': path.relative_to(ROOT).as_posix(), 'texture': texture['file'], 'crop': list(box)}
    return assets


def parse_font(data):
    """LIQDFONT: magic, float 1.0, glyph count, size count, then per size a 7-int header, pixels and glyphs."""
    if data[:8] != b'LIQDFONT' or struct.unpack_from('<f', data, 8)[0] != 1:
        raise ValueError('unsupported font')
    glyph_count, size_count = struct.unpack_from('<2I', data, 12)
    offset, sizes = 20, []
    for _ in range(size_count):
        size, _zero, space, spacing, width, height, layers = struct.unpack_from('<7I', data, offset); offset += 28
        if not (0 < width <= 2048 and 0 < height <= 2048 and 0 < layers <= 8 and 0 < size <= 128):
            raise ValueError('implausible font page')
        pixels = data[offset:offset + width * height * 4 * layers]; offset += len(pixels)
        image = Image.frombytes('RGBA', (width, height * layers), pixels, 'raw', 'BGRA')
        glyphs = []
        for _ in range(glyph_count):
            code, advance, u0, v0, u1, v1 = struct.unpack_from('<2I4f', data, offset); offset += 24
            layer, char = code >> 16, code & 0xFFFF
            if layer >= layers or not (0 <= u0 <= u1 <= 1 and 0 <= v0 <= v1 <= 1):
                raise ValueError('glyph outside its page')
            glyphs.append({'char': char, 'advance': advance, 'x': round(u0 * width), 'y': layer * height + round(v0 * height),
                           'width': round((u1 - u0) * width), 'height': round((v1 - v0) * height)})
        sizes.append({'size': size, 'space': space, 'spacing': spacing, 'image': image, 'glyphs': glyphs})
    if offset != len(data):
        raise ValueError('font has trailing bytes')
    return sizes


def export_font(target):
    target.mkdir(parents=True, exist_ok=True)
    sizes = []
    for page in parse_font(FONT.read_bytes()):
        name = 'immortal_%d.png' % page['size']; page['image'].save(target / name)
        # Every glyph cell is one line tall; the baseline is the row under the ink of 'H'.
        h = next(g for g in page['glyphs'] if g['char'] == ord('H'))
        alpha = np.asarray(page['image'].getchannel('A'))[h['y']:h['y'] + h['height'], h['x']:h['x'] + h['width']]
        baseline = int(np.nonzero((alpha > 96).any(axis=1))[0].max()) + 1
        sizes.append({'size': page['size'], 'space': page['space'], 'spacing': page['spacing'], 'baseline': baseline,
                      'file': 'fonts/' + name, 'glyphs': page['glyphs']})
    (target / 'immortal.json').write_text(json.dumps({'source': FONT.relative_to(ROOT).as_posix(), 'source_sha256': sha256(FONT),
                                                      'sizes': sizes}, indent=1) + '\n')
    return {'file': 'fonts/immortal.json', 'sizes': [s['size'] for s in sizes]}


def bilinear(image, u, v):
    """Samples an HxWx3 array at normalized (u, v), clamped at the edges."""
    h, w = image.shape[:2]
    x, y = np.clip(u * w - .5, 0, w - 1), np.clip(v * h - .5, 0, h - 1)
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    x1, y1 = np.minimum(x0 + 1, w - 1), np.minimum(y0 + 1, h - 1)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    return (image[y0, x0] * (1 - fx) + image[y0, x1] * fx) * (1 - fy) + (image[y1, x0] * (1 - fx) + image[y1, x1] * fx) * fy


def sky_panorama(side, top, width=2048, height=1024):
    """An equirectangular sky from a skybox's four (identical) sides and its top; below the horizon repeats the sides."""
    side, top = np.asarray(side.convert('RGB'), np.float32), np.asarray(top.convert('RGB'), np.float32)
    lon = (np.arange(width) + .5) / width * 2 * math.pi
    lat = math.pi / 2 - (np.arange(height) + .5) / height * math.pi
    lon, lat = np.meshgrid(lon, lat)
    x, y, z = np.cos(lat) * np.sin(lon), np.sin(lat), np.cos(lat) * np.cos(lon)
    out = np.zeros((height, width, 3), np.float32)
    # The top face where y dominates and points up; a side face elsewhere (the sides are one texture).
    up = (y > 0) & (y >= np.maximum(np.abs(x), np.abs(z)))
    out[up] = bilinear(top, (x[up] / y[up] + 1) / 2, (z[up] / y[up] + 1) / 2)
    rest = ~up
    major = np.where(np.abs(x[rest]) > np.abs(z[rest]), np.abs(x[rest]), np.abs(z[rest]))
    across = np.where(np.abs(x[rest]) > np.abs(z[rest]), -z[rest] * np.sign(x[rest]), x[rest] * np.sign(z[rest]))
    out[rest] = bilinear(side, (across / major + 1) / 2, np.clip((1 - y[rest] / major) / 2, 0, 1))
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGB')


def export_menu_scene(target, resolver):
    summary = wor2gltf.export(MENU_SCENE, target, resolver)
    box = resolver.db['Skyboxes'].get(SKYBOX)
    side = resolver.texture(box.values['FrontTexture']); top = resolver.texture(box.values['TopTexture'])
    with Image.open(ROOT / side['file']) as s, Image.open(ROOT / top['file']) as t:
        sky_panorama(s, t).save(target / 'sky.png')
    # MenuScene keeps one camera waypoint (CamWayPt 6): source position x, y, height and two angles in degrees.
    scene = json.loads((target / 'scene.json').read_text(encoding='utf-8'))
    waypoints = [[struct.unpack('<f', struct.pack('<I', w & 0xFFFFFFFF))[0] for w in record[4:10]]
                 for record in scene['tail']['camera_waypoints']]
    return {'file': 'menu/scene.glb', 'sky': 'menu/sky.png', 'skybox': SKYBOX, 'sky_sources': [side['file'], top['file']],
            'source': MENU_SCENE.relative_to(ROOT).as_posix(), 'source_sha256': sha256(MENU_SCENE),
            'camera_waypoints': waypoints, 'placed_objects': summary['placed_objects']}


def export(output=DEFAULT_OUT, resolver=None):
    output = Path(output).resolve()
    if any(output.is_relative_to(ROOT / p) for p in ['extracted', 'TLOTR - War of the Ring']):
        raise ValueError('front-end assets must be outside original and extracted data')
    resolver = resolver or Resolver()
    index = {'format_version': 1, 'assets': export_art(output / 'interface', resolver), 'font': export_font(output / 'fonts'),
             'menu_scene': export_menu_scene(output / 'menu', resolver)}
    index['files'] = {p.relative_to(output).as_posix(): sha256(p) for p in sorted(output.rglob('*'))
                      if p.is_file() and p.name != 'index.json' and p.parent.name != 'models'}
    (output / 'index.json').write_text(json.dumps(index, indent=2) + '\n')
    return index


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('--out', type=Path, default=DEFAULT_OUT)
    args = parser.parse_args()
    result = export(args.out)
    print('Front-end assets: %d images, font sizes %s, menu scene %d objects -> %s' % (
        len(result['assets']), result['font']['sizes'], result['menu_scene']['placed_objects'], args.out))
