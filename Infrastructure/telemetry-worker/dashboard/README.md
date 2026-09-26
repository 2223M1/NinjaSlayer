# 忍者杀手观测室

公开观测室：https://2223m1.github.io/NinjaSlayer/ 。使用 GitHub Pages、原生 HTML/CSS/JavaScript；本机关机不影响访问。本机仍提供私有反馈管理页。

## 公开网站

`.github/workflows/observatory.yml` 在相关 main 更新、手动运行及每 15 分钟计划执行时生成静态快照并部署。GitHub 的计划任务可能延迟，页面显示每个数据源最近成功同步时间；按钮只重新读取已发布的快照。同步失败保留最近成功数据并显示错误，首次尚未配置时显示未连接，不放入示例数据。

Actions secrets：`POSTHOG_PERSONAL_API_KEY`（项目查询权限）、`POSTHOG_PROJECT_ID`、`POSTHOG_QUERY_HOST`（美国或欧洲区）以及与 Worker 同值的 `OBSERVATORY_READ_TOKEN`。仅构建步骤读取这些凭据，Pages 产物只包含匿名汇总和明确同意公开的反馈正文。没有服务器端运行时，不需要本机服务、自启动任务或隧道。

`node Infrastructure/telemetry-worker/dashboard/build-pages.mjs build/pages` 生成站点。`OBSERVATORY_PREVIOUS_URL` 用于保留上一份成功快照。公开数据按 UTC 日期、模组与宿主版本、模式、人数、进阶及读档情况汇总；卡牌统计保留分母，跨版本对局仍按战斗采集版本过滤使用次数。不会输出玩家 ID、种子、完整牌组或原始对局。

仅 `mod_context.publishDescription=true` 的新反馈公开正文、分类、时间与版本；旧反馈不公开。截图和日志不进入 Pages，公开导出接口也不提供附件。反馈保留 180 天；管理删除在下一次成功同步后从网页消失。网站素材来源及 SHA-256 记录在 `assets/sources.json`，正文沿用官网系统字体栈。

## 启动

需要 Node.js 24 或更新版本；首次在 `Infrastructure/telemetry-worker` 运行 `npm ci`。

```powershell
pwsh -NoProfile -File tools/Start-NinjaSlayerDashboard.ps1
```

也可在 Worker 目录运行 `npm run dashboard`。默认地址 `http://127.0.0.1:4178`，仅监听本机。
启动脚本会打开浏览器；`-NoBrowser` 仅启动服务。日志在 `build/dashboard/`。
关闭服务可结束该 Node 进程；网页关闭不会停止服务。可用 `NINJASLAYER_DASHBOARD_PORT` 指定端口。

## 连接

- F2 反馈复用这个 Worker 的 Wrangler 登录；首次需要在 Worker 目录运行 `npx wrangler login`。索引和正文读取 KV，截图和日志读取私有 R2，网页不提供删除接口。
- 对局数据在“连接设置”填写 PostHog 区域、数字项目 ID 和具有该项目查询权限的个人 API key。游戏使用的 ingestion key 无读取权限。
- 连接凭据只存在于本次 Node 进程内，不发送给浏览器、不写入项目或 localStorage。重启后重新输入；也可通过 `POSTHOG_QUERY_HOST`、`POSTHOG_PROJECT_ID`、`POSTHOG_PERSONAL_API_KEY` 环境变量提供。
- 可导入 RitsuLib batch、PostHog 查询响应或事件数组 JSON；导入只替换本机视图，不改远端。PostHog 查询列顺序为 `uuid, timestamp, properties`。
- 本机页面打开后同步一次；“同步数据”重新读取远端，若已配置 PostHog则替换导入视图。每分钟重读内存视图，不自动重复查询远端。
- 未连接、读取失败、真实零条记录分别显示。反馈截图和日志按需读取。私有页面拒绝跨站访问和非本机 Host，所有外部文本使用 textContent。

