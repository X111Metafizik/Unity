Shader "Ladir/PlanetWater" {
 SubShader { Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
 Pass {Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "AtmosphereCommon.hlsl"
 TEXTURE2D(_LadirOcean);SAMPLER(sampler_LadirOcean);
 float4 _LadirOrigin,_LadirNoiseOrigin,_LadirWaveOrigin;float _LadirOceanTime,_LadirWaterGain;
 struct A {float4 p:POSITION;float3 n:NORMAL;float2 water:TEXCOORD0;};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 water:TEXCOORD2;};
 V vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.p.xyz);o.p=p.positionCS;o.w=p.positionWS;o.n=TransformObjectToWorldNormal(i.n);o.water=i.water;return o;}
 float hashNoise(float3 p){uint3 v=(uint3)(int3)floor(p);uint h=v.x*374761393u+v.y*668265263u+v.z*2246822519u;h=(h^(h>>13u))*1274126177u;return float(h^(h>>16u))/4294967295.0;}
 float valueNoise(float3 p){float3 i=floor(p),f=frac(p),u=f*f*(3-2*f);return lerp(lerp(lerp(hashNoise(i),hashNoise(i+float3(1,0,0)),u.x),lerp(hashNoise(i+float3(0,1,0)),hashNoise(i+float3(1,1,0)),u.x),u.y),lerp(lerp(hashNoise(i+float3(0,0,1)),hashNoise(i+float3(1,0,1)),u.x),lerp(hashNoise(i+float3(0,1,1)),hashNoise(i+float3(1,1,1)),u.x),u.y),u.z);}
 float fbm(float3 p){float v=0,a=.55;for(int i=0;i<4;i++){v+=valueNoise(p)*a;p=p*2.03+float3(17.3,9.2,4.1);a*=.49;}return v;}
 float fft(float2 uv){return SAMPLE_TEXTURE2D_LOD(_LadirOcean,sampler_LadirOcean,uv+.5/64,0).r;}
 half4 frag(V i):SV_Target{
  float3 p=i.w+_LadirNoiseOrigin.xyz,n=normalize(i.n);p.x=-p.x;float distance=length(i.w-_WorldSpaceCameraPos),wave=0,small=.5,gain=_LadirWaterGain*(1-smoothstep(250,2500,distance));
  if(distance<2500&&_LadirWaterGain>0){float3 w=i.w+_LadirWaveOrigin.xyz;w.x=-w.x;float3 weights=pow(abs(n),4);wave=(fft(w.yz/640)*weights.x+fft(w.zx/640)*weights.y+fft(w.xy/640)*weights.z)/(weights.x+weights.y+weights.z);}
  if(distance<3000)small=(fbm(p*.025+float3(_LadirOceanTime*.45,0,_LadirOceanTime*.18))-.5)*(1-smoothstep(400,3000,distance))+.5;
  float height=(wave*.045+small*.2)*gain;float3 dx=ddx(i.w),dy=ddy(i.w),r1=cross(dy,n),r2=cross(n,dx);float det=dot(dx,r1);n=normalize(abs(det)*n-sign(det)*(ddx(height)*r1+ddy(height)*r2));
  float shallow=i.water.x,foam=max(0,small-.72)*shallow*1.2;half3 base=lerp(half3(.008,.047,.074),half3(.024,.25,.25),shallow)+foam;
  half3 view=normalize(GetWorldSpaceViewDir(i.w));Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));float fresnel=.04+.96*pow(1-saturate(dot(n,view)),5),roughness=(i.water.y>1.5?.12:.20)+small*.15+foam;
  float spec=pow(saturate(dot(n,normalize(sun.direction+view))),2/max(.002,roughness*roughness)-2);float3 reflectDirection=reflect(-view,n);half3 environment=lerp(half3(.04,.10,.17),half3(.38,.59,.79),reflectDirection.y*.5+.5)*.55;
  half3 lit=(base*(SampleSH(n)+sun.color*(.25+.75*saturate(dot(n,sun.direction)))*sun.shadowAttenuation)+sun.color*spec*fresnel+environment*fresnel)*LadirCloudShadow(i.w+_LadirOrigin.xyz);return half4(lit,1);
 }
 ENDHLSL }
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
