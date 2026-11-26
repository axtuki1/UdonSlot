# UdonSlot

VRChat World向けのパチスロ基盤パッケージです。

Current version: `1.0.0`

リール制御、抽選、配当、入力、表示、サウンド、貸出機など、パチスロワールドを構成するための基本部品をまとめています。

> [!NOTE]  
> 本README含め、このソースの大部分はClaude Code（初期はCodex）によって作成されています。  
> 文章として読み取りにくい箇所などが存在する場合があります。

## Requirements

- VRChat SDK - Worlds 3.10.4 以降
- [lilToon](https://github.com/lilxyzw/lilToon) 2.3.2 以降（リール照明シェーダー `UdonSlot/Reel/lilToon` が使います。VCC では lilToon のリポジトリを追加してから導入してください）

## Structure

`<REV>` は互換性を保つためのリビジョンフォルダです。現在のリビジョンは `REV1_0` です。

- `Runtime/<REV>/Slot`: メイン基板、サブ基板、リール制御、抽選テーブル、入力、表示、サウンド、追従補助などのスロット本体用部品
- `Runtime/<REV>/Slot/Mobile`: モバイル向け表示/フォーカス補助
- `Runtime/<REV>/Slot/WatchDog`: Udon 監視とエラー表示切り替え用の補助
- `Runtime/<REV>/Slot/Serialize`: リール配列、図柄、滑り定義の ScriptableObject
- `Runtime/<REV>/Sand`: カード、カード挿入口、サンド本体などの貸出機用部品
- `Runtime/<REV>/DataLamp`: データランプ用部品
- `Editor/Slot`: リールデータ、滑りテーブル、抽選表、配当表、ボーナス表などのインスペクタ拡張
- `Editor/ReelMaker`: リール配列と図柄画像からリールテクスチャを作るリール作成ツール（`UdonSlot > リール作成ツール`）
- `Editor/ReelRule`: 機種エディタ（`UdonSlot > 機種エディタ`）。抽選表・配当表・ボーナス/RT・リール制御をまとめて編集し、滑りテーブルを生成する
- `Samples~/BasicSlot`: 基本的な機種実装サンプル

## Namespace

既存の `AX.Slot` 実装と同じ Unity project 内で共存できるよう、package 版は `AX.UdonSlot.REV1_0` 名前空間に分離しています。

## Notes

- 新しいスロットを作り始める手順は `Docs/GetStarted.md` にまとめています。
- 各クラスの役割は `Docs/ClassRoles.md` にまとめています。
- 枚数優先・個数優先の自動リール制御は `Docs/ReelPriorityControl.md` にまとめています。
- 機種エディタ（出目の出し分けから滑りテーブルを生成、抽選表・配当表・ボーナス/RTの編集）は `Docs/ReelRule.md` にまとめています。
- 基礎遊技フローの構成とInspector設定は `Docs/BasicSlotFlow.md` にまとめています。
- リール上下照明シェーダーは `Docs/ReelLighting.md` にまとめています。

## ライセンス

UdonSlot は MIT License です。全文は `LICENSE` を確認してください。

リール照明シェーダーには lilToon（MIT License, © lilxyzw）を元にしたコードが含まれます。著作権表示とライセンス全文は `THIRD_PARTY_NOTICES.md` にあります。

### ワールドでのクレジット表記

UdonSlot を使ったワールドを公開するときは、ワールド内のクレジット・ライセンス表示に次の表記を入れてください。

```
UdonSlot © axtuki1 / MIT License
```

アップロードしたワールドには `LICENSE` ファイルが含まれないため、この表記で MIT License の著作権表示の条件を満たしたものとします。

リール照明シェーダーを使う場合は、lilToon の表記（例: `lilToon © lilxyzw / MIT License`）も入れてください。

## Version History

### 1.0.0

- 初期リリース。
