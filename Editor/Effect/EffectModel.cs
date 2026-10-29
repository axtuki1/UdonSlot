using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    // EffectPlayer の平坦な配列を、編集しやすいアクションとパターンのリストに読み書きする。
    // 書き込みは SerializedObject 経由（Undo とプレハブの上書きに対応）。

    public sealed class EffectAnim
    {
        public Animator Target;
        public string Name = "";
        /// <summary>EffectPlayer.AnimTrigger など。</summary>
        public int Kind;
        public float Value;
    }

    public sealed class EffectActive
    {
        public GameObject Target;
        /// <summary>null はオブジェクトごと（SetActive）。Behaviour・Renderer・Collider なら有効／無効を切り替える。</summary>
        public Component Component;
        public bool Value = true;
    }

    public sealed class EffectCode
    {
        public UdonSharpBehaviour Target;
        public string Method = "";
    }

    public sealed class EffectReelKey
    {
        /// <summary>0=左 1=中 2=右。</summary>
        public int Reel;
        public int Time;
        /// <summary>相対：前のキーから順回転の向きに何コマ（−は逆回転）。絶対：中段に来るコマ番号（下が1）。</summary>
        public float Value;
        public bool Absolute;
        /// <summary>絶対キーの回り方（EffectPlayer.ReelShortest など）。前のキーの位置から数える。</summary>
        public int Direction;
        /// <summary>絶対キーで余分に回る周回数（順回転・逆回転のとき）。</summary>
        public int Turns;
        /// <summary>前のキーからこのキーまでの動き方（EffectPlayer.EaseLinear など）。</summary>
        public int Ease;

        public EffectReelKey Copy() { return (EffectReelKey)MemberwiseClone(); }
    }

    public sealed class EffectAction
    {
        public string Name = "";
        public int DelayMs;
        public int LockMs;
        public List<EffectAnim> Anims = new List<EffectAnim>();
        public List<EffectActive> Actives = new List<EffectActive>();
        /// <summary>SoundType。-1 は音なし。</summary>
        public int SoundType = -1;
        public int SoundClip;
        public List<EffectReelKey> ReelKeys = new List<EffectReelKey>();
        public List<EffectCode> Codes = new List<EffectCode>();

        public int ReelLength
        {
            get
            {
                int max = 0;
                foreach (var k in ReelKeys) max = Mathf.Max(max, k.Time);
                return max;
            }
        }

        public EffectAction Copy(string name)
        {
            var a = new EffectAction { Name = name, DelayMs = DelayMs, LockMs = LockMs, SoundType = SoundType, SoundClip = SoundClip };
            foreach (var x in Anims) a.Anims.Add(new EffectAnim { Target = x.Target, Name = x.Name, Kind = x.Kind, Value = x.Value });
            foreach (var x in Actives) a.Actives.Add(new EffectActive { Target = x.Target, Component = x.Component, Value = x.Value });
            foreach (var x in ReelKeys) a.ReelKeys.Add(x.Copy());
            foreach (var x in Codes) a.Codes.Add(new EffectCode { Target = x.Target, Method = x.Method });
            return a;
        }
    }

    /// <summary>セルに置いたアクション1件。</summary>
    public sealed class EffectEntry
    {
        public int Layer;
        public int Step;
        public int Branch;
        public EffectAction Action;
    }

    public sealed class EffectPattern
    {
        public string Name = "";
        /// <summary>コードから呼ぶときの名前（英数字と _）。</summary>
        public string Id = "";
        public EffectPattern Next;
        /// <summary>チャンネルから外れたら、触ったオブジェクトの ON/OFF を開始時に戻す。</summary>
        public bool ResetOnLeave = true;
        /// <summary>流している間はリールの停止音を鳴らさない。</summary>
        public bool MuteStopSound;
        public List<string> Layers = new List<string>();
        public List<EffectEntry> Entries = new List<EffectEntry>();

        public List<EffectEntry> Cell(int layer, int step)
        {
            var list = new List<EffectEntry>();
            foreach (var e in Entries) if (e.Layer == layer && e.Step == step) list.Add(e);
            return list;
        }

        /// <summary>空のまま分けたセルの分け方（ベイクしない。アクションがあればそちらから決まる）。</summary>
        public Dictionary<int, int> EmptySplits = new Dictionary<int, int>();

        /// <summary>セルの分け方。SplitNone／SplitResult／SplitReel。</summary>
        public int SplitOf(int layer, int step)
        {
            foreach (var e in Entries)
            {
                if (e.Layer != layer || e.Step != step || e.Branch == EffectPlayer.BranchCommon) continue;
                return e.Branch >= EffectPlayer.BranchLeft ? EffectModel.SplitReel : EffectModel.SplitResult;
            }
            int split;
            return EmptySplits.TryGetValue(layer * 16 + step, out split) ? split : EffectModel.SplitNone;
        }

        public bool IsSplit(int layer, int step)
        {
            return SplitOf(layer, step) != EffectModel.SplitNone;
        }
    }

    public sealed class EffectModel
    {
        public static readonly string[] StepNames = { "レバーオン", "リール回転開始", "第一停止", "第二停止", "第三停止", "ゲーム終了" };
        public static readonly string[] ReelNames = { "左", "中", "右" };

        public EffectPlayer Player;
        public List<EffectAction> Actions = new List<EffectAction>();
        public List<EffectPattern> Patterns = new List<EffectPattern>();

        public const int SplitNone = 0;
        public const int SplitResult = 1;
        public const int SplitReel = 2;

        /// <summary>入賞／非入賞に分けられる列（第三停止・ゲーム終了）。</summary>
        public static bool CanSplit(int step)
        {
            return step == EffectPlayer.StepThirdStop || step == EffectPlayer.StepGameEnd;
        }

        /// <summary>押したリールで分けられる列（第一〜第三停止）。</summary>
        public static bool CanSplitReel(int step)
        {
            return step >= EffectPlayer.StepFirstStop && step <= EffectPlayer.StepThirdStop;
        }

        /// <summary>分け方ごとの分岐。</summary>
        public static int[] BranchesOf(int split)
        {
            if (split == SplitResult) return new[] { EffectPlayer.BranchWin, EffectPlayer.BranchLose };
            if (split == SplitReel) return new[] { EffectPlayer.BranchLeft, EffectPlayer.BranchCenter, EffectPlayer.BranchRight };
            return new[] { EffectPlayer.BranchCommon };
        }

        public static EffectModel Load(EffectPlayer player)
        {
            var m = new EffectModel { Player = player };
            var so = new SerializedObject(player);

            var names = so.FindProperty("actionNames");
            var delays = so.FindProperty("actionDelayMs");
            var locks = so.FindProperty("actionLockMs");
            var animStart = so.FindProperty("actionAnimStart");
            var animCount = so.FindProperty("actionAnimCount");
            var animTargets = so.FindProperty("animTargets");
            var animNames = so.FindProperty("animNames");
            var animKinds = so.FindProperty("animKinds");
            var animValues = so.FindProperty("animValues");
            var activeStart = so.FindProperty("actionActiveStart");
            var enableStart = so.FindProperty("actionEnableStart");
            var enableCount = so.FindProperty("actionEnableCount");
            var enableBehaviours = so.FindProperty("enableBehaviours");
            var enableRenderers = so.FindProperty("enableRenderers");
            var enableColliders = so.FindProperty("enableColliders");
            var enableValues = so.FindProperty("enableValues");
            var activeCount = so.FindProperty("actionActiveCount");
            var activeTargets = so.FindProperty("activeTargets");
            var activeValues = so.FindProperty("activeValues");
            var soundType = so.FindProperty("actionSoundType");
            var soundClip = so.FindProperty("actionSoundClip");
            var codeStart = so.FindProperty("actionCodeStart");
            var codeCount = so.FindProperty("actionCodeCount");
            var codeTargets = so.FindProperty("codeTargets");
            var codeMethods = so.FindProperty("codeMethods");
            var reelStart = so.FindProperty("actionReelStart");
            var reelCount = so.FindProperty("actionReelCount");
            var keyReel = so.FindProperty("reelKeyReel");
            var keyTime = so.FindProperty("reelKeyTime");
            var keyValue = so.FindProperty("reelKeyValue");
            var keyAbs = so.FindProperty("reelKeyAbsolute");
            var keyDir = so.FindProperty("reelKeyDirection");
            var keyTurns = so.FindProperty("reelKeyTurns");
            var keyEase = so.FindProperty("reelKeyEase");

            for (int a = 0; a < names.arraySize; a++)
            {
                var action = new EffectAction
                {
                    Name = Str(names, a),
                    DelayMs = Int(delays, a),
                    LockMs = Int(locks, a),
                    SoundType = a < soundType.arraySize ? soundType.GetArrayElementAtIndex(a).intValue : -1,
                    SoundClip = Int(soundClip, a)
                };
                foreach (int i in Range(animStart, animCount, a, animTargets.arraySize))
                    action.Anims.Add(new EffectAnim { Target = animTargets.GetArrayElementAtIndex(i).objectReferenceValue as Animator, Name = Str(animNames, i), Kind = Int(animKinds, i), Value = Float(animValues, i) });
                foreach (int i in Range(activeStart, activeCount, a, activeTargets.arraySize))
                    action.Actives.Add(new EffectActive { Target = activeTargets.GetArrayElementAtIndex(i).objectReferenceValue as GameObject, Value = Bool(activeValues, i) });
                foreach (int i in Range(enableStart, enableCount, a, enableValues.arraySize))
                {
                    var c = Obj(enableBehaviours, i) ?? Obj(enableRenderers, i) ?? Obj(enableColliders, i);
                    var component = c as Component;
                    action.Actives.Add(new EffectActive { Target = component != null ? component.gameObject : null, Component = component, Value = Bool(enableValues, i) });
                }
                foreach (int i in Range(codeStart, codeCount, a, codeTargets.arraySize))
                    action.Codes.Add(new EffectCode { Target = codeTargets.GetArrayElementAtIndex(i).objectReferenceValue as UdonSharpBehaviour, Method = Str(codeMethods, i) });
                foreach (int i in Range(reelStart, reelCount, a, keyReel.arraySize))
                    action.ReelKeys.Add(new EffectReelKey { Reel = Int(keyReel, i), Time = Int(keyTime, i), Value = Float(keyValue, i), Absolute = Bool(keyAbs, i), Direction = Int(keyDir, i), Turns = Int(keyTurns, i), Ease = Int(keyEase, i) });
                m.Actions.Add(action);
            }

            var pNames = so.FindProperty("patternNames");
            var pIds = so.FindProperty("patternIds");
            var pNext = so.FindProperty("patternNext");
            var pKeep = so.FindProperty("patternKeepActive");
            var pMute = so.FindProperty("patternMuteStopSound");
            var pLayerStart = so.FindProperty("patternLayerStart");
            var pLayerCount = so.FindProperty("patternLayerCount");
            var layerNames = so.FindProperty("layerNames");
            for (int p = 0; p < pNames.arraySize; p++)
            {
                var pattern = new EffectPattern { Name = Str(pNames, p), Id = Str(pIds, p), ResetOnLeave = !(pKeep != null && Bool(pKeep, p)), MuteStopSound = pMute != null && Bool(pMute, p) };
                foreach (int i in Range(pLayerStart, pLayerCount, p, layerNames.arraySize)) pattern.Layers.Add(Str(layerNames, i));
                m.Patterns.Add(pattern);
            }
            // 内部名がない古いデータは、名前の英数字か Pattern1 などを付ける。
            var usedIds = new List<string>();
            foreach (var pattern in m.Patterns)
            {
                if (pattern.Id.Length == 0 || usedIds.Contains(pattern.Id))
                {
                    pattern.Id = UniqueId(pattern.Name, usedIds);
                    m.IdsAdded = true;
                }
                usedIds.Add(pattern.Id);
            }
            for (int p = 0; p < m.Patterns.Count; p++)
            {
                int next = p < pNext.arraySize ? pNext.GetArrayElementAtIndex(p).intValue : -1;
                if (next >= 0 && next < m.Patterns.Count) m.Patterns[p].Next = m.Patterns[next];
            }

            var ePattern = so.FindProperty("entryPattern");
            var eLayer = so.FindProperty("entryLayer");
            var eStep = so.FindProperty("entryStep");
            var stepVersion = so.FindProperty("stepVersion");
            bool oldSteps = stepVersion != null && stepVersion.intValue < EffectPlayer.CurrentStepVersion;
            var eBranch = so.FindProperty("entryBranch");
            var eAction = so.FindProperty("entryAction");
            for (int i = 0; i < ePattern.arraySize; i++)
            {
                int p = Int(ePattern, i), action = Int(eAction, i);
                if (p < 0 || p >= m.Patterns.Count || action < 0 || action >= m.Actions.Count) continue;
                var pattern = m.Patterns[p];
                // レイヤーはパターン内の番号で持つ（配列上は通し番号）。
                int layer = Int(eLayer, i) - (p < pLayerStart.arraySize ? pLayerStart.GetArrayElementAtIndex(p).intValue : 0);
                if (layer < 0 || layer >= pattern.Layers.Count) continue;
                pattern.Entries.Add(new EffectEntry { Layer = layer, Step = OldStep(Int(eStep, i), oldSteps), Branch = Int(eBranch, i), Action = m.Actions[action] });
            }
            return m;
        }

        public void Save(string label)
        {
            Undo.RecordObject(Player, label);
            var so = new SerializedObject(Player);
            int n = Actions.Count;
            var names = Resize(so, "actionNames", n);
            var delays = Resize(so, "actionDelayMs", n);
            var locks = Resize(so, "actionLockMs", n);
            var animStart = Resize(so, "actionAnimStart", n);
            var animCount = Resize(so, "actionAnimCount", n);
            var activeStart = Resize(so, "actionActiveStart", n);
            var enableStart = Resize(so, "actionEnableStart", n);
            var enableCount = Resize(so, "actionEnableCount", n);
            var activeCount = Resize(so, "actionActiveCount", n);
            var soundType = Resize(so, "actionSoundType", n);
            var soundClip = Resize(so, "actionSoundClip", n);
            var codeStart = Resize(so, "actionCodeStart", n);
            var codeCount = Resize(so, "actionCodeCount", n);
            var reelStart = Resize(so, "actionReelStart", n);
            var reelCount = Resize(so, "actionReelCount", n);

            int anims = 0, actives = 0, codes = 0, keys = 0;
            int enables = 0;
            foreach (var a in Actions)
            {
                anims += a.Anims.Count; codes += a.Codes.Count; keys += a.ReelKeys.Count;
                foreach (var x in a.Actives) { if (x.Component != null) enables++; else actives++; }
            }
            var enableBehaviours = Resize(so, "enableBehaviours", enables);
            var enableRenderers = Resize(so, "enableRenderers", enables);
            var enableColliders = Resize(so, "enableColliders", enables);
            var enableValues = Resize(so, "enableValues", enables);
            enables = 0;
            var animTargets = Resize(so, "animTargets", anims);
            var animNames = Resize(so, "animNames", anims);
            var animKinds = Resize(so, "animKinds", anims);
            var animValues = Resize(so, "animValues", anims);
            var activeTargets = Resize(so, "activeTargets", actives);
            var activeValues = Resize(so, "activeValues", actives);
            var codeTargets = Resize(so, "codeTargets", codes);
            var codeMethods = Resize(so, "codeMethods", codes);
            var keyReel = Resize(so, "reelKeyReel", keys);
            var keyTime = Resize(so, "reelKeyTime", keys);
            var keyValue = Resize(so, "reelKeyValue", keys);
            var keyAbs = Resize(so, "reelKeyAbsolute", keys);
            var keyDir = Resize(so, "reelKeyDirection", keys);
            var keyTurns = Resize(so, "reelKeyTurns", keys);
            var keyEase = Resize(so, "reelKeyEase", keys);

            anims = actives = codes = keys = 0;
            for (int i = 0; i < n; i++)
            {
                var a = Actions[i];
                names.GetArrayElementAtIndex(i).stringValue = a.Name;
                delays.GetArrayElementAtIndex(i).intValue = Mathf.Max(0, a.DelayMs);
                locks.GetArrayElementAtIndex(i).intValue = Mathf.Max(0, a.LockMs);
                soundType.GetArrayElementAtIndex(i).intValue = a.SoundType;
                soundClip.GetArrayElementAtIndex(i).intValue = a.SoundClip;

                animStart.GetArrayElementAtIndex(i).intValue = anims;
                animCount.GetArrayElementAtIndex(i).intValue = a.Anims.Count;
                foreach (var x in a.Anims)
                {
                    animTargets.GetArrayElementAtIndex(anims).objectReferenceValue = x.Target;
                    animNames.GetArrayElementAtIndex(anims).stringValue = x.Name ?? "";
                    animKinds.GetArrayElementAtIndex(anims).intValue = x.Kind;
                    animValues.GetArrayElementAtIndex(anims).floatValue = x.Value;
                    anims++;
                }
                activeStart.GetArrayElementAtIndex(i).intValue = actives;
                enableStart.GetArrayElementAtIndex(i).intValue = enables;
                int activeN = 0, enableN = 0;
                foreach (var x in a.Actives)
                {
                    if (x.Component != null)
                    {
                        enableBehaviours.GetArrayElementAtIndex(enables).objectReferenceValue = x.Component as Behaviour;
                        enableRenderers.GetArrayElementAtIndex(enables).objectReferenceValue = x.Component as Renderer;
                        enableColliders.GetArrayElementAtIndex(enables).objectReferenceValue = x.Component as Collider;
                        enableValues.GetArrayElementAtIndex(enables).boolValue = x.Value;
                        enables++;
                        enableN++;
                        continue;
                    }
                    activeTargets.GetArrayElementAtIndex(actives).objectReferenceValue = x.Target;
                    activeValues.GetArrayElementAtIndex(actives).boolValue = x.Value;
                    actives++;
                    activeN++;
                }
                activeCount.GetArrayElementAtIndex(i).intValue = activeN;
                enableCount.GetArrayElementAtIndex(i).intValue = enableN;
                codeStart.GetArrayElementAtIndex(i).intValue = codes;
                codeCount.GetArrayElementAtIndex(i).intValue = a.Codes.Count;
                foreach (var x in a.Codes)
                {
                    codeTargets.GetArrayElementAtIndex(codes).objectReferenceValue = x.Target;
                    codeMethods.GetArrayElementAtIndex(codes).stringValue = x.Method ?? "";
                    codes++;
                }
                reelStart.GetArrayElementAtIndex(i).intValue = keys;
                reelCount.GetArrayElementAtIndex(i).intValue = a.ReelKeys.Count;
                foreach (var x in a.ReelKeys)
                {
                    keyReel.GetArrayElementAtIndex(keys).intValue = x.Reel;
                    keyTime.GetArrayElementAtIndex(keys).intValue = Mathf.Max(0, x.Time);
                    keyValue.GetArrayElementAtIndex(keys).floatValue = x.Value;
                    keyAbs.GetArrayElementAtIndex(keys).boolValue = x.Absolute;
                    keyDir.GetArrayElementAtIndex(keys).intValue = x.Direction;
                    keyTurns.GetArrayElementAtIndex(keys).intValue = x.Turns;
                    keyEase.GetArrayElementAtIndex(keys).intValue = x.Ease;
                    keys++;
                }
            }

            int pc = Patterns.Count;
            var pNames = Resize(so, "patternNames", pc);
            var pIds = Resize(so, "patternIds", pc);
            var pNext = Resize(so, "patternNext", pc);
            var pKeep = Resize(so, "patternKeepActive", pc);
            var pMute = Resize(so, "patternMuteStopSound", pc);
            var pLayerStart = Resize(so, "patternLayerStart", pc);
            var pLayerCount = Resize(so, "patternLayerCount", pc);
            int layers = 0, entries = 0;
            foreach (var p in Patterns) { layers += p.Layers.Count; entries += p.Entries.Count; }
            var layerNames = Resize(so, "layerNames", layers);
            var ePattern = Resize(so, "entryPattern", entries);
            var eLayer = Resize(so, "entryLayer", entries);
            var eStep = Resize(so, "entryStep", entries);
            so.FindProperty("stepVersion").intValue = EffectPlayer.CurrentStepVersion;
            var eBranch = Resize(so, "entryBranch", entries);
            var eAction = Resize(so, "entryAction", entries);

            layers = entries = 0;
            for (int p = 0; p < pc; p++)
            {
                var pattern = Patterns[p];
                pNames.GetArrayElementAtIndex(p).stringValue = pattern.Name;
                pIds.GetArrayElementAtIndex(p).stringValue = pattern.Id;
                pNext.GetArrayElementAtIndex(p).intValue = pattern.Next != null ? Patterns.IndexOf(pattern.Next) : -1;
                pKeep.GetArrayElementAtIndex(p).boolValue = !pattern.ResetOnLeave;
                pMute.GetArrayElementAtIndex(p).boolValue = pattern.MuteStopSound;
                pLayerStart.GetArrayElementAtIndex(p).intValue = layers;
                pLayerCount.GetArrayElementAtIndex(p).intValue = pattern.Layers.Count;
                int baseLayer = layers;
                foreach (string layer in pattern.Layers) layerNames.GetArrayElementAtIndex(layers++).stringValue = layer;
                // レイヤー順・列順に並べておく（再生時に上のレイヤーから順に動く）。
                var sorted = new List<EffectEntry>(pattern.Entries);
                sorted.Sort((x, y) => x.Layer != y.Layer ? x.Layer.CompareTo(y.Layer) : x.Step.CompareTo(y.Step));
                foreach (var e in sorted)
                {
                    int action = Actions.IndexOf(e.Action);
                    if (action < 0) continue;
                    ePattern.GetArrayElementAtIndex(entries).intValue = p;
                    eLayer.GetArrayElementAtIndex(entries).intValue = baseLayer + e.Layer;
                    eStep.GetArrayElementAtIndex(entries).intValue = e.Step;
                    eBranch.GetArrayElementAtIndex(entries).intValue = e.Branch;
                    eAction.GetArrayElementAtIndex(entries).intValue = action;
                    entries++;
                }
            }
            // 消えたアクションを指していた分を詰める。
            Resize(so, "entryPattern", entries);
            Resize(so, "entryLayer", entries);
            Resize(so, "entryStep", entries);
            Resize(so, "entryBranch", entries);
            Resize(so, "entryAction", entries);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(Player);
        }

        /// <summary>同じ名前があれば「名前 (1)」のように番号を付ける。</summary>
        /// <summary>読み込み時に内部名を付けたパターンがある（書き戻しが要る）。</summary>
        public bool IdsAdded;

        public List<string> PatternIds()
        {
            var list = new List<string>();
            foreach (var p in Patterns) list.Add(p.Id);
            return list;
        }

        /// <summary>
        /// 内部名に直す。英数字と _ だけ残し（ほかは _）、数字から始まるなら前に _。何も残らなければ Pattern。
        /// 重なれば後ろに 2, 3 … を付ける。
        /// </summary>
        public static string UniqueId(string name, ICollection<string> used)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (ok) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string id = sb.ToString().Trim('_');
            if (id.Length == 0) id = "Pattern";
            if (char.IsDigit(id[0])) id = "_" + id;
            if (!used.Contains(id)) return id;
            string root = System.Text.RegularExpressions.Regex.Replace(id, @"\d+$", "");
            if (root.Length == 0 || root == "_") root = id;
            for (int i = 2; ; i++)
            {
                string candidate = root + i;
                if (!used.Contains(candidate)) return candidate;
            }
        }

        public static string UniqueName(string name, ICollection<string> used)
        {
            if (!used.Contains(name)) return name;
            string root = name;
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.*) \((\d+)\)$");
            if (match.Success) root = match.Groups[1].Value;
            for (int i = 1; ; i++)
            {
                string candidate = root + " (" + i + ")";
                if (!used.Contains(candidate)) return candidate;
            }
        }

        public List<string> ActionNames()
        {
            var list = new List<string>();
            foreach (var a in Actions) list.Add(a.Name);
            return list;
        }

        public List<string> PatternNames()
        {
            var list = new List<string>();
            foreach (var p in Patterns) list.Add(p.Name);
            return list;
        }

        public void RemoveAction(EffectAction action)
        {
            Actions.Remove(action);
            foreach (var p in Patterns) p.Entries.RemoveAll(e => e.Action == action);
        }

        public void RemovePattern(EffectPattern pattern)
        {
            Patterns.Remove(pattern);
            foreach (var p in Patterns) if (p.Next == pattern) p.Next = null;
        }

        private static IEnumerable<int> Range(SerializedProperty starts, SerializedProperty counts, int index, int limit)
        {
            if (index >= starts.arraySize || index >= counts.arraySize) yield break;
            int start = starts.GetArrayElementAtIndex(index).intValue;
            int end = Mathf.Min(start + counts.GetArrayElementAtIndex(index).intValue, limit);
            for (int i = Mathf.Max(0, start); i < end; i++) yield return i;
        }

        private static string Str(SerializedProperty array, int index)
        {
            return index < array.arraySize ? array.GetArrayElementAtIndex(index).stringValue ?? "" : "";
        }

        /// <summary>「リール回転開始」の列が無かったころのデータは、レバーオンより後の列を1つずらす。</summary>
        private static int OldStep(int step, bool old)
        {
            return old && step >= EffectPlayer.StepReelStart ? step + 1 : step;
        }

        private static Object Obj(SerializedProperty array, int index)
        {
            if (array == null || index >= array.arraySize) return null;
            var value = array.GetArrayElementAtIndex(index).objectReferenceValue;
            return value != null ? value : null;
        }

        private static int Int(SerializedProperty array, int index)
        {
            return array != null && index < array.arraySize ? array.GetArrayElementAtIndex(index).intValue : 0;
        }

        private static float Float(SerializedProperty array, int index)
        {
            return index < array.arraySize ? array.GetArrayElementAtIndex(index).floatValue : 0f;
        }

        private static bool Bool(SerializedProperty array, int index)
        {
            return index < array.arraySize && array.GetArrayElementAtIndex(index).boolValue;
        }

        private static SerializedProperty Resize(SerializedObject so, string name, int size)
        {
            var p = so.FindProperty(name);
            p.arraySize = size;
            return p;
        }
    }
}
