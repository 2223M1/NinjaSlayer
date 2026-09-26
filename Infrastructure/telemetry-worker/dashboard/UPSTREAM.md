# 参考实现与复用范围

网站面向玩家。界面保留忍杀素材、字体栈和黑红配色；常用筛选、图表分类、卡图列表及详情按 Spire Codex 的使用方式组织。后台凭据、存储和故障诊断只放在本机管理页及维护文档中。

## Spire Codex

本次核对版本：[69b3c898a1b62fa277359a17970b9061abf60354](https://github.com/ptrlrd/spire-codex/tree/69b3c898a1b62fa277359a17970b9061abf60354)。

- `backend/app/routers/charts.py` 与 `frontend/lib/i18n-x8b.ts`：复用 26 类图表的分类和中文名称。`charts.mjs` 中保留对应图表 ID；与本项目测量单位不同的项目调整标题、说明，不照抄不成立的统计结论。
- `frontend/app/[locale]/charts/ChartsClient.tsx`：复用 Chart.js tooltip 配置；采用分类导航、图表、筛选、详细数据的布局。原项目为 Next.js，本网站继续使用 Pages 可直接部署的静态 JavaScript。
- `backend/app/services/run_entity_stats.py`：参考按正式卡牌目录确定统计范围的处理。忍杀直接使用已发布 DLL 的运行时目录，不用反编译文件名、名称前缀或人工退役名单推测卡牌是否有效。普通奖励、初始牌和衍生牌均可在当前目录中查看；目录外卡牌不进入本站卡牌统计。
- `backend/app/services/replays_db.py`：核对压缩正文、记录身份与 SHA-256 去重、受限解压、独立索引。该版本实际将战报写入 MongoDB；`r2_storage.py` 服务于公开 tier-list 预览图，并非战报存储。这里使用 Worker 原生 R2 binding 存私有正文，保留现有 KV 索引与 RitsuLib 重试协议，不引入 Python 服务、MongoDB 或浏览器存储密钥。

实际复制及改写的文案和 tooltip 配置遵守 [PolyForm Noncommercial 1.0.0](vendor/LICENSE.Spire-Codex.md)。署名与许可随 Pages 一起发布。其余源文件只作行为参考，没有复制后台或游戏反编译源码。

## 其他参考

- [LexNinja2](https://github.com/Flimsyyy/LexNinja2) 的 `LexNinja2Code/Api/NinjaTelemetry.cs` 使用 RitsuLib applicant、私有 contribution provider、PostHog adapter 和原生结束对局记录。忍杀已有同一套 RitsuLib 接入，保留独立授权和队列；自己的采集入口还负责公开战报授权、忍杀机制及精确序列化 UInt64 玩家 ID。没有复制 LexNinja2 源码。
- [STS Tracker](https://ststracker.app/zh) 的公开页面用于对照卡图列表、胜率/次数列、玩家用语及简短空状态。本次未获得其后端或 R2 实现，不将页面观察当成源码复用。

不复用账号检索、玩家排名、付费服务和自动扩容。R2 的 7 GB 清理阈值、8 GB 上限及操作预算是本项目的明确要求；到期或容量清理不删除汇总统计。
