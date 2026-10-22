using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>RT・ボーナスの状態遷移を図で見るウィンドウ（表示のみ。ノードは動かせる）。</summary>
    public sealed class RtFlowWindow : EditorWindow
    {
        private const float NodeW = 150, NodeH = 44;
        private ReelRuleEditorWindow owner;
        private Vector2 pan = new Vector2(80, 80);
        private float zoom = 1f;
        private string dragging;
        private Vector2 dragOffset;
        private bool panning;
        private readonly Dictionary<string, Vector2> positions = new Dictionary<string, Vector2>();

        private sealed class Node
        {
            public string Key;
            public string Label;
            public string Sub;
            public Color Back;
            public Color Text;
        }

        private sealed class Edge
        {
            public string From;
            public string To;
            public string Label;
            public Color Color;
        }

        public static void Open(ReelRuleEditorWindow owner)
        {
            var window = GetWindow<RtFlowWindow>("RTフロー図");
            window.owner = owner;
            window.minSize = new Vector2(480, 320);
            window.LoadPositions();
            window.Show();
        }

        private ReelRuleEditorWindow Owner
        {
            get
            {
                if (owner == null)
                {
                    var found = Resources.FindObjectsOfTypeAll<ReelRuleEditorWindow>();
                    if (found.Length > 0) owner = found[0];
                }
                return owner;
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            var o = Owner;
            var table = o != null ? o.FlowBonusTable : null;
            if (table == null) return;
            var model = BonusModel.Load(table);
            List<Node> nodes;
            List<Edge> edges;
            Build(model, o.FlowPayoutTable, out nodes, out edges);
            if (positions.Count == 0) LoadPositions();
            bool placed = false;
            foreach (var n in nodes) if (!positions.ContainsKey(n.Key)) { AutoLayout(nodes, edges); placed = true; break; }

            DrawToolbar(nodes, edges);
            var area = new Rect(0, 22, position.width, position.height - 22);
            EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));
            GUI.BeginClip(area);
            var local = new Rect(0, 0, area.width, area.height);
            DrawGrid(local);
            DrawEdges(edges);
            foreach (var n in nodes) DrawNode(n);
            HandleInput(local, nodes);
            GUI.EndClip();
            if (placed) SavePositions();
        }

        private void DrawToolbar(List<Node> nodes, List<Edge> edges)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("自動配置", EditorStyles.toolbarButton, GUILayout.Width(64))) { AutoLayout(nodes, edges); SavePositions(); Fit(nodes); }
                if (GUILayout.Button("全体表示", EditorStyles.toolbarButton, GUILayout.Width(64))) Fit(nodes);
                GUILayout.Space(8);
                if (GUILayout.Button("−", EditorStyles.toolbarButton, GUILayout.Width(22))) ZoomAt(new Vector2(position.width / 2, (position.height - 22) / 2), zoom / 1.2f);
                GUILayout.Label(Mathf.RoundToInt(zoom * 100) + "%", EditorStyles.miniLabel, GUILayout.Width(40));
                if (GUILayout.Button("＋", EditorStyles.toolbarButton, GUILayout.Width(22))) ZoomAt(new Vector2(position.width / 2, (position.height - 22) / 2), zoom * 1.2f);
                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------ 図の中身

        private static void Build(BonusModel model, PayoutTable payout, out List<Node> nodes, out List<Edge> edges)
        {
            nodes = new List<Node>();
            edges = new List<Edge>();
            var stateBack = new Color(0.23f, 0.23f, 0.23f);
            var stateText = new Color(0.88f, 0.88f, 0.88f);
            var symbolColor = new Color(0.45f, 0.75f, 0.52f);
            var gameColor = new Color(0.94f, 0.77f, 0.42f);
            var bonusColor = new Color(0.96f, 0.55f, 0.62f);
            var endColor = new Color(0.65f, 0.65f, 0.65f);
            bool any = false;

            for (int s = 0; s < model.States.Count; s++)
            {
                var state = model.States[s];
                nodes.Add(new Node
                {
                    Key = "s:" + state.Name,
                    Label = state.Label,
                    Sub = s == 0 ? "基底" : state.GameLimit > 0 ? state.GameLimit + "G" : "",
                    Back = s == 0 ? new Color(0.18f, 0.30f, 0.45f) : stateBack,
                    Text = stateText
                });
            }
            for (int b = 0; b < model.Bonuses.Count; b++)
            {
                var bonus = model.Bonuses[b];
                if (bonus.StartRole.Length == 0) continue;
                bool big = bonus.Kind == BonusKind.BB || bonus.Kind == BonusKind.MB;
                nodes.Add(new Node
                {
                    Key = "b:" + bonus.Name,
                    Label = bonus.Label + "作動中",
                    Sub = bonus.Kind.ToString(),
                    Back = big ? new Color(0.36f, 0.12f, 0.16f) : new Color(0.12f, 0.23f, 0.36f),
                    Text = big ? new Color(0.96f, 0.65f, 0.71f) : new Color(0.65f, 0.80f, 0.96f)
                });
                string from = bonus.WonState >= 0 && bonus.WonState < model.States.Count ? "s:" + model.States[bonus.WonState].Name : "any";
                if (from == "any") any = true;
                else edges.Add(new Edge { From = "any", To = from, Label = "成立 " + bonus.Label, Color = bonusColor });
                if (bonus.WonState >= 0) any = true;
                edges.Add(new Edge { From = from, To = "b:" + bonus.Name, Label = Role(payout, bonus.StartRole), Color = bonusColor });
                int end = bonus.EndState >= 0 && bonus.EndState < model.States.Count ? bonus.EndState : 0;
                if (model.States.Count > 0)
                    edges.Add(new Edge { From = "b:" + bonus.Name, To = "s:" + model.States[end].Name, Label = "終了", Color = endColor });
            }
            foreach (var m in model.Moves)
            {
                if (m.To < 0 || m.To >= model.States.Count) continue;
                string label = Role(payout, m.Role) + (m.OnlyFlag.Length > 0 ? "（" + m.OnlyFlag + "）" : "");
                string from = m.From >= 0 && m.From < model.States.Count ? "s:" + model.States[m.From].Name : "any";
                if (from == "any") any = true;
                edges.Add(new Edge { From = from, To = "s:" + model.States[m.To].Name, Label = label, Color = symbolColor });
            }
            for (int s = 1; s < model.States.Count; s++)
                if (model.States[s].GameLimit > 0)
                    edges.Add(new Edge { From = "s:" + model.States[s].Name, To = "s:" + model.States[0].Name, Label = model.States[s].GameLimit + "G", Color = gameColor });
            if (any)
                nodes.Insert(0, new Node { Key = "any", Label = "すべて", Sub = "", Back = new Color(0.2f, 0.2f, 0.2f), Text = new Color(0.6f, 0.6f, 0.6f) });
        }

        private static string Role(PayoutTable payout, string role)
        {
            return payout != null ? payout.GetWinPairDisplayName(role) : role;
        }

        // ------------------------------------------------------------ 描画

        private Rect NodeRect(string key)
        {
            Vector2 p;
            if (!positions.TryGetValue(key, out p)) p = Vector2.zero;
            return new Rect(pan.x + p.x * zoom, pan.y + p.y * zoom, NodeW * zoom, NodeH * zoom);
        }

        private void DrawGrid(Rect rect)
        {
            float step = 40 * zoom;
            if (step < 8) return;
            var color = new Color(0.19f, 0.19f, 0.19f);
            for (float x = Mathf.Repeat(pan.x, step); x < rect.width; x += step) EditorGUI.DrawRect(new Rect(x, 0, 1, rect.height), color);
            for (float y = Mathf.Repeat(pan.y, step); y < rect.height; y += step) EditorGUI.DrawRect(new Rect(0, y, rect.width, 1), color);
        }

        private GUIStyle Style(int size, FontStyle font, Color color)
        {
            var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(6, Mathf.RoundToInt(size * zoom)), fontStyle = font, clipping = TextClipping.Clip };
            style.normal.textColor = color;
            return style;
        }

        private void DrawNode(Node n)
        {
            var rect = NodeRect(n.Key);
            EditorGUI.DrawRect(rect, n.Back);
            var border = new Color(0.08f, 0.08f, 0.08f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), border);
            if (string.IsNullOrEmpty(n.Sub))
            {
                GUI.Label(rect, n.Label, Style(12, FontStyle.Bold, n.Text));
                return;
            }
            GUI.Label(new Rect(rect.x, rect.y + 2 * zoom, rect.width, rect.height * 0.6f), n.Label, Style(12, FontStyle.Bold, n.Text));
            GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.55f, rect.width, rect.height * 0.4f), n.Sub, Style(10, FontStyle.Normal, new Color(n.Text.r, n.Text.g, n.Text.b, 0.7f)));
        }

        /// <summary>
        /// 線は上下の辺から出入りさせ、同じ辺に付く線は相手のノードの左右の並びで辺に沿って振り分ける。
        /// 下にあるノードへは下辺→上辺、上にあるノードへは上辺→下辺、同じ段どうしは上辺から弧で結ぶ。
        /// </summary>
        private void DrawEdges(List<Edge> all)
        {
            var visible = new List<Edge>();
            foreach (var e in all) if (positions.ContainsKey(e.From) && positions.ContainsKey(e.To) && e.From != e.To) visible.Add(e);

            // 辺ごとの接続点: key = ノード + 辺, value = (線, 始点側か)
            var sides = new Dictionary<string, List<KeyValuePair<Edge, bool>>>();
            var fromBottom = new Dictionary<Edge, bool>();
            var toTop = new Dictionary<Edge, bool>();
            foreach (var e in visible)
            {
                var a = NodeRect(e.From);
                var b = NodeRect(e.To);
                float dy = b.center.y - a.center.y;
                bool down = dy > a.height * 0.75f, up = dy < -a.height * 0.75f;
                fromBottom[e] = down;
                toTop[e] = !up;
                AddPort(sides, e.From + (down ? "|b" : "|t"), e, true);
                AddPort(sides, e.To + (up ? "|b" : "|t"), e, false);
            }

            var ports = new Dictionary<string, Vector2>();
            foreach (var pair in sides)
            {
                string key = pair.Key.Substring(0, pair.Key.Length - 2);
                bool bottom = pair.Key.EndsWith("|b");
                var rect = NodeRect(key);
                var list = pair.Value;
                list.Sort((x, y) => OtherX(x).CompareTo(OtherX(y)));
                for (int i = 0; i < list.Count; i++)
                {
                    float px = rect.x + rect.width * (i + 1) / (list.Count + 1);
                    ports[PortKey(list[i].Key, list[i].Value)] = new Vector2(px, bottom ? rect.yMax : rect.y);
                }
            }

            var labels = new List<KeyValuePair<Rect, KeyValuePair<GUIContent, GUIStyle>>>();
            foreach (var e in visible)
            {
                Vector2 start = ports[PortKey(e, true)], end = ports[PortKey(e, false)];
                Vector2 startDir = fromBottom[e] ? Vector2.up : Vector2.down;   // GUI座標は下が+y
                Vector2 endDir = toTop[e] ? Vector2.down : Vector2.up;           // 終点の辺の外向き
                float reach = Mathf.Max(40 * zoom, Mathf.Abs(end.y - start.y) * 0.5f);
                Vector2 c1 = start + startDir * reach, c2 = end + endDir * reach;
                Handles.BeginGUI();
                Handles.DrawBezier(start, end, c1, c2, e.Color, null, 2f);
                Handles.color = e.Color;
                Vector2 into = -endDir;
                Vector2 side = new Vector2(-into.y, into.x);
                Handles.DrawAAConvexPolygon(end, end - into * 10 * zoom + side * 5 * zoom, end - into * 10 * zoom - side * 5 * zoom);
                Handles.EndGUI();

                var style = Style(10, FontStyle.Normal, e.Color);
                var content = new GUIContent(e.Label);
                var size = style.CalcSize(content);
                // ラベルは線の中ほどに置き、ほかのラベルと重なるなら線に沿ってずらす。
                Rect labelRect = default(Rect);
                foreach (float t in LabelPositions)
                {
                    Vector2 p = Bezier(start, c1, c2, end, t);
                    labelRect = new Rect(p.x - size.x / 2 - 3, p.y - size.y / 2, size.x + 6, size.y);
                    bool hit = false;
                    foreach (var other in labels) if (other.Key.Overlaps(labelRect)) { hit = true; break; }
                    if (!hit) break;
                }
                labels.Add(new KeyValuePair<Rect, KeyValuePair<GUIContent, GUIStyle>>(labelRect, new KeyValuePair<GUIContent, GUIStyle>(content, style)));
            }
            // 線の上にラベルを描く。
            foreach (var label in labels)
            {
                EditorGUI.DrawRect(label.Key, new Color(0.13f, 0.13f, 0.13f, 0.9f));
                GUI.Label(label.Key, label.Value.Key, label.Value.Value);
            }
        }

        private static readonly float[] LabelPositions = { 0.5f, 0.35f, 0.65f, 0.25f, 0.75f, 0.15f, 0.85f };

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        private static void AddPort(Dictionary<string, List<KeyValuePair<Edge, bool>>> sides, string key, Edge e, bool isStart)
        {
            List<KeyValuePair<Edge, bool>> list;
            if (!sides.TryGetValue(key, out list)) sides[key] = list = new List<KeyValuePair<Edge, bool>>();
            list.Add(new KeyValuePair<Edge, bool>(e, isStart));
        }

        private float OtherX(KeyValuePair<Edge, bool> port)
        {
            return NodeRect(port.Value ? port.Key.To : port.Key.From).center.x;
        }

        private static string PortKey(Edge e, bool isStart)
        {
            return e.GetHashCode() + (isStart ? ">" : "<");
        }

        // ------------------------------------------------------------ 操作

        private void HandleInput(Rect area, List<Node> nodes)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt)
                    {
                        for (int i = nodes.Count - 1; i >= 0; i--)
                        {
                            var rect = NodeRect(nodes[i].Key);
                            if (!rect.Contains(e.mousePosition)) continue;
                            dragging = nodes[i].Key;
                            dragOffset = e.mousePosition - rect.position;
                            e.Use();
                            return;
                        }
                    }
                    if (e.button == 2 || e.button == 0)
                    {
                        panning = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (dragging != null)
                    {
                        var p = (e.mousePosition - dragOffset - pan) / zoom;
                        positions[dragging] = p;
                        e.Use();
                    }
                    else if (panning)
                    {
                        pan += e.delta;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (dragging != null) SavePositions();
                    dragging = null;
                    panning = false;
                    break;
                case EventType.ScrollWheel:
                    ZoomAt(e.mousePosition, zoom * (e.delta.y > 0 ? 1 / 1.1f : 1.1f));
                    e.Use();
                    break;
            }
        }

        private void ZoomAt(Vector2 point, float next)
        {
            next = Mathf.Clamp(next, 0.3f, 2.5f);
            // point の下にある図の位置を動かさずに拡大縮小する
            Vector2 world = (point - pan) / zoom;
            zoom = next;
            pan = point - world * zoom;
            Repaint();
        }

        private void Fit(List<Node> nodes)
        {
            if (nodes.Count == 0) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in nodes)
            {
                Vector2 p;
                if (!positions.TryGetValue(n.Key, out p)) continue;
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x + NodeW);
                maxY = Mathf.Max(maxY, p.y + NodeH);
            }
            float w = position.width - 80, h = position.height - 22 - 80;
            zoom = Mathf.Clamp(Mathf.Min(w / Mathf.Max(1, maxX - minX), h / Mathf.Max(1, maxY - minY)), 0.3f, 1.5f);
            pan = new Vector2(40 - minX * zoom + (w - (maxX - minX) * zoom) / 2, 40 - minY * zoom + (h - (maxY - minY) * zoom) / 2);
            Repaint();
        }

        /// <summary>
        /// 上段に状態と「すべて」（右端）、下段に作動中のボーナス。
        /// ボーナスは成立元のノードの真下あたりに並べ、線が交差しにくいようにする。
        /// </summary>
        private void AutoLayout(List<Node> nodes, List<Edge> edges)
        {
            int column = 0;
            foreach (var n in nodes)
                if (n.Key.StartsWith("s:")) positions[n.Key] = new Vector2(column++ * (NodeW + 70), 0);
            foreach (var n in nodes)
                if (n.Key == "any") positions[n.Key] = new Vector2(column++ * (NodeW + 70), 0);

            var bonusNodes = new List<Node>();
            var order = new Dictionary<string, float>();
            foreach (var n in nodes)
            {
                if (!n.Key.StartsWith("b:")) continue;
                bonusNodes.Add(n);
                float x = 0;
                foreach (var e in edges)
                    if (e.To == n.Key && positions.ContainsKey(e.From)) { x = positions[e.From].x; break; }
                order[n.Key] = x;
            }
            bonusNodes.Sort((a, b) => order[a.Key].CompareTo(order[b.Key]));
            float next = 0;
            foreach (var n in bonusNodes)
            {
                float x = Mathf.Max(next, order[n.Key] - (NodeW + 40) * 0.5f);
                positions[n.Key] = new Vector2(x, 170);
                next = x + NodeW + 40;
            }
        }

        private void LoadPositions()
        {
            var o = Owner;
            positions.Clear();
            var list = o != null ? o.FlowPositions : null;
            if (list == null) return;
            foreach (var p in list) positions[p.key] = new Vector2(p.x, p.y);
        }

        private void SavePositions()
        {
            var o = Owner;
            if (o == null) return;
            var list = new List<FlowNodePosition>();
            foreach (var pair in positions) list.Add(new FlowNodePosition { key = pair.Key, x = pair.Value.x, y = pair.Value.y });
            o.SetFlowPositions(list);
        }
    }
}
