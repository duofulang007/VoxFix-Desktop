# sensevoice 权重来源

## 当前部署（2026-10-23，VoxFix Desktop 1.6.46.0）

生产权重为 R5 整轮微调 INT8，CPU 本地推理：

- `model.int8.onnx`：239,234,182 字节；SHA-256 `a002044fef99b50be903b059c39b012072bc3f131df0ad58e59d548cfde7535d`。
- 检查点与导出来源、验证边界见 [R5 模型启用](../../docs/R5模型启用-2026-10-23.md)。6257 条封存比较为 PyTorch 口径，不是完整 INT8 独立集重测。
- R4 回滚：`work/r5-deploy-20261023/rollback/model.int8.onnx.r4`，SHA-256 `3ff553570631f032153331b1474dd8a8016eb0ed13bf380ef99ba9eb38386e8f`。
- 当前资产清单：[assets.json](../../work/sensevoice-20261021/assets.json)。模型、训练语料和私有样本不入库。
- 上游 Apache-2.0 许可不等于微调数据的许可；训练与本机使用条件见启用记录，不分发训练数据或微调权重。

## 历史部署：R2（1.6.29.0，已被替换）

当时 `model.int8.onnx` 换成**多源真人语料微调后的权重**，不是上游原始导出：

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

本桌面仓库使用 `language=auto`、关闭 ITN；无 Android APK 或粤语二遍采用链路。
