# General SenseVoiceSmall

Source model: https://modelscope.cn/models/iic/SenseVoiceSmall
ONNX conversion: https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17
(revision `2365baeacb507f821a0c8120fcee3d484dba7a07`)

- `model.int8.onnx` SHA-256: `c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51`
- `tokens.txt` SHA-256: `f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc`
- License: Apache-2.0 (see LICENSE).

Downloaded and verified by `tools/setup_voice.py`; the APK ships the build-time optimized graph produced by
`tools/prepare_streaming_models.py` (receipt stays local).

This replaces the 2025-09-09 ASLP-lab Cantonese fine-tune. The general model's language detection is
required by the Cantonese second-pass adoption policy; the 2025 fine-tune reported `<|yue|>` for every input.