## 统计口径

RitsuLib 0.5.12/0.5.20 的 `run_history.completed` 内部是原生 **SerializableRun**，并非历史面板的 RunHistory。胜负读取 `is_victory`，角色与选择通过 `net_id`/`player_id` 对应，种子在 `rng.seed`。贡献位于 `private_contributions.NinjaSlayer.ninja_slayer_balance_context`。不兼容的结构会明确排除，不猜测字段。

| 指标 | 分子、分母及限制 |
| --- | --- |
| 完成对局 / 通关率 | 已授权、收到、未放弃的忍者杀手对局；失败计入。通关对局 ÷ 对局数。 |
| 提供 / 选中 / 抓取率 | 原生 card_choices 中每张提供的牌和 was_picked。分母是提供次数，包含跳过；同一对局多次出现分别计数。 |
| 持有率 / 持有通关率 | 结束牌组按模型 ID 去重，同一角色持有多份或升级不重复算样本。持有角色 ÷ 忍者杀手角色样本；持有且通关 ÷ 持有角色。 |
| 抓取后 / 只跳过通关率 | 按角色对局分组。至少选中过一次为前者；提供过但从未选中过为后者。它们不是随机实验，不能解释为卡牌导致的胜率变化。 |
| 平均抓取楼层 | 每次选中时的全局地图点序号平均值。一个地图点内部的额外房间不加一层。 |
| 升级 / 移除 | 原生 upgraded_cards / cards_removed 条目数。不是升级优先级或“应删牌”的建议。 |
| 战斗覆盖 | 有测量的忍者杀手角色战斗 ÷ 原生 monster/elite/boss 房间 × 对应角色。缺失记录不补零。 |
| 抽到 | 原生 CardDrawnEntry；生成、直接拿回手牌不冒充抽牌。 |
| 手动 / 自动打出 | 原生 CardPlayStarted 的系列首发，按 IsAutoPlay 区分。原生绕过手动条件的自动打出也计入。 |
| 开始 / 完成结算 | 原生 CardPlayStarted / CardPlayFinished，重复效果逐次计入。自伤死亡可能只有开始而无完成。 |
| 实付能量 / 星数 | 首发 ResourceInfo.EnergySpent/StarsSpent；重复系列只计一次，不用 EnergyValue/StarValue。指已开始打出系列的支付，不包括在开始之前被取消的操作。 |

卡牌总表合并基础与升级；新版机制表按事件发生时的升级状态分别统计。旧版没有即时快照，升级状态保持缺测。生成牌可以不经抽牌而打出，**打出÷抽到不是使用概率**。伤害只关联命令明确提供的卡牌来源；黑炎、空手道和充能球单独列出。不提供综合强度分数或因果胜率。A10 胜率分组需要至少 20 场有效 A10 对局，属于样本分组，不是玩家身份检索。

同种子、同开始时间、同玩家集合的上传按一局去重；先采用较高读档次数对应的最终结果，同一次读档合并不同角色贡献；较多测量只在同一结果中补齐覆盖。同局胜负冲突的记录排除并提示。旧版经过 JavaScript 后已丢失的 ID 精度无法恢复；新版在发送端把 ID 写为精确十进制字符串。身份发生碰撞的记录排除。

查询固定上界并按 timestamp、uuid 排序分页，最多载入最近 50,000 条事件；达到上限会显示截断提示。筛选只作用于已载入数据。少于 20 次提供会提示小样本；20 次并非统计显著性的门槛。持有胜率有存活偏差，建议结合进阶、宿主、版本、模式、人数、读档和抓取时机看待。

## 新增采集

