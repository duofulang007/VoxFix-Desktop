"""Rescore every stored hypothesis with corrected tokenization; do not run models."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import re

from p0_score import units, distance, exact

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "work"


def load(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def chinese(rows):
    return [r for r in rows if r["category"] != "cantonese" and re.search(r"[\u3400-\u9fff]", r["expected"])]


def metric(rows):
    count = sum(len(units(r["expected"])) for r in rows)
    errors = sum(distance(units(r["expected"]), units(r["text"])) for r in rows)
    return {"n": len(rows), "exact": sum(exact(r["expected"], r["text"]) for r in rows),
            "unitErrors": errors, "referenceUnits": count, "unitErrorRate": errors/max(1,count),
            "protocolErrors": sum(bool(r.get("error")) for r in rows),
            "nonPrefixFinal": sum(bool(r.get("nonPrefixFinal")) for r in rows),
            "sampleMismatch": sum(bool(r.get("sampleMismatch")) for r in rows)}


def main():
    initial = load(WORK/"chinese-stress-20261020/results.jsonl")
    tail = load(WORK/"chinese-tail-controls-20261020/results.jsonl")
    local = json.loads((WORK/"chinese-local-controls-20261020.json").read_text(encoding="utf-8"))["rows"]
    baseline = [r for r in initial if r["group"] == "baseline-stream"]
    improved = [r for r in tail if r["group"] == "finalize-pad320"]
    assert len(baseline) == len(improved) == 174
    before = {r["id"]:r for r in baseline}
    paired = []
    for r in improved:
        old = before[r["id"]]
        if exact(r["expected"],old["text"]) != exact(r["expected"],r["text"]):
            paired.append({"id":r["id"],"expected":r["expected"],"before":old["text"],"after":r["text"],"gain":exact(r["expected"],r["text"])})
    controls = json.loads((WORK/"chinese-stress-20261020/summary.json").read_text(encoding="utf-8"))["controls"]
    rows = initial + tail + local
    all_groups = {g:metric([r for r in rows if r["group"]==g]) for g in sorted(set(r["group"] for r in rows))}
    comparison = {g:metric(chinese([r for r in rows if r["group"]==g])) for g in ["baseline-stream","finalize-pad320","finalize-pad500","offline-control","zh-CN-control","zipformer","sensevoice"]}
    paced = [r for r in initial if r["group"]=="paced"]
    firsts = sorted(r["firstWallMs"] for r in paced if r["firstWallMs"] is not None)
    model=ROOT/"models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf"
    with model.open("rb") as handle: model_hash=hashlib.file_digest(handle,"sha256").hexdigest()
    summary = {"recognitionRuns":len(rows),"controlRuns":sum(controls[k] for k in ["cancel","empty","invalid"]),
               "baseAudio":174,"mandarinRows":len(chinese(baseline)),"distinctMandarinReferences":len(set(r["expected"] for r in chinese(baseline))),
               "modelSHA256":model_hash,"groups":all_groups,"mandarinComparison":comparison,"controls":controls,"pairedChanges":paired,
               "protocolErrors":sum(bool(r.get("error")) for r in rows),
               "nonPrefixFinal":sum(bool(r.get("nonPrefixFinal")) for r in rows),
               "sampleMismatch":sum(bool(r.get("sampleMismatch")) for r in rows),
               "paced": {"n":len(paced),"beforeStop":len(firsts),"firstWallMs":firsts}}
    (WORK/"chinese-stress-20261020/final-summary.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    lines=["# VoxFix Desktop 中文识别暴力测试", "", "日期：2026-10-20。测试使用本机 CPU、已有本地模型；没有下载新模型，没有上传或保存现场麦克风录音。", "",
           f"完成 **{len(rows)} 次识别 + {summary['controlRuns']} 次控制流程检查，共 {len(rows)+summary['controlRuns']} 项**。基于 174 条固定音频，其中普通话/中英混说 126 条，普通话金标只有 71 个不同文本；其余运行来自变体、重复会话与对照，不是两千多条独立真人录音。", "",
           "普通话和英文素材主要为 Microsoft 合成语音；公开粤语录音单列。结果能定位程序与模型问题，不能等同于用户真人口音准确率，也没有与 Win+H 做同音频对比。", "",
           "## 主要结论", "", "1. 停止补尾有可复现问题。送入的采样数完整，但旧版 finalize 会把“好的/备份/到账”截成“好/备/到”。通用追加 320ms 静音右上下文后恢复；320ms 与 500ms 的普通话整句全对数和总错误数相同，个别具体输出不同，采用较短者。补的是模型上下文，不是等待 320ms 或保存更多录音。",
           "2. 20ms、100ms、320ms 与不规则分块在同一 76 条核心语料上得到相同文本，不能靠换采集块大小解决同音词错误。",
           "3. 强制 zh-CN 没有提高普通话成绩，且严重损害纯英文输出，不采用。",
           "4. 停止补尾之外仍有明显中文同音词和中英术语错误，不能宣称当前模型中文最强，也不能靠修 UI 消除。", "",
           "## 同音频普通话/混说对照", "", "口径：忽略标点与大小写；汉字逐字、英文逐词、其他文字保留为错误单元。数字格式严格比较，未做金额/日期等语义等价折算。一句任一单元错误即不是整句全对。", "",
           "| 路径 | 整句全对 | 混合单元错误率 |", "|---|---:|---:|"]
    labels={"baseline-stream":"Nemotron 原流式","finalize-pad320":"Nemotron 流式 + 320ms 停止补尾","finalize-pad500":"Nemotron 流式 + 500ms 停止补尾","offline-control":"Nemotron 整段离线","zh-CN-control":"Nemotron 强制普通话","zipformer":"现有 Zipformer 流式对照","sensevoice":"现有 SenseVoice 离线对照"}
    for g,m in comparison.items(): lines.append(f"| {labels[g]} | {m['exact']}/{m['n']} | {m['unitErrorRate']:.2%} |")
    gains=sum(r["gain"] for r in paired);losses=len(paired)-gains
    lines += ["", f"全 174 条的补尾对照：{gains} 条从错变全对，{losses} 条从全对变错。以下逐条保留回退，不隐藏代价。", "",
              "| 样例 ID | 金标 | 修复前 | 补尾后 | 结果 |", "|---|---|---|---|---|"]
    for r in paired: lines.append("| "+" | ".join(str(r[k]).replace("|","/").replace("\n"," ") for k in ["id","expected","before","after"])+(" | 改善 |" if r["gain"] else " | 回退 |"))
    lines += ["", "## 分场景基线与补尾后", "", "| 场景 | 原流式全对 | 补尾后全对 |", "|---|---:|---:|"]
    categories={"baseline":"普通话简单句","everyday":"日常句","negation":"含否定词句","short":"短词","number":"数字/量词","mixed":"中英混说","quiet":"既有轻声变体","noise15":"既有 15dB 噪声变体","fast":"既有加速变体","pause600":"既有尾部补静音600ms","long":"长句/句间停顿","cold":"冷启动标签音频（本轮模型常驻）","sync":"既有重复音频","word":"纯英文单词","sentence":"纯英文整句","silence":"静音/噪声","cantonese":"公开粤语（独立）"}
    for cat,label in categories.items():
        a=metric([r for r in baseline if r["category"]==cat]);b=metric([r for r in improved if r["category"]==cat])
        lines.append(f"| {label} | {a['exact']}/{a['n']} | {b['exact']}/{b['n']} |")
    lines += ["", "## 压力扰动与稳定性", "", "| 扰动 | 整句全对 |", "|---|---:|"]
    for g in ["chunk20","chunk320","jitter","quiet","noise20","noise10","speed120","leading800","midpause600","clip250","non-speech","long-session"]:
        m=all_groups[g];lines.append(f"| {g} | {m['exact']}/{m['n']} |")
    lines += ["", "noise20/noise10 是固定种子的高斯噪声；speed120 为重采样加速（会改变音高）；midpause600 在音频中点插入静音，可能切断音素，是严苛扰动而非真人自然停顿。long-session 为 30/90/179.9 秒的重复句，测试持续解码与状态，不代表长篇内容理解。", "",
              f"控制检查：取消 {controls['cancel']} 次、空结束 {controls['empty']} 次、非法 PCM {controls['invalid']} 次；失败 {len(controls['failures'])} 次。整轮协议异常 {summary['protocolErrors']}，最终文本回退 {summary['nonPrefixFinal']}，输入样本计数不一致 {summary['sampleMismatch']}。", "",
              f"真实节奏延迟样本 {len(paced)} 条，停止前出字 {len(firsts)} 条。首字墙钟毫秒逐条值：{firsts}。其他批量运行是加速喂音，只用于准确率与吞吐，不能当实时延迟。", "",
              "## 仍存在的模型错误", "", "| 金标 | 当前补尾后输出 |", "|---|---|"]
    failures=[r for r in chinese(improved) if not exact(r["expected"],r["text"])]
    seen=set()
    for r in failures:
        if r["expected"] in seen:continue
        seen.add(r["expected"]);lines.append(f"| {r['expected']} | {r['text']} |")
    lines += ["", "## 产物与复现", "", "- `work/chinese-stress-20261020/results.jsonl`：初始 1227 条识别结果。", "- `work/chinese-tail-controls-20261020/results.jsonl`：600 条补尾/语言对照结果。", "- `work/chinese-local-controls-20261020.json`：348 条现有模型对照结果。", "- `work/chinese-stress-20261020/final-summary.json`：修正评分后的统一汇总。", "- `work/chinese-stress-20261020/VoxFix Desktop-recognize-before.py`：修复前工作器快照。", "",
              "复现修复前基线：`.venv/Scripts/python.exe tools/chinese_stress.py --baseline-snapshot`；复现补尾对照：`.venv/Scripts/python.exe tools/chinese_tail_controls.py --baseline-snapshot`。模型/控制协议回归：`.venv/Scripts/python.exe tools/test_microphone.py`；计分回归：`.venv/Scripts/python.exe tools/test_asr_scoring.py`。", "",
              "修复只补停止右上下文，没有逐词硬编码，没有改语言为强制普通话，没有启用大模型润色，也没有证明所有输入软件或用户真人场景已经通过验收。后续提升应优先引入真人保留集，按同音词、术语、噪声逐类对比新候选，而不是继续靠品牌推断中文能力。"]
    report=ROOT/"docs/VoxFix Desktop-中文识别暴力测试-2026-10-20.md"
    report.write_text("\n".join(lines)+"\n",encoding="utf-8")
    print(json.dumps({"report":str(report),"recognitionRuns":len(rows),"controls":summary["controlRuns"],"comparison":comparison,"gains":gains,"losses":losses},ensure_ascii=True))


if __name__=="__main__":main()
