# リール上下照明

`UdonSlot/Reel/lilToon` は lilToon の発光2ndに上下別のHDRカラー倍率を追加します。
発光1st、発光2ndのテクスチャ・マスク・合成モード・点滅はそのまま使えます。
公式テンプレートの拡張方法: https://lilxyzw.github.io/lilToon/ja_JP/dev/custom_shader.html

## マテリアル

リールのマテリアルを選択し、`Assets > UdonSlot > Convert to Reel Light Shader` を実行します。
Shader欄は `UdonSlot/Reel/lilToon` です。不透明・輪郭線なし専用です。
変換メニューは通常の `lilToon` マテリアルのみを対象とします。
透過、Cutout、輪郭線、Lite、Multi、テッセレーション、Fur、屈折などの派生版は同梱しません。
発光2ndを有効にし、既存の白黒マスクを設定してください。
既存の `ReelShadow_Mask` と Tiling `(1, 7)` をそのまま使用できます。

「リール上下照明」の上側・下側の色は、発光2ndの色に掛ける倍率です。
両方白なら元の描画になります。HDR値で明るさを上げられます。
色のAlphaは使用せず、不透明度はlilToonの発光2nd側で設定します。
黒は対象側の発光色を0にします。加算では光が消え、乗算ではその領域が黒くなります。

回転補正後のマスクUVを1周期ごとに分け、Yが境界以上を上側、未満を下側として着色します。
境界の既定値は0.5です。モデル上の上下と逆の場合は「上下を入れ替える」を有効にします。
マスク形状の変更やRGマスクへの変換は不要です。境界は黒い領域内に配置してください。

## スクリプト

`AX.UdonSlot.REV1_0.SubBoard`（派生クラスを含む）から呼び出せます。
リール番号は左=0、中央=1、右=2です。

```csharp
subBoard.SetReelLightColors(0, Color.red, Color.blue);
subBoard.SetReelUpperLightColor(1, Color.yellow);
subBoard.SetReelLowerLightColor(2, Color.cyan * 2f);
subBoard.SetAllReelLightColors(Color.white, Color.white);
```

各リールのマテリアルは実行時に独立します。色変更は即時反映され、StartLogicからも呼び出せます。
不正なリール番号、参照未設定、標準lilToonマテリアルへの色変更は無視します。
Offset追従を無効にしても色APIは利用できます。
リールRendererとマテリアルは初期化時に取得するため、実行途中の差し替えには対応しません。

色はローカル演出です。全プレイヤーで一致させる場合は、同期された演出状態や既存のネットワークイベントから
各クライアントで同じAPIを呼び出してください。フレームごとの色同期は行いません。

## 図柄照明：表示窓の9箇所

表示窓の上・中・下×3リールを制御します。図柄の種類やリール帯の図柄番号を指定するAPIではありません。
リール番号は左=0、中央=1、右=2、段番号は上=0、中=1、下=2です。

```csharp
// 左リール上段だけ消灯
subBoard.SetReelSymbolLight(0, 0, false);

// 中央リールの中段を50%の発光量にする（0～1）
subBoard.SetReelSymbolBrightness(1, 1, 0.5f);

// 右リール：上段ON、中段OFF、下段ON
subBoard.SetReelSymbolLights(2, true, false, true);

// 通常点灯へ戻す
subBoard.SetAllReelSymbolLights(true);
```

図柄バックライト（発光1st）のみを制御します。0でバックライトの寄与をなくし、1で従来の描画に戻します。
消灯しても通常の陰影で描画された図柄は残ります。リール内の上下照明（発光2nd）は点灯状態を維持します。
`SetReelSymbol*` はバックライト、`SetReelLightColors` / `SetReelUpperLightColor` / `SetReelLowerLightColor` は上下照明に作用します。
発光2ndを乗算で合成している場合、最終的な見た目の明るさは下地にも依存しますが、バックライトの点滅値は発光2ndに掛けません。
不正なリール・段番号は無視し、発光量は0～1に制限します。

### 点灯パターンと点滅

9ビットのパターンで9箇所を一括指定できます。bit0～2が左の上中下、bit3～5が中央の上中下、
bit6～8が右の上中下です。1が点灯、0が消灯です。

```csharp
// V字：左の上中、中央の下、右の上中だけ点灯
subBoard.SetReelSymbolLightPattern(0b011_100_011);

// 中段ラインだけ点灯
subBoard.SetReelSymbolLightPattern(0b010_010_010);

// 全消灯 / 全点灯
subBoard.SetAllReelSymbolLights(false);
subBoard.SetAllReelSymbolLights(true);
```

点滅の時間制御は機種固有の演出側で行います。例えばUpdateLogic内で、演出中だけ以下を実行すれば
中段ラインが0.2秒ごとに点滅します。他の段の状態は変えません。

```csharp
bool on = Mathf.Repeat(Time.time, 0.4f) < 0.2f;
SetReelSymbolLight(0, 1, on);
SetReelSymbolLight(1, 1, on);
SetReelSymbolLight(2, 1, on);
```

演出終了時に `SetAllReelSymbolLights(true)` を呼んで戻してください。
新しいゲーム開始時の自動リセットやネットワーク同期は行いません。

### 表示位置

UV0のY=1側から上段・中段・下段の順に配置された既存リールモデルを基準にしています。
「図柄バックライト（発光1st）」で上・中・下の発光量と表示窓の位置（UV）を確認・調整できます。
図柄数は実行時にMainBoard.pictureCountから取得します。1周のUVを使うため、20図柄・21図柄に対応します。
表示窓の位置は初期マテリアルの値を基準に毎フレーム補正され、回転中・停止時の揺れにも追従します。
発光2ndマスクのTilingや追従の有効/無効とは独立しています。
窓の外に当たるリール帯は通常点灯です。実行中の調整にはInspectorではなく上記APIを使用してください。

## ASLOT-1のVフラッシュデモ

`ASLOT1SubBoard` は通常ゲーム終了時、当選フラグが `Cherry` の場合にVフラッシュを開始します。
入賞図柄の判定ではなく当選フラグを使います。疑似遊技の終了時には再生しません。

V字点灯→全消灯の2ステップを、次ゲームのレバー操作まで繰り返します。
既定では各ステップ0.12秒、1周期0.24秒です。
Inspectorの `Enable Cherry V Flash Demo` で有効/無効、`V Flash Step Seconds` で各ステップの時間を設定できます。
点灯配列は `ASLOT1SubBoard.vFlashPatterns` にあり、LateUpdateLogicから経過時間に合うパターンを反映します。

当選フラグの判定と開始通知はMainBoardの所有者が行い、開始サーバ時刻を全員に通知します。
次のレバー操作時に全員で終了して全点灯へ戻ります。上下の照明色は変更しません。

## 保守

Shaderフォルダに残すシェーダー定義は次の2つです。

| ファイル | 用途 |
| --- | --- |
| `lts.lilcontainer` | マテリアルで選択する不透明・輪郭線なしシェーダー |
| `ltspass_opaque.lilcontainer` | 上記から参照する描画本体。影・追加光・ベイク用のパスも含むため削除不可 |

`custom*.hlsl`、`lilCustomShader*.lilblock`、`Editor`も機能の実装とInspectorに必要です。
Inspectorの描画モード切替は無効にしています。新しいバリエーションが必要な場合は
テンプレートから対応するコンテナと描画本体を戻し、Inspectorの切替先も追加してください。

`custom_insert.hlsl` は導入済みlilToonの `lilEmission2nd` を基に、色倍率のみ追加した実装です。
lilToon更新時は元関数との差分を確認してください。lilToon本体のファイルは変更しません。
