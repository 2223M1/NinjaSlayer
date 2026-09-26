# 奈落事件动态海报

第 6 集高清脸与内圈火焰，逐帧匹配第 2 集同相位的完整外围火焰。
两段同一动画的前 200 帧一一对应，平均火焰相关度 0.99192。
排除第 2 集也触顶的帧后，采用连续索引 81–129，共 49 帧，
`24000/1001 fps`、2.043708 秒；不倒放、不补帧、不变速、不生成过渡帧。

这是黑底不透明视频，不宣称从单一黑底推导出了火焰的真实透明度。
没有亮度硬抠或内部调色。第 6 集画框内侧 24 px 的烟雾接缝使用保存的
8 位 selection，第 6 集其余区域逐字节保持原 RGB。每种输出从源帧直接采样。

## 重建

使用本机 `C:/Users/theon/.codex/runtimes/source-pixel-cutout/.venv/Scripts/python.exe`：

```text
python tools/naraku-event/build_portrait.py prepare
python tools/naraku-event/build_portrait.py analyze
python tools/naraku-event/build_portrait.py compose
python tools/naraku-event/verify_portrait.py
```

原视频按脚本配置从本机视频目录读取。中间源帧、无损合成帧、每帧账本、
变换、selection、审核报告和 MP4 预览位于 `build/naraku-event/`。
发行素材为 `NinjaSlayer/videos/naraku_event.ogv`，来源契约为同目录
`naraku_event.source.json`，静态首帧海报位于 `NinjaSlayer/images/events/`。

## 接入与实机验证

保留原版事件布局，通过 `EventAssetProfile.VfxScenePath` 挂到 Portrait。
场景采用带 Mod ID 的原生 VFX 命名，兼容原生 GetAssetPaths 的预加载路径，
不增加全局补丁。原生 VfxOffset `(268,49)` 加上场景局部偏移后，
脸的 Portrait 坐标中心为 `(880,615)`；全循环采用同一比例和位置。
2026-09-24 先放大 20%、上移 46.8 屏幕像素，再按用户反馈在新版基础上放大 40%，
总比例为最初版本的 1.68 倍。随后按用户反馈左移 30 Portrait 像素（31.2 屏幕像素），大小和高度不变。
用户明确允许最左侧烟尾自然出屏；完整视频不裁切，脸部、右侧文字和其他方向仍检查边界。
仅调整构图可运行 `python tools/naraku-event/build_portrait.py layout`，
同步更新场景、静态海报和构图记录，不改动视频编码或源帧。

先编译当前安装主机对应的 Debug 版，再通过 Godot 导出 PCK 到
`build/naraku-event/runtime/NinjaSlayer.pck`。编译
`tools/smoke-harness/NinjaSlayer.NarakuEventProbe/NarakuEventProbe.csproj` 后，
运行 `pwsh -File tools/naraku-event/preview.ps1`。

测试使用隔离游戏副本与 APPDATA，禁用遥测，不修改实际安装目录或真实存档。
检查十次循环、静音、鼠标穿透、三次 5/6/7 伤害、授予遗物、页面切换不重启、
退出释放和重新进入，同时保存实际游戏截图与录像。
素材保持待用户验收状态，不自动安装或交给其他任务作为已验收资源。
