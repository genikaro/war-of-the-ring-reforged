"""The content pack's combat rules and blood tables (combat/rules.json and combat/blood.json) as the Python import builds
them from an install, for tests/formats --python-combat to check the C# port against (build-201).

python tools/combat_fingerprint.py "C:/Games/War of the Ring" combat.json

Writes {"rules": ..., "blood": ...}: combat_rules.export and blood_effects.export for the content pack's roster, run on
a layout of the install's archives in a temporary folder (resolve_fingerprint.lay_out), so the result is this
install's whatever extracted/ holds. The files those exports write (sounds, sprite frames, projectile and effect
models) are written into the temporary folder and dropped. The rules' Sources name each traced file as the import
names it: an archive file by its extracted/ path and a database table relative to the repository when the install is
inside it, else by its file name. The install is only read.
"""
import argparse
import json
import tempfile
from pathlib import Path

import resolve_fingerprint
import wor

REPOSITORY = wor.ROOT
READ = ('Models', 'Skeletons', 'Cloths', 'UVAs', 'Meshes', 'Weights', 'Textures', 'Effects', 'Projectiles', 'SoundEffects')


def build(install):
    import blood_effects
    import combat_rules
    import healing_visual
    import lms2gltf
    import prepare_prototype
    with tempfile.TemporaryDirectory(prefix='reforged-combat-') as root:
        resolve_fingerprint.READ, resolve_fingerprint.LISTED = READ, ()
        resolve_fingerprint.lay_out(install, root)
        resolver = resolve_fingerprint.resolver_for(install, root)
        layout = Path(root)
        wor.ROOT = combat_rules.ROOT = healing_visual.ROOT = layout
        lms2gltf.ROOT = root
        output = layout / 'pack'
        output.mkdir()
        roster = prepare_prototype.SKIRMISH_ROSTER
        rules = combat_rules.export(output, resolver, roster=roster)
        blood = blood_effects.export(output, resolver, roster)
        rules['Sources'] = import_names(rules['Sources'], resolver)
    return {'rules': rules, 'blood': blood}


def import_names(sources, resolver):
    """Sources traced in a layout, with each database table named as the import names it: relative to the repository
    when the install is inside it, else by its file name (the layout's tables come out by file name)."""
    tables = {Path(path).name: Path(path).resolve() for path in resolver.db.files.values()}
    result = {}
    for key, digest in sources.items():
        table = tables.get(key) if '/' not in key else None
        if table is not None:
            key = table.relative_to(REPOSITORY).as_posix() if table.is_relative_to(REPOSITORY) else table.name
        result[key] = digest
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    args = parser.parse_args()
    Path(args.out).write_text(json.dumps(build(args.install), ensure_ascii=False, indent=1) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
