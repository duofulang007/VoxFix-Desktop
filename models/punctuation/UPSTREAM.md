# CT-Transformer punctuation

Reused without modification from existing VoxFix assets. INT8 ONNX, 75519198 bytes.

- Source model: https://modelscope.cn/models/iic/punc_ct-transformer_zh-cn-common-vocab272727-pytorch/summary
- Export: https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2
- License: Apache-2.0; accompanying LICENSE retained from existing assets.
- Exact local SHA-256 recorded in `work/sensevoice-20261021/assets.json`.

This model adds punctuation only. The application rejects changes to content,
ASCII word boundaries, decimal numbers, names or negation.
