using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>
    /// 制御ルールファイルから全フラグの滑りテーブルを生成し、結果を確認して ReelControlData へベイクする画面。
    /// 編集はルールファイル（テキスト）で行い、この画面は「生成・確認・ベイク」だけを受け持つ。
    /// </summary>
    public sealed class ReelRuleWindow : EditorWindow
    {
        private const string MachinePrefKey = "AX.UdonSlot.ReelRule.Machine";
        private const string RulePrefKey = "AX.UdonSlot.ReelRule.RuleFile";

        private ReelOutputController machine;
        private TextAsset ruleFile;
        private List<ControlResult> results;
        private List<string> errors = new List<string>();
        private string generatedFrom;
        private long elapsedMs;
        private int maxSlip;
        private Vector2 scroll;
        private readonly HashSet<string> expanded = new HashSet<string>();

        [MenuItem("UdonSlot/ルール制御")]
        public static void Open()
        {
            GetWindow<ReelRuleWindow>("ルール制御");
        }

        private void OnEnable()
        {
            GlobalObjectId id;
            string saved = EditorPrefs.GetString(MachinePrefKey, "");
            if (machine == null && GlobalObjectId.TryParse(saved, out id))
                machine = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as ReelOutputController;
            if (ruleFile == null)
            {
                string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(RulePrefKey, ""));
                if (!string.IsNullOrEmpty(path)) ruleFile = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            machine = (ReelOutputController)EditorGUILayout.ObjectField("機種", machine, typeof(ReelOutputController), true);
            using (new EditorGUILayout.HorizontalScope())
            {
                ruleFile = (TextAsset)EditorGUILayout.ObjectField("ルールファイル", ruleFile, typeof(TextAsset), false);
                using (new EditorGUI.DisabledScope(machine == null))
                    if (GUILayout.Button("新規", GUILayout.Width(48)))
                    {
                        try { CreateRuleFile(); }
                        catch (Exception e) { EditorUtility.DisplayDialog("ルール制御", e.Message, "OK"); }
                        GUIUtility.ExitGUI();
                    }
            }
            if (EditorGUI.EndChangeCheck()) SavePrefs();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(machine == null || ruleFile == null))
                    if (GUILayout.Button("生成", GUILayout.Height(28))) { Generate(); GUIUtility.ExitGUI(); }
                bool ready = results != null && errors.Count == 0 && !IsStale();
                using (new EditorGUI.DisabledScope(!ready))
                {
                    if (GUILayout.Button("レポート保存", GUILayout.Height(28))) { SaveReport(); GUIUtility.ExitGUI(); }
                    if (GUILayout.Button("ベイク", GUILayout.Height(28))) { Bake(); GUIUtility.ExitGUI(); }
                }
            }

            foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (results == null) return;
            if (IsStale()) EditorGUILayout.HelpBox("ルールファイルまたは機種が生成後に変わりました。生成し直してください。", MessageType.Warning);
            EditorGUILayout.LabelField("最大滑り " + maxSlip + "・" + results.Count + "フラグ・" + elapsedMs + "ms", EditorStyles.miniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var result in results) DrawResult(result);
            EditorGUILayout.EndScrollView();
        }

        private void DrawResult(ControlResult r)
        {
            var s = r.Stats;
            bool open = expanded.Contains(r.Name);
            string head = r.Name + "　" + ReelRuleReport.WonSummary(r);
            if (s.ViolationPaths > 0) head += "　非成立入賞 " + s.ViolationPaths;
            if (s.UnavoidableCells > 0) head += "　回避不能 " + s.UnavoidableCells;
            bool next = EditorGUILayout.Foldout(open, head, true);
            if (next != open)
            {
                if (next) expanded.Add(r.Name); else expanded.Remove(r.Name);
            }
            if (!next) return;

            using (new EditorGUI.IndentLevelScope())
            {
                if (r.LotteryFlag != r.Name) EditorGUILayout.LabelField("抽選フラグ", r.LotteryFlag);
                EditorGUILayout.LabelField("優先順位", r.GoalLines.Count == 0 ? "（なし）" : string.Join(" → ", r.GoalLines));
                DrawRateHeader();
                for (int i = 0; i < s.RoleNames.Length; i++)
                {
                    if (s.RoleFormed[i] == 0 && !s.RoleWon[i]) continue;
                    var rates = new string[6];
                    for (int o = 0; o < 6; o++) rates[o] = ReelRuleReport.Percent(s.RoleFormedByOrder[i, o], s.PathsByOrder[o]);
                    DrawRateRow((s.RoleWon[i] ? "入賞 " : "非成立 ") + s.RoleNames[i], ReelRuleReport.Percent(s.RoleFormed[i], s.Paths), rates);
                }
                for (int k = 0; k < s.ShapeNames.Count; k++)
                {
                    var rates = new string[6];
                    for (int o = 0; o < 6; o++) rates[o] = ReelRuleReport.Percent(s.ShapeMatchedByOrder[k, o], s.PathsByOrder[o]);
                    DrawRateRow("停止形 " + s.ShapeNames[k], ReelRuleReport.Percent(s.ShapeMatched[k], s.Paths), rates);
                }

                EditorGUILayout.LabelField("出目（多い順、全" + s.Windows.Count + "種類）", EditorStyles.boldLabel);
                foreach (var w in s.TopWindows(12))
                {
                    string[] cells = w.Key.Split('\t');
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(ReelRuleReport.Percent(w.Value, s.Paths), GUILayout.Width(80));
                        var text = cells[0] + " " + cells[1] + " " + cells[2] + "\n" + cells[3] + " " + cells[4] + " " + cells[5] + "\n" + cells[6] + " " + cells[7] + " " + cells[8];
                        EditorGUILayout.LabelField(text, EditorStyles.helpBox, GUILayout.Height(48));
                    }
                }
            }
            EditorGUILayout.Space();
        }

        private static void DrawRateHeader()
        {
            DrawRateRow("", "全体", ReelRuleEngine.OrderNames);
        }

        private static void DrawRateRow(string label, string total, string[] rates)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(180));
                GUILayout.Label(total, GUILayout.Width(52));
                foreach (string rate in rates) GUILayout.Label(rate, GUILayout.Width(52));
            }
        }

        // ------------------------------------------------------------ 操作

        private void SavePrefs()
        {
            EditorPrefs.SetString(MachinePrefKey, machine == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(machine).ToString());
            EditorPrefs.SetString(RulePrefKey, ruleFile == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ruleFile)));
        }

        private string Signature()
        {
            if (machine == null || ruleFile == null) return "";
            try
            {
                return machine.GetInstanceID() + "\n" + ruleFile.text + "\n" + MachineSignature(BuildMachine());
            }
            catch (Exception e)
            {
                return "error:" + e.Message;
            }
        }

        private bool IsStale()
        {
            return results != null && generatedFrom != Signature();
        }

        private void Generate()
        {
            errors.Clear();
            results = null;
            var watch = Stopwatch.StartNew();
            var lastUpdate = DateTime.MinValue;
            try
            {
                var m = BuildMachine();
                var rules = ReelRuleParser.Parse(ruleFile.text);
                var engine = new ReelRuleEngine(m, rules);
                maxSlip = engine.MaxSlip;
                results = engine.GenerateAll(() =>
                {
                    if ((DateTime.Now - lastUpdate).TotalMilliseconds < 200) return false;
                    lastUpdate = DateTime.Now;
                    return EditorUtility.DisplayCancelableProgressBar("ルール制御", "生成中…", 0.5f);
                });
                generatedFrom = Signature();
            }
            catch (RuleException e)
            {
                foreach (var error in e.Errors) errors.Add(error.ToString());
            }
            catch (OperationCanceledException)
            {
                errors.Add("キャンセルしました");
            }
            catch (Exception e)
            {
                errors.Add(e.Message);
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            elapsedMs = watch.ElapsedMilliseconds;
            if (errors.Count > 0) results = null;
        }

        private void SaveReport()
        {
            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(ruleFile));
            string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(ruleFile));
            string path = EditorUtility.SaveFilePanel("レポート保存", dir, name + ".report.md", "md");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, ReelRuleReport.Markdown(results, 20));
            AssetDatabase.Refresh();
        }

        private void Bake()
        {
            var control = new SerializedObject(machine).FindProperty("reelControl").objectReferenceValue as ReelControl;
            if (control == null || control.flagNames == null || control.reelControlDataList == null)
            {
                EditorUtility.DisplayDialog("ベイク", "機種に ReelControl が設定されていません。", "OK");
                return;
            }
            var targets = new List<KeyValuePair<ControlResult, ReelControlData>>();
            var missing = new List<string>();
            foreach (var result in results)
            {
                int index = Array.IndexOf(control.flagNames, result.Name);
                ReelControlData data = index >= 0 && index < control.reelControlDataList.Length ? control.reelControlDataList[index] : null;
                if (data == null) missing.Add(result.Name);
                else targets.Add(new KeyValuePair<ControlResult, ReelControlData>(result, data));
            }
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("ベイク", "ReelControl に一致する検索名がありません。\n" + string.Join("、", missing), "OK");
                return;
            }
            string message = targets.Count + "件の ReelControlData を上書きします。";
            if (missing.Count > 0) message += "\n\nReelControl に登録がないため書き込まないフラグ:\n" + string.Join("、", missing);
            int violations = 0;
            foreach (var pair in targets) violations += (int)Math.Min(int.MaxValue, pair.Key.Stats.ViolationPaths);
            if (violations > 0) message += "\n\n非成立入賞が避けられない経路があります（" + violations + "件）。";
            if (!EditorUtility.DisplayDialog("ベイク", message, "ベイク", "キャンセル")) return;

            var m = BuildMachine();
            var lengths = new[] { m.Strips[0].Length, m.Strips[1].Length, m.Strips[2].Length };
            var objects = new List<UnityEngine.Object>();
            foreach (var pair in targets) objects.Add(pair.Value);
            Undo.RecordObjects(objects.ToArray(), "ルール制御のベイク");
            foreach (var pair in targets)
            {
                var baked = ReelRuleBaker.Bake(pair.Key, lengths);
                var data = pair.Value;
                data.reelStopSlipCount = baked.ReelStopSlipCount;
                data.IsReelStopTableStyle = baked.IsReelStopTableStyle;
                data.conditionPatterns = baked.ConditionPatterns;
                data.conditionLeftPositions = baked.ConditionLeftPositions;
                data.conditionCenterPositions = baked.ConditionCenterPositions;
                data.conditionRightPositions = baked.ConditionRightPositions;
                data.conditionSlipOffsets = baked.ConditionSlipOffsets;
                data.conditionSlipLengths = baked.ConditionSlipLengths;
                data.conditionSlips = baked.ConditionSlips;
                EditorUtility.SetDirty(data);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                if (data.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(data.gameObject.scene);
            }
            EditorUtility.DisplayDialog("ベイク", targets.Count + "件をベイクしました。シーン（またはPrefab）を保存してください。", "OK");
        }

        private void CreateRuleFile()
        {
            string path = EditorUtility.SaveFilePanelInProject("ルールファイル", "ReelRules", "txt", "制御ルールの保存先");
            if (string.IsNullOrEmpty(path)) return;
            var m = BuildMachine();
            var text = new System.Text.StringBuilder();
            text.AppendLine("# UdonSlot 制御ルール。書き方は Packages/com.axtuki1.udonslot/Docs/ReelRule.md を参照。");
            text.AppendLine("# ルールを書かないフラグも、当選役の引込と非成立役の回避は自動で行います。");
            text.AppendLine();
            text.AppendLine("最大滑り " + m.MaxSlip);
            text.AppendLine();
            text.AppendLine("# 停止形の例（上段・中段・下段 × 左・中・右。* は何でもよい）");
            text.AppendLine("# 停止形 右下がりスイカ");
            text.AppendLine("#   スイカ *      *");
            text.AppendLine("#   *      スイカ *");
            text.AppendLine("#   *      *      スイカ");
            text.AppendLine();
            foreach (var flag in m.Flags)
            {
                text.AppendLine("# フラグ " + flag.Name + (flag.Roles.Count > 0 ? "    # 当選役: " + string.Join(" ", flag.Roles) : "    # 当選役なし"));
            }
            File.WriteAllText(path, text.ToString());
            AssetDatabase.ImportAsset(path);
            ruleFile = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            SavePrefs();
        }

        // ------------------------------------------------------------ 機種データの読み取り

        private Machine BuildMachine()
        {
            var m = new Machine();
            if (machine.reelDatas == null || machine.reelDatas.Length != 3) throw new InvalidOperationException("機種の reelDatas が3本ではありません");
            for (int r = 0; r < 3; r++)
            {
                if (machine.reelDatas[r] == null || machine.reelDatas[r].symbols == null) throw new InvalidOperationException("リール配列が未設定です");
                m.Strips[r] = (string[])machine.reelDatas[r].symbols.Clone();
            }
            var so = new SerializedObject(machine);
            m.MaxSlip = so.FindProperty("maxSlipCount").intValue;
            m.BonusFirst = so.FindProperty("rolePriority").enumValueIndex == (int)ReelRolePriority.BonusFirst;
            var payout = so.FindProperty("payoutTable").objectReferenceValue as PayoutTable;
            var lottery = so.FindProperty("lotteryTable").objectReferenceValue as LotteryTable;
            if (payout == null) throw new InvalidOperationException("機種に配当表（PayoutTable）が設定されていません");
            if (lottery == null) throw new InvalidOperationException("機種に抽選表（LotteryTable）が設定されていません");

            for (int i = 0; i < payout.GetPayLineCount(); i++)
            {
                if (!payout.IsPayLineValid(i)) continue;
                m.Lines.Add(new PayLine
                {
                    Rows = new[] { payout.GetPayLineRow(i, 0), payout.GetPayLineRow(i, 1), payout.GetPayLineRow(i, 2) },
                    BetMask = payout.payLineBetMasks[i]
                });
            }
            for (int i = 0; i < payout.GetWinPairCount(); i++)
            {
                if (!payout.IsWinPairValid(i)) continue;
                var role = new Role
                {
                    Name = payout.winPairNames[i],
                    IsBonus = payout.IsBonus(i),
                    IsReplay = payout.IsReplay(i),
                    Payout = payout.payouts[i]
                };
                for (int r = 0; r < 3; r++) role.Symbols[r] = SplitSymbols(payout.GetSymbol(i, r));
                if (!payout.isReelStopOrderAny[i])
                    role.StopOrder = new[] { payout.GetStopOrder(i, 0), payout.GetStopOrder(i, 1), payout.GetStopOrder(i, 2) };
                m.Roles.Add(role);
            }

            var lso = new SerializedObject(lottery);
            var names = lso.FindProperty("winpairNames");
            bool hasRoles = lottery.HasWinningRoleDefinitions();
            for (int f = 0; f < names.arraySize; f++)
            {
                var flag = new LotteryFlag { Name = names.GetArrayElementAtIndex(f).stringValue };
                if (hasRoles)
                {
                    if (lottery.winningRoleStartIndexes != null && lottery.winningRoleCounts != null &&
                        f < lottery.winningRoleStartIndexes.Length && f < lottery.winningRoleCounts.Length)
                    {
                        int start = lottery.winningRoleStartIndexes[f];
                        int end = Math.Min(start + lottery.winningRoleCounts[f], lottery.winningRoleNames.Length);
                        for (int k = Math.Max(0, start); k < end; k++) flag.Roles.Add(lottery.winningRoleNames[k]);
                    }
                }
                else
                {
                    // 旧方式: 配当表側の対応フラグで当選役を決める
                    for (int i = 0; i < payout.GetWinPairCount(); i++)
                        if (payout.IsControlCandidate(i, flag.Name) && !flag.Roles.Contains(payout.winPairNames[i]))
                            flag.Roles.Add(payout.winPairNames[i]);
                }
                if (m.FindFlag(flag.Name) == null) m.Flags.Add(flag);
            }
            return m;
        }

        private static string[] SplitSymbols(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            var list = new List<string>();
            foreach (string part in text.Split(','))
            {
                string symbol = part.Trim();
                if (symbol.Length > 0) list.Add(symbol);
            }
            return list.ToArray();
        }

        private static string MachineSignature(Machine m)
        {
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < 3; r++) sb.Append(string.Join(",", m.Strips[r])).Append('|');
            foreach (var line in m.Lines) sb.Append(string.Join(",", line.Rows)).Append(':').Append(line.BetMask).Append('|');
            foreach (var role in m.Roles)
            {
                sb.Append(role.Name).Append('=');
                foreach (var symbols in role.Symbols) sb.Append(string.Join("/", symbols)).Append(',');
                if (role.StopOrder != null) sb.Append(string.Join("", role.StopOrder));
                sb.Append('|');
            }
            foreach (var flag in m.Flags) sb.Append(flag.Name).Append('=').Append(string.Join(",", flag.Roles)).Append('|');
            sb.Append(m.MaxSlip).Append(m.BonusFirst);
            return sb.ToString();
        }
    }
}
