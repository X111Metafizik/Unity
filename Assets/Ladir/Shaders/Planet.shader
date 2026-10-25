Shader "Ladir/Planet" {
 Properties { _Rock("Packed mineral albedo / height / cracks",2D)="gray"{} _BaseMap("White",2D)="white"{} _BaseColor("Base",Color)=(1,1,1,1) _Cull("Cull",Float)=2 _Cutoff("Cutoff",Float)=.5 }
 SubShader { Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
 Pass {Name "Forward" Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_Rock);SAMPLER(sampler_Rock);
 #include "AtmosphereCommon.hlsl"
 float4 _LadirOrigin,_LadirNoiseOrigin;
 float _LadirTint,_LadirTintOpacity,_LadirInspect,_LadirTactical;
 struct A{float4 p:POSITION;float3 n:NORMAL;half4 c:COLOR;float2 surface:TEXCOORD0;float4 climate:TEXCOORD1;};
 struct V{float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;half4 c:COLOR;float2 surface:TEXCOORD2;float4 climate:TEXCOORD3;half fog:TEXCOORD4;};
 V vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.p.xyz);o.p=p.positionCS;o.w=p.positionWS;o.n=TransformObjectToWorldNormal(i.n);o.c=i.c;o.surface=i.surface;o.climate=i.climate;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
 float noise(float3 p){return SAMPLE_TEXTURE3D(_LadirNoise3D,sampler_LadirNoise3D,p).r*2-1;}
 half3 mineral(float3 p,float3 n,float scale,float3 dx,float3 dy){float3 weights=abs(n);weights/=max(.0001,weights.x+weights.y+weights.z);return SAMPLE_TEXTURE2D_GRAD(_Rock,sampler_Rock,p.yz/scale,dx.yz/scale,dy.yz/scale).rgb*weights.x+SAMPLE_TEXTURE2D_GRAD(_Rock,sampler_Rock,p.zx/scale,dx.zx/scale,dy.zx/scale).rgb*weights.y+SAMPLE_TEXTURE2D_GRAD(_Rock,sampler_Rock,p.xy/scale,dx.xy/scale,dy.xy/scale).rgb*weights.z;}
 half4 frag(V i):SV_Target{
  float3 n=normalize(i.n),radial=normalize(i.w+_LadirOrigin.xyz),p=i.w+_LadirNoiseOrigin.xyz;p.x=-p.x;float3 texNormal=float3(-n.x,n.y,n.z),dx=ddx(p),dy=ddy(p);float distance=length(i.w-_WorldSpaceCameraPos),footprint=length(abs(dx)+abs(dy)),range=1-smoothstep(4000,8000,distance),filter=1-smoothstep(.12,2,footprint);
  float coarse=0,medium=0,fine=0,grain=0;if(distance<300000)coarse=(SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p/65536,0).r-.5)*(1-smoothstep(100000,300000,distance));if(distance<180000)medium=(SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p/4096,0).g-.5)*(1-smoothstep(80000,180000,distance));if(distance<10000)fine=(SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p/1024,0).b-.5)*(1-smoothstep(4000,10000,distance));if(distance<8000&&footprint<2)grain=(SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p/32,0).r-.5)*range;float steep=1-abs(dot(n,radial)),biome=i.surface.y,lava=i.c.a;
  half3 rock=half3(.5,.5,0),detail=half3(.5,.5,0),ground=half3(.5,.5,0);if(distance<8000){rock=mineral(p,texNormal,64,dx,dy);ground=mineral(p,texNormal,16,dx,dy);if(footprint<2)detail=mineral(p,texNormal,4,dx,dy);}
  float rockMask=smoothstep(.035,.24,steep+(rock.g-.5)*.065*range)*(1-lava)*step(1.5,biome),snow=smoothstep(.08,.85,i.climate.x-smoothstep(.16,.6,steep)*.82+medium*.12+(ground.g-.5)*.16*range)*smoothstep(0,.2,i.climate.x);
  half3 sediment=(biome>8.5&&biome<10.5)||(biome>12.5&&biome<13.5)?half3(.38,.255,.145):half3(.245,.225,.185);if(biome>10.5&&biome<12.5)sediment=half3(.075,.069,.065);if(biome>13.5&&biome<14.5)sediment=half3(.29,.29,.28);
  float grit=(detail.r-.5)*range*filter+.5;half3 stone=sediment*((rock.r-.5)*range*1.25+1.005)*(grit*.4+.8)*(1+medium*.85+coarse*.3)+sin(i.surface.x*.17+medium*6)*.009*(1-smoothstep(1500,6000,distance));
  half3 groundColor=i.c.rgb*(1+coarse*.3+medium*.28+fine*.36+grain*.28*(1-smoothstep(.25,2,footprint)))*(1+((ground.r-.5)*.62+(detail.r-.5)*.28*filter*range)*range);
  half3 snowColor=half3(.79,.84,.86)*(1+coarse*.1+((rock.r-.5)*.24+(ground.r-.5)*.2)*range+(grit-.5)*.55);
  half3 base=lerp(lerp(groundColor*(1-i.climate.y*.10*(1-rockMask)*(1-snow)),stone,rockMask),snowColor,snow);
  // Screen derivatives reuse the same filtered mineral height. No second set
  // of three shifted triplanar evaluations per pixel.
  float height=rock.g+ground.g*.16+detail.g*.22*filter*range;float3 dpdx=ddx(i.w),dpdy=ddy(i.w),r1=cross(dpdy,n),r2=cross(n,dpdx);float det=dot(dpdx,r1);float3 gradient=sign(det)*(ddx(height)*r1+ddy(height)*r2);n=normalize(abs(det)*n-gradient*(.16+.16*rockMask)*range*(1-snow*.5));
  float region=smoothstep(.34,.62,SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p/65536,0).g);half3 tint=_LadirTint==2?half3(.59,.075,.025):_LadirTint==3?half3(.03,.48,.42):_LadirTint==4?lerp(half3(.40,.09,.57),half3(.03,.48,.42),region):half3(.40,.09,.57);if(_LadirTint>0)base=lerp(base,tint,region*_LadirTintOpacity);
  if(_LadirInspect==1)base=half3(.48,.48,.48);if(_LadirInspect==2)base=lerp(half3(.10,.24,.31),half3(.95,.73,.35),saturate(i.surface.x/5000));if(_LadirInspect==3)base=i.c.rgb;if(_LadirInspect==4)base=lerp(base,half3(.08,.72,.8),i.climate.z*.8);if(_LadirInspect==5)base=lerp(half3(.12,.10,.09),half3(.1,.5,.65),i.climate.z);if(_LadirInspect==6)base=lerp(half3(.2,.2,.2),half3(.8,.16,.12),i.climate.z);
  if(_LadirTactical>.5){float h=i.surface.x/500,w=max(.006,fwidth(h)*1.15),contour=1-smoothstep(w,w*2.4,abs(frac(h+.5)-.5));base=lerp(lerp(base,half3(.11,.23,.25),.18),half3(.12,.48,.46),contour*.45);}
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));half3 lit=base*(SampleSH(n)*.85+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation)*LadirCloudShadow(i.w+_LadirOrigin.xyz);lit+=half3(2.8,.24,.015)*pow(lava,4)*(.32+.68*smoothstep(.18,.55,fine*2+.5));return half4(MixFog(lit,i.fog),1);
 }
 ENDHLSL }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
