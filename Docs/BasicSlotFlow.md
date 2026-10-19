# Basic Slot Flow

`MainBoard`、`SubBoard`、`ReelOutputController`を継承すると、基礎遊技を構成できます。ボーナス・RTは`BonusTable`を設定すると基底が扱います（`Docs/ReelRule.md`の「⑤ ボーナス・RT」）。

## Flow

1. BETまたはMaxBETでクレジットをBETへ移します。
2. レバー入力で`LotteryTable`からフラグを抽選します。
3. MainBoardが3リールを回転させます。
4. 各停止ボタンの入力時に、ベイクした制御テーブル（ReelControlData）またはPayoutTableの優先制御で滑りコマ数を決定します。
5. 全リール停止後、PayoutTableと有効ラインから成立役を判定します。
6. 成立役の配当合計をクレジットへ加算します。
7. `BonusTable`があれば、ボーナスの作動・終了とRTの移行を行います（`UpdateGameState`）。

告知や演出はこの基礎フローに含めません。リプレイ成立時のBET再装填とロックは基礎フローに含み、機種固有の再遊技音や表示はMainBoard、SubBoard派生クラスで追加します。

## Components

### MainBoard

- 3本のReel Transformを設定します。
- `reelOutputController`へ機種固有ReelOutputControllerを設定します。
- `subBoard`へ機種固有SubBoardを設定します。
- 必要BET、最大BET、リール速度などを設定します。

### ReelOutputController派生

- `reelDatas`: 左、中、右のReelDataを設定します。
- `mainBoard`: 対象MainBoardを設定します。
- `lotteryTable`: フラグ抽選表を設定します。
- `bonusTable`: ボーナス・RTを使う場合に設定します。
- `payoutTable`: 成立役・配当・有効ライン表を設定します。
- `rolePriority`: 小役優先またはボーナス優先を選択します。
- `priority`: 枚数優先または個数優先を選択します。
- `maxSlipCount`: 最大滑りコマ数を0〜4で設定します。

`lotteryTable`、`payoutTable`、`activeFlag`は基底のReelOutputControllerが保持します。MainBoardからレバーON時に`GenerateFlag`が呼ばれると、LotteryTableによる抽選、内部使用名の保存、同期、`GenerateFlagLogic`の順で実行されます。

全リール停止後に`JudgePayout`が呼ばれると、基底が現在BETの有効ライン、停止順、実際に表示されている図柄を照合して成立役の内部使用名を返します。続く`GetPayout`と`IsReplay`も、その成立結果をPayoutTableから判定します。LotteryTableの当選役は停止制御の候補にだけ使用し、成立役判定と払出には使用しません。機種固有Controllerでこれらをオーバーライドした場合は、機種固有実装が優先されます。

`rolePriority`、`priority`、`maxSlipCount`も基底が保持します。ReelControlの明示テーブルを使用できない停止では、LotteryTableの当選役とPayoutTableから0〜`maxSlipCount`コマの滑りを自動算出します。まず`rolePriority`の優先役種を引き込める停止候補を選び、その候補内を`priority`の枚数または個数で評価します。

### LotteryTable

`最大確率分母`を設定し、各フラグへ表示名、内部使用名、BET 1〜3それぞれの抽選比率、0個以上の当選役を登録します。各フラグには、BETごとの実確率がパーセントと`1/N`で表示されます。

例:

最大確率分母を`65536`にした場合:

| 表示名 | 内部使用名 | BET 1 | BET 2 | BET 3 | 当選役 |
| --- | --- | ---: | ---: | ---: | --- |
| ハズレ | None | 自動 | 自動 | 自動 | なし |
| ベル | Bell | 4096 | 6144 | 8192 | Bell |
| リプレイ | Replay | 4096 | 4096 | 4096 | Replay |
| ベル＋チェリー | BellCherry | 0 | 512 | 1024 | Bell、Cherry |

ハズレにするフラグの`ハズレ（余り）`を有効にします。ハズレの抽選比率は入力せず、BETごとに最大確率分母から他フラグの抽選比率合計を引いた残りが自動的に割り当てられます。

通常フラグの抽選比率合計が最大確率分母を超えると、該当BETでは後ろのフラグから抽選領域が不足します。InspectorにBETを示したエラーが表示されるため、合計が最大確率分母以下になるよう設定してください。0以下の通常フラグは抽選対象外です。

対象BETの通常フラグ比率がすべて0の場合は、抽選結果がすべてハズレになります。`GenerateFlag`へBET 1〜3以外が渡された場合もハズレです。規定BET枚数の指定や、現在のBETが遊技可能かどうかの判定はLotteryTableでは行いません。

既存の抽選比率はBET 3として保持されます。新しく追加されたBET 1、BET 2は初期値0のため、必要な比率をInspectorで設定してください。

ハズレを指定していない既存データは、従来どおり全項目の合計に対する相対比率で抽選されます。

`表示名`はInspectorや画面表示用で、日本語を使用できます。`内部使用名`は`activeFlag`、同期、ReelControlとの照合、機種固有コードで使用します。内部使用名はASCIIを推奨しますが、入力自体は制限しません。表示名が空の場合は内部使用名を表示名として扱います。

内部使用名は抽選結果を識別する名前で、成立役名とは別です。1つのフラグへ複数の当選役を追加すると、複数役の同時当選になります。当選役名はPayoutTableの成立役名と完全に一致させます。

