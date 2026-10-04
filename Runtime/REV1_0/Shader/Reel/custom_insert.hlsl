// UV0 runs from the last symbol at Y=0 to the first symbol at Y=1.
// SubBoard compensates a full revolution, so this also works for non-multiples of 3.
float udonSlotSymbolLight(float2 uv)
{
    float row = floor((1.0 - frac(uv.y + _ReelSymbolWindow.y)) * max(_ReelSymbolWindow.x, 3.0));
    if (row < 1.0) return saturate(_ReelSymbolLights.x);
    if (row < 2.0) return saturate(_ReelSymbolLights.y);
    if (row < 3.0) return saturate(_ReelSymbolLights.z);
    return 1.0;
}

// Gate only the symbol backlight (Emission1st). Cabinet lighting (Emission2nd) stays independent.
#if defined(LIL_FEATURE_EMISSION_1ST) && !defined(LIL_LITE)
    #define OVERRIDE_EMISSION_1ST \
        { \
            float3 reelBeforeEmission = fd.col.rgb; \
            lilEmission(fd LIL_SAMP_IN(sampler_MainTex)); \
            fd.col.rgb = lerp(reelBeforeEmission, fd.col.rgb, udonSlotSymbolLight(fd.uv0)); \
        }
#endif

// Emission2nd adapted from lilToon (MIT, lilxyzw).
// Keep standard texture, mask, blink, AudioLink and blend behavior.
#if defined(LIL_FEATURE_EMISSION_2ND) && !defined(LIL_LITE)
    void udonSlotReelEmission2nd(inout lilFragData fd LIL_SAMP_IN_FUNC(samp))
    {
        if(_UseEmission2nd)
        {
            float4 emission2ndColor = _Emission2ndColor;
            // Same transformed UV as the existing mask. White tints preserve the original emission.
            float2 reelMaskUV = fd.uvMain * _Emission2ndBlendMask_ST.xy + _Emission2ndBlendMask_ST.zw;
            #if defined(LIL_FEATURE_ANIMATE_EMISSION_MASK_UV)
                #if defined(LIL_BAKER) || defined(LIL_WITHOUT_ANIMATION)
                    reelMaskUV = lilCalcUVWithoutAnimation(fd.uv0, _Emission2ndBlendMask_ST, _Emission2ndBlendMask_ScrollRotate);
                #else
                    reelMaskUV = lilCalcUV(fd.uv0, _Emission2ndBlendMask_ST, _Emission2ndBlendMask_ScrollRotate);
                #endif
            #endif
            float upperWeight = step(_ReelLightSplit, frac(reelMaskUV.y));
            upperWeight = lerp(upperWeight, 1.0 - upperWeight, saturate(_ReelLightSwap));
            // Alpha remains controlled by the standard emission opacity.
            emission2ndColor.rgb *= lerp(_ReelLowerLightColor.rgb, _ReelUpperLightColor.rgb, upperWeight);
            // UV
            float2 emission2ndUV = fd.uv0;
            if(_Emission2ndMap_UVMode == 1) emission2ndUV = fd.uv1;
            if(_Emission2ndMap_UVMode == 2) emission2ndUV = fd.uv2;
            if(_Emission2ndMap_UVMode == 3) emission2ndUV = fd.uv3;
            if(_Emission2ndMap_UVMode == 4) emission2ndUV = fd.uvRim;
            //if(_Emission2ndMap_UVMode == 4) emission2ndUV = fd.uvPanorama;
            float2 _Emission2ndMapParaTex = emission2ndUV + _Emission2ndParallaxDepth * fd.parallaxOffset;
            // Texture
            #if defined(LIL_FEATURE_Emission2ndMap)
                #if defined(LIL_FEATURE_ANIMATE_EMISSION_UV)
                    emission2ndColor *= LIL_GET_EMITEX(_Emission2ndMap, _Emission2ndMapParaTex);
                #else
                    emission2ndColor *= LIL_SAMPLE_2D_ST(_Emission2ndMap, sampler_Emission2ndMap, _Emission2ndMapParaTex);
                #endif
            #endif
            // Mask
            #if defined(LIL_FEATURE_Emission2ndBlendMask)
                #if defined(LIL_FEATURE_ANIMATE_EMISSION_MASK_UV)
                    emission2ndColor *= LIL_GET_EMIMASK(_Emission2ndBlendMask, fd.uv0);
                #else
                    emission2ndColor *= LIL_SAMPLE_2D_ST(_Emission2ndBlendMask, samp, fd.uvMain);
                #endif
            #endif
            // Gradation
            #if defined(LIL_FEATURE_Emission2ndGradTex)
                #if defined(LIL_FEATURE_EMISSION_GRADATION) && defined(LIL_FEATURE_AUDIOLINK)
                    if(_Emission2ndUseGrad)
                    {
                        float gradUV = _Emission2ndGradSpeed * LIL_TIME + fd.audioLinkValue * _AudioLink2Emission2ndGrad;
                        emission2ndColor *= LIL_SAMPLE_1D_LOD(_Emission2ndGradTex, lil_sampler_linear_repeat, gradUV, 0);
                    }
                #elif defined(LIL_FEATURE_EMISSION_GRADATION)
                    if(_Emission2ndUseGrad) emission2ndColor *= LIL_SAMPLE_1D(_Emission2ndGradTex, lil_sampler_linear_repeat, _Emission2ndGradSpeed * LIL_TIME);
                #endif
            #endif
            #if defined(LIL_FEATURE_AUDIOLINK)
                if(_AudioLink2Emission2nd) emission2ndColor.a *= fd.audioLinkValue;
            #endif
            emission2ndColor.rgb = lerp(emission2ndColor.rgb, emission2ndColor.rgb * fd.invLighting, _Emission2ndFluorescence);
            emission2ndColor.rgb = lerp(emission2ndColor.rgb, emission2ndColor.rgb * fd.albedo, _Emission2ndMainStrength);
            float emission2ndBlend = _Emission2ndBlend * lilCalcBlink(_Emission2ndBlink) * emission2ndColor.a;
            #if LIL_RENDER == 2 && !defined(LIL_REFRACTION)
                emission2ndBlend *= fd.col.a;
            #endif
            fd.col.rgb = lilBlendColor(fd.col.rgb, emission2ndColor.rgb, emission2ndBlend, _Emission2ndBlendMode);
        }
    }
#define OVERRIDE_EMISSION_2ND udonSlotReelEmission2nd(fd LIL_SAMP_IN(sampler_MainTex));
#endif
