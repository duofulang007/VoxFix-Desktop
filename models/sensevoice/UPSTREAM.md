# sensevoice 权重来源

## 当前部署（2026-10-22 起，VoxFix Desktop 1.6.29.0）

生产 `model.int8.onnx` 已换成**多源真人语料微调后的权重**，不是上游原始导出：

- SHA-256: `27377bac3e1f2e2588cbce401c8a5ab48991a4ad9da963357fc7a964ad3adaa6`（239,234,125 字节）
- 来源：`iic/SenseVoiceSmall` 原始 `model.pt` 用 ASCEND + AISHELL-1 + LibriSpeech 三源混合
  微调 1 轮（27.88 h / 19,412 条，lr 5e-5），再走与上游一致的 sherpa-onnx 导出与 QUInt8 量化。
- 记录：[docs/多源真人语料微调SenseVoice-2026-10-22.md](../../docs/多源真人语料微调SenseVoice-2026-10-22.md)
- 结构、元数据、`tokens.txt` 与上游一致（词表 SHA-256 `f449eb28…a1dc`），只是权重不同。
- **许可提示**：训练用到 AISHELL-1（其 ModelScope 上传页写"仅学术研究、禁止商用"），
  商用分发前需澄清，详见该记录第 10.2 节。
- 回滚：原上游权重备份在 `work/model.int8.onnx.20261018-original`
  （SHA-256 `c71f0ce0…cd51`），拷回本目录并重启即可。

## 上游原始文件（用于对照）

Source model: https://modelscope.cn/models/iic/SenseVoiceSmall
ONNX conversion: https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17
(revision `2365baeacb507f821a0c8120fcee3d484dba7a07`)

- 原始 `model.int8.onnx` SHA-256: `c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51`
- `tokens.txt` SHA-256: `f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc`
- License: Apache-2.0 (see LICENSE).

Downloaded and verified by `tools/setup_voice.py`; the APK ships the build-time optimized graph produced by
`tools/prepare_streaming_models.py` (receipt stays local).

This replaces the 2025-09-09 ASLP-lab Cantonese fine-tune. The general model's language detection is
required by the Cantonese second-pass adoption policy; the 2025 fine-tune reported `<|yue|>` for every input.
