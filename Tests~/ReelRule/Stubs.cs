// Runtime の ReelControlData.cs を Unity なしでコンパイルするための最小スタブ。
namespace UdonSharp { public class UdonSharpBehaviour { } }
namespace UnityEngine
{
    public class HideInInspector : System.Attribute { }
    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) { return value < min ? min : value > max ? max : value; }
    }
}
namespace VRC.Udon.Serialization.OdinSerializer { public class OdinSerialize : System.Attribute { } }
