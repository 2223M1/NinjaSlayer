# 奈落事件最终选项统计

现有 `run_history.completed` 上传完整的原版 `SerializableRun`，其中 `map_point_history[*][*].player_stats[*].event_choices[*].title.key` 已包含页面及选项标识。无需新增游戏内采集，也不增加玩家数据字段。

在 GitHub Actions 运行 **Naraku event choices**，可填写最近天数（0 为全部）及精确模组版本（留空为全部）。工作流使用现有 PostHog 查询凭据，只输出汇总报告，不保存原始对局或玩家标识。

也可对已有 RitsuLib/PostHog JSON 导出运行：

```powershell
node tools/analytics/naraku-choices.mjs --input path/to/export.json --days 7 --version 1.2.8
```

不传 `--input` 时读取 `POSTHOG_QUERY_HOST`、`POSTHOG_PROJECT_ID`、`POSTHOG_PERSONAL_API_KEY`。结果默认为 `build/analytics/naraku-choices.json`。

统计以已有对局去重规则处理重复上报与 SL；每位忍者杀手玩家每次遇到事件只计算最终选择。不同页面的 `SILENCE_NARAKU` 合并为“令其闭嘴”，`ACCEPT_NARAKU` 合并为“接受奈落”。比例分母为两者之和；仅有中间呼唤、缺记录及未知选项分别列出，不推断结果。无最终选择样本时比例为 `null`。

样本仅涵盖同意平衡遥测且以胜利或失败结束的对局，不含放弃、仍在进行或未上报的对局。时间筛选按对局完成时间，版本按结束时的模组版本。该比例是样本中的选择占比，不是事件出现概率，也不是所有玩家的无偏估计。旧版本已有选项记录可直接统计。
