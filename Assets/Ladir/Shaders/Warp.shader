Shader "Ladir/Warp" {
 SubShader { Tags { "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" } Pass {
  Blend SrcAlpha One ZWrite Off Cull Off
  HLSLPROGRAM
  #pragma vertex vert
  #pragma fragment frag
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  float _LadirWarpTime,_LadirWarp;
  struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
  V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
  half4 frag(V i):SV_Target{float t=_LadirWarpTime,v=i.uv.y,u=i.uv.x*6.283185;float helix=pow(saturate(sin(u*7+v*32-t*11)*.5+.5),14);float ring=pow(saturate(sin(v*150-t*27)*.5+.5),18);float noise=sin(u*23+v*117+t*5)*sin(u*13-v*67-t*9);float fade=smoothstep(0,.06,v)*(1-smoothstep(.65,1,v));half3 color=lerp(half3(.12,.33,.7),half3(.45,1.5,1.8),helix);float alpha=(.025+helix*.12+ring*.07+noise*.014)*fade;return half4(color,alpha);}
  ENDHLSL
 } }
}
