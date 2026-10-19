"""Decode the demo's mono Microsoft ADPCM impact WAVs to 16-bit PCM.

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
    if tag != 2 or channels != 1 or bits != 4 or not 8000 <= rate <= 192000:
        raise ValueError('unsupported WAV format; expected mono Microsoft ADPCM')
    if block < 7 or samples != 2+(block-7)*2 or len(fmt) != 18+extra or len(fmt) != 22+count*4 or not count:
        raise ValueError('invalid ADPCM format')
    if not payload or len(payload)%block:
        raise ValueError('incomplete ADPCM block')
    coefficients = [struct.unpack_from('<hh',fmt,22+i*4) for i in range(count)]
    total = struct.unpack('<I',fact)[0]
    if not 0 < total <= len(payload)//block*samples:
        raise ValueError('invalid ADPCM sample count')
    output = bytearray()
    for start in range(0,len(payload),block):
        predictor, delta, recent, older = struct.unpack_from('<Bhhh',payload,start)
        if predictor >= count or delta < 16:
            raise ValueError('invalid ADPCM block header')
        first, second = coefficients[predictor]
        output.extend(struct.pack('<hh',older,recent))
        for value in payload[start+7:start+block]:
            for nibble in (value>>4,value&15):
                value = recent*first+older*second
                # Signed integer division truncates toward zero, including
                # negative predictions (Python // would round down).
                predicted = (abs(value)//256)*(1 if value >= 0 else -1)
                sample = max(-32768,min(32767,predicted+(nibble if nibble < 8 else nibble-16)*delta))
                older, recent = recent, sample
                delta = max(16,ADAPTATION[nibble]*delta//256)
                output.extend(struct.pack('<h',sample))
    result = io.BytesIO()
    with wave.open(result,'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(rate)
        wav.writeframes(output[:total*2])
    return result.getvalue()
