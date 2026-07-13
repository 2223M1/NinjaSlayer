# 调试模式

`NinjaSlayerDebugCharacter` 是角色选择界面中的独立角色。它与正常忍者杀手共享人物实现、起始卡组、初始遗物、药水池、动画和美术，但使用 `NinjaSlayerDebugCardPool`。

调试池的唯一成员清单位于 `Content/NinjaSlayerDebugCardCatalog.cs`：

- `BaselineCards`：调试池的默认内容。目前包含全部初始卡、白卡和先古卡。
- `RemovedCards`：临时从默认内容中删除卡牌。
- `Replacements`：用测试卡替换一张默认卡，格式为 `(typeof(原卡), typeof(替换卡))`。
- `AdditionalCards`：向调试池追加测试卡。

例如：

```csharp
private static readonly Type[] RemovedCards =
[
    typeof(Chop),
];

private static readonly (Type Original, Type Replacement)[] Replacements =
[
    (typeof(PalmThrust), typeof(MyPalmThrustTest)),
];

private static readonly Type[] AdditionalCards =
[
    typeof(MyNewTestCard),
];
```

测试卡类型必须先通过 RitsuLib 注册到任一卡池，调试池只负责选择已注册的卡牌模型。正常忍者杀手始终使用 `NinjaSlayerCardPool`，不会受到调试清单改动影响。
