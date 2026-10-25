Shader "Ladir/Water" {
 SubShader {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   struct A {float4 positionOS:POSITION;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half fog:TEXCOORD1;};
   V vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 frag(V i):SV_Target {
    float2 p=i.positionWS.xz;float t=_Time.y;
    float footprint=max(length(ddx(p)),length(ddy(p)));half ripple=saturate(1-footprint/2);
    half3 n=normalize(half3(sin(p.x*.6+p.y*.37+t)*.05*ripple,1,cos(p.y*.43-p.x*.3+t*1.2)*.05*ripple));
    half3 view=normalize(GetWorldSpaceViewDir(i.positionWS));Light sun=GetMainLight();half fresnel=pow(1-saturate(dot(n,view)),4);
    half spec=pow(saturate(dot(n,normalize(sun.direction+view))),150)*.8;
    half3 col=lerp(half3(.025,.145,.17),half3(.36,.52,.65),fresnel)+sun.color*spec;
    return half4(MixFog(col,i.fog),1);
   }
   ENDHLSL
  }
 }
}
