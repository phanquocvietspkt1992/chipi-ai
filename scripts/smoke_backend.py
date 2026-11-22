"""Run against a running Chipi demo server."""
import base64
import io
import json
import urllib.error
import urllib.request
import wave
from pathlib import Path

root = Path(__file__).resolve().parent.parent
sample = (root / 'backend/src/main/resources/demo.wav').read_bytes()
url = 'http://127.0.0.1:8080'
with urllib.request.urlopen(url + '/health', timeout=5) as response:
    assert json.load(response) == {'status': 'ok', 'mode': 'demo'}

def post(audio):
    boundary = 'chipi-smoke-boundary'
    header = (f'--{boundary}\r\nContent-Disposition: form-data; name="audio"; filename="sample.wav"\r\n'
              'Content-Type: audio/wav\r\n\r\n').encode()
    request = urllib.request.Request(url + '/api/turn',
        data=header + audio + f'\r\n--{boundary}--\r\n'.encode(),
        headers={'Content-Type': f'multipart/form-data; boundary={boundary}'})
    return urllib.request.urlopen(request, timeout=5)

with post(sample) as response:
    turn = json.load(response)
    assert turn['mode'] == 'demo' and turn['transcript'] and turn['reply']
    with wave.open(io.BytesIO(base64.b64decode(turn['audioBase64']))) as audio:
        assert audio.getnframes() > audio.getframerate()
        assert any(audio.readframes(audio.getnframes()))

try:
    post(b'invalid')
except urllib.error.HTTPError as error:
    assert error.code == 400, error
else:
    raise AssertionError('Malformed WAV should receive HTTP 400')
print('PASS: health, multipart voice turn, nonempty PCM WAV, malformed input')
