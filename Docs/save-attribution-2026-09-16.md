# 胜利保存等待归因：未关闭

2026-09-16，本地基线 `8c3cfa43ec4e9b99a81f2533772d2f4ed0b266fc`。

## 结论

本轮没有复现玩家的长时间保存等待，不能归责于原版、RitsuLib 或 NinjaSlayer，
也不能据此排除其中任何一方。未改生产保存流程，未增加保存超时、写入队列或 Steam 回调补丁。
现有瀑布巨人、泽渡、居合和武器改动保留；没有提交或发布。

五组各完成 10 次胜利保存，共 50 次，每次只收到一次 CombatWon 并显示奖励，
随后每组正常返回主菜单。另对多模组含 NinjaSlayer 组追加两次运行、20 次胜利，合计 70 次。
所有运行都包含独立诊断探针；**不是无探针的纯原版验收**。
未取得玩家 ModLaunchManager，因此多模组组也不是玩家环境的完全重建。

## 运行与时间

真实 Windows / Vulkan / Steam 回调，stable 0.107.1。
宿主 MVID `97f10687-c306-4798-ab75-8b9f23f34dfb`；宿主 DLL SHA256
`A1F9E653F1E28E4076558FEE1E60D218619CB7E057B887C6417F62C62C6D7A52`。
使用 Workshop 发布版 NinjaSlayer **0.2.12**，没有将当前未发布工作树冒充玩家版本。

证据根目录：`build/save-attribution-20260916/`。下表均为十次胜利中的最大值，单位毫秒。

| 运行目录 | 实际加载（均另含 SaveProbe） | 胜利数 | 本地写入 | Steam 请求至回调 | SaveRun 总耗时 | 保存结束至奖励可见 |
|---|---|---:|---:|---:|---:|---:|
| baseline-05 | 原版内容 | 10 | 8.77 | 20.17 | 36.23 | 566.95 |
| ritsu-01 | RitsuLib | 10 | 8.94 | 20.46 | 36.42 | 557.72 |
| ninja-01 | RitsuLib、NinjaSlayer | 10 | 13.08 | 13.29 | 31.99 | 548.48 |
| no-ninja-01 | RitsuLib、BaseLib、Flagellant、LexNinja2、旁白、劣人TV | 10 | 16.28 | 26.04 | 46.30 | 565.82 |
| full-01 | 上一组加 NinjaSlayer | 10 | 14.81 | 19.01 | 32.09 | 545.99 |
| full-02 | 同 full-01，另记录 Godot 异常时刻 | 10 | 12.90 | 17.18 | 37.04 | 545.86 |
| full-03 | 同 full-02 | 10 | 15.22 | 18.75 | 38.69 | 568.76 |

各组返回主菜单耗时依次为 811.51、851.60、861.98、843.63、874.73 ms。
追加两组分别为 869.09、866.16 ms。
每组含一次开局保存及十次胜利保存，共 11 个异步 Steam 写入请求和 11 个成功回调，
没有失败回调或越出隔离命名空间的请求。胜利写入请求至回调期间，ProcessFrame 均继续推进。

每组五次攻击击杀、五次状态伤害击杀。原版角色组使用原生毒；忍者杀手组使用手中黑炎回合末伤害。
首场由原生 AutoSlay 开局，其后通过原生 `fight TURRET_OPERATOR_WEAK` 进入战斗。
探针将敌人设为 1 血并移除格挡，以固定击杀；这不是玩家 A10 第 35 层的原存档复现。
本次没有完整长局、原存档重载或正在等待保存时退出的自然故障复现。
例如 ninja-01 开局存档为 39559 字节，第一次胜利为 40976 字节，明显小于玩家的
276287 字节；本轮小存档计时不能排除原存档规模或内容相关问题。

## 版本及配置

- RitsuLib 0.6.2，运行日志确认其 Steam cloud mirror **disabled**。
- BaseLib 3.4.7、Flagellant 0.5.2、LexNinja2 3.0.9、劣人TV 0.1.3。
- 旁白 0.2.0，使用玩家日志相同的 Workshop 条目 **3748718202**，不是另一份英文条目。
- 玩家日志里的 ModLaunchManager 1.0.0.0／条目 3788556097 本机没有；它会改变模组加载上下文，
  缺少它必须视为重要环境差异。未将历史对话中的以撒、CrashGuard 等加入本次日志未启用的组合。
- 各组 `assemblies.json` 记录全部暂存 DLL 的路径和 SHA256，`events.jsonl` 的 `mods` 记录实际加载结果。
  各组 `run.json` 记录基线和隔离范围；本地 settings/config 在对应 `appdata/` 中保留。

关键 DLL SHA256：

