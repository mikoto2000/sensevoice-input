# Log-Mel numerical reference

`whisper-mel-reference.json` contains 63 output values from Hugging Face Transformers **v4.46.3** `audio_utils.py` (Apache-2.0):
https://github.com/huggingface/transformers/blob/v4.46.3/src/transformers/audio_utils.py

Generate with `scripts/create-mel-fixture.py` from the repository root after placing the upstream file at `artifacts/reference_audio_utils.py`. NumPy is required only for this optional fixture-generation tool. The Windows app and all C# tests never launch Python.

Waveform: 16,000 samples `((i*37)%2001-1000)/2000` as float32, first sample replaced by 0.9; zero pad to 480,000 samples. Periodic Hann 400, hop160, centered reflect padding, power2, Slaney128 filters with area normalization, log10, discard last frame, clamp at max-8, `(x+4)/4`. Test tolerance: absolute 2e-5. Includes first/last mel bands, reflection boundary, sound/silence boundary and far-right padding. No speech or model weights are stored here.
