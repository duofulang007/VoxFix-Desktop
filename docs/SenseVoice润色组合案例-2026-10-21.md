# SenseVoice 与本地润色组合案例核查

日期：2026-10-21。只核查公开仓库与作者资料；没有安装、运行或替换 VoxFix Desktop 的模型。

## 可核验组合

| 项目 | ASR 与后处理 | 本地/远程与量化 | 已公开数据 | 对本项目的意义 |
|---|---|---|---|---|
| [Lumen ASR](https://github.com/fakechris/lumen-asr) | 默认 `local_sensevoice`；本地 Ollama/LM Studio 纠错，当前默认 `qwen3.5:9b`，可配置其他模型 | ASR 与 LLM 都可以本机运行；也可选在线接口。README及本次读取配置没有给该组合的固定量化规格 | 未找到这个组合的完整润色延迟、中文 CER 或净纠错率表 | 最接近当前 Windows 桌面 SenseVoice→LLM→插入输入框的已实现案例，证明组合有人做，不证明2B/4B谁更快更准 |
| [sensevoice-vibe](https://github.com/Henderson11/sensevoice-vibe) | SenseVoice-Small ONNX INT8 + 声纹 + DeepSeek-V3.2后处理 | Linux本机CPU ASR；LLM默认内网GPU API，并有公网DeepSeek fallback，不是同一台电脑的本地小模型 | 作者报i7-13700H单核CPU：5秒音频ASR约500ms、声纹约200ms、内网LLM约1000–2000ms、IBus约50ms；含1.5秒端点等待的尾延迟约3.5秒。未给中文CER前后配对表 | 可说明后处理会显著增加尾延迟；不能把其数值套成5060Ti本地2B/4B成绩 |
| [local-wisprflow-windows](https://github.com/darian-gajgic/local-wisprflow-windows) | faster-whisper large-v3 → Ollama `gemma3:4b` → Win32 SendInput | Windows全本地；README未指定量化，列清理模型磁盘约3.3GB | 未找到可比的润色阶段测速、中文纠错率或完整VRAM表 | 非千问本地润色的真实Windows实现；ASR是Whisper、LLM是Gemma3，不是SenseVoice＋Gemma4 E2B的性能证据 |

## 源码核对

### Lumen ASR

- 当前 [config.rs](https://github.com/fakechris/lumen-asr/blob/main/apps/desktop/src-tauri/src/config.rs) 第313行：ASR默认 `local_sensevoice`；第790–792行：纠错provider默认 `ollama`，URL为 `http://127.0.0.1:11434/v1`，model默认 `qwen3.5:9b`，允许环境变量覆盖。
- [openai_compat.rs](https://github.com/fakechris/lumen-asr/blob/main/crates/lumen-corrector/src/openai_compat.rs) 有本地Ollama检测，并明确为速度关闭思考；短请求上下文下限4096，上限32768。它不是只在README里列了兼容模型而没有实现调用。
- 当前README写有Windows 10/11 x64与Windows安装包；较旧ARCHITECTURE.md仍把Windows写作future，存在文档漂移。因此平台功能以当前README和对应实现为准，不能从旧架构文档断言Windows不可用。未在Windows安装验收。

### sensevoice-vibe

- [配置模板](https://github.com/Henderson11/sensevoice-vibe/blob/master/config/llm.env.example) 明确本地模型路径、ONNX/FP32后端、`DeepSeek-V3.2`、内网服务地址占位、公网fallback、1800ms LLM超时与72输出token限制。
- README性能表为作者报告，未独立复跑。短句、输出预算、服务端硬件及网络均会影响LLM延迟。

### local-wisprflow-windows

- README给出完整Whisper→Gemma3:4b→SendInput链路。
- [wf_ollama.py](https://github.com/darian-gajgic/local-wisprflow-windows/blob/main/wf_ollama.py) 实现本机11434连接、按需启动、`keep_alive`、卸载与读取驻留显存。其代码注释还记录了某台机器将KV cache设置为q4_0会破坏小模型输出，Windows隔离模式显式用f16；这是项目作者观察，不是所有小模型的一般定律。

## 能得出的结论

1. SenseVoice搭配本地LLM做桌面听写整理已有真实代码案例；非千问的Gemma本地整理也已有Windows实现。
2. 本次未找到同时覆盖SenseVoice＋Qwen3.5-2B/4B、Gemma4 E2B、Phi4-mini，并在相同硬件、同一中文听写集上报告完整延迟与净纠错率的公开横评。
3. 项目存在和模型兼容不等于该组合已经有可用的性能排名。网络数据可用于缩小候选，但最终自动采用仍需对原始和润色结果计算修对、改坏、数字否定保护与冷/热延迟。

未做这些外部项目的安装、第三方输入框或真人准确率验收；没有修改 VoxFix 产品代码。
