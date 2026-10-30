using UdonSharp;

namespace AX.UdonSlot.REV1_0
{
    /// <summary>
    /// フラグ抽選、自動滑り制御、成立役判定、払出計算を提供する基礎Controller。
    /// 告知、演出などの機種固有処理は派生クラスで追加する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class BasicReelOutputController : ReelOutputController
    {
    }
}
