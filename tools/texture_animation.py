"""UVA atlas/UV descriptors and LTA texture sequences. See docs/NOTES-presentation-assets.md."""
import math

from asset_io import Reader


def read_uva(r):
    version = r.header(b'LIQDUVAD', (1, 2, 3))
    texture, frames, per_row, frame_ms, parameter = r.read('i4I')
    rect = r.read('4f')
    # Reader 0x655e71 copies the remaining eleven words unchanged. Preserve
    # their bits: some are enums, rather than reinterpreting everything as floats.
    parameters = r.read('11I')
    flags = list(r.take(4)) if version >= 2 else [1, 0, 0, 0]
    if not frames or not per_row:
        raise ValueError('empty UVA grid')
    return {'version': version, 'texture': texture, 'frame_count': frames, 'frames_per_row': per_row,
            'frame_ms': frame_ms, 'parameter': parameter, 'rect': rect,
            'start_mode': parameters[4], 'playback_mode': parameters[6],
            'tiling_mode': parameters[8], 'primitive_mode': parameters[10],
            'parameters': parameters, 'flags': flags[:2]}


def parse_uva(data):
    r = Reader(data)
    if data[:8] == b'LIQDUVAD':
        result = read_uva(r)
    else:
        # Three editor-era descriptors predate LIQDUVAD and refer to a texture
        # filename. The fixed record is 52 bytes, followed by an LQR string.
        r.expect(3)
        frames, per_row, frame_ms, mode = r.read('4I')
        parameters = r.read('2I')
        values = r.read('6f')
        result = {'version': 'legacy-3', 'texture': None, 'frame_count': frames,
                  'frames_per_row': per_row, 'frame_ms': frame_ms, 'parameter': mode,
                  'rect': values[1:5], 'flags': [], 'parameters': parameters,
                  'legacy_values': values, 'texture_path': r.string(), 'tail': r.read('I')}
        if not frames or not per_row:
            raise ValueError('empty legacy UVA grid')
    r.finish()
    return result


def parse_lta(data):
    r = Reader(data)
    version = r.header(b'LIQDTANM', (1,))
    count = r.count()
    frame_ms, mode = r.read('II')
    if not frame_ms or not count:
        raise ValueError('empty or zero-duration texture animation')
    textures = [r.read('i') for _ in range(count)]
    r.finish()
    return {'version': version, 'frame_ms': frame_ms, 'mode': mode, 'textures': textures}


def sequence_texture(sequence, seconds):
    """Pure looping lookup, in seconds. Seeking backwards has no accumulated state."""
    if not math.isfinite(seconds):
        raise ValueError('time must be finite')
    frame = max(0, math.floor(seconds * 1000 / sequence['frame_ms']))
    return sequence['textures'][frame % len(sequence['textures'])]
