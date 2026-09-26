# 0.3.3 整合与发布验证（2026-09-26）

本记录覆盖九模组兼容、单人不歪种，以及用户追加指定的由加乃首次射箭弹出、阿拉巴马落和 Power／遗物图标。历史兼容记录保留其原始候选和执行范围；本记录不将历史候选结果冒充最终包验证。

## 来源与范围

- 基线为 `main@e0bb1c01fd7993381418f3231106fac767f8ead8`，包含已经发布的 0.3.2 及独立合并的网站 R2 更新。本轮没有另改网站业务。
- 九模组兼容与 CFC 来自 `codex/cfc-singleplayer-seeds` 的本地修改，细节见 [兼容矩阵](../compatibility-nine-mods.md) 和 [既有测试记录](compatibility-nine-mods-20260926.md)。
- 演出和图标来自 `card-board-v115-integration` 当前文件，以 0.3.2 发布时保存的同一工作区快照作三方合并基准。保留已发布的处决、偷牌返还、音画同步修正及本轮外部缩放修复；没有将旧工作区整文件覆盖到新基线。
- 包含 7 件遗物的 21 张图片和 26 张 Power 图片，共 47 张。由加乃包含视频、图集、裁切蒙版、shader 和帧数据。
- 原工作区新增的 `tools/ep26-naraku-popup/` 16 个源文件作为离线制作工具提交；不包含生成缓存、原始整集动画和私人路径中的素材。
- 半奈落火焰试作继续归档，不回接、不进入发布包。原有半奈落形态仍保留。
- 本次未改变普通模式卡牌规则、数量或奖励池，因此未修改卡牌规格和目录。

## 新候选验证

本地日志均位于 `build/release-v033/`。提交前候选 DLL 的 SourceRevision 为上述基线 SHA，**包含未提交整合修改**；精确二进制和源文件指纹见同目录证据及版本化证据 JSON。正式发布必须另从合并后的干净提交构建，不以基线 SHA 冒称最终提交。

| 项目 | 执行结果与证据 |
|---|---|
| 产品构建及资源包 | stable 0.107.1、preview 0.111.0 均通过；`package-stable-01.log`、`package-preview-01.log` |
| 自动桥接与通用包 | `bundle-01.log` 通过；预览变体自动编译并包含匹配版本和源码的 `NinjaSlayer.AutoAnthony.dll`，不包含第三方 DLL |
| 产品契约 | 双宿主通过；`stable-all-01.log`、`preview-all-01.log` |
| RitsuLib 契约 | 双宿主通过；`stable-ritsu-02.log`、`preview-ritsu-02.log` |
| 东尼契约 | `preview-anthony-01.log` 通过；84 个来源的基础／升级及序列化、生成池、原生设置、核心组件结算、重载 |
| 逻辑测试 | `logic-01.log`：360 通过、0 跳过 |
| 构建／发布边界 | `boundaries-01.log`、`artifacts-01.log`、`powershell-01.log`、`current-host-01.log`、`isolation-01.log` 通过 |
| 仓库一致性 | `repository-01.log`、`sync-01.log`、`git diff --check` 通过 |
| 图标和电影包内检查 | `resources-01.log`：47 张图标从 PCK 成功解码且尺寸等于源文件；电影、shader、帧数据逐字节校验一致 |
| 本机九模组特定交互 | `live-preview-focused-03/attestation.json` 通过；详见下文 |
| 本机完整战斗及重载 | `live-preview-all-01/attestation.json` 通过；含新局、重载、反向处决，所有阶段使用实际九模组组合 |
| SmokeDriver | stable 编译 `driver-stable-01.log`、preview 实机驱动编译通过 |

本机特定交互使用实际 preview 0.111.0、RitsuLib 0.6.2 和已记录的九个模组版本。由加乃在视频位置 1.0166667 秒释放箭弹，释放时目标未扣血，随后造成 15 点实际伤害；电影正常清理。Better Menu 快速读取保留已播标记，同种子和随机重开清除标记。阿拉巴马落期间外部 MaxHP 修改后的视觉缩放保持 2.1111112。另覆盖 CookieCursor 已有文件优先、友军不增加 Minty 来伤、原生删卡价格、建筑师真实投药水／继续／唯一胜利。

## 测试修正与实际边界

- 整合后早期 Ritsu 契约仍把演出目标查询次数固定为 1；新流程会分别查询姿势和死亡表现。改为验证确实经过故障注入点，仍要求死亡提交一次、清理一次，不将内部查询次数当成玩法。
- 首次实机驱动引用已经删除的建筑师 `PlayBriefGreeting`，导致测试 Mod 初始化失败。预览补丁改到当前原生问候接线 `PlayGreetingBow`，不恢复过时产品入口。
- 第二次弹出测试仅人工加入宠物，未持有遗物，按正式规则伤害倍数为零。补齐真实遗物后重跑；没有为测试改变产品伤害计算。
- 本轮所有图形测试启用 `-NoScreenshots`，最终证据检查截图数量为零；不录制视频。
- 当前本机安装是 preview，未再运行最终 stable 图形游戏。stable 证据是准确宿主 DLL 的构建和契约；headless 资源使用本机 preview PCK，不代表完整 stable 资源实测。
- 完整九模组双人／四人图形联机、所有东尼来源的实际结算及海克斯全部组合仍未覆盖。此前双进程 ENet 契约记录保持其原候选边界。
- 建筑师投药水 1.4.1 与东尼 0.3.102 的当前接口要求 preview，未在忍者杀手内伪造 stable API。
- 契约日志包含故意注入的异常、资源替身诊断及 headless 退出时 CanvasItem／ObjectDB 释放警告；没有将日志描述为“零错误”。

## 发布约束

版本 0.3.3。GitHub 合并后确认 main CI，再从干净合并提交构建并复核双宿主包。上传现有 Workshop `3776911445`，保持不公开列出及 RitsuLib 依赖 `3747602295`；不创建 GitHub Release。发布后重新下载，核对全部包文件 SHA-256、版本、更新说明、依赖与可见性。最终提交、PR、CI、文件校验值及远端核验写入本地 `build/releases/workshop-v0.3.3-evidence.json`，不回写历史测试证据。

用户在发布过程中要求清理磁盘。远端核验后不再保留每版完整本地包；保留小体积证据和必要日志，清理可再生成的构建产物及结束测试的缓存。源码、未提交工作、明确归档的素材和必要宿主参考不在清理范围。已清理的日志路径是历史执行位置，证据 JSON 留存其哈希和实际结果。
