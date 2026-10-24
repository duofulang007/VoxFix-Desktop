"""Small scoring/provenance boundary checks; no model or downloaded data."""
import tempfile
import json
import io
from pathlib import Path
import wave
from unittest.mock import patch

import numpy as np

from evaluate_sensevoice import main, sha256, summarize
from p0_score import exact
from prepare_ascend_test import pcm16, stress, write_wav

rows = [{'category': 'mixed/original', 'exact': True, 'fullExact': False,
         'error': None, 'unitErrors': 0, 'refUnits': 5},
        {'category': 'mixed/original', 'exact': False, 'fullExact': False,
         'error': 'rejected', 'unitErrors': 3, 'refUnits': 3}]
summary = summarize(rows)['mixed/original']
assert summary == {'total': 2, 'exact': 1, 'fullExact': 0, 'errors': 1,
                   'unitErrors': 3, 'refUnits': 8, 'unitErrorRate': 3 / 8}
assert not exact('', '。')
with patch('sys.argv', ['evaluate_sensevoice.py']), patch('sys.stderr', new_callable=io.StringIO), \
     patch('evaluate_sensevoice.load_model') as model:
    try:
        main()
    except SystemExit as error:
        assert error.code == 2
    else:
        raise AssertionError('Evaluation requires an explicit corpus; never default to legacy174')
    model.assert_not_called()
audio = .1 * np.sin(np.arange(16000) * .1)
pcm = pcm16(audio)
assert stress(pcm, 'original', None, 0) == pcm
quiet = np.frombuffer(stress(pcm, 'quiet30db', None, 0), dtype='<i2')
assert .030 < np.linalg.norm(quiet) / np.linalg.norm(np.frombuffer(pcm, dtype='<i2')) < .033
noise = stress(pcm, 'noise15db', None, 123)
assert noise == stress(pcm, 'noise15db', None, 123)
assert noise != stress(pcm, 'noise15db', None, 124)
delta = np.frombuffer(noise, dtype='<i2').astype(float) / 32768 - np.frombuffer(pcm, dtype='<i2') / 32768
assert 14.8 < 20 * np.log10(np.linalg.norm(audio) / np.linalg.norm(delta)) < 15.2
with tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent.parent / 'work') as temporary:
    path = Path(temporary) / 'audio.wav'
    write_wav(path, pcm)
    with wave.open(str(path), 'rb') as wav:
        assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 16000)
        assert wav.readframes(wav.getnframes()) == pcm
    before = sha256(path)
    write_wav(path, noise)
    assert sha256(path) != before
    with patch('sys.argv', ['evaluate_sensevoice.py', '--manifest', 'unused.json', '--output', str(path)]):
        try:
            main()
        except FileExistsError:
            pass
        else:
            raise AssertionError('Existing results must never be reused or overwritten')
    assert sha256(path) != before
    manifest = Path(temporary) / 'manifest.json'
    output = Path(temporary) / 'result.json'
    manifest.write_text(json.dumps([{'id': 'tampered', 'category': 'test',
        'expected': 'hello', 'wav': str(path), 'pcmSha256': 'wrong'}]), encoding='utf-8')
    with patch('sys.argv', ['evaluate_sensevoice.py', '--manifest', str(manifest), '--output', str(output)]), \
         patch('evaluate_sensevoice.load_model', return_value=(object(), 'test')), \
         patch('evaluate_sensevoice.sha256', return_value='test'):
        try:
            main()
        except ValueError as error:
            assert 'checksum mismatch' in str(error)
        else:
            raise AssertionError('Changed audio must be rejected before inference')
    assert not output.exists(), 'A failed audio check cannot produce a completed report'
print('PASS: weighted scoring / silence / audio provenance / quiet / seeded SNR / WAV roundtrip')
