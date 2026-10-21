# sherpa-onnx CPU runtime

Version 1.13.8, Windows x64, CPython 3.13. Copied from the previously installed
VoxFix evaluation environment; no package was installed or fetched at runtime.
The package includes its native ONNX Runtime DLL and does not require a separate
Python onnxruntime installation. NumPy remains supplied by this project's venv.

Upstream: https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.8

Original distribution metadata is retained in `sherpa_onnx_core-1.13.8.dist-info`.
Apache-2.0 LICENSE is included. This is the regular CPU package, separate from
the older experimental `sherpa-context-51aaac5` directory.
