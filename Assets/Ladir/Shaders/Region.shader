Shader "Ladir/Region" {
 Properties { _BaseMap("White",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _Cutoff("Cutoff",Float)=0.5 _Cull("Cull",Float)=2 }
 SubShader {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Name "Forward" Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 color:COLOR;half fog:TEXCOORD2;};
   V vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.color=i.color;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 frag(V i):SV_Target {
    half3 n=normalize(i.normalWS);Light sun=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
    // Fine material contrast only; the mountain silhouette comes from the baked
    // height geometry. Derivatives filter detail beyond its screen footprint.
    float footprint=max(length(ddx(i.positionWS)),length(ddy(i.positionWS)));
    half grain=sin(i.positionWS.x*1.7+sin(i.positionWS.z*2.3))*sin(i.positionWS.z*1.9+i.positionWS.y*.9);
    grain*=.025*saturate(1-footprint/.6);
    half3 albedo=max(.01,i.color.rgb+grain);half diffuse=saturate(dot(n,sun.direction));
    half3 lit=albedo*(SampleSH(n)*.75+sun.color*diffuse*sun.shadowAttenuation);
    lit=lit/(1+lit*.6); // Keep bright snow slopes readable without clipping.
    return half4(MixFog(lit,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
