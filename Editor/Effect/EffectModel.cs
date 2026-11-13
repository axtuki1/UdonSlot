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

    public sealed class EffectLightKey
    {
        /// <summary>EffectPlayer の LightTrack（0～8 は1コマ、9 入賞図柄、10 入賞図柄以外、11～16 照明の色）。</summary>
        public int Track;
        public int Time;
        /// <summary>明るさのトラックは r だけを使う（0～1）。</summary>
        public Color Value = Color.white;
        /// <summary>次のキーまで値をそのまま保つ（切り替え）。false はリニア。</summary>
        public bool Hold;

        public EffectLightKey Copy() { return (EffectLightKey)MemberwiseClone(); }
    }

    public sealed class EffectAction
    {
        public string Name = "";
        public int DelayMs;
        public int LockMs;
        /// <summary>停止の列で流すと、その停止の停止音を鳴らさない。</summary>
        public bool MuteStopSound;
        public List<EffectAnim> Anims = new List<EffectAnim>();
        public List<EffectActive> Actives = new List<EffectActive>();
        /// <summary>SoundType。-1 は音なし。</summary>
        public int SoundType = -1;
        public int SoundClip;
        public List<EffectReelKey> ReelKeys = new List<EffectReelKey>();
        public List<EffectCode> Codes = new List<EffectCode>();
        public List<EffectLightKey> LightKeys = new List<EffectLightKey>();
        /// <summary>リール照明の1回の長さ(ms)。0 は最後のキーの時間。</summary>
        public int LightLength;
        public bool LightLoop;
        public bool LightRestore = true;
        /// <summary>「入賞図柄」「入賞図柄以外」トラックの左・中・右の遅れ(ms)。</summary>
        public int[] LightDelay = new int[3];
        /// <summary>再生中のリール照明を止めるきっかけ（EffectPlayer.StopOnBet など）。</summary>
        public int StopMask = EffectPlayer.StopOnBet | EffectPlayer.StopOnLeverOn;
        /// <summary>流したときに切り替える入賞演出のセット。null は切り替えない。</summary>
        public EffectWinSet WinSet;

        /// <summary>リール照明の1回の長さ（未設定なら最後のキーの時間）。</summary>
        public int LightEnd
        {
            get
            {
                if (LightLength > 0) return LightLength;
                int max = 0;
                foreach (var k in LightKeys) max = Mathf.Max(max, k.Time);
                return max;
            }
        }

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
            var a = new EffectAction
            {
                Name = name, DelayMs = DelayMs, LockMs = LockMs, MuteStopSound = MuteStopSound, SoundType = SoundType, SoundClip = SoundClip,
                LightLength = LightLength, LightLoop = LightLoop, LightRestore = LightRestore, LightDelay = (int[])LightDelay.Clone(), StopMask = StopMask, WinSet = WinSet
            };
            foreach (var x in LightKeys) a.LightKeys.Add(x.Copy());
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
        /// <summary>次ゲームの予定。パターンか抽選テーブルのどちらか（両方 null はなし）。</summary>
        public EffectPattern Next;
        public EffectLotTable NextTable;
        /// <summary>次のレバーオンでボーナス成立中のときの予定。両方 null は通常の予定と同じ。</summary>
        public EffectPattern NextBonus;
        public EffectLotTable NextBonusTable;
        /// <summary>チャンネルから外れたら、触ったオブジェクトの ON/OFF を開始時に戻す。</summary>
        public bool ResetOnLeave = true;
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

    /// <summary>振り分けの1行。Pattern が null なら「停止」。</summary>
    public sealed class EffectLotRow
    {
        public EffectPattern Pattern;
        public int Parameter;
        /// <summary>設定ごとの値（65536 分のいくつか）。全設定共通のときは 0 番だけを使う。</summary>
        public int[] Values = new int[1];

        public EffectLotRow Copy()
        {
            return new EffectLotRow { Pattern = Pattern, Parameter = Parameter, Values = (int[])Values.Clone() };
        }
    }

    /// <summary>成立フラグ（＋成立中のボーナス）ごとの振り分け表。</summary>
    public sealed class EffectLotCell
    {
        /// <summary>成立フラグの内部名。空は「共通」。</summary>
        public string Flag = "";
        /// <summary>成立中のボーナスの内部名。空は通常。</summary>
        public string Bonus = "";
        public bool PerSetting;
        public List<EffectLotRow> Rows = new List<EffectLotRow>();

        /// <summary>その設定での値。全設定共通なら 0 番。</summary>
        public int Value(EffectLotRow row, int setting)
        {
            int index = PerSetting ? Mathf.Clamp(setting, 0, row.Values.Length - 1) : 0;
            return row.Values[index];
        }

        public int Sum(int setting)
        {
            int sum = 0;
            foreach (var r in Rows) sum += Value(r, setting);
            return sum;
        }

        public EffectLotCell Copy(string flag, string bonus)
        {
            var c = new EffectLotCell { Flag = flag, Bonus = bonus, PerSetting = PerSetting };
            foreach (var r in Rows) c.Rows.Add(r.Copy());
            return c;
        }
    }

    /// <summary>演出の抽選テーブル。</summary>
    public sealed class EffectLotTable
    {
        public string Name = "";
        /// <summary>コードから引くときの名前（英数字と _）。</summary>
        public string Id = "";
        public int Channel;
        public bool Auto = true;
        /// <summary>自動で引く演出モード。空は指定なし。</summary>
        public string Mode = "";
        /// <summary>自動で引く状態（「S:状態」「B:役物」）。空はすべて。</summary>
        public List<string> Situations = new List<string>();
        public List<EffectLotCell> Cells = new List<EffectLotCell>();

        public EffectLotCell Cell(string flag, string bonus)
        {
            return Cells.Find(c => c.Flag == flag && c.Bonus == bonus);
        }

        public EffectLotTable Copy(string name, string id)
        {
            var t = new EffectLotTable { Name = name, Id = id, Channel = Channel, Auto = Auto, Mode = Mode, Situations = new List<string>(Situations) };
            foreach (var c in Cells) t.Cells.Add(c.Copy(c.Flag, c.Bonus));
            return t;
        }
    }

    /// <summary>入賞演出の1行。Action が null なら「なし」（既定のセットも流さない）。</summary>
    public sealed class EffectWinEntry
    {
        public string Role = "";
        public EffectAction Action;
    }

    /// <summary>入賞演出のセット。役ごとに入賞時に流すアクションを持つ。</summary>
    public sealed class EffectWinSet
    {
        public string Name = "";
        public List<EffectWinEntry> Entries = new List<EffectWinEntry>();

        public List<EffectWinEntry> Row(string role)
        {
            return Entries.FindAll(e => e.Role == role);
        }
    }

    public sealed class EffectModel
    {
        public static readonly string[] StepNames = { "レバーオン", "リール回転開始", "第一停止", "第二停止", "第三停止", "ゲーム終了" };
        public static readonly string[] ReelNames = { "左", "中", "右" };

        public EffectPlayer Player;
        public List<EffectAction> Actions = new List<EffectAction>();
        public List<EffectPattern> Patterns = new List<EffectPattern>();
        public List<EffectWinSet> WinSets = new List<EffectWinSet>();
        public List<EffectLotTable> LotTables = new List<EffectLotTable>();
        /// <summary>演出モードの名前。</summary>
        public List<string> Modes = new List<string>();
        /// <summary>設定の数（機種の抽選表から）。振り分けの値はこの数だけ持つ。</summary>
        public int SettingCount = 1;

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
            var mutes = so.FindProperty("actionMuteStopSound");
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
            var lightStart = so.FindProperty("actionLightStart");
            var lightCount = so.FindProperty("actionLightCount");
            var lightLength = so.FindProperty("actionLightLength");
            var lightLoop = so.FindProperty("actionLightLoop");
            var lightRestore = so.FindProperty("actionLightRestore");
            var lightDelay = so.FindProperty("actionLightDelay");
            var stopMask = so.FindProperty("actionStopMask");
            var winSetOf = so.FindProperty("actionWinSet");
            var lightTrack = so.FindProperty("lightKeyTrack");
            var lightTime = so.FindProperty("lightKeyTime");
            var lightValue = so.FindProperty("lightKeyValue");
            var lightHold = so.FindProperty("lightKeyHold");

            var setNames = so.FindProperty("winSetNames");
            for (int i = 0; i < setNames.arraySize; i++) m.WinSets.Add(new EffectWinSet { Name = Str(setNames, i) });

            for (int a = 0; a < names.arraySize; a++)
            {
                var action = new EffectAction
                {
                    Name = Str(names, a),
                    DelayMs = Int(delays, a),
                    LockMs = Int(locks, a),
                    MuteStopSound = mutes != null && Bool(mutes, a),
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
                foreach (int i in Range(lightStart, lightCount, a, lightTrack.arraySize))
                    action.LightKeys.Add(new EffectLightKey { Track = Int(lightTrack, i), Time = Int(lightTime, i), Value = i < lightValue.arraySize ? lightValue.GetArrayElementAtIndex(i).colorValue : Color.white, Hold = Bool(lightHold, i) });
                action.LightLength = Int(lightLength, a);
                action.LightLoop = Bool(lightLoop, a);
                // 照明の項目が無かったころのデータは既定値（戻す、BET・レバーオンで止める）。
                action.LightRestore = a >= lightRestore.arraySize || Bool(lightRestore, a);
                for (int r = 0; r < 3; r++) action.LightDelay[r] = Int(lightDelay, a * 3 + r);
                if (a < stopMask.arraySize) action.StopMask = Int(stopMask, a);
                int winSet = a < winSetOf.arraySize ? Int(winSetOf, a) : -1;
                if (winSet >= 0 && winSet < m.WinSets.Count) action.WinSet = m.WinSets[winSet];
                m.Actions.Add(action);
            }

            var pNames = so.FindProperty("patternNames");
            var pIds = so.FindProperty("patternIds");
            var pNext = so.FindProperty("patternNext");
            var pKeep = so.FindProperty("patternKeepActive");
            var pLayerStart = so.FindProperty("patternLayerStart");
            var pLayerCount = so.FindProperty("patternLayerCount");
            var layerNames = so.FindProperty("layerNames");
            for (int p = 0; p < pNames.arraySize; p++)
            {
                var pattern = new EffectPattern { Name = Str(pNames, p), Id = Str(pIds, p), ResetOnLeave = !(pKeep != null && Bool(pKeep, p))};
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

            var wSet = so.FindProperty("winEntrySet");
            var wRole = so.FindProperty("winEntryRole");
            var wAction = so.FindProperty("winEntryAction");
            for (int i = 0; i < wSet.arraySize; i++)
            {
                int set = Int(wSet, i), action = Int(wAction, i);
                if (set < 0 || set >= m.WinSets.Count || action >= m.Actions.Count) continue;
                m.WinSets[set].Entries.Add(new EffectWinEntry { Role = Str(wRole, i), Action = action >= 0 ? m.Actions[action] : null });
            }
            LoadLottery(m, so);
            return m;
        }

        private static int MachineSettingCount(EffectPlayer player)
        {
            var controller = player.mainBoard != null ? player.mainBoard.reelOutputController : null;
            if (controller == null) return 0;
            var lottery = new SerializedObject(controller).FindProperty("lotteryTable");
            var table = lottery != null ? lottery.objectReferenceValue as LotteryTable : null;
            return table != null ? table.GetSettingCount() : 0;
        }

        private static void LoadLottery(EffectModel m, SerializedObject so)
        {
            var modeNames = so.FindProperty("effectModeNames");
            for (int i = 0; i < modeNames.arraySize; i++) m.Modes.Add(Str(modeNames, i));

            int stored = Mathf.Max(1, so.FindProperty("lotSettingCount").intValue);
            int machine = MachineSettingCount(m.Player);
            m.SettingCount = machine > 0 ? machine : stored;

            var names = so.FindProperty("lotNames");
            var ids = so.FindProperty("lotIds");
            var channels = so.FindProperty("lotChannel");
            var autos = so.FindProperty("lotAuto");
            var modes = so.FindProperty("lotMode");
            var sitStart = so.FindProperty("lotSituationStart");
            var sitCount = so.FindProperty("lotSituationCount");
            var situations = so.FindProperty("lotSituations");
            var cellStart = so.FindProperty("lotCellStart");
            var cellCount = so.FindProperty("lotCellCount");
            var cellFlag = so.FindProperty("lotCellFlag");
            var cellBonus = so.FindProperty("lotCellBonus");
            var rowStart = so.FindProperty("lotCellRowStart");
            var rowCount = so.FindProperty("lotCellRowCount");
            var perSetting = so.FindProperty("lotCellPerSetting");
            var rowPattern = so.FindProperty("lotRowPattern");
            var rowParameter = so.FindProperty("lotRowParameter");
            var rowValues = so.FindProperty("lotRowValues");

            for (int t = 0; t < ids.arraySize; t++)
            {
                int mode = Int(modes, t);
                var table = new EffectLotTable
                {
                    Name = Str(names, t), Id = Str(ids, t), Channel = Int(channels, t), Auto = Bool(autos, t),
                    Mode = t < modes.arraySize && mode >= 0 && mode < m.Modes.Count ? m.Modes[mode] : ""
                };
                foreach (int i in Range(sitStart, sitCount, t, situations.arraySize)) table.Situations.Add(Str(situations, i));
                foreach (int c in Range(cellStart, cellCount, t, cellFlag.arraySize))
                {
                    var cell = new EffectLotCell { Flag = Str(cellFlag, c), Bonus = Str(cellBonus, c), PerSetting = Bool(perSetting, c) };
                    foreach (int r in Range(rowStart, rowCount, c, rowPattern.arraySize))
                    {
                        int pattern = Int(rowPattern, r);
                        if (pattern >= m.Patterns.Count) continue;
                        var row = new EffectLotRow { Pattern = pattern >= 0 ? m.Patterns[pattern] : null, Parameter = Int(rowParameter, r), Values = new int[m.SettingCount] };
                        // 設定の数が変わっていたら、足りない分は最後の設定の値で埋める。
                        for (int s = 0; s < m.SettingCount; s++) row.Values[s] = Int(rowValues, r * stored + Mathf.Min(s, stored - 1));
                        cell.Rows.Add(row);
                    }
                    table.Cells.Add(cell);
                }
                m.LotTables.Add(table);
            }

            var pNextTable = so.FindProperty("patternNextTable");
            var pNextBonus = so.FindProperty("patternNextBonus");
            var pNextBonusTable = so.FindProperty("patternNextBonusTable");
            for (int p = 0; p < m.Patterns.Count; p++)
            {
                int nextTable = p < pNextTable.arraySize ? Int(pNextTable, p) : -1;
                int nextBonus = p < pNextBonus.arraySize ? Int(pNextBonus, p) : -1;
                int nextBonusTable = p < pNextBonusTable.arraySize ? Int(pNextBonusTable, p) : -1;
                if (m.Patterns[p].Next == null && nextTable >= 0 && nextTable < m.LotTables.Count) m.Patterns[p].NextTable = m.LotTables[nextTable];
                if (nextBonus >= 0 && nextBonus < m.Patterns.Count) m.Patterns[p].NextBonus = m.Patterns[nextBonus];
                else if (nextBonusTable >= 0 && nextBonusTable < m.LotTables.Count) m.Patterns[p].NextBonusTable = m.LotTables[nextBonusTable];
            }
        }

        private void SaveLottery(SerializedObject so)
        {
            var modeNames = Resize(so, "effectModeNames", Modes.Count);
            for (int i = 0; i < Modes.Count; i++) modeNames.GetArrayElementAtIndex(i).stringValue = Modes[i];
            int columns = Mathf.Max(1, SettingCount);
            so.FindProperty("lotSettingCount").intValue = columns;

            int tc = LotTables.Count, situationTotal = 0, cellTotal = 0, rowTotal = 0;
            foreach (var t in LotTables)
            {
                situationTotal += t.Situations.Count;
                cellTotal += t.Cells.Count;
                foreach (var c in t.Cells) rowTotal += c.Rows.Count;
            }
            var names = Resize(so, "lotNames", tc);
            var ids = Resize(so, "lotIds", tc);
            var channels = Resize(so, "lotChannel", tc);
            var autos = Resize(so, "lotAuto", tc);
            var modes = Resize(so, "lotMode", tc);
            var sitStart = Resize(so, "lotSituationStart", tc);
            var sitCount = Resize(so, "lotSituationCount", tc);
            var situations = Resize(so, "lotSituations", situationTotal);
            var cellStart = Resize(so, "lotCellStart", tc);
            var cellCount = Resize(so, "lotCellCount", tc);
            var cellFlag = Resize(so, "lotCellFlag", cellTotal);
            var cellBonus = Resize(so, "lotCellBonus", cellTotal);
            var rowStart = Resize(so, "lotCellRowStart", cellTotal);
            var rowCount = Resize(so, "lotCellRowCount", cellTotal);
            var perSetting = Resize(so, "lotCellPerSetting", cellTotal);
            var rowPattern = Resize(so, "lotRowPattern", rowTotal);
            var rowParameter = Resize(so, "lotRowParameter", rowTotal);
            var rowValues = Resize(so, "lotRowValues", rowTotal * columns);

            int si = 0, ci = 0, ri = 0;
            for (int t = 0; t < tc; t++)
            {
                var table = LotTables[t];
                names.GetArrayElementAtIndex(t).stringValue = table.Name;
                ids.GetArrayElementAtIndex(t).stringValue = table.Id;
                channels.GetArrayElementAtIndex(t).intValue = Mathf.Clamp(table.Channel, 0, EffectPlayer.ChannelCount - 1);
                autos.GetArrayElementAtIndex(t).boolValue = table.Auto;
                modes.GetArrayElementAtIndex(t).intValue = Modes.IndexOf(table.Mode);
                sitStart.GetArrayElementAtIndex(t).intValue = si;
                sitCount.GetArrayElementAtIndex(t).intValue = table.Situations.Count;
                foreach (string key in table.Situations) situations.GetArrayElementAtIndex(si++).stringValue = key;
                cellStart.GetArrayElementAtIndex(t).intValue = ci;
                cellCount.GetArrayElementAtIndex(t).intValue = table.Cells.Count;
                foreach (var cell in table.Cells)
                {
                    cellFlag.GetArrayElementAtIndex(ci).stringValue = cell.Flag ?? "";
                    cellBonus.GetArrayElementAtIndex(ci).stringValue = cell.Bonus ?? "";
                    perSetting.GetArrayElementAtIndex(ci).boolValue = cell.PerSetting;
                    rowStart.GetArrayElementAtIndex(ci).intValue = ri;
                    rowCount.GetArrayElementAtIndex(ci).intValue = cell.Rows.Count;
                    foreach (var row in cell.Rows)
                    {
                        rowPattern.GetArrayElementAtIndex(ri).intValue = row.Pattern != null ? Patterns.IndexOf(row.Pattern) : -1;
                        rowParameter.GetArrayElementAtIndex(ri).intValue = row.Parameter;
                        for (int s = 0; s < columns; s++)
                            rowValues.GetArrayElementAtIndex(ri * columns + s).intValue = Mathf.Max(0, cell.Value(row, s));
                        ri++;
                    }
                    ci++;
                }
            }

            int pc = Patterns.Count;
            var pNextTable = Resize(so, "patternNextTable", pc);
            var pNextBonus = Resize(so, "patternNextBonus", pc);
            var pNextBonusTable = Resize(so, "patternNextBonusTable", pc);
            for (int p = 0; p < pc; p++)
            {
                var pattern = Patterns[p];
                pNextTable.GetArrayElementAtIndex(p).intValue = pattern.Next == null && pattern.NextTable != null ? LotTables.IndexOf(pattern.NextTable) : -1;
                pNextBonus.GetArrayElementAtIndex(p).intValue = pattern.NextBonus != null ? Patterns.IndexOf(pattern.NextBonus) : -1;
                pNextBonusTable.GetArrayElementAtIndex(p).intValue = pattern.NextBonus == null && pattern.NextBonusTable != null ? LotTables.IndexOf(pattern.NextBonusTable) : -1;
            }
        }

        public void Save(string label)
        {
            Undo.RecordObject(Player, label);
            var so = new SerializedObject(Player);
            int n = Actions.Count;
            var names = Resize(so, "actionNames", n);
            var delays = Resize(so, "actionDelayMs", n);
            var locks = Resize(so, "actionLockMs", n);
            var mutes = Resize(so, "actionMuteStopSound", n);
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
            var lightStart = Resize(so, "actionLightStart", n);
            var lightCount = Resize(so, "actionLightCount", n);
            var lightLength = Resize(so, "actionLightLength", n);
            var lightLoop = Resize(so, "actionLightLoop", n);
            var lightRestore = Resize(so, "actionLightRestore", n);
            var lightDelay = Resize(so, "actionLightDelay", n * 3);
            var stopMask = Resize(so, "actionStopMask", n);
            var winSetOf = Resize(so, "actionWinSet", n);
            int lightKeys = 0;
            foreach (var a in Actions) lightKeys += a.LightKeys.Count;
            var lightTrack = Resize(so, "lightKeyTrack", lightKeys);
            var lightTime = Resize(so, "lightKeyTime", lightKeys);
            var lightValue = Resize(so, "lightKeyValue", lightKeys);
            var lightHold = Resize(so, "lightKeyHold", lightKeys);
            lightKeys = 0;

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
                mutes.GetArrayElementAtIndex(i).boolValue = a.MuteStopSound;
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
                lightStart.GetArrayElementAtIndex(i).intValue = lightKeys;
                lightCount.GetArrayElementAtIndex(i).intValue = a.LightKeys.Count;
                // 再生時はトラックごとに時間順に読むので、トラック順・時間順に並べる。
                var sortedLight = new List<EffectLightKey>(a.LightKeys);
                sortedLight.Sort((x, y) => x.Track != y.Track ? x.Track.CompareTo(y.Track) : x.Time.CompareTo(y.Time));
                foreach (var x in sortedLight)
                {
                    lightTrack.GetArrayElementAtIndex(lightKeys).intValue = x.Track;
                    lightTime.GetArrayElementAtIndex(lightKeys).intValue = Mathf.Max(0, x.Time);
                    lightValue.GetArrayElementAtIndex(lightKeys).colorValue = x.Value;
                    lightHold.GetArrayElementAtIndex(lightKeys).boolValue = x.Hold;
                    lightKeys++;
                }
                lightLength.GetArrayElementAtIndex(i).intValue = Mathf.Max(0, a.LightLength);
                lightLoop.GetArrayElementAtIndex(i).boolValue = a.LightLoop;
                lightRestore.GetArrayElementAtIndex(i).boolValue = a.LightRestore;
                for (int r = 0; r < 3; r++) lightDelay.GetArrayElementAtIndex(i * 3 + r).intValue = Mathf.Max(0, a.LightDelay[r]);
                stopMask.GetArrayElementAtIndex(i).intValue = a.StopMask;
                winSetOf.GetArrayElementAtIndex(i).intValue = a.WinSet != null ? WinSets.IndexOf(a.WinSet) : -1;
            }

            var setNames = Resize(so, "winSetNames", WinSets.Count);
            int winEntries = 0;
            foreach (var set in WinSets) winEntries += set.Entries.Count;
            var wSet = Resize(so, "winEntrySet", winEntries);
            var wRole = Resize(so, "winEntryRole", winEntries);
            var wAction = Resize(so, "winEntryAction", winEntries);
            winEntries = 0;
            for (int w = 0; w < WinSets.Count; w++)
            {
                setNames.GetArrayElementAtIndex(w).stringValue = WinSets[w].Name;
                foreach (var e in WinSets[w].Entries)
                {
                    int action = e.Action != null ? Actions.IndexOf(e.Action) : -1;
                    if (e.Action != null && action < 0) continue;
                    wSet.GetArrayElementAtIndex(winEntries).intValue = w;
                    wRole.GetArrayElementAtIndex(winEntries).stringValue = e.Role ?? "";
                    wAction.GetArrayElementAtIndex(winEntries).intValue = action;
                    winEntries++;
                }
            }
            Resize(so, "winEntrySet", winEntries);
            Resize(so, "winEntryRole", winEntries);
            Resize(so, "winEntryAction", winEntries);

            int pc = Patterns.Count;
            var pNames = Resize(so, "patternNames", pc);
            var pIds = Resize(so, "patternIds", pc);
            var pNext = Resize(so, "patternNext", pc);
            var pKeep = Resize(so, "patternKeepActive", pc);
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
            SaveLottery(so);
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
            foreach (var set in WinSets) set.Entries.RemoveAll(e => e.Action == action);
        }

        public List<string> WinSetNames()
        {
            var list = new List<string>();
            foreach (var set in WinSets) list.Add(set.Name);
            return list;
        }

        public void RemoveWinSet(EffectWinSet set)
        {
            WinSets.Remove(set);
            foreach (var a in Actions) if (a.WinSet == set) a.WinSet = null;
        }

        public void RemovePattern(EffectPattern pattern)
        {
            Patterns.Remove(pattern);
            foreach (var p in Patterns)
            {
                if (p.Next == pattern) p.Next = null;
                if (p.NextBonus == pattern) p.NextBonus = null;
            }
            foreach (var t in LotTables)
                foreach (var c in t.Cells) c.Rows.RemoveAll(r => r.Pattern == pattern);
        }

        public List<string> LotTableNames()
        {
            var list = new List<string>();
            foreach (var t in LotTables) list.Add(t.Name);
            return list;
        }

        public List<string> LotTableIds()
        {
            var list = new List<string>();
            foreach (var t in LotTables) list.Add(t.Id);
            return list;
        }

        public void RemoveLotTable(EffectLotTable table)
        {
            LotTables.Remove(table);
            foreach (var p in Patterns)
            {
                if (p.NextTable == table) p.NextTable = null;
                if (p.NextBonusTable == table) p.NextBonusTable = null;
            }
        }

        public void RenameMode(string oldName, string newName)
        {
            int index = Modes.IndexOf(oldName);
            if (index < 0) return;
            Modes[index] = newName;
            foreach (var t in LotTables) if (t.Mode == oldName) t.Mode = newName;
        }

        public void RemoveMode(string name)
        {
            Modes.Remove(name);
            foreach (var t in LotTables) if (t.Mode == name) t.Mode = "";
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
