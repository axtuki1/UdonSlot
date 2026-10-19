using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    // ⑤ 役物の種別のヘルプ
    public sealed partial class ReelRuleEditorWindow
    {
        private sealed class BonusKindHelpPopup : PopupWindowContent
        {
            private readonly Regulation regulation;
            private Vector2 scroll;

            public BonusKindHelpPopup(Regulation regulation)
            {
                this.regulation = regulation;
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(460, 520);
            }

            public override void OnGUI(Rect rect)
            {
                var title = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
                var body = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
                string gen = RegulationLimits.Names[(int)regulation];

                scroll = GUILayout.BeginScrollView(scroll);

                Section(title, body, "BB（ビッグボーナス）",
                    "作動中はRBを連続で作動させる大きいボーナス。払出が規定枚数を超えたら終了（" + gen + "は " +
                    RegulationLimits.BigPayout(regulation) + " 枚以下で設定）。「連続作動」にRBを選ぶ。",
                    "6号機Aタイプの多くのBIG");
                Section(title, body, "RB（レギュラーボーナス）",
                    "作動中は作動中の抽選表で小役が揃いやすくなる。" + RegulationLimits.RegularGames + " 遊技か " +
                    RegulationLimits.RegularWins + " 回入賞で終了。単独でも、BBの中身としても使う。",
                    "6号機のドンちゃん2・ライトニング・新ハナビのREG");
                Section(title, body, "MB（ミドルボーナス）",
                    "作動中はCBを連続で作動させる、BBより小さいボーナス。払出が規定枚数を超えたら終了（" + gen + "は " +
                    RegulationLimits.MiddlePayout(regulation) + " 枚以下で設定）。「連続作動」にCBを選ぶ。",
                    "1回で少しだけ増えるボーナスや、純増の小さいボーナスを作りたいとき");
                Section(title, body, "CB（チャレンジボーナス）",
                    "1遊技だけ作動。作動中の抽選表で小役をすべて当選にして、どれでも揃えられるようにする使い方が多い。単独ではなく MB の中身として使うのが普通。",
                    null);
                Section(title, body, "SB（シングルボーナス）",
                    "1遊技だけ作動する小さいボーナス。作動中は小役が揃いやすくなる。今の機種ではほとんど使わない。",
                    null);

                GUILayout.Space(6);
                GUILayout.Label("よくある組み合わせ", title);
                GUILayout.Label("・Aタイプ: BB（連続作動: RB）＋ RB", body);
                GUILayout.Label("・REGを払出の少ないBBで作る機種もある（例: 6号機のCCエンジェル）。配列の揃い目が少ないときに選べる。", body);
                GUILayout.Label("・ボーナスを1種類だけにする: RB だけ", body);
                GUILayout.Label("・6号機のAT機の「疑似ボーナス」（例: スマスロ北斗の拳のBB）は役物ではない。役物を使わず、RT（状態）と押し順ベルで作る。", body);

                GUILayout.EndScrollView();
            }

            private static void Section(GUIStyle title, GUIStyle body, string name, string text, string example)
            {
                GUILayout.Label(name, title);
                GUILayout.Label(text, body);
                if (example != null) GUILayout.Label("例: " + example, body);
                GUILayout.Space(6);
            }
        }
    }
}
