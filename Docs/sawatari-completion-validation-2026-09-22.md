# 泽渡事件收尾与 0.3.0 验证

源码基线 `25aea27233a3cdadb68f001045ada453f08ca570`。本轮包含手里剑槽位整改、泽渡事件收尾修复和拖牌姿态改动。
用户要求只在本机继续验证，并授权发布 GitHub 和现有 Workshop 条目；ninja5080 不再作为发布门槛。
用户随后明确将拖牌姿态一同发布，并指定版本为 0.3.0。无关 `Docs/analysis/` 留在原工作区。

## 原因与改动

旧 FMOD bank 的最后一个转场区覆盖了 Duel Outro 的目标位置，导致到达尾奏后再次跳回尾奏开头。
本机旧包对照持续 25 秒停留在 350.65～350.80 秒，未到达停止状态。新 bank 将 106 个短转场区
收敛为 4 个连续阶段区域，在目标标记前 2 ms 截止，并让循环只在对应阶段生效。
实际音源、事件 ID、音乐总线及 0.12 秒淡变保留；源项目差异保存于 `tools/fmod/sawatari-outro.patch`。
决斗死亡确认时立即切入尾奏，结果页不重复启动音乐。

首战击杀后不再在胜利检查中直接暂停。改在原生 `EndPlayerTurnPhaseOneInternal` 完成后接管，
由宿主等待所有参与者结束回合及回合末出牌／钩子结算。选项等待位于行动执行器之外；挑战泽渡后
继续同一次原生回合切换，不追加第二个结束回合命令。决斗结果页也等待行动结算完成后才暂停。
保留原生分裂、召唤物退场、玩家战败和继续战斗钩子的判定，不删除存活敌人。

## 开发候选验证

证据位于 `build/sawatari-completion/`，下述实机均为本机 Windows Vulkan 真实游戏进程。
stable 0.107.1 MVID 为 `97f10687-c306-4798-ab75-8b9f23f34dfb`；preview 0.111.0 MVID 为
`73b63ee0-6c0a-47bb-b0d1-b21f6d94222e`。编译引用 RitsuLib 0.5.12，实机加载 0.6.2。

| 项目 | 结果及证据 |
| --- | --- |
| 旧 bank 复现 | `live/run-A-outro-before/` 如预期失败；记录尾奏反复跳回及时间线。 |
| 新 bank 对照 | `live/run-A-outro-after/` 通过；完整播放约 13 秒后进入 FMOD STOPPED。 |
| stable 事件实机 | `live/run-A-event-after-01/` 22 个检查点通过。包括音乐读档恢复、击杀后拖动并松开攻击／技能牌、手动结束回合接管、决斗新回合、尾奏及原生奖励；回归黑暗忍者音乐和退出清理。 |
| stable 清场实机 | `live/run-A-cleanup-after-01/` 16 个检查点通过。真实地精佣兵分裂、雾菇／利齿之眼、两幕普通领奖及决斗、助战／群体／反伤击杀；回归居合死亡资格。 |
| 双宿主产品及 SmokeDriver | 全部通过，零警告、零错误；`*-build.log`、`*-smoke-build.log`。 |
| 双宿主泽渡产品契约 | `stable-contract.log`、`preview-contract.log` 通过；安装准确宿主的原生回合末补丁目标，覆盖既有数值、武器、原生活力、伤害与保存字段。 |
| 双进程 ENet | `enet-stable-02/`、`enet-preview-01/` 通过。第一位玩家结束后另一位仍能行动，两位均结束后生产回调只进入一次。也回归手里剑与事件遗物的联机归属。 |
| 逻辑及仓库 | 358 项逻辑测试通过（包括当时工作区瞄准测试）；compatibility、repository、build-boundary 和差异空白检查通过。 |

