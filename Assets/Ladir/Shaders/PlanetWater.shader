Shader "Ladir/PlanetWater" {
 SubShader {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
  Pass {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   float4 _LadirOrigin,_LadirNoiseOrigin;
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
   struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;half fog:TEXCOORD2;};
   V vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);o.positionCS=p.positionCS;o.world=p.positionWS;o.normal=TransformObjectToWorldNormal(i.normalOS);o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 frag(V i):SV_Target {
    float3 p=i.world+_LadirNoiseOrigin.xyz,n=normalize(i.normal);
    float footprint=max(length(ddx(p)),length(ddy(p)));float ripple=1-smoothstep(.5,3,footprint),t=_Time.y;
    float3 gradient=float3(.03*cos(dot(p,float3(.6,.37,.21))+t),.025*sin(dot(p,float3(.31,.43,.17))+t*1.2),.03*cos(dot(p,float3(.22,.19,.52))+t*.8));
    n=normalize(n-(gradient-n*dot(gradient,n))*ripple);
    half3 view=normalize(GetWorldSpaceViewDir(i.world));Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));half fresnel=pow(1-saturate(dot(n,view)),4);
    half spec=pow(saturate(dot(n,normalize(sun.direction+view))),150)*.8*sun.shadowAttenuation;
    half3 col=lerp(half3(.025,.145,.17),half3(.36,.52,.65),fresnel)*(SampleSH(n)+sun.color*(.25+.75*saturate(dot(n,sun.direction)))*sun.shadowAttenuation)+sun.color*spec;
    return half4(MixFog(col,i.fog),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
