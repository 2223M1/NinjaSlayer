# 数据分享与隐私

[English](#english) · [日本語](#日本語)

## 对局统计

我会用玩家分享的数据调整卡牌平衡。对局结束后，模组会发送路线、选择、牌组、结果与战斗统计。放弃的对局，以及开启过自由操控的整局，不计入统计。不会补传过去的对局。

首次提示出现时，本次启动暂不自动上传。选择“现在开启”会立即启用；直接关闭或忽略提示，下次启动游戏后会默认开启。选择“关闭分享”则持续停用，之后可在模组设置中更改。已经拒绝过分享的选择会保留。

汇总分析使用 PostHog，上传记录包含用于关联对局和玩家的标识。[忍杀情报站](https://2223m1.github.io/NinjaSlayer/)只显示汇总结果，不公开这些标识、种子、截图或日志。服务不会保存或转发原始 IP；防滥用只使用不可逆的摘要。

## 公开完整战报

这是单独的开关，默认关闭。开启后从后续房间开始记录；在战斗中开启，则从下一场战斗开始。之前未记录的部分会注明，关闭后不会提交当前仍在记录的战报。

公开内容包括路线、选择、卡牌和逐回合行动，不含账号、玩家自填姓名、种子、文件路径、截图或日志。联机只公开你授权的详细记录，其他玩家未提供的部分会注明。

战报先保存在本机，对局结束后排队上传。排队不代表上传成功，可以到网页确认是否收录。本机和服务器上的完整战报最多保留90天，空间不足时可能提前清理。汇总统计单独保留。关闭开关不会自动撤回已经公开的战报。

## F2 反馈

本机档案首次使用忍者杀手通关标准模式后，会打开一次反馈框；已有通关记录不补弹。打开不会发送，关闭后不再自动提示，也不改变统计分享开关。公开反馈会显示解决状态、作者回应及更新时间；这些信息与反馈一同到期或删除。

发送前会再次确认。反馈正文、分类、时间及游戏和模组版本会公开，请勿填写个人隐私。截图和游戏日志只供 Mod 作者查看，不会公开，也不会发送给 MegaCrit。日志可能包含设备、存档和已安装 Mod 的信息。

反馈最多保留180天，空间不足时可能提前清理。删除或到期后，网页会在下次成功更新时移除相应条目。已经被他人下载的公开反馈或战报无法收回。

有疑问可以通过 [GitHub](https://github.com/2223M1/NinjaSlayer/issues)联系我，请勿在公开留言中附上隐私信息。

## English

### Run statistics

I use shared run data to balance the cards. When a run ends, the mod sends its route, choices, deck, result and combat statistics. Abandoned runs and any run that used free control are excluded. Past runs are not uploaded later.

When the first notice appears, nothing is sent automatically during that session. “Enable now” starts sharing immediately. Closing or ignoring the notice enables sharing on the next launch. “Disable sharing” keeps it off. You can change this in the mod settings. Earlier refusals remain respected.

Aggregate analysis uses PostHog. Uploaded records include identifiers that link runs and players. [Ninja Slayer Intel](https://2223m1.github.io/NinjaSlayer/?lang=eng) shows aggregates, not those identifiers, seeds, screenshots or logs. The service does not store or forward raw IP addresses; abuse prevention uses a non-reversible digest.

### Public battle reports

This separate option is off by default. It records subsequent rooms; enabling it during combat starts capture with the next combat. Missing sections are marked. Turning it off stops the active report from being submitted.

Public reports include routes, choices, cards and turn-by-turn actions. They exclude account IDs, player-entered names, seeds, file paths, screenshots and logs. In multiplayer, only the details you authorize are published. Uncollected sections for other players are marked.

Reports are saved locally and queued when the run ends. Queued does not mean received; check the site for confirmation. Local and server copies are kept for up to 90 days and may be removed earlier if storage is low. Aggregate statistics are kept separately. Switching off does not automatically withdraw reports already published.

### F2 feedback

The first standard-mode Ninja Slayer victory on a local profile opens the feedback form once. Profiles with an earlier victory are not prompted. Opening sends nothing, dismissing prevents another automatic prompt, and statistics consent is unchanged. Public feedback shows its resolution status, author response and update time; these expire or are deleted with the submission.

A confirmation appears before sending. The message, category, time, and game and mod versions will be public. Do not include personal information. Screenshots and game logs are available only to the mod author, not the public or MegaCrit. Logs may include device, save and installed-mod information.

Feedback is kept for up to 180 days and may be removed earlier if storage is low. Deleted or expired entries disappear from the site after its next successful update. Public feedback or reports that others have already downloaded cannot be recalled.

Contact me on [GitHub](https://github.com/2223M1/NinjaSlayer/issues) with questions. Do not post private information in public issues.

## 日本語

### プレイ統計

共有されたデータをカードのバランス調整に使います。冒険の終了後、ルート、選択、デッキ、結果、戦闘の統計を送信します。途中で放棄した冒険と、自由操作を一度でも使った冒険は集計しません。過去の冒険を後から送信することもありません。

初回の案内が出た時点では、その起動中に自動送信しません。「今すぐ有効にする」で共有を開始します。案内を閉じるか無視した場合、次回の起動から自動で有効になります。「共有しない」を選ぶと無効のまま維持されます。後からMod設定で変更でき、以前の拒否も維持されます。

集計にはPostHogを使います。送信する記録には、冒険とプレイヤーを関連付ける識別子が含まれます。[ニンジャ情報局](https://2223m1.github.io/NinjaSlayer/?lang=jpn)に表示するのは集計結果だけです。識別子、シード、画像、ログは公開しません。サービスは生のIPアドレスを保存・転送せず、不正利用対策には元に戻せない要約値だけを使います。

### 詳細なプレイ記録の公開

独立した設定で、初期状態は無効です。有効にした後の部屋から記録します。戦闘中に有効にした場合は次の戦闘から始まります。未記録の区間は明示し、無効にすると記録中のレポートは送信しません。

ルート、選択、カード、ターンごとの行動を公開します。アカウント、プレイヤーが入力した名前、シード、ファイルパス、画像、ログは含みません。マルチプレイでは自分が許可した詳細だけを公開し、他のプレイヤーについて記録のない部分は明示します。

記録は端末に保存し、冒険終了後に送信待ちになります。送信待ちは受信完了を意味しません。サイトへの掲載を確認してください。端末とサーバーの記録は最長90日間保存し、容量不足の際は早く削除する場合があります。集計結果は別に保持します。設定を無効にしても、公開済みの記録は自動では取り下げられません。

### F2フィードバック

ローカルプロフィールでニンジャスレイヤーの通常モードを初めてクリアすると、一度だけフォームを開きます。過去にクリア済みの場合は表示しません。開くだけでは送信されず、閉じると自動表示は繰り返しません。統計共有の設定も変わりません。公開された投稿には対応状況、作者の返信、更新日時を表示し、投稿と同時に期限切れ・削除となります。

送信前に確認画面が出ます。本文、分類、日時、ゲームとModのバージョンは公開されます。個人情報を書かないでください。スクリーンショットとログを確認できるのはMod作者だけで、公開もMegaCritへの送信もしません。ログには端末、セーブ、導入済みModの情報が含まれる場合があります。

フィードバックは最長180日間保存し、容量不足の際は早く削除する場合があります。削除や期限切れの項目は、次にサイト更新が成功した時点で消えます。他の人がダウンロード済みの公開フィードバックやプレイ記録は回収できません。

ご質問は[GitHub](https://github.com/2223M1/NinjaSlayer/issues)へお願いします。公開の投稿に個人情報を添付しないでください。
