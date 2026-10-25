Shader "Ladir/Clouds" {
 SubShader { Tags {"RenderPipeline"="UniversalPipeline"}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 TEXTURE3D(_LadirNoise3D);SAMPLER(sampler_LadirNoise3D);
 TEXTURE2D(_LadirCloudTexture);SAMPLER(sampler_LadirCloudTexture);
 float4 _LadirWeather,_LadirOrigin,_LadirLightning;
 float noise(float3 p){return SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p,0).r;}
 float density(float3 p){float h=length(p)-_LadirWeather.w;float layer=smoothstep(2000,3000,h)*(1-smoothstep(9000,12000,h));float3 km=float3(-p.x,p.y,p.z)/1000,drift=float3(_LadirWeather.y*.0001,0,_LadirWeather.y*.00006);float weather=noise(km*.001+drift*.1);float3 shape=SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,km*.022+drift,0).rgb;return smoothstep(.68-_LadirWeather.x*.30,.82-_LadirWeather.x*.30,weather*.35+shape.r*.90-shape.b*.12)*layer;}
 float2 sphere(float3 o,float3 d,float radius){float b=dot(o,d),c=dot(o,o)-radius*radius,h=b*b-c;if(h<0)return float2(1e20,-1e20);h=sqrt(h);return float2(-b-h,-b+h);}
 ENDHLSL
 Pass {Name "Raymarch" ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment frag
 half4 frag(Varyings i):SV_Target{
  if(_LadirWeather.x<=.001)return 0;float depth=SampleSceneDepth(i.texcoord);
  #if UNITY_REVERSED_Z
   float farDepth=.00001;
  #else
   float farDepth=.99999;depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
  #endif
  float3 end=ComputeWorldSpacePosition(i.texcoord,farDepth,UNITY_MATRIX_I_VP),ray=normalize(end-_WorldSpaceCameraPos),o=_WorldSpaceCameraPos+_LadirOrigin.xyz;
  float3 scene=ComputeWorldSpacePosition(i.texcoord,depth,UNITY_MATRIX_I_VP);float limit=length(scene-_WorldSpaceCameraPos);
  float2 shell=sphere(o,ray,_LadirWeather.w+12000),inner=sphere(o,ray,_LadirWeather.w+2000);float start=max(0,shell.x),stop=min(shell.y,limit);if(inner.x>start)stop=min(stop,inner.x);else if(inner.y>start)start=inner.y;
  stop=min(stop,start+70000);if(stop<=start)return 0;float step=(stop-start)/32,trans=1;float3 color=0;
  // Deterministic midpoint samples: no checkerboard/dither pattern and no
  // camera-dependent random offset that crawls over the cloud silhouette.
  [loop]for(int k=0;k<32;k++){float t=start+(k+.5)*step;float3 p=o+ray*t;float den=density(p);float alpha=1-exp(-den*step*.0012);float sunlight=.55+.45*saturate((length(p)-_LadirWeather.w-1800)/5000);float flash=_LadirLightning.w*exp(-distance(p,_LadirLightning.xyz)/1800);float3 lit=lerp(float3(.30,.39,.47),float3(.94,.97,1.0),sunlight)+float3(.5,.8,1.3)*flash*4;color+=trans*alpha*lit;trans*=1-alpha;if(trans<.02)break;}
  return half4(color,1-trans);
 }
 ENDHLSL }
 Pass {Name "Composite" ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment frag
  half4 frag(Varyings i):SV_Target{
   half4 scene=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord);float centerDepth=LinearEyeDepth(SampleSceneDepth(i.texcoord),_ZBufferParams);half4 cloud=0;float weights=0;
   [unroll]for(int k=0;k<4;k++){float2 offset=float2((k&1)?1:-1,(k&2)?1:-1)/_ScreenParams.xy;float2 uv=i.texcoord+offset;float depth=LinearEyeDepth(SampleSceneDepth(uv),_ZBufferParams);float weight=exp(-abs(depth-centerDepth)/max(8,centerDepth*.002));cloud+=SAMPLE_TEXTURE2D(_LadirCloudTexture,sampler_LadirCloudTexture,uv)*weight;weights+=weight;}
   cloud/=max(.0001,weights);return half4(scene.rgb*(1-cloud.a)+cloud.rgb,scene.a);
  }
 ENDHLSL }
 }
}
