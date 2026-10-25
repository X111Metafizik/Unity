Shader "Ladir/Sky" {
 Properties {_Exposure("Exposure",Float)=1.1 _AtmosphereThickness("Atmosphere",Float)=.8 _SunSize("Sun",Float)=.035 _SkyTint("Sky",Color)=(.52,.55,.6,1)}
 SubShader {Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"} Cull Off ZWrite Off
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 float4 _LadirOrigin,_LadirSun;float _Exposure,_AtmosphereThickness;
 struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 ray:TEXCOORD0;};
 V vert(A i){V o;o.p=UnityObjectToClipPos(i.p);o.ray=i.p.xyz;return o;}
 float hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
 half4 frag(V i):SV_Target{
  float3 ray=normalize(i.ray),camera=_WorldSpaceCameraPos+_LadirOrigin.xyz,up=normalize(camera),sun=normalize(_LadirSun.xyz);float altitude=max(0,length(camera)-_LadirOrigin.w),space=smoothstep(30000,160000,altitude),h=dot(ray,up),day=smoothstep(-.18,.1,dot(up,sun));
  float3 horizon=lerp(float3(.07,.10,.18),float3(.56,.73,.82),day),zenith=lerp(float3(.003,.008,.025),float3(.17,.36,.61),day);float3 sky=lerp(horizon,zenith,pow(saturate(h),.42));sky*=lerp(.08,1,saturate(_AtmosphereThickness*1.25));
  float sunDot=saturate(dot(ray,sun));float3 disc=float3(1.6,1.25,.85)*pow(sunDot,4500)+float3(.42,.28,.13)*pow(sunDot,36)*day;
  float star=step(.9996,hash(floor(ray*1500)))*.9;float3 cosmos=float3(.001,.003,.008)+star;
  float b=dot(camera,ray),closest=sqrt(max(0,dot(camera,camera)-b*b)),rim=exp(-max(0,closest-_LadirOrigin.w)/18000)*step(_LadirOrigin.w,closest)*step(b,0);cosmos+=float3(.15,.40,.75)*rim*saturate(dot(up,sun)+.6)*.6;
  return half4((lerp(sky,cosmos,space)+disc)*_Exposure,1);
 }
 ENDHLSL }
 }
}