`balance_schema = ninja_slayer_run_history_v3` 在 `applicant_payload.mod_payload.combats` 保存按楼层/房间键控的战斗汇总。每项含 encounter、rounds、won、采集版本、各角色及卡牌计数。使用独立的 `balance_runs` 申请项，沿用 RitsuLib 的队列、适配器和原有 Worker。忍者杀手设置提供开关与说明。未知授权默认显示开启，但首次告知前不投递；确认立即开启，忽略本次不上传、下次进程启动默认开启，明确拒绝持续关闭。已有 RitsuLib 拒绝和仅有旧 `run_history` 的授权不会被升级覆盖，不补传历史对局。

Worker 同时接受已发布的 `run_history` 与新的 `balance_runs` 请求。新请求的 Worker 校验改动须在分发新版 DLL 前部署。

逐项数据在 CombatHistory.Changed 和原生回调内即时快照，胜利在 CombatEndedEvent 保存该场测量；终局失败在 RunEndedEvent 保存当前测量。战斗数据通过 RitsuLib RunSavedData 保存，重打同一房间替换该项，不累加已放弃的尝试。此前版本或不同同意状态导致的缺口由覆盖率显示。退出/放弃不会上传半局。

## 本地验证

`npm test` 包含独立统计样例、重复/冲突/多人记录、64 位 ID、版本和读档筛选、缺失测量、查询分页、密钥隔离、HTTP 访问边界与导入失败保留旧视图。设置 `NINJASLAYER_TELEMETRY_FIXTURE` 可额外验证双宿主产品契约生成的原生 JSON；公共 CI 没有宿主时明确跳过该项。

产品 OrbContractTests 验证原生抽牌、手动系列、Echo Form 重复、免费自动打出及精确 ID。FullAutoSlay 和 TelemetryLoss 使用隔离 APPDATA 和本地文件遥测适配器，不向线上发送测试记录；结束时要求每场战斗都能与原生房间数对应。TelemetryLoss 还验证失败结算与重复 OnEnded 的单次上报。

启用过自由操控的整局不会上报平衡统计。标记随该局存档保存，中途关闭开关或读档不恢复统计资格。

## 图表、战报和内容版本

26 类图表由 `charts.mjs` 定义，独立统计聚合在 `chart-data.mjs`。日期、版本、进阶、模式、人数、胜负、读档和 A10 分组写入 URL；比例使用 Wilson 95% 区间。图表显示样本单位和测量点数，缺测不补零。机制表的“涉及战斗”按已完整测量的角色战斗计数，只包含出现该项的战斗；版本不同的机制行不合并。每实付能量伤害包含免费效果的伤害，不能解释为卡牌固有效率。

公开战报使用独立默认关闭授权、`ninja_slayer_replay_v1` 白名单和 RitsuLib 队列。私有 R2 保留 90 天，Pages 定期拉取 KV 匿名索引，详情通过 Worker 按需读取。路线依次展开房间、读档尝试、回合和行动。未授权队友不公开明细，缺段和过期明确显示。撤销授权保存在本地 journal sidecar，不能被旧存档恢复。

反馈正文静态进入 Pages；反馈元数据留在 KV，截图和日志存入私有 R2，均不进入 PostHog。容量与请求次数由共享预算限制，超额响应可重试。R2 预留达到 7 GB 后按原始上传时间从旧到新清理，8 GB 为硬上限；每日最多 2,000 次写入和 20,000 次未缓存公开读取。确认删除后才释放容量；180／90 天为最长保留期限，容量清理可能提前结束旧原始数据的保留。汇总统计不受影响。详情见 Worker README 与 `Docs/privacy.md`。

网站卡牌名称、双语原生格式化文案、升级、关键词与卡图均来自实际 DLL 的 `WebsiteCatalogExporter`。运行 Smoke 的 `Catalog` 模式导出后，用 `tools/release/import-website-catalog.mjs` 校验内容和图片 SHA-256；历史版本只写一次。`Website/content/current.json` 只能在 Workshop 远端版本、说明和包校验通过后推进。查看旧战报使用其版本目录，目录缺失显示缺测，不能用测试规格代替生产内容。
