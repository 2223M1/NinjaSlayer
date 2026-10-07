# 1.0.18 Strike 分类与名称

基于 1.0.17，打击·打击的简体中文显示名改为作者指定的 `Strike · 打击`，16 语言按 `Strike · 本地原名后半部分` 同步标题和生成牌描述。`StrikeStrike` 模型 ID、资源路径、升级和生成行为不变，无存档迁移。

原生 stable 0.107.1／preview 0.111.0 的 `StrikeDummy.ModifyDamageAdditive`、`FakeStrikeDummy.ModifyDamageAdditive` 和 `PerfectedStrike.CanonicalVars` 都读取 `CardTag.Strike`，不读取牌名。普通打击已具备该标签；补上 Chop打击和 Strike · 打击的 CanonicalTags。三张牌及基础／升级／生成实体遵循真实原生增幅：打击木偶 +3、假打击木偶 +1；完美打击计入战斗卡堆内各自所属玩家的打击牌。非打击攻击、非攻击／Unpowered 伤害和非基础牌的基础打击／防御判定不扩大。

采用内容标签，不 Patch 原版遗物／卡牌，不额外实现加伤、名称匹配或兼容框架。同步卡牌目录和独立 metadata 规格；保留 16 语言工坊介绍及语言标签，保留原有图片／依赖／公开状态，不部署 Worker、不创建 GitHub Release。

验证要求包括两宿主完整契约、升级／生成／回手／弃牌／消耗计数、两类木偶真实伤害、非打击对照与完美打击预览／实际伤害，以及一个隔离 Windows 预览版客户端的真实动作队列。最终源码、CI、冻结包和工坊重新下载核验将在完成后记录。
