Shader "Ladir/Vegetation" {
 Properties {_Tint("Tint",Color)=(1,1,1,1) _BaseColor("Base",Color)=(1,1,1,1) _BaseMap("White",2D)="white"{} _Cull("Cull",Float)=2 _Cutoff("Cutoff",Float)=.5}
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" } Pass {
  Tags {"LightMode"="UniversalForward"}
  HLSLPROGRAM
  #pragma target 3.5
  #pragma vertex vert
  #pragma fragment frag
  #pragma multi_compile_instancing
  #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
  #pragma multi_compile_fragment _ _SHADOWS_SOFT
  #pragma multi_compile_fog
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  CBUFFER_START(UnityPerMaterial) half4 _Tint; CBUFFER_END
  float _LadirAlien;
  struct A {float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;half3 n:TEXCOORD1;half4 c:COLOR;half fog:TEXCOORD2;};
  V vert(A i){UNITY_SETUP_INSTANCE_ID(i);V o;VertexPositionInputs p=GetVertexPositionInputs(i.p.xyz);o.p=p.positionCS;o.w=p.positionWS;o.n=TransformObjectToWorldNormal(i.n);o.c=i.c;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
  half4 frag(V i):SV_Target{Light l=GetMainLight(TransformWorldToShadowCoord(i.w));half3 n=normalize(i.n),color=i.c.rgb;if(_LadirAlien>.5&&i.c.a>.5)color=i.c.a<1.5?half3(.0823,.0666,.1746):i.c.a<2.5?half3(.0595,.1529,.1620):half3(.1812,.1170,.2502);return half4(MixFog(color*_Tint.rgb*(SampleSH(n)+l.color*saturate(dot(n,l.direction))*l.shadowAttenuation),i.fog),1);}
  ENDHLSL
 } UsePass "Universal Render Pipeline/Lit/ShadowCaster" UsePass "Universal Render Pipeline/Lit/DepthOnly" }
}
