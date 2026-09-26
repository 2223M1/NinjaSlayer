# 九个可选模组的兼容边界

本说明对应 0.3.3 的兼容实现。兼容工作最初基于 `7ef64c976636f6d4963c893f01be80328be97972`，随后并入最新主线及动画／图标修改。初轮实际验证及未执行项目见 [2026-09-26 验证记录](validation/compatibility-nine-mods-20260926.md)，发布候选记录见 [0.3.3 整合记录](validation/release-0.3.3-20260926.md)。

## 接入范围

| 模组／检查版本 | 本地实现与已验证边界 | 限制 |
|---|---|---|
| Loadout 2 0.5.8 | 已知无敌前缀不再导致处决整体停用；无敌先于奈落扣血，关闭后恢复真实扣血与吸收记录；东尼分解读取改后的数值、费用与关键词 | 未穷举 Loadout 所有编辑功能及其联机同步 |
| CookieCursor 0.7.0 | 通过其自定义光标目录提供 `ninjaslayercharacter/icon.png`，已有文件优先；实际缓存和刷新保留检查通过 | 远端光标、自定义图片替换及所有控制滑块未完整实测 |
| Minty Spire 2 1.2.0 | 同场小季宠物不会增加预计敌方来伤；沿用原生队伍和伤害，不添加提示补丁 | 全部多段总和、奈落血条提示和洗牌 UI 未穷举 |
| Better Menu，清单版本 v0.0.0 | 使用 0.107.1／0.111.0 对应运行库；实际快速读取保留不歪种计数，同种子／随机重开重置计数 | 未完成完整九模组联机重连流程 |
| RemovalCostViewer 0.1.0 | 实际显示与原生删卡价格一致，覆盖删卡次数 0／1／2；不额外计算价格 | 折扣组合和顶栏所有布局未穷举 |
| Player Size Tied to Max Hp 1.2 | 演出不再恢复其没有修改的角色根缩放或 `Visuals.Scale`；阿拉巴马落中改变最大生命，结束后保持新缩放 | 所有形态、移动和多人组合未完整实测 |
| 向建筑师投掷药水 1.4.1 | 原生对话中可以投药水，继续后等待已投药水，再处决并结算一次胜利 | 此版本引用 stable 缺少的 `Player.CanUseOrRemovePotions`，仅 preview 完成兼容验证 |
| ARAM: Mayhem 0.9.6，3747501308 | 奈落吸收关联最终伤害结果；濒死保命不误报死亡；已知前缀不再整体停用处决 | 尚未穷举海克斯所有抽牌、重复攻击和复活组合 |
| 东尼算法 0.3.102 | 可选程序集提供来源组件、稳定生成槽位、原生组件执行、牌池与本局设置保存；重载前恢复定义 | 第三方版本要求 preview 0.111.0；完整多人重连、全部组件实际出牌和 Loadout 联机组合未完成 |

这些模组均不成为普通 NinjaSlayer 产品的运行依赖。没有证据表明冲突的 Minty、Better Menu 和 RemovalCostViewer 不增加产品补丁。

## 伤害、处决与演出所有权

`NarakuLifeDamagePatch` 在 Loadout 无敌判定后处理奈落，末尾将吸收量关联最终 `DamageResult`。即便海克斯跳过原函数，也保留本次实际发生的吸收；没有吸收则不产生收据。弱引用随伤害结果生命周期释放。

`FinisherProtectionService` 仅允许已检查的 `HextechCombatHooks+NearDeathFeastLoseHpPatch.Prefix` 和 `TildeKeyGodmodeLoseHpPatch.Prefix`。未知跳过原函数或改写结果的补丁仍受原保护。致死入口检查 `__runOriginal`，不为保命成功的伤害建立处决记录。

阿拉巴马落及先古入场删除未拥有的缩放快照。人物大小继续由原生 `NCreature.ScaleTo` 和体型模组负责；不在演出归位时覆盖后来发生的外部修改。

建筑师以原生 `WinRun` 作为“继续”边界，问候使用原生对话。仅装有投药水模组时，在其精确的 `ThrowAndConsume`／`ShouldOfferThrow` 边界跟踪本房间的飞行任务；继续后不接受新投掷，等待已有投掷与问候后执行处决。没有吞掉执行失败并自动胜利的降级路径。

## 东尼算法

