# Class Roles

UdonSlot package 内の各クラスの役割メモです。

## Runtime/REV1_0/Slot

### Core

- `MainBoard`: スロット遊技状態の中心。クレジット、BET、リプレイBET再装填と返却ロック、レバー、リール回転/停止、停止精度、押し順、払い出しなどを同期管理するメイン基板。
- `SubBoard`: メイン基板の状態を受けて、演出、音、停止ボタン発光、リプレイ再装填通知などのサブ側処理を動かす基底。
- `ReelOutputController`: LotteryTableによる共通フラグ抽選と同期、PayoutTableによる標準成立役判定・BET別払出・リプレイ判定、リール制御、BonusTableによるボーナス成立・作動・終了とRT移行を担う基底。

### Reel/Data

- `ReelData`: 各リールの図柄配列データ。
- `ReelControl`: 制御名と `ReelControlData` の対応、および機種エディタが書き出す割り当て表（フラグ × 成立中のボーナス → 制御名）を保持するリール制御テーブルの入口。
- `ReelControlData`: 押し順パターンごとの滑りテーブル。
- `LotteryTable`: 最大確率分母、フラグの表示名と内部使用名、BET 1〜3別の抽選比率、余りを受け取るハズレ、フラグごとの複数の当選役を保持する抽選テーブル。ボーナス付きの行（例 `Cherry_RedBB`）で、フラグと同時にボーナスを成立させる。設定ごとの比率を持つ。
- `PayoutTable`: 成立役の表示名と内部使用名、小役・リプレイ・ボーナス・移行図柄の役種別、ANY・カンマ区切りOR対応の図柄、BET別払出枚数、停止順条件、BET別の有効ラインを保持するInspector編集可能な配当表。
- `BonusTable`: 役物（SB・RB・CB・BB・MB）の作動役・作動中の抽選表・終了条件・規定BETと、RT状態・図柄表示による移行を定義する表。状態の保持は `ReelOutputController` が行う。
- `WinPair`: 成立役の図柄組み合わせ定義。
- `Serialize/ReelArray`: リール配列を定義する ScriptableObject。
- `Serialize/ReelPicture`: 図柄名と図柄画像を定義する ScriptableObject。
- `Serialize/StopThrough`: リールの滑りコマ数を定義する ScriptableObject。

### Input

- `Lever`: レバー入力を `MainBoard` に渡す。
- `StopButton`: 停止ボタン入力を `MainBoard` に渡す。
- `MaxBet`: MaxBet 入力を `MainBoard` に渡す。
- `CenterButton`: 中央ボタン入力を `MainBoard` に渡す。
- `ButtonPush`: 任意 Udon の任意メソッドを呼ぶ汎用ボタン。
- `MakeCredit`: デバッグ/簡易用途のクレジット付与。

### View/Utility

- `BetView`: BET 表示。
- `CreditView`: クレジット、払い出し等のセグメント表示。
- `SoundBoard`: SE/BGM の再生、停止、遅延再生、ネットワーク再生管理。
- `PlayerTracking`: 指定ボーンへオブジェクトを追従させる補助。
- `DebugTextView`: 対象 Transform 名を表示するデバッグ補助。現状は仮置きに近い。
- `CameraRenderManager`: 現状は空。名前上はカメラ/RenderTexture 管理予定。
- `AutoCanvasApply`: Canvas/RenderTexture 周りの自動適用補助。

### Mobile

- `Mobile/FocusViewSupport`: モバイル向け表示/フォーカス補助。

### WatchDog

- `WatchDog/SlotWatchDog`: Udon がエラーによって停止していないか監視し、エラー画面へ切り替える。
- `WatchDog/CallUdon`: ローカル/ネットワークで任意 Udon メソッドを呼ぶ汎用インタラクト。

## Runtime/REV1_0/Sand

- `Sand`: 貸出機本体。残高、貯メダル、手持ちメダル、カード排出/投入、クレジット投入を管理する。
- `Card`: カードに残高、貯メダル、手持ちメダル、会員種別を保持する。
- `CardInsertPoint`: カードが挿入口に入った時に `Sand.InsertCard` へ渡す。

## Runtime/REV1_0/DataLamp

- `DataView`: データランプ枠。今後、回転数、差枚、BB/RB、履歴などを集約していく候補。

## Runtime/REV1_0/Example

- `BasicReelOutputController`: 基底だけで共通抽選、自動滑り、成立判定、払出が動くことを示す空のサンプル派生。実際の機種では`ReelOutputController`を継承して専用Controllerを作成する。

## Editor/ReelRule

- `ReelRuleEditorWindow`: 機種エディタ（`UdonSlot > 機種エディタ`）。制御・出目・抽選表・配当表・ボーナス/RTを編集し、滑りテーブルを生成してベイクする。
- `ReelDesignAsset`: 出目と制御の設計を保存するアセット。
- `ReelMachineReader`: シーンの機種から配列・配当表・抽選表・ボーナス表を読む。
- `RtFlowWindow`: RTの移行を図で表示する。
- `Core`: 滑りテーブルを生成する Unity 非依存のエンジン。

## Editor/ReelMaker

- `ReelMaker`: リール配列と図柄画像からリールテクスチャを作る（`UdonSlot > ReelMaker`）。

## Editor/Slot

- `LotteryTableEditor`: `LotteryTable` の固定分母、表示名、内部使用名、BET別抽選比率、ハズレ、複数の当選役を編集し、BET別の実確率を表示する Inspector。
- `PayoutTableEditor`: `PayoutTable` の成立役表示名・内部使用名、リプレイ属性、BET別払出枚数、停止順、BET別有効ラインを行単位で編集・検証する Inspector。
- `BonusTableEditor`: `BonusTable` の Inspector。
- `ReelControllDataEditor`: `ReelControlData` の滑りテーブル編集用 Inspector。
- `ReelDataEditor`: `ReelArray` から `ReelData` を生成する補助 Inspector。
- `WinPairEditor`: `WinPair` の図柄組み合わせ編集用 Inspector。

## Samples~/BasicSlot

- `SampleController`: `ReelOutputController` のサンプル実装。抽選、動的リール制御、成立役判定、配当計算を持つ。
- `SampleSubController`: `SubBoard` のサンプル継承。
- `SlotSetting`: サンプル機種の設定差/抽選範囲定義。
- `DebugWindow`: サンプル機種の内部状態表示。
- `SlotSettingEditor`: `SlotSetting` 用 Inspector。
