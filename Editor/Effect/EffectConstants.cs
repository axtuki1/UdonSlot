using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>パターンの内部名を定数にしたファイルを書き出す（機種コードで打ち間違えないように）。</summary>
    internal static class EffectConstants
    {
        /// <summary>書き出し先を選んで書き出す。</summary>
        public static void Choose(EffectPlayer player, EffectModel model)
        {
            string current = player.constantsPath ?? "";
            string dir = current.Length > 0 ? Path.GetDirectoryName(current) : "Assets";
            string name = current.Length > 0 ? Path.GetFileNameWithoutExtension(current) : "EffectPatterns";
            string path = EditorUtility.SaveFilePanelInProject("定数ファイル", name, "cs", "", dir);
            if (string.IsNullOrEmpty(path)) return;
            var so = new SerializedObject(player);
            so.FindProperty("constantsPath").stringValue = path;
            so.ApplyModifiedProperties();
            Write(player, model);
        }

        /// <summary>書き出し先があり、ファイルの中身が今のパターンと違う（未書き出しを含む）。</summary>
        public static bool IsStale(EffectPlayer player, EffectModel model)
        {
            string path = player != null ? player.constantsPath : null;
            if (string.IsNullOrEmpty(path) || model == null) return false;
            return !File.Exists(path) || File.ReadAllText(path) != Text(path, model);
        }

        private static string Text(string path, EffectModel model)
        {
            return Build(Identifier(Path.GetFileNameWithoutExtension(path), new HashSet<string>()), model);
        }

        /// <summary>書き出し先が決まっていれば書き出す。中身が変わらなければ書かない（再コンパイルを避ける）。</summary>
        public static void Write(EffectPlayer player, EffectModel model)
        {
            string path = player != null ? player.constantsPath : null;
            if (string.IsNullOrEmpty(path) || model == null) return;
            string text = Text(path, model);
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, new UTF8Encoding(true));
            AssetDatabase.ImportAsset(path);
        }

        /// <summary>
        /// パターンは「ファイル名」のクラス。抽選テーブルは「ファイル名＋Tables」、演出モードは「ファイル名＋Modes」のクラス（あるときだけ出す）。
        /// </summary>
        private static string Build(string className, EffectModel model)
        {
            var sb = new StringBuilder();
            sb.Append("// 演出エディタが書き出したファイルです。演出エディタの「定数ファイル書き出し」で書き直されます。\n");
            var entries = new List<KeyValuePair<string, string>>();
            foreach (var pattern in model.Patterns) entries.Add(new KeyValuePair<string, string>(pattern.Id, pattern.Name));
            AppendClass(sb, className, entries);
            if (model.LotTables.Count > 0)
            {
                entries.Clear();
                foreach (var table in model.LotTables) entries.Add(new KeyValuePair<string, string>(table.Id, table.Name));
                sb.Append("\n");
                AppendClass(sb, className + "Tables", entries);
            }
            if (model.Modes.Count > 0)
            {
                entries.Clear();
                foreach (string mode in model.Modes) entries.Add(new KeyValuePair<string, string>(mode, mode));
                sb.Append("\n");
                AppendClass(sb, className + "Modes", entries);
            }
            return sb.ToString();
        }

        private static void AppendClass(StringBuilder sb, string className, List<KeyValuePair<string, string>> entries)
        {
            sb.Append("public static class ").Append(className).Append("\n{\n");
            var used = new HashSet<string>();
            foreach (var entry in entries)
            {
                string name = (entry.Value ?? "").Replace("\r", " ").Replace("\n", " ").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
                sb.Append("    /// <summary>").Append(name).Append("</summary>\n");
                sb.Append("    public const string ").Append(Identifier(entry.Key, used)).Append(" = \"")
                  .Append(entry.Key.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\";\n");
            }
            sb.Append("}\n");
        }

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
            "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
            "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long",
            "namespace","new","null","object","operator","out","override","params","private","protected","public",
            "readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch",
            "this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void",
            "volatile","while"
        };

        /// <summary>C# の名前に使えない文字を _ にする。重なったら _2, _3 …。</summary>
        private static string Identifier(string name, HashSet<string> used)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string id = sb.ToString().TrimEnd('_');
            if (id.Length == 0) id = "_";
            if (char.IsDigit(id[0])) id = "_" + id;
            if (Keywords.Contains(id)) id += "_";
            string unique = id;
            for (int n = 2; used.Contains(unique); n++) unique = id + "_" + n;
            used.Add(unique);
            return unique;
        }
    }
}
