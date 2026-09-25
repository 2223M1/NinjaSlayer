# 固定事件音乐

三首不受忍杀电台设置控制。来源通过 SHA-256 与用户的 `bgm评选` 文件逐字节匹配。

| 事件 | 用户标题 | 原曲 | FMOD 事件名 |
| --- | --- | --- | --- |
| YukanoEvent | 茶室 | Ash | yukano_teahouse |
| YamotoKokiCuteEvent | 小季事件 - Remix | ア・ガール・フロム・キョート・リパブリック ~ABE RYUDAI Remix ver.~ | yamoto_koki_remix |
| NarakuEvent | 忍魂 - 变奏 | Crystal part2 | naraku_crystal_variation |

路径前缀为 `event:/NinjaSlayerEventMusic/music/`。独立 `NinjaSlayerEventMusic.bank`
及 `EventMusicGUIDs.txt` 随资源包导出，不替换已有 NinjaSlayer Bank 或 GUIDs.txt。
三个 Event Master 均为 −7.2dB，子项 0dB，流式播放，路由到 Music Bus。

## 生命周期

- 由原版仅对本地玩家调用的 `AfterEventStarted` 开始；使用原版 run music controller 接管章节音乐。
- Intro 后自然循环；中间事件页面、开关地图不重启曲目。小季原有问候语音保留。
- `IsFinished` 后只发送一次局部 `event_end=1`，经当前出口播放完整 Outro；自然停止后恢复当前房间的章节音乐状态。
- 未等尾奏结束就离开时，在 `RoomExited` 中交还原版控制器，不把尾奏带入下一房间；节点退出也清理订阅。
- 返回菜单由 run/controller 生命周期停止音频；已完成事件的加载不重新播放 Intro。
- Bank 缺失时保留章节音乐并记录警告，不留下静音或同时叠放两套音乐。

## 制作与验证入口

独立音频制作工作区的 `tools/ninja_slayer_radio/fixed_events.py` 构建独立副本，
`verify_fixed_events.py` 在真实 FMOD WAVWRITER 中检查退出、回绕及完整尾奏。
结果和原始录音位于 `outputs/fixed_event_music/`。不会更新电台全库的验证结果或解除其技术阻塞。
发布前检查原工程 metadata 与原有 Bank 哈希不变，只新增三个事件及其素材。

游戏实机探针为 `tools/smoke-harness/NinjaSlayer.FixedEventMusicProbe`，
使用隔离安装副本和存档，检查不叠播、选择后尾奏、章节音乐恢复、提前离开、重进、返回菜单。
音乐自然度仍可继续通过试听修改；技术检查不是用户听感批准。

## 2026-09-24 制作阶段验证（整合前历史证据）

- stable / preview Release 构建均为 0 警告、0 错误；现有逻辑测试 358/358。
- 三首各 6 个实时场景通过：Intro 早退、中段、最后小节、末端前、跨循环末端、重复结束请求。
  完整尾奏逐段对照源波形；退出响应 0.436–1.940 秒，采集最高真峰 −6.0dBTP。
- 独立游戏探针三首全部通过，不叠播、自然尾奏、章节恢复、提前退出、重进、菜单清理。
  证据：工作区 `outputs/fixed_event_music/live/qa-20260924-122210/result.json`。
- 原有 FMOD metadata 及所有旧 Bank 哈希不变；作者工程为 58 个事件。
  新增 Bank 28,104,928 字节，SHA-256
  `D0B9AC9685CC18B026C121477FA387B40BCA665715E25C2173C24DEE47BC7FF6`。
- 一次超过 100ms 调度门槛的录音保留，重录使用独立目录；未降低验证门槛。
- 只接入工作区及隔离测试包，未安装到 Steam 游戏目录，未发布创意工坊。

## 本分支整合验证

实机脚本 `tools/smoke-harness/NinjaSlayer.FixedEventMusicProbe/preview.ps1` 必须显式传入 `-Assembly` 与 `-Pack`，使用本次 stable 候选。整合后的三首单实例、完整尾奏、章节恢复、重进、提前退出和菜单清理结果位于 `build/fixed-event-music-live/qa-20260924-144247/result.json`；完整范围及宿主见 `Docs/validation/card-board-v115-20260924.md`。