代码在 `Integrations/AutoAnthony/`，主项目排除该目录的编译。普通产品不引用 `AutoAnthony.dll`。检测到东尼且处于 preview 时，在原生内容发现之前加载同目录的 `NinjaSlayer.AutoAnthony.dll`，关联原生模组身份，再经 Ritsu 安装必要接线。装有东尼而缺少桥接 DLL 会明确失败。

- 使用 `ExternalComponentCharacterApi`、`ComponentPackageApi`、`ComponentRuntimeApi`、原生命名及价值接口。配置名为 `ninjaslayer`，基础平衡配置继承 Ironclad，使用忍者杀手能量图标。
- 来源表是 `NinjaComponentSources.Sources` 的 84 行，包含 80 张奖励牌及 4 个初始牌模型；每行记录组件出现次数，数值从实际基础／升级实例读取。稳定槽位 000～089：10 初始、20 普通、35 罕见、25 稀有。关闭东尼后普通卡牌、初始卡组和奖励池不变。
- 通用伤害、格挡、抽弃牌交给东尼原生组件；空手道、手里剑、茶道、奈落和预见调用现有 NinjaSlayer 命令。沿用原生初始牌池、奇巧、奥斯提及衍生牌约束修复和审计。保留固定规则的强·手里剑与事件大刀本体。
- 继承启用、替换初始、保留原牌、数值随机、数值优化、终极混沌及随机卡图开关。当前卡图来源按来源表槽位分配，开启随机卡图后使用东尼的对应素材变体；并未移植东尼内部“最佳卡图来源”排序。
- 使用独立的种子派生随机源，不消耗猫粮杯奖励随机流。多人采用东尼已有的权威开局设置与原生种子，不增加网络协议。
- Ritsu 的 `autoanthony_pool` 保存本局设置、东尼原生序列化定义及卡图来源。每次原生保存前捕获编辑后的定义；在卡牌反序列化前恢复。快速读取不重新抽取牌池。
- 三处非公开访问限于已核实的外部边界：东尼卡牌缓存重置、东尼卡图变体枚举、Ritsu 在卡牌反序列化前解码本模组的数据槽。升级这些依赖时必须重新检查；不承诺任意未来版本兼容。

原有 [单人不歪种规则](singleplayer-seeds.md) 不变。一人 Host 仍是联机；原版角色和联机不启用本模组的不歪种接线。原猫粮杯自身存在时仍优先。

## 构建与打包

桥接单独构建，必须使用匹配的 preview NinjaSlayer 候选和已安装的 AutoAnthony 0.3.102 引用，引用均为 `Private=false`。例如从仓库根目录执行，路径变量指向实际本机文件：

```powershell
dotnet build Integrations/AutoAnthony/NinjaSlayer.AutoAnthony.csproj -c Release `
  -p:NinjaSlayerHostChannel=preview -p:NinjaSlayerVersion=0.3.2 `
  "-p:RepositoryCommit=$candidateBaseSha" "-p:Sts2DataDir=$previewReferences" `
  "-p:NinjaSlayerAssemblyPath=$previewProductDll" `
  "-p:AutoAnthonyAssemblyPath=$installedAnthonyDll" "-p:OutputPath=$bridgeOutput"
```

`New-NinjaSlayerWorkshopBundle.ps1 -AutoAnthonyBridgePath <DLL>` 将桥接仅加入 `lib/0.111.0/`。包契约检查程序集身份、来源 SHA、宿主、引用、调试与路径边界，不携带任何第三方 DLL。普通产品构建不需要安装东尼。

0.3.3 已把桥接构建接入通用打包入口、两个发布脚本以及 release／smoke workflow。未提供预构建桥接时，打包入口使用 `-PreviewSts2DataDir` 和 `-AutoAnthonyAssemblyPath`（或 `NINJASLAYER_AUTOANTHONY_ASSEMBLY_PATH`）构建。缺少引用立即失败，不生成缺桥接的发布包；原生产品构建仍无需东尼。临时 Release／Smoke runner 通过同名参数接收引用，并复制为隔离只读输入。Workshop ZIP 和程序集契约均要求 preview 目录有该桥接，第三方 DLL 不进入包。

## 普通卡牌目录

本轮未改变正常模式卡牌效果、模型 ID 或牌池；不修改 `Docs/card-catalog.md`。东尼生成牌及组件来源由上述可选集成拥有，不加入普通卡牌目录。
