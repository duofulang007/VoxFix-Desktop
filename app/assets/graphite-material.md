# 参考图曲面材质

用于 1.6.21.0；日期：2026-10-21。

`graphite-material.png` 为 2168×725 的原始材质图，用内置 imagegen 按用户给定截图去除白色麦克风与声纹，保留石墨曲面、反射及背景映射。生成器对局部细节有重建，因此不能把材质称为逐像素相同的原图。两块取样坐标在 `LoadMaterialTextures` 中，均来自同一张图；`DrawMaterial` 随外形长宽比连续混合两端材质，保留可打断形变、整体按压与动态状态覆盖。波环、白色图标、状态文字及进度线由程序独立实时绘制，不烘焙进材质。构建将图片直接嵌入 EXE，无临时路径依赖。

内置 imagegen 提示词：

```text
Precise object edit of this exact image, for a software UI material texture atlas. Change ONLY the white microphone symbols (both) and the white waveform bars: REMOVE these white symbols entirely and inpaint their tiny areas with the continuous underlying dark graphite glass material. Preserve the EXACT existing two widget silhouettes, their positions, their relative sizes, their curved three-dimensional pillow shading, glossy thin edge highlights, optical thickness, ALL local blue gray and warm mauve reflections, soft shadows, and surrounding blue lavender peach wallpaper. The left circle and the right horizontal pill must remain, both now empty with absolutely no icons or symbols. Critical: do not redesign or flatten the material, do not brighten it, do not replace it with generic gradient glass, do not change geometry, do not add decorations. Preserve the original screenshot material as faithfully as possible. This is removing only the small white markings, NOT removing the actual gray UI surfaces. Keep the same very wide horizontal composition and tightly cropped framing, no margins. Empty circle and empty capsule on the exact original background. No white marks, no text, no waveforms, no microphones, no logos.
```
