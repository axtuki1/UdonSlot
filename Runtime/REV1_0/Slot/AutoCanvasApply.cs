
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace AX.UdonSlot.REV1_0.Common
{
    /// <summary>
    /// カメラ用RenderTextureを生成して対象Materialへ割り当てる。
    /// ローカルプレイヤーとの距離に応じたカメラのエコ動作も管理する。
    /// </summary>
    public class AutoCanvasApply : UdonSharpBehaviour
    {
        [SerializeField]
        private Camera referenceCamera;
        
        [SerializeField]
        private Vector2 referenceResolution = new Vector2(1920, 1080);
        
        [SerializeField]
        private Material[] materials;
        
        [SerializeField]
        private Material[] emissionMaterials;
        
        [SerializeField]
        private MeshRenderer[] meshRenderersForMainTex;

        [SerializeField]
        private int[] rendererForMainTexMaterialIndex;
        
        [SerializeField]
        private MeshRenderer[] meshRenderersForEmissionMap;

        [SerializeField]
        private int[] rendererForEmissionMapMaterialIndex;
        
        [SerializeField]
        private bool isRenderTextureFormatDefault = false;
        
        [SerializeField]
        private RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGB32;

        [SerializeField]
        private int renderTextureDepth = 24;
        
        private RenderTexture renderTexture;
        
        private bool isEco = false;
        
        [SerializeField]
        private float ecoDistance = 10f;
        
        [SerializeField]
        protected GameObject ecoView;
        
        private VRCPlayerApi localPlayer;
        
        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            SendCustomEventDelayedSeconds(nameof(Apply), 0.1f);
        }

        public void Apply()
        {
            if (isRenderTextureFormatDefault)
            {
                renderTexture = new RenderTexture(
                    (int)referenceResolution.x, 
                    (int)referenceResolution.y, 
                    renderTextureDepth
                    );
            }
            else
            {
                renderTexture = new RenderTexture(
                    (int)referenceResolution.x, 
                    (int)referenceResolution.y, 
                    renderTextureDepth,
                    renderTextureFormat
                    );
            }
            renderTexture.Create();

            ApplyTextureToMaterials(materials, "_MainTex");
            ApplyTextureToMaterials(emissionMaterials, "_EmissionMap");
            ApplyTextureToRenderers(meshRenderersForMainTex, rendererForMainTexMaterialIndex, "_MainTex");
            ApplyTextureToRenderers(meshRenderersForEmissionMap, rendererForEmissionMapMaterialIndex, "_EmissionMap");
            
            referenceCamera.targetTexture = renderTexture;
        }

        private void ApplyTextureToMaterials(Material[] targetMaterials, string propertyName)
        {
            for (int i = 0; i < targetMaterials.Length; i++)
            {
                targetMaterials[i].SetTexture(propertyName, renderTexture);
            }
        }

        private void ApplyTextureToRenderers(MeshRenderer[] renderers, int[] materialIndexes, string propertyName)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].materials[materialIndexes[i]].SetTexture(propertyName, renderTexture);
            }
        }

        private void Update()
        {
            if (!Utilities.IsValid(localPlayer)) return;
            // エコモード
            isEco = Vector3.Distance(localPlayer.GetPosition(), transform.position) > ecoDistance;
            if (isEco && referenceCamera.enabled && ecoView.activeSelf)
            {
                // エコモード + カメラ有効 + エコビュー表示
                //  -> カメラを無効
                referenceCamera.enabled = false;
            } else if (!isEco)
            {
                referenceCamera.enabled = true;
            }
            ecoView.SetActive(isEco);
        }
    }

}
