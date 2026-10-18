using UnityEngine;

namespace AX.UdonSlot.REV1_0.Editor.ReelRule
{
    /// <summary>機種エディタの設計データ（出目と制御）を保存するアセット。</summary>
    [CreateAssetMenu(menuName = "UdonSlot/リール制御設計", fileName = "ReelDesign")]
    public sealed class ReelDesignAsset : ScriptableObject
    {
        public ReelDesign design = new ReelDesign();
        /// <summary>3リールを横に並べ、各リールの0番を上にしたリールテクスチャ。図柄の表示に使う。</summary>
        public Texture2D reelTexture;
    }
}
