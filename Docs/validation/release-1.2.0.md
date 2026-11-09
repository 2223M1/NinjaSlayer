# 1.2.0 发布记录

本次按用户要求发布 Workshop 1.2.0，包含 11 项卡牌设计调整、戒备与转机两项额外改名、7 项机制与显示修复，以及61张既有卡图替换。完整发布说明固定于 Workshop/change-note.md，与 Workshop/workshop.json 的 changeNote 一致。

备镖改为戒备（On Guard），韧性及对应能力改为转机（Turning Point），同步16语言、东尼算法和卡牌目录。连打与破势一击沿用此前确定的名称，其他命名复查候选未应用。

此前本地验证与改动明细见 unpublished-20261109.md。本次最终包重新导出资源，以明确的1.2.0版本构建两个受支持宿主。发布状态与最终文件哈希将在上传并回下载核验后记录，当前尚未发布。

官网 A0–A10 筛选改动保留在源码，本次仅发布 Workshop，不部署官网、不修改反馈状态、不创建 GitHub Release。

追加范围

- 沢渡继续仅替换强敌阶段的问号战斗。使用原生已生成或载入的 RoomSet.events，纳入共享事件和解锁过滤，再按 IsAllowed 与 VisitedEventIds 过滤。将沢渡计作额外一个等权事件槽位。事件概率依据宿主最终允许的房间类型计算，仍受可替换战斗概率上限约束，教程、弱敌阶段及已遇到沢渡的跑局不触发。
- 小季、鸟居、奈落及京都事件保留标准 RegisterSharedEvent/RegisterActEvent 与原生事件队列；原有角色、幕数及奈落单人牌组条件不变，没有新增权重或额外抽签。
- 鲔鱼寿司改为拾起时从可附魔牌中选择3张；可选牌不足3张时沿用原版选牌命令处理。滋养附魔每张牌每场战斗首次打出时增加1最大生命，使用 CreatureCmd.GainMaxHp，因此同时按原生规则回复相应生命。同张牌重打不重复生效，原生牌组存档与战斗副本负责跨战斗持久化。16种语言的遗物和附魔描述同步。

原版证据为 stable 0.107.1、preview 0.111.0 的 Core/Models/ActModel.cs（GenerateRooms）、Core/Rooms/RoomSet.cs（EnsureNextEventIsValid）、Core/Odds/UnknownMapPointOdds.cs（Roll），三份文件在双宿主导出中完全一致。寿司遵循 Kifuda 的附魔选择、Swift 的 EnchantmentStatus 首次打出语义及 CreatureCmd.GainMaxHp 的上限/生命结算。未添加新的私有 API 反射入口。
