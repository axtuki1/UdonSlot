#ifndef UDONSLOT_REEL_CUSTOM_INCLUDED
#define UDONSLOT_REEL_CUSTOM_INCLUDED
#define LIL_CUSTOM_PROPERTIES \
    float4 _ReelUpperLightColor; \
    float4 _ReelLowerLightColor; \
    float _ReelLightSplit; \
    float _ReelLightSwap; \
    float4 _ReelSymbolLights; \
    float4 _ReelSymbolWindow;
#define LIL_CUSTOM_TEXTURES
#define LIL_CUSTOM_VERT_COPY
#define LIL_V2F_FORCE_TEXCOORD0
#endif
