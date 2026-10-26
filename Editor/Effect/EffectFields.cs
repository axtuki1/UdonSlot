using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.Effect
{
    /// <summary>
    /// 数値の入力欄。全角数字（IME が全角のまま）でも読めるようにする。
    /// 読めない入力は元の値のまま（0 にしない）。
    /// </summary>
    internal static class EffectFields
    {
        internal static int Int(int value, params GUILayoutOption[] options)
        {
            string text = EditorGUILayout.DelayedTextField(value.ToString(CultureInfo.InvariantCulture), options);
            float parsed;
            return TryParse(text, out parsed) ? Mathf.RoundToInt(parsed) : value;
        }

        internal static int Int(string label, int value, params GUILayoutOption[] options)
        {
            string text = EditorGUILayout.DelayedTextField(label, value.ToString(CultureInfo.InvariantCulture), options);
            float parsed;
            return TryParse(text, out parsed) ? Mathf.RoundToInt(parsed) : value;
        }

        internal static float Float(float value, params GUILayoutOption[] options)
        {
            string text = EditorGUILayout.DelayedTextField(value.ToString(CultureInfo.InvariantCulture), options);
            float parsed;
            return TryParse(text, out parsed) ? parsed : value;
        }

        private static bool TryParse(string text, out float value)
        {
            string normalized = (text ?? "").Normalize(NormalizationForm.FormKC).Replace("ー", "-").Replace("−", "-").Trim();
            return float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
