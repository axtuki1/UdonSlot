# Get Started

この文書では、UdonSlotで新しいスロットを作り始めるための最小構成を説明します。

まずは `MainBoard`、`SubBoard`、`ReelOutputController` を継承し、告知やボーナス状態を含まない基本遊技を動かします。演出、特殊制御、ボーナス、サンド連携などは、基本遊技が通ってから追加すると確認しやすくなります。

## 1. リビジョンを選ぶ

Runtimeの実装は `Runtime/<REV>/...` に配置されています。

現在のリビジョンは `REV1_0` です。新しく作るスロットでは、同じリビジョン内のRuntime、Editor、名前空間を揃えて使用してください。

```csharp
using AX.UdonSlot.REV1_0;
```

## 2. リールを用意する

3本のリール用GameObjectを作成し、それぞれに `ReelData` を追加します。

`ReelData.symbols` には、リールの図柄名を物理配列順に登録します。図柄名は `PayoutTable` の図柄組み合わせと完全一致で比較されます。

`ReelArray` を使う場合は、Projectビューで `Create > UdonSlot > ReelArray` と `Create > UdonSlot > ReelPicture` からアセットを作成できます。`ReelData` のInspectorに `ReelArray` を指定し、`ReelArrayからデータを生成` を押すと、`ReelPicture.pictureName` から `symbols` を生成できます。

## 3. 抽選表を作る

スロット本体とは別のGameObjectを作成し、`LotteryTable` を追加します。

最低限、次のフラグを登録します。

| 表示名 | 内部使用名 | ハズレ（余り） | 当選役 |
| --- | --- | --- | --- |
| ハズレ | None | 有効 | なし |
| ベル | Bell | 無効 | Bell |
| リプレイ | Replay | 無効 | Replay |

`最大確率分母` は、固定抽選領域の大きさです。たとえば `65536` にすると、抽選比率 `4096` は `4096 / 65536` として扱われます。

BET 1、BET 2、BET 3の抽選比率をそれぞれ設定します。ハズレフラグは比率を直接入力せず、他フラグの合計を差し引いた残りが自動的に割り当てられます。

内部使用名は同期値やコード上の判定に使います。あとから表示名を変えても挙動が変わらないよう、内部使用名は `Bell`、`Replay` のようなASCII名にしておくと扱いやすくなります。

## 4. 配当表を作る

別のGameObjectを作成し、`PayoutTable` を追加します。

`成立役` には、抽選表の当選役から参照する役を登録します。

| 表示名 | 内部使用名 | 図柄組み合わせ | 払出 |
| --- | --- | --- | --- |
| ベル | Bell | 左 `Bell` / 中 `Bell` / 右 `Bell` | 任意 |
| リプレイ | Replay | 左 `Replay` / 中 `Replay` / 右 `Replay` | リプレイとして扱う |

図柄組み合わせの空文字はANYとして扱われます。停止順を成立条件に含めない場合は、`停止順不問` を有効にします。

`有効ライン` には、BET数ごとに使うラインを登録します。各リールの表示段は `上段`、`中段`、`下段` から選びます。

## 5. 機種用クラスを作る

新しい機種では、次の3種類の派生クラスを作成します。

- `MainBoard` 派生: BET、レバー、リール開始/停止、ゲーム終了に機種固有処理を追加します。
- `SubBoard` 派生: 音、ランプ、映像、告知などのサブ側演出を追加します。
- `ReelOutputController` 派生: 必要に応じて抽選後処理や機種固有の滑り制御を実装します。PayoutTableによる自動滑り、成立役判定、払出、リプレイ判定は基底に実装されています。

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
- `rolePriority`: `SmallRoleFirst` または `BonusFirst`
- `priority`: `Payout` または `CombinationCount`
- `maxSlipCount`: 最大滑りコマ数。通常は `4`

通常役の自動滑りは基底だけで動作します。特定フラグや押し順だけ専用の停止テーブルにしたい場合は、あとから `ReelControl` と `ReelControlData` を追加します。

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

- 抽選後に状態を追加したい: `ReelOutputController.GenerateFlagLogic(int, string)` を使います。
- 抽選そのものを置き換えたい: `GenerateFlag` をオーバーライドします。
- 入力、回転開始、停止、ゲーム終了に処理を足したい: `MainBoard` の各Logicメソッドを使います。
- 音、ランプ、映像などの演出を足したい: `SubBoard` を継承します。

`Runtime/<REV>/Example/BasicReelOutputController.cs` は、基底機能だけを使う最小の空派生です。配布用の機種では、必要な機種固有処理だけを持つ派生クラスを作成してください。

基礎遊技の詳細は `Docs/BasicSlotFlow.md`、自動滑り制御の詳細は `Docs/ReelPriorityControl.md`、各クラスの役割は `Docs/ClassRoles.md` を参照してください。
