Shader "Ladir/Clouds" {
 SubShader { Tags {"RenderPipeline"="UniversalPipeline"}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 #include "AtmosphereCommon.hlsl"
 TEXTURE2D(_LadirCloudTexture);SAMPLER(sampler_LadirCloudTexture);
 TEXTURE2D(_LadirHistory);SAMPLER(sampler_LadirHistory);
 TEXTURE2D(_LadirHistoryMeta);SAMPLER(sampler_LadirHistoryMeta);
 float4 _LadirOrigin,_LadirHistoryControl;float4x4 _LadirPreviousVP;float3 _LadirCameraDelta;
 ENDHLSL
 Pass {Name "Atmosphere and temporal clouds" ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment frag
 struct Output {half4 color:SV_Target0;float2 metadata:SV_Target1;};
 Output frag(Varyings i){
  float depth=SampleSceneDepth(i.texcoord);
  #if UNITY_REVERSED_Z
   float farDepth=.00001;bool sky=depth<=.0000001;
  #else
   float farDepth=.99999;bool sky=depth>=.999999;depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
  #endif
  float3 end=ComputeWorldSpacePosition(i.texcoord,farDepth,UNITY_MATRIX_I_VP),ray=normalize(end-_WorldSpaceCameraPos),ro=(_WorldSpaceCameraPos+_LadirOrigin.xyz)*.001;
  float3 scene=ComputeWorldSpacePosition(i.texcoord,depth,UNITY_MATRIX_I_VP);float distance=sky?1e8:length(scene-_WorldSpaceCameraPos)*.001;
  float reference=min(distance,500);float2 shell=LadirSphere(ro,ray,_LadirWeather.w*.001+5.5);float hit=shell.x>0?shell.x:shell.y;if(hit>0)reference=min(reference,hit);
  float4 clip=mul(_LadirPreviousVP,float4(ray*reference+_LadirCameraDelta,1));float2 uv=clip.xy/max(.000001,clip.w)*.5+.5;
  #if UNITY_UV_STARTS_AT_TOP
   uv.y=1-uv.y;
  #endif
  bool valid=_LadirHistoryControl.x>.5&&clip.w>0&&all(uv>0)&&all(uv<1);float2 old=0;
  if(valid){old=SAMPLE_TEXTURE2D_LOD(_LadirHistoryMeta,sampler_LadirHistoryMeta,uv,0).rg;float expected=min(60000,length(ray*min(distance,60000)+_LadirCameraDelta));valid=abs(old.x-expected)<=max(.002,expected*.015)&&old.y<4;}
  int2 tile=(int2)i.positionCS.xy/8;bool selected=(tile.x+tile.y*2)%5==(int)_LadirHistoryControl.y;bool reuse=valid&&!selected;
  // Keep the expensive raymarch outside a conditional select: a flattened
  // ternary can execute BOTH operands even for a valid history pixel. The 8x8
  // phase blocks above keep neighbouring lanes on the same branch.
  Output o;
  [branch] if(reuse){o.color=SAMPLE_TEXTURE2D_LOD(_LadirHistory,sampler_LadirHistory,uv,0);o.metadata=float2(min(distance,60000),old.y+1);return o;}
  o.color=LadirVolume(ro,ray,distance);o.metadata=float2(min(distance,60000),0);return o;
 }
 ENDHLSL }
 Pass {Name "Depth-aware composite" ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment frag
 half4 frag(Varyings i):SV_Target{
  half4 scene=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord);float center=LinearEyeDepth(SampleSceneDepth(i.texcoord),_ZBufferParams);half4 cloud=0;float weights=0;
  [unroll]for(int k=0;k<4;k++){float2 uv=i.texcoord+float2((k&1)?1:-1,(k&2)?1:-1)/_ScreenParams.xy;float d=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams),w=exp(-abs(d-center)/max(8,center*.002));cloud+=SAMPLE_TEXTURE2D(_LadirCloudTexture,sampler_LadirCloudTexture,uv)*w;weights+=w;}
  // Thin silhouettes can reject all four neighbours. Retain the center
  // estimate instead of multiplying the scene by an empty (black) result.
  cloud=weights>.001?cloud/weights:SAMPLE_TEXTURE2D(_LadirCloudTexture,sampler_LadirCloudTexture,i.texcoord);return half4(scene.rgb*cloud.a+cloud.rgb,scene.a);
 }
 ENDHLSL }
 }
}