`LotteryTable.GetFlagDisplayName`へ内部使用名を渡すと表示名を取得できます。ReelOutputController派生では`GetActiveFlagDisplayName`から現在の表示名を取得できます。

### PayoutTable

成立役ごとに次を設定します。

- `表示名`: Inspectorや画面表示に使用する名前。日本語を使用できます。
- `内部使用名`: LotteryTableとの照合、成立判定結果、機種固有コードで使用する名前。
- `役種別`: 小役、リプレイ、ボーナスから選択します。リプレイはメダル払出を0枚として扱い、BET別払出枚数を非表示にします。
- `1 BET / 2 BET / 3 BET 払出枚数`: 各BETで1ライン成立したときの払出枚数。
- `図柄組み合わせ`: 左、中、右の図柄名。空文字はANY、半角カンマ区切りはOR。
- `停止順不問`: 無効時は左、中、右の必要停止順を指定。

既存の単一配当データは3 BET払出枚数として保持されます。BET 1、BET 2のデータがまだ存在しない既存アセットでは、従来の結果を維持するため3 BET払出枚数を使用し、Inspectorを開いた時点で同じ値がBET 1、BET 2へ設定されます。

フラグとの対応はLotteryTable側で設定します。LotteryTableの当選役名にはPayoutTableの内部使用名を指定してください。同じ内部使用名を複数行へ使用する場合、BET別払出枚数は同じ値に統一してください。

リプレイ設定された役が成立すると、メダル払出は0枚となり、MainBoardが前回BETをクレジット消費なしで再装填します。`MainBoard.isReplay`は次のリール回転開始までtrueになり、その間は通常BETとMaxBETを受け付けません。

遊技終了時、`MainBoard.lastPayLineSymbols`には現在BETで有効な全ラインの停止図柄名が同期保存されます。配列はPayoutTableの有効ライン登録順で、各ラインを`左, 中, 右`の3要素単位で並べます。たとえば2ラインが有効なら、要素0〜2が1本目、要素3〜5が2本目の結果です。成立役の有無にかかわらず、有効ライン上に実際に停止した図柄を格納します。

図柄欄へ`Bell,RedBell,BlueBell`と入力すると、そのリールは3図柄のいずれかで成立します。候補前後の半角・全角スペースは無視されます。このOR条件は成立役判定と自動滑り制御の両方に使用されます。

BETをクレジットへ戻す機種固有処理では、`MainBoard.CanReturnBetToCredit()`がtrueの場合だけ返却してください。リプレイ再装填中はfalseとなるため、再遊技BETをクレジットへ変換できません。

有効ラインごとに次を設定します。

- 1BET、2BET、3BETのどれで有効か。各項目は縦に表示され、任意の組み合わせを選択できます。
- 左、中、右リールの表示段。上段、中段、下段から選択。

## Control Selection

`ReelControl`に対象フラグの明示停止テーブルがあり、現在の停止順パターンで`テーブル制御を有効`にしている場合は、その滑りテーブルを優先します。

明示テーブルがない場合、対象フラグのReelControlDataがない場合、または対象パターンでテーブル制御が無効の場合は、基底`DynamicControl`がPayoutTableから滑りを自動算出します。機種固有Controllerで`DynamicControl`をオーバーライドした場合は、その実装が優先されます。

自動算出はLotteryTableで当選中の役を引き込みつつ、今回の停止で非当選役が確定する位置を可能な範囲で避けます。`左リール指定 / ANY / ANY`のような1リール役も、指定リールの停止時点で成立する役として蹴り判定の対象になります。

このため、通常役は自動制御、特殊停止・リーチ目・押し順専用出目だけ明示テーブル、という混在が可能です。

## Migration

旧方式でPayoutTable側に対応フラグを保存している場合、LotteryTableの当選役が1件も登録されていない間は旧設定を使用します。

LotteryTableへ当選役を1件以上追加すると、すべてのフラグと役の対応が新方式へ切り替わります。切り替える際は、ハズレを含む全フラグについて当選役を確認してください。ハズレフラグの当選役は空のままにし、`ハズレ（余り）`を有効にします。

## Machine-Specific Extensions

- `ReelOutputController.GenerateFlagLogic(int, string)`: 共通抽選直後の機種固有処理。第2引数に抽選された内部使用名が渡されます。
- `ReelOutputController.GenerateFlagLogic(int)`: 既存派生クラスとの互換用フック。
- `MainBoard`の各Logicメソッド: 入力、回転開始、停止、ゲーム終了の追加処理。
- `MainBoard.ReplayBetLogic(int)`: リプレイBET再装填直後の機種固有処理。
- `SubBoard`の各Logicメソッド: 告知、ランプ、映像、サウンドなどの演出。
- `SubBoard.ReplayBetLogic(int)`: 全プレイヤーへ通知される再遊技音・表示用の拡張ポイント。

ボーナス・RTは`BonusTable`で定義し、成立・作動・終了・RT移行は基底が同期します。演出は`SubBoard`の`BonusWonLogic`（成立、レバーオン時）・`BonusStartLogic`・`BonusEndLogic`・`ChildBonusStartLogic`／`ChildBonusEndLogic`（BB・MB中の連続作動の役物、JACゲームなど）・`StateChangeLogic`で追加します。これらは所有者が判定し、全員のSubBoardで呼ばれます。共通の抽選後に処理を追加する場合は`GenerateFlagLogic`を使用し、抽選そのものを置き換える場合だけ`GenerateFlag`をオーバーライドします。
