# 上传工坊

<!-- Source: https://tutorials.sts2modding.com/docs/11-upload-workshop/ -->

## 下载上传器

下载官方mod上传器： [https://github.com/megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader)

## 创建新mod

- 双击 `ModUploader.exe`，会生成一个名为 `NewModWorkspace` 的文件夹（工作区）。
- 把 `NewModWorkspace` 重命名成你想要的名字。
- 把你的mod内容放进工作区里的 `Content` 目录。不需要压缩成压缩包，只需要你mod的文件（例如json,dll,pck）。
- 填写工作区里的 `workspace.json`。建议是不填任何信息，只保留一个`tags`字段，之后在工坊改就行。（因为tags在工坊无法修改，其他如果填了上传时会覆盖工坊内容）
- 把工作区里的 `image.jpg` 替换成你自己的mod预览图（文件名保持 `image.jpg` 不变）。
- 在工作区文件夹里打开命令行窗口。（右键在终端运行）
- 运行 `ModUploader.exe upload -w <你的文件夹名字>` 上传mod。（不需要`<>`）

## 更新已有mod

- 把更新后的mod文件放进工作区的 `Content` 目录。
- （可选）在 `workspace.json` 的 `changeNotes` 字段里填写本次更新说明。
- 在工作区文件夹里打开命令行窗口。
- 运行 `ModUploader.exe upload -w <你的文件夹名字>` 更新mod。mod ID 会自动从目录里的 `mod_id.txt` 读取。
- 建议是把上传的命令放在一个bat,cmd或者sh里，之后更新直接运行这个脚本就行。或者ci脚本自动化。

## 补充说明

- mod预览图不能大于1MB。否则无法上传。
- 建议描述、changelog等除了tag的直接删除，之后在工坊改就行。或者使用上传器mod（工坊订阅）。
- `dependencies` 写那个项目的工坊ID，不需要引号。上传器的README里有说明。
- tags无法在工坊改，先看有哪些常用tag。（常用：`Characters`,`QoL`, `Cards`, `Relics`, `schinese`(简体中文), `English`等）
- 不要忘了更改可见性。
- json里所有的设置都会覆盖工坊的内容，除非你不写或者删除。

版权声明：本文采用 [CC BY-NC-SA 4.0 CN](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans) 协议进行许可
本页目录

[English](/en/docs/11-upload-workshop/)
[GitHub](https://github.com/GlitchedReme/SlayTheSpire2ModdingTutorials)
