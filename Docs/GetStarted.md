# Get Started

この文書では、UdonSlotで新しいスロットを作り始めるための最小構成を説明します。

まずは `MainBoard`、`SubBoard`、`ReelOutputController` を継承し、ボーナスや告知を含まない基本遊技を動かします。出目の作り込み、ボーナス、演出、サンド連携などは、基本遊技が通ってから追加すると確認しやすくなります。

## 1. リビジョンを選ぶ

Runtimeの実装は `Runtime/<REV>/...` に配置されています。

現在のリビジョンは `REV1_0` です。新しく作るスロットでは、同じリビジョン内のRuntime、Editor、名前空間を揃えて使用してください。

```csharp
using AX.UdonSlot.REV1_0;
```

## 2. リールを用意する

3本のリール用GameObjectを作成し、それぞれに `ReelData` を追加します。

`ReelData.symbols` には、リールの図柄名を物理配列順に登録します。図柄名は `PayoutTable` の図柄組み合わせと完全一致で比較されます。

リール配列とリールテクスチャは `UdonSlot > ReelMaker` で作れます。`ReelArray` を使う場合は、Projectビューで `Create > UdonSlot > ReelArray` と `Create > UdonSlot > ReelPicture` からアセットを作成できます。`ReelData` のInspectorに `ReelArray` を指定し、`ReelArrayからデータを生成` を押すと、`ReelPicture.pictureName` から `symbols` を生成できます。

## 3. 抽選表と配当表を作る

スロット本体とは別のGameObjectに `LotteryTable` と `PayoutTable` を追加し、`ReelOutputController` 派生（手順5）の `lotteryTable`・`payoutTable` に設定します。中身は `UdonSlot > 機種エディタ` の③抽選表・④配当表で編集します（Inspectorでも編集できます）。

最低限、次のフラグと役を登録します。

| フラグ（内部名） | ハズレ（余り） | 当選役 |
| --- | --- | --- |
| NoWin | 有効 | なし |
| Bell | 無効 | Bell |
| Replay | 無効 | Replay |

| 役（内部名） | 種別 | 図柄の組合せ | 払出 |
| --- | --- | --- | --- |
| Bell | 小役 | `Bell` / `Bell` / `Bell` | 任意 |
| Replay | リプレイ | `Replay` / `Replay` / `Replay` | なし |

- `最大確率分母` は固定抽選領域の大きさです。`65536` なら、抽選比率 `4096` は `4096 / 65536` です。
- ハズレ（余り）のフラグには、他フラグの合計を差し引いた残りが自動で入ります。
- 内部名は同期値やコード上の判定に使います。表示名を変えても挙動が変わらないよう、`Bell`、`Replay` のようなASCII名にしてください。
- 抽選表の当選役名は、配当表の役の内部名と完全一致で照合します。
- 有効ラインはBETごとに登録します。

## 4. ボーナス・RTを作る（必要なら）

機種エディタの⑤ボーナス・RTで「ボーナス・RT表を作成」を押すと、`BonusTable` が作られ機種に設定されます。役物（BB・RBなど）と作動役、終了条件、RTを登録し、③抽選表でフラグの下にボーナス付きの行（例 `Cherry_RedBB`）を追加すると、そのフラグと同時にボーナスが成立します。

成立・作動・RTの管理は基底の `ReelOutputController` が行うので、機種側のコードは不要です。演出は `SubBoard` の `BonusWonLogic`・`BonusStartLogic`・`BonusEndLogic`・`ChildBonusStartLogic`・`ChildBonusEndLogic`・`StateChangeLogic` に書きます。

## 5. 機種用クラスを作る

新しい機種では、次の3種類の派生クラスを作成します。

- `MainBoard` 派生: BET、レバー、リール開始/停止、ゲーム終了に機種固有処理を追加します。
- `SubBoard` 派生: 音、ランプ、映像、告知などのサブ側演出を追加します。
- `ReelOutputController` 派生: 必要に応じて抽選後処理を実装します。抽選、自動滑り、ベイクした制御の選択、成立役判定、払出、リプレイ判定、ボーナス・RTは基底に実装されています。

`LotteryTable` と `PayoutTable` を使う最小の `ReelOutputController` 派生は、次のように作れます。

```csharp
using UdonSharp;

namespace AX.UdonSlot.REV1_0
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MyReelOutputController : ReelOutputController
    {
    }
}
```

## 6. 制御を接続する

