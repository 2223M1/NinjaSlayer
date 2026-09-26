# 由加乃拉箭播片 + 透明分镜框

按最终要求直接播放原片，不抠人物，原片背景保留。
选取该分镜结束前 36 帧，约 5:09.226–5:10.727（后者为下一镜头起点），原速 1.5015 秒。
胸部与拉箭手是画面中心，原片内容按 0.95 倍、偏移 (-430,-240) 放进左上角框。
取景中心比上一版再右移约 52.63px（相对初版累计 105.26px）；框的位置、比例、高度不变。

`preview-right-no-hold.mp4` 是带棋盘格的效果预览，包含 5 帧入场、36 帧原速播片、紧接 4 帧退场，共 45 帧 / 1.876875 秒。不添加暂停帧或尾部空白帧。
`yukano-original.ogv` 是 Godot 可直接播放的 Theora + Vorbis，1920×1080，24000/1001 fps。
`yukano-original.mp4` 是通用查看版。两者均保留原背景，不宣称视频自身透明。
`synced-original-audio.wav` 是同时间段的日语正片音轨（非评论音轨），48000 Hz 双声道，72072 个采样。
包含原片混音中的音效、对白、音乐，未进行声音分离。

## 游戏复用

`yukano_popup.gd` 挂到 Node2D，同目录资源整体复制：

```gdscript
popup.play()    # 本次交付默认：原速播放一次，无末帧暂停，结束后立即退场
popup.play(2.0) # 原速播放一次，末帧再停留 2 秒后退场
popup.close()   # 提前开始退场
```

进场 5 帧、退场 4 帧速度不变。停留不会重播视频或音效，也不慢放。
声音与画面在同一个 OGV 流中播放，避免两个播放器各自计时造成漂移。
`interior-clip.png`、`clip.gdshader` 只裁掉框外内容，不做人物逐帧 Alpha 分割。
`runtime-atlas.png`、`animation.json` 保留原生框线位置。
`last-frame.png` 仅在显式指定额外停留时用于冻结画面，默认不显示；原片音轨仅播放一次。
未绑定游戏行为，未写入实际游戏安装。

独立测试：Godot 4.5.1 `--headless --path <此目录> --editor --import --quit`，
然后 `--headless --path <此目录> --script res://test_yukano_player.gd`。
