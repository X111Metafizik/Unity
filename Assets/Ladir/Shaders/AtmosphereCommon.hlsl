#ifndef LADIR_ATMOSPHERE
#define LADIR_ATMOSPHERE
TEXTURE3D(_LadirNoise3D);SAMPLER(sampler_LadirNoise3D);
TEXTURE2D(_LadirTransmittance);SAMPLER(sampler_LadirTransmittance);
float4 _LadirWeather,_LadirAir,_LadirSun,_LadirLightning;
float LadirDensity(float3 p)
{
 float radius=_LadirWeather.w*.001,h=length(p)-radius,profile=smoothstep(2,3,h)*(1-smoothstep(9,12,h));
 if(profile<.001||_LadirWeather.x<.01)return 0;
 p.x=-p.x;float3 drift=float3(_LadirWeather.y*.0001,0,_LadirWeather.y*.00006);
 float weather=SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p*.001+drift*.1,0).r;
 float3 shape=SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,p*.022+drift,0).rgb;
 return smoothstep(.68-_LadirWeather.x*.30,.82-_LadirWeather.x*.30,weather*.35+shape.r*.90-shape.b*.12)*profile;
}
float2 LadirSphere(float3 o,float3 d,float r){float b=dot(o,d),h=b*b-dot(o,o)+r*r;if(h<0)return float2(1e8,-1e8);h=sqrt(h);return float2(-b-h,-b+h);}
float LadirCloudShadow(float3 meters)
{
 if(_LadirWeather.x<.001)return 1;float3 p=meters*.001,sun=normalize(_LadirSun.xyz);float radius=_LadirWeather.w*.001,b=dot(p,sun),t=-b+sqrt(max(0,b*b+(radius+6)*(radius+6)-dot(p,p)));
 return exp(-LadirDensity(p+sun*t)*1.8);
}
float4 LadirVolume(float3 ro,float3 ray,float distance)
{
 float strength=_LadirAir.x,fog=_LadirAir.y,coverage=_LadirWeather.x,radius=_LadirWeather.w*.001;float3 sun=normalize(_LadirSun.xyz);
 if(strength<=0&&coverage<=0&&fog<=0)return float4(0,0,0,1);
 float2 hit=LadirSphere(ro,ray,radius+100),ground=LadirSphere(ro,ray,radius-.01);float entry=max(0,hit.x),exit=min(hit.y,distance);if(ground.x>0)exit=min(exit,ground.x);if(exit<=entry)return float4(0,0,0,1);
 int steps=min((int)_LadirAir.z,max(6,(int)ceil((exit-entry)/.75)));float ds=(exit-entry)/steps,mu=dot(ray,sun),phaseR=.0596831*(1+mu*mu),g=.73,phaseM=.079577*(1-g*g)/pow(max(.03,1+g*g-2*g*mu),1.5);float3 trans=1,light=0;
 if(strength>0||fog>0)for(int i=0;i<steps;i++)
 {
  float3 p=ro+ray*(entry+(i+.5)*ds);float h=max(0,length(p)-radius);float3 sourceP=float3(-p.x,p.y,p.z);
  float mist=exp(-h/2.5)*fog*smoothstep(.48,.70,SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,sourceP*.0004,0).r);
  float3 betaR=float3(.0036,.008,.018)*exp(-h/8)*strength,betaM=.008*exp(-h/1.2)*strength,extinction=betaR+betaM+mist*.045;
  float3 sunTrans=pow(SAMPLE_TEXTURE2D_LOD(_LadirTransmittance,sampler_LadirTransmittance,float2(dot(normalize(p),sun)*.5+.5,saturate(h/100)),0).rgb,strength);
  float day=smoothstep(-.15,.22,dot(normalize(p),sun));float3 ambient=float3(.28,.39,.52)*(.03+day*.27),scatter=(betaR*phaseR+betaM*phaseM)*sunTrans*16+mist*.045*(sunTrans*.3+ambient)+betaR*ambient*.24,segment=exp(-extinction*ds);
  light+=trans*scatter*(1-segment)/max(extinction,.000001);trans*=segment;if(max(trans.x,max(trans.y,trans.z))<.003)break;
 }
 float3 sky=distance>100000?float3(1,.78,.51)*pow(max(0,mu),12000)*12:0,cloudLight=0;float cloudTrans=1;
 if(coverage>.001)
 {
  float2 outer=LadirSphere(ro,ray,radius+12),inner=LadirSphere(ro,ray,radius+2);float a=max(0,outer.x),b=min(distance,outer.y);if(inner.x>0)b=min(b,inner.x);if(length(ro)<radius+2)a=max(a,inner.y);b=min(b,exit);
  if(b>a){float step=(b-a)/24;for(int k=0;k<24;k++)
  {
   float3 p=ro+ray*(a+(k+.5)*step);float den=LadirDensity(p),attenuation=exp(-den*.20*step),day=smoothstep(-.06,.22,dot(normalize(p),sun));float3 lightPoint=p+sun*2.5;lightPoint.x=-lightPoint.x;
   float shape=SAMPLE_TEXTURE3D_LOD(_LadirNoise3D,sampler_LadirNoise3D,lightPoint*.022+float3(_LadirWeather.y*.0001,0,_LadirWeather.y*.00006),0).r,shadow=smoothstep(.46,.72,shape)*den,d=length(p-_LadirLightning.xyz*.001);
   float flash=_LadirLightning.w*(1-smoothstep(3,13,d))*exp(-d*d*.012)*smoothstep(.28,.62,den);
   float3 illumination=float3(.86,.89,.93)*(.12+day*.88)*exp(-shadow*.6)+float3(.72,.84,1)*flash*9;
   cloudLight+=cloudTrans*(1-attenuation)*illumination;cloudTrans*=attenuation;if(cloudTrans<.008)break;
  }}
 }
 return float4((cloudLight+sky)*trans+light,cloudTrans*dot(trans,float3(.2126,.7152,.0722)));
}
#endif
