from p0_score import units, distance, overall, exact

assert units("please send me the file") == ["please", "send", "me", "the", "file"]
assert units("打开 VoxFix Desktop，再用WorkBuddy") == ["打", "开", "voxfix", "desktop", "再", "用", "workbuddy"]
assert units("don't cancel") == ["don't", "cancel"]
assert units("Hello,world!") == ["hello", "world"]
assert len(units("हिंदी")) > 0, "Foreign hallucinations must not disappear from scoring"
assert not exact("", "。"), "Silence requires no output, not just no letters"
assert distance(units("please send the file"), units("please send file")) == 1
assert distance(units("不要删除文件"), units("要删除文件")) == 1
assert overall([{"expected": "hello world", "text": "hello"}], lambda r: r["text"])["unitErrorRate"] == 0.5
print("PASS: English word boundaries / Chinese-English tokens / punctuation boundaries / negation deletion")
