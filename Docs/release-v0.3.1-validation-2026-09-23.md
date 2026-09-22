# 0.3.1 卡牌与本地演出验证

日期：2026-09-23。发布基线：`main@04a8d9bb8648fd574f0ba9b0473faf79b5c6a59e`（0.3.0）。
候选工作树：`C:/Users/theon/Documents/NinjaSlayer/release-v0.3.1`。
本记录描述提交前候选；此阶段 DLL 的 SourceRevision 是基线，不能把它误认作包含本轮修改的提交。最终合并 SHA、正式包哈希与远端回读结果另存发布凭据。

## 内容与边界

- 守势两版2费，空手道每次结算获得1/2点格挡。从容两版1费、每次弃牌2格挡，升级仅增加原生固有。两者仍为 Unpowered。
- 更新独立93张卡基础/升级规格及目录。中英说明已有动态 Block 变量，继续使用原版关键词显示，无需重复写入固有文本。
- 迁入37个非火焰文件差异：Architect原生Spine死亡动作及爆散时序；黑暗忍者实际伤害时点的原生偷牌飞行、手持、镜像和死亡掉落；25%低血量喷血及语音阈值；相应资产、工具、预览和4份带历史基线的泽渡分析。
- 半奈落火焰的15项文件差异全部保留在原工作树，候选中没有火焰合成器、shader、资源占位、接线、测试或工具。原工作树未重置。
- 修复整合时发现的暗打回归：致死反伤先移除攻击者，导致其 AfterDamageGiven 不再派发。伤害回调未偷牌时，在同次实际伤害返回后处理返还，仍以真实/Naraku损失为依据，单次只偷一次。既有三张加最后一张的四份原生奖励测试通过。
- 修复 NativeStolenCardMotion 的 Stop 与 ExitTree 重复退订 FramePreDraw；重录确认不再出现该退订错误。
- 更新旧契约：Architect保留原版3.667秒抛起/落地轨迹，0.9秒是爆散画面节点，不是整个动作结束；RitsuLib事务数量包含0.3.0已加入的充能球目标，实际115项、关键78项。

## 环境

| 项目 | 实际值 |
|---|---|
| stable | 0.107.1；MVID `97f10687-c306-4798-ab75-8b9f23f34dfb` |
| preview | 0.111.0；MVID `73b63ee0-6c0a-47bb-b0d1-b21f6d94222e` |
| Godot | 4.5.1 Mono Windows x64 |
| 编译RitsuLib | 0.5.12，按宿主使用对应包 |
| stable实机RitsuLib | 0.6.2，来自现有Workshop安装 |
| 实机隔离 | 本机独立游戏副本、存档目录，强制Steam关闭，并对测试程序配置出站阻断；未使用ninja5080 |

## 自动验证

- 逻辑测试358通过、0失败、0跳过；仓库一致性、兼容配置同步、构建边界、diff空白检查通过。
- stable/preview产品与SmokeDriver构建；完整Orb产品契约、RitsuLib事务/回滚契约及两个ENet进程契约。
- 两张卡的全部基础/升级元数据、双语描述、群攻一次/多段多次、基础升级叠加、批量弃牌，以及敏捷/勒紧排除均由候选产品契约检查。
- 暗打覆盖真实生命、奈落完全吸收/溢出、格挡、缓冲、闪避、零伤害、升级附魔、固定种子、双玩家归属、致死受击、致死反伤、连续偷牌、原生奖励逐张领取/放弃/重载。
- 日志：`build/v031/logic-tests.log`、`verify-final-local.log`、`{stable,preview}-contract-07.log`、`ritsu-{stable,preview}-07.log`、`enet-{stable,preview}-07/`。
- 产品路径：`build/channel-build/{stable,preview}/package/NinjaSlayer/NinjaSlayer.dll`。提交前最终stable SHA256 `E347CEB48C50FEF2C5A5254D87ED292D938B1ECB95DAC26D202438EAD7838C24`；preview `072D6C083602F09BA48520DF1ABB8BF0D8E3332EE71C3BCC1D1EAEA6F2758CA2`。

## stable渲染实测

| 场景 | 证据与检查 |
|---|---|
| Architect | `build/v031/live-architect/`；实际产品演出完成，检查原始姿态、白化、爆散及镜头归位；14秒1920×1080录制 |
| 低血喷血 | `build/v031/live-blood-02/`；25%阈值、连击单实例、全格挡/闪避/自伤/非攻击排除、各形态、固定头/移动/镜像、暂停恢复、Fast/Instant和零血清理 |
| 黑暗忍者偷牌 | `build/v031/live-theft-02/`；原版Thieving Hopper同场对照，实际扣血帧与飞行差小于40ms，原生手持尺寸、两侧飞行、遮挡、失败不偷、暂停及飞行中死亡清理 |

三组录制均完成自动场景断言和人工抽帧检查。音频校准最大残差分别约0、26、19.3毫秒；编码60fps，但存在13.7%～19.6%的重复帧，不宣称60个独立采集帧/秒。
Architect/喷血使用演出候选DLL SHA256 `274B27905F3B9494339C671CE738114507DD38E4D7E21EFB35566B79B9C02BE5`；修复退订后的偷牌复测DLL SHA256 `BC50E7D2C8175EEF9019643E75AAFC6A9B97E17D9032DE92DF7BA70E41490D45`。完整录制的宿主、DLL、驱动及资源哈希见各目录 `recording-build.json`。

## 实测限制

- preview完成构建、产品契约和ENet契约，未执行preview图形实机；联机验证为真实双进程协议/模型测试，不称作双人图形实机。
- Architect录制仅在预览驱动中抑制最终事件离场，便于保存录像，不能作为完整事件领奖流程的实测证据。
- 引擎仍输出已有节点路径和退出诊断：本轮Architect节点路径诊断954条，与先前 `build/theater/architect-execution-fixed/` 相同。偷牌预览在非黑暗忍者事件音乐中创建/销毁怪物时还会报告缺少 `dark_ninja_progress` 参数。本轮不改原音乐流程，也不宣称日志零错误。
- 不发布半奈落火焰、不修改官网或编辑板、不新增运行存档格式。