| 文件 | SHA256 |
|---|---|
| NinjaSlayer 发布包 loader | 630CB3C75C03BF5EC5B730AB809B927DA973BD138A2AFA460285A1A479B953E6 |
| NinjaSlayer lib/0.107.1 | 672950C69071C47CE75D5D0D774DB7DFE121279244F8BC0789EB7B439CA4608B |
| RitsuLib compat/0.107.1/STS2-RitsuLib.dll | 1278F675D582234A48D242A38E6721AD9BF04AAA423C62DF592B08BB296848C8 |
| RitsuLib compat/0.107.1/STS2-RitsuLib.Runtime.dll | 824A2A606DC9A578639E2AF77B954B909B9FD50C20B0EB76A6DCBDEAF24410A5 |

## 探针与存档隔离

`tools/save-probe/` 是不引用 RitsuLib 或 NinjaSlayer 的独立诊断模组，不进入产品注册或发布包。
探针在原生云同步前安装路径隔离，将 SteamRemoteSaveStore 的路径置于
`ninjaslayer-diagnostics/<组名-GUID>/` 下；原生目录枚举仍返回相对文件名。
同步、异步 Steam 写入入口检查该范围，初始化失败则退出诊断进程。
Steam API 的写入任务、回调及原生回调泵不被延迟、替换或提前完成。
异步观察器只记录完成结果，不替换原任务。测试禁用 StoreStats 提交，避免提交测试成就。

APPDATA/LOCALAPPDATA 同时隔离，但不将此视为 Steam 隔离证明。
实际 Steam 本地缓存已核对在：
`C:/Program Files (x86)/Steam/userdata/1377137616/2868840/remote/ninjaslayer-diagnostics/`。
所有有效组的请求路径均与各自 `run.json` 的命名空间相符。
测试文件仍留在这些独立命名空间内，没有删除或替换正式存档。
无探针纯原版未运行：当前路径隔离依赖探针，移除探针即无法沿用本次 Steam 存档隔离保证。
因此本轮证据只能称为“原版逻辑加诊断探针”，不能用来宣称纯原版已通过。

## 排除的诊断运行

- baseline-01：探针目录枚举补丁参数名错误，初始化中断；无有效计时。
- baseline-02：旧探针产物，Steam 初始化后失联，启动异常退出；无有效计时。
- baseline-03：探针已修正，但本机 Steam 活动进程登记指向不存在的进程，
  原生 `IsSteamRunning` 为 false 并停止回调。普通桌面同样发生，不能归因于独立桌面。
- 正常退出并重启 Steam 后，`IsSteamRunning` 恢复 true。此测试环境问题不是玩家根因证据。
- baseline-04：前两次保存完成，第三场测试攻击未穿透敌人格挡，未达成预定胜利；停止该诊断。
  修正测试准备后以 baseline-05 完整重跑，前两次不混入十次统计。

## 与玩家日志的关系

`godot (4).log` 第 35166 行已有 276287 字节本地存档，第 35193 行等待保存后返回菜单，
第 42845 行才记录同大小 Steam 写入完成。日志没有逐行时间，无法由行距算等待时长。
原生 EndCombatInternal 先写入已完成战斗存档，再等待 SaveRun 完成，随后发 CombatWon 和奖励；
因此保存等待期间退出、重载进入奖励与该流程相符，但没有说明是谁使回调滞后。
Steam remote 写入完成回调也不等于互联网云上传完成，不能直接建议“关云同步即可解决”。

多模组两组都出现了旁白访问 RitsuLib 已不存在的 `get_CombatState()` 签名异常，
但仍完成保存和奖励；该报错本身不足以证明保存卡死。
full-01 出现一次 `CueFrameSequencePlayer.Advance` 的 CompressedTexture2D 已释放异常，
但十次胜利保存、奖励和返回菜单仍完成。其 stderr 没有逐行时间，不能仅凭输出位置
把它定为保存等待之前或之后。追加只读 Godot 异常时间观察器后，full-02、full-03 的二十次胜利
同样完成，没有再次出现该异常。不能判定玩家纹理异常是保存等待的起因还是退出清理的伴随症状。
此前人为延迟保存实验仅解释画面与存档现象，不纳入责任归属证据。
此前瀑布巨人终结判定修复是另一场战斗的独立缺陷，不能声称已解决本次炮塔战保存等待。

## 交付与后续边界

新增诊断工具、逐次时间 CSV、汇总 JSON 和本文。本轮只构建独立 stable 探针（零警告、零错误），
未修改产品保存行为；项目文件仅排除独立探针的编译输入，MSBuild 实际 Compile 列表已确认
不含 save-probe。没有以重复双宿主构建替代本次缺失的自然故障证据。
有效运行全部 exit 0，七十次奖励均 wins=1；详见 `run-checks.json`。
源码与原始日志可继续用于后续对照，但当前故障保持未关闭。
进一步归因需要玩家实际存档／配置及缺失的 ModLaunchManager，或带时间的自然复现记录。
在出现“加入 NinjaSlayer 才失败，移除具体相关代码后消失”的对照前，不加入保存兼容补丁。