| 开发候选 | SHA-256 |
| --- | --- |
| `stable/Release/NinjaSlayer.dll` | `67051373e6f6e8c2b030c64dffe9a13732f656be90c3f4f415e2077f7d80a6db` |
| `preview/Release/NinjaSlayer.dll` | `3779e4c10a83916701f17c50c36a099007d3f680084d537029f4c4d8fb0c39cf` |
| `NinjaSlayer.pck` | `38d9ee02d1717d536e2f81ec10f5232cf72c457b566f41efbb33d7a7d8158f1b` |
| 新 `NinjaSlayer.bank` | `fa46e4e80b3ae96e325ceef9f9885ab5f085e1d3ff6b43bec2b29b3fc4ff5255` |

这些文件来自含其他演出修改的开发工作区，不是不可变提交产物。干净发布候选须另行验证。
双进程契约不冒充完整双客户端可视事件投票；本机尚未执行 preview 可视实机或完整 AutoSlay。
Godot 退出仍有既有节点／资源清理警告，不将断言通过写成零警告游戏日志。

## 0.3.0 干净候选

从独立干净工作树的 `ec4b1303fc5b0794984c6d2a222060f7b5331772` 构建，显式指定版本 `0.3.0`。
候选包含拖牌平滑跟随、至多 3 度的边界回弹、预览与实际朝向分离，以及取消／攻击接管处理。
证据根目录 `build/v030/`，文件清单见 `candidate-evidence.json`。

| 文件（`../release-v0.3.0/build/candidate-channels/` 下） | SHA-256 |
| --- | --- |
| `stable/package/NinjaSlayer/NinjaSlayer.dll` | `dbb360f1ffc5a897ff863178a00158e24811655b4f89cb880ed7b24bd173d860` |
| `preview/package/NinjaSlayer/NinjaSlayer.dll` | `2e678d9c6d3ea05465780a24370f52b35d26827fe63a33ff76f6ffda69b441d4` |
| `stable/package/NinjaSlayer/NinjaSlayer.pck` | `4901f8b1532644300aeaba170a617918731744fec5521211b442327bbddbc049` |

- 两宿主产品打包、资源契约及 SmokeDriver 均通过；`*-package-02.log`、`*-smoke.log`。
- 两宿主完整产品契约通过；`*-full-contract-final-02.log`。此前受击断言失败是测试用例只复原
  Sprite 变换、未复原实际朝向所致；测试改用现有朝向接口初始化并在子用例结束后恢复，
  保留受击方向、位移、时长及 UI 稳定的原断言。仅测试接线改动，候选产品 DLL 未改。
- 两宿主双进程 ENet 通过；`enet-stable/`、`enet-preview/`。验证混合槽位、库存归属和
  所有人结束回合后只触发一次泽渡回调。
- stable 真实游戏 `live/run-A-event-candidate-02/` 22 个检查点通过，包含尾奏播放完成、
  击杀后继续拖牌、手动结束回合、读档音乐及奖励。
- stable 拖牌演出 `live/run-A-aim-candidate-02/` 14 个检查点通过，覆盖普通／各奈落形态、
  取消重拖、包围状态与攻击接管。`aim-contact.png`、`preview.mp4`、`motion.json` 保留可视证据。
- stable 手里剑 `live/run-A-orb-candidate/` 23 个检查点通过，覆盖混合槽位、10＋1槽、
  实际激发、预览、退出及原生 Continue 重载。已查看对应布局截图。
- 358 项逻辑测试、仓库一致性、兼容配置和构建边界通过。

此后提交仅补充测试状态恢复和验证文档；最终发布包按合并后的完整 SHA 重新构建。
GitHub CI、最终包文件哈希、远端重新下载及更新说明核验记录写入本地
`build/releases/workshop-v0.3.0-evidence.json`。保留不公开列出与 RitsuLib 依赖，不创建 GitHub Release。
未执行本机 preview 可视实机、完整 AutoSlay 或双客户端可视事件投票；不再在 ninja5080 测试。
