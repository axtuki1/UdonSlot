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
            foreach (var n in nodes) if (!positions.ContainsKey(n.Key)) { AutoLayout(nodes); placed = true; break; }

            DrawToolbar(nodes);
            var area = new Rect(0, 22, position.width, position.height - 22);
            EditorGUI.DrawRect(area, new Color(0.16f, 0.16f, 0.16f));
            GUI.BeginClip(area);
            var local = new Rect(0, 0, area.width, area.height);
            DrawGrid(local);
            foreach (var e in edges) DrawEdge(e, edges);
            foreach (var n in nodes) DrawNode(n);
            HandleInput(local, nodes);
            GUI.EndClip();
            if (placed) SavePositions();
        }

        private void DrawToolbar(List<Node> nodes)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("自動配置", EditorStyles.toolbarButton, GUILayout.Width(64))) { AutoLayout(nodes); SavePositions(); }
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

        private void DrawEdge(Edge e, List<Edge> all)
        {
            if (!positions.ContainsKey(e.From) || !positions.ContainsKey(e.To)) return;
            var a = NodeRect(e.From);
            var b = NodeRect(e.To);
            // 同じ2ノード間の線は少しずらす
            int lane = 0, lanes = 0;
            foreach (var other in all)
            {
                if ((other.From == e.From && other.To == e.To) || (other.From == e.To && other.To == e.From))
                {
                    if (other == e) lane = lanes;
                    lanes++;
                }
            }
            Vector2 start = a.center, end = b.center;
            Vector2 dir = (end - start).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            float offset = (lane - (lanes - 1) / 2f) * 16 * zoom;
            if (e.From.CompareTo(e.To) > 0) offset = -offset;
            start = Edgepoint(a, end) + normal * offset;
            end = Edgepoint(b, start) + normal * offset;
            Handles.BeginGUI();
            Handles.color = e.Color;
            Handles.DrawAAPolyLine(2f, start, end);
            Vector2 tip = end;
            Vector2 left = tip - dir * 10 * zoom + normal * 5 * zoom;
            Vector2 right = tip - dir * 10 * zoom - normal * 5 * zoom;
            Handles.DrawAAConvexPolygon(tip, left, right);
            Handles.EndGUI();

            var style = Style(10, FontStyle.Normal, e.Color);
            var content = new GUIContent(e.Label);
            var size = style.CalcSize(content);
            Vector2 mid = (start + end) / 2;
            var labelRect = new Rect(mid.x - size.x / 2 - 3, mid.y - size.y / 2, size.x + 6, size.y);
            EditorGUI.DrawRect(labelRect, new Color(0.13f, 0.13f, 0.13f, 0.9f));
            GUI.Label(labelRect, content, style);
        }

        /// <summary>ノードの枠と、中心から target へ向かう線の交点。</summary>
        private static Vector2 Edgepoint(Rect rect, Vector2 target)
        {
            Vector2 c = rect.center;
            Vector2 d = target - c;
            if (d.sqrMagnitude < 0.01f) return c;
            float sx = d.x != 0 ? (rect.width / 2) / Mathf.Abs(d.x) : float.MaxValue;
            float sy = d.y != 0 ? (rect.height / 2) / Mathf.Abs(d.y) : float.MaxValue;
            return c + d * Mathf.Min(sx, sy);
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

        /// <summary>上段に「すべて」、中段に状態、下段に作動中のボーナス。</summary>
        private void AutoLayout(List<Node> nodes)
        {
            int states = 0, bonuses = 0;
            foreach (var n in nodes)
            {
                if (n.Key == "any") positions[n.Key] = new Vector2(0, 0);
                else if (n.Key.StartsWith("s:")) positions[n.Key] = new Vector2(states++ * (NodeW + 70), 130);
                else positions[n.Key] = new Vector2(bonuses++ * (NodeW + 40), 280);
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
