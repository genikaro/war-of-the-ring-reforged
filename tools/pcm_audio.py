"""Decode original mono/stereo Microsoft ADPCM effects and speech WAVs to 16-bit PCM.

Only the source format used by this slice is accepted. Original bytes remain
untouched. Coefficients and sample count come from the RIFF header.
"""
import io
import struct
import wave

ADAPTATION = (230,230,230,230,307,409,512,614,768,614,512,409,307,230,230,230)


def decode_ms_adpcm(data):
    if len(data) < 12 or data[:4] != b'RIFF' or data[8:12] != b'WAVE':
        raise ValueError('not a RIFF WAVE')
    end = struct.unpack_from('<I',data,4)[0]+8
    if end != len(data):
        raise ValueError('invalid RIFF length')
    chunks = {}; offset = 12
    while offset < end:
        if offset+8 > end:
            raise ValueError('truncated WAV chunk')
        name, size = struct.unpack_from('<4sI',data,offset); offset += 8
        if offset+size+(size&1) > end or name in chunks:
            raise ValueError('invalid WAV chunk')
        chunks[name] = data[offset:offset+size]; offset += size+(size&1)
    fmt, payload, fact = (chunks.get(key,b'') for key in (b'fmt ',b'data',b'fact'))
    if len(fmt) < 22 or len(fact) != 4:
        raise ValueError('missing ADPCM format/sample count')
    tag, channels, rate, _, block, bits, extra, samples, count = struct.unpack_from('<HHIIHHHHH',fmt)
    if tag != 2 or channels not in (1, 2) or bits != 4 or not 8000 <= rate <= 192000:
        raise ValueError('unsupported WAV format; expected mono/stereo Microsoft ADPCM')
    header = 7*channels
    if block < header or samples != 2+(block-header)*2//channels or len(fmt) != 18+extra or len(fmt) != 22+count*4 or not count:
        raise ValueError('invalid ADPCM format')
    remainder = len(payload)%block
    if not payload or 0 < remainder < header:
        raise ValueError('incomplete ADPCM block')
    coefficients = [struct.unpack_from('<hh',fmt,22+i*4) for i in range(count)]
    total = struct.unpack('<I',fact)[0]
    available = len(payload)//block*samples + (2+(remainder-header)*2//channels if remainder else 0)
    # Shipped speech often ends in a short block; odd-length source recordings
    # can declare one final sample whose nibble the original encoder omitted.
    if remainder and total == available+1:
        total = available
    if not 0 < total <= available:
        raise ValueError('invalid ADPCM sample count')
    output = bytearray()
    for start in range(0,len(payload),block):
        predictors = payload[start:start+channels]
        deltas = list(struct.unpack_from('<'+'h'*channels,payload,start+channels))
        recent = list(struct.unpack_from('<'+'h'*channels,payload,start+3*channels))
        older = list(struct.unpack_from('<'+'h'*channels,payload,start+5*channels))
        if any(p >= count for p in predictors) or any(d < 16 for d in deltas):
            raise ValueError('invalid ADPCM block header')
        output.extend(struct.pack('<'+'h'*(channels*2),*older,*recent))
        for value in payload[start+header:min(start+block,len(payload))]:
            for channel,nibble in enumerate((value>>4,value&15)):
                channel = channel if channels == 2 else 0
                first, second = coefficients[predictors[channel]]
                value = recent[channel]*first+older[channel]*second
                # Signed integer division truncates toward zero, including
                # negative predictions (Python // would round down).
                predicted = (abs(value)//256)*(1 if value >= 0 else -1)
                sample = max(-32768,min(32767,predicted+(nibble if nibble < 8 else nibble-16)*deltas[channel]))
                older[channel], recent[channel] = recent[channel], sample
                deltas[channel] = max(16,ADAPTATION[nibble]*deltas[channel]//256)
                output.extend(struct.pack('<h',sample))
    result = io.BytesIO()
    with wave.open(result,'wb') as wav:
        wav.setnchannels(channels); wav.setsampwidth(2); wav.setframerate(rate)
        wav.writeframes(output[:total*channels*2])
    return result.getvalue()
