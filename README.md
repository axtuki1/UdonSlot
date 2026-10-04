# UdonSlot

VRChat world向けのパチスロ基盤パッケージです。

Current version: `1.0.0`

リール制御、抽選、配当、入力、表示、サウンド、貸出機など、パチスロワールドを構成するための基本部品をまとめています。

## Structure

`<REV>` は互換性を保つためのリビジョンフォルダです。現在のリビジョンは `REV1_0` です。

- `Runtime/<REV>/Slot`: メイン基板、サブ基板、リール制御、抽選テーブル、入力、表示、サウンド、プレイ履歴QR、追従補助などのスロット本体用部品
- `Runtime/<REV>/Slot/Mobile`: モバイル向け表示/フォーカス補助
- `Runtime/<REV>/Slot/WatchDog`: Udon 監視とエラー表示切り替え用の補助
- `Runtime/<REV>/Slot/Serialize`: リール配列、図柄、滑り定義の ScriptableObject
- `Runtime/<REV>/Sand`: カード、カード挿入口、サンド本体などの貸出機用部品
- `Runtime/<REV>/DataLamp`: データランプ用部品
- `Editor/Slot`: リールデータ、滑りテーブル、配当テーブルなどのスロット本体向けインスペクタ拡張
- `Samples~/BasicSlot`: 基本的な機種実装サンプル

## Namespace

既存の `AX.Slot` 実装と同じ Unity project 内で共存できるよう、package 版は `AX.UdonSlot.REV1_0` 名前空間に分離しています。

## Notes

- リール制御エディタの再設計・合意事項・実装進捗は `Docs/ReelControlEditorDesign.md` にまとめています。
- 新しいスロットを作り始める手順は `Docs/GetStarted.md` にまとめています。
- 各クラスの役割は `Docs/ClassRoles.md` にまとめています。
- 枚数優先・個数優先の自動リール制御は `Docs/ReelPriorityControl.md` にまとめています。
- ルールファイルから滑りテーブルを生成する「ルール制御」（試作）は `Docs/ReelRule.md` にまとめています。
- 基礎遊技フローの構成とInspector設定は `Docs/BasicSlotFlow.md` にまとめています。

## Version History

### 1.0.0

- 初期リリース。