スロット制御用GameObjectを作成し、作成した `MainBoard` 派生、`SubBoard` 派生、`ReelOutputController` 派生を追加します。

`MainBoard` には次を設定します。

- `reel1Transform`、`reel2Transform`、`reel3Transform`: 3本のリールTransform
- `reelOutputController`: 同じスロットで使う `ReelOutputController` 派生
- `subBoard`: 同じスロットで使う `SubBoard` 派生
- `requiredBet`: レバーONに必要なBET数
- `maxBet`: 最大BET数
- `pictureCount`: リール1周の図柄数

`ReelOutputController` 派生には次を設定します。

- `mainBoard`: 対象の `MainBoard`
- `lotteryTable`: 作成した `LotteryTable`
- `payoutTable`: 作成した `PayoutTable`
- `bonusTable`: ボーナス・RTを使う場合の `BonusTable`
- `rolePriority`: `SmallRoleFirst` または `BonusFirst`（ベイクした制御が無いときの自動滑りで使います）
- `priority`: `Payout` または `CombinationCount`（同上）
- `maxSlipCount`: 最大滑りコマ数。通常は `4`

通常役の自動滑りは基底だけで動作します。出目の出し分けやリーチ目を作るときは、機種エディタの①制御・②出目で設定して「ベイク」します。`ReelControl` と `ReelControlData` はベイク時に自動で作られます（詳しくは `Docs/ReelRule.md`）。

## 7. 入力を置く

レバー、MaxBET、停止ボタン用のGameObjectを配置し、必要な入力コンポーネントを追加します。

- `Lever`: `mainBoard` に対象の `MainBoard` を設定します。
- `MaxBet`: `mainBoard` に対象の `MainBoard` を設定します。
- `StopButton`: `mainBoard` と `stopReelIndex` を設定します。左、中、右はそれぞれ `0`、`1`、`2` です。

各入力は `Interact` で動作します。プレイヤー追従Colliderによる押下を使う場合は、接触側に `PlayerTracking` を設定します。

## 8. 表示と音を追加する

必要に応じて、表示や音の部品を追加します。

- `BetView`: `MainBoard.bet` に合わせてBET表示画像の透明度を切り替えます。
- `CreditView`: `MainBoard.credit` をTextMeshProへ表示します。
- `SoundBoard`: SE/BGMの再生やネットワーク再生を扱います。
- `SubBoard`: レバー、BET、停止、ゲーム終了などの通知を受け、演出やランプ制御を追加する基底です。

まずは入力、リール停止、払出が通ることを確認してから、表示や音を増やすと原因を切り分けやすくなります。

## 9. 動作確認する

Unity上で次の流れを確認します。

1. クレジットを付与する。
2. BETまたはMaxBETでBETを積む。
3. レバーでリールが回転する。
4. 左、中、右の停止ボタンでリールが止まる。
5. 全リール停止後に成立役が判定され、払出がクレジットへ加算される。
6. リプレイ成立時は、クレジットを消費せず前回BETが再装填される。

うまく動かない場合は、まず次を確認してください。

- 3本のリールTransformに `ReelData` が付いているか。
- `MainBoard.pictureCount` と `ReelData.symbols` の要素数が一致しているか。
- `LotteryTable` の当選役名と `PayoutTable` の内部使用名が一致しているか。
- `PayoutTable` に現在のBETで有効なラインがあるか。
- `ReelOutputController` 派生に `LotteryTable` と `PayoutTable` が設定されているか。

## 10. 機種固有処理を追加する

基本遊技が動いたら、必要に応じて派生クラスを作成します。

- 抽選後に処理を追加したい: `ReelOutputController.GenerateFlagLogic(int, string)` を使います。
- 抽選そのものを置き換えたい: `GenerateFlag` をオーバーライドします。
- 入力、回転開始、停止、ゲーム終了に処理を足したい: `MainBoard` の各Logicメソッドを使います。
- 音、ランプ、映像などの演出を足したい: `SubBoard` を継承します。

`Runtime/<REV>/Example/BasicReelOutputController.cs` は、基底機能だけを使う最小の空派生です。配布用の機種では、必要な機種固有処理だけを持つ派生クラスを作成してください。

機種エディタは `Docs/ReelRule.md`、基礎遊技の詳細は `Docs/BasicSlotFlow.md`、自動滑り制御の詳細は `Docs/ReelPriorityControl.md`、各クラスの役割は `Docs/ClassRoles.md` を参照してください。
