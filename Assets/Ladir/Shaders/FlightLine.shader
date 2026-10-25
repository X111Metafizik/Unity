Shader "Ladir/FlightLine" {
 SubShader { Tags { "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" } Pass {
  Blend SrcAlpha One ZWrite Off Cull Off
  HLSLPROGRAM
  #pragma vertex vert
  #pragma fragment frag
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  struct A {float4 positionOS:POSITION;half4 color:COLOR;};struct V {float4 positionCS:SV_POSITION;half4 color:COLOR;};
  V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;return o;}
  half4 frag(V i):SV_Target{return i.color;}
  ENDHLSL
 } }
}
