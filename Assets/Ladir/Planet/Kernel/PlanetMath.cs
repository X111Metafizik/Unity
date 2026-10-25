using System;
using System.Text;

namespace Ladir.Planet
{
    // Metres and double precision are retained until the floating-origin render boundary.
    public struct D3
    {
        public double x, y, z;
        public D3(double x, double y, double z) { this.x=x; this.y=y; this.z=z; }
        public D3(double[] a) : this(a[0],a[1],a[2]) { }
        public double Length => Math.Sqrt(x*x+y*y+z*z);
        public D3 Unit => this / Math.Max(1e-30,Length);
        public static D3 operator +(D3 a,D3 b)=>new D3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static D3 operator -(D3 a,D3 b)=>new D3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static D3 operator -(D3 a)=>a*-1;
        public static D3 operator *(D3 a,double b)=>new D3(a.x*b,a.y*b,a.z*b);
        public static D3 operator *(double b,D3 a)=>a*b;
        public static D3 operator /(D3 a,double b)=>a*(1/b);
        public static double Dot(D3 a,D3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static D3 Cross(D3 a,D3 b)=>new D3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        public double[] Array()=>new[]{x,y,z};
    }
    public static class PM
    {
        public static double Clamp(double x,double a=0,double b=1)=>Math.Max(a,Math.Min(b,x));
        public static double Mix(double a,double b,double t)=>a+(b-a)*t;
        public static double Smooth(double a,double b,double x) { var t=Clamp((x-a)/(b-a));return t*t*(3-2*t); }
        public static double Fade(double t)=>t*t*t*(t*(t*6-15)+10);
        public static double Sq(double t)=>t*t;
        public static double Hypot(double x,double y)=>Math.Sqrt(x*x+y*y);
        public static uint Hash(int x,int y,int z,uint s)
        { unchecked { uint h=(uint)(x*374761393)^(uint)(y*668265263)^(uint)(z*2147483647)^s;h=(h^(h>>13))*1274126177;return h^(h>>16); } }
        public static double Random(int x,int y,int z,uint s)=>Hash(x,y,z,s)/4294967296.0;
        public static uint HashString(string value)
        { unchecked {uint h=2166136261;foreach(var b in Encoding.UTF8.GetBytes(value.Normalize()))h=(h^b)*16777619;h^=h>>16;h*=0x7feb352d;h^=h>>15;h*=0x846ca68b;return h^(h>>16);} }
        public static double RandomAt(ulong seed,int id)
        {
            // Decimal UTF-8 FNV without allocating a string at every tree candidate.
            uint h=2166136261;Append(seed,ref h);unchecked{h=(h^47)*16777619;}Append((ulong)id,ref h);
            unchecked{h^=h>>16;h*=0x7feb352d;h^=h>>15;h*=0x846ca68b;return (h^(h>>16))/4294967296.0;}
        }
        static void Append(ulong value,ref uint hash)
        {Span<byte> digits=stackalloc byte[20];int n=0;do{digits[n++]=(byte)(48+value%10);value/=10;}while(value>0);unchecked{while(n>0)hash=(hash^digits[--n])*16777619;}}
        static double Lattice(int x,int y,int z,uint s)=>Hash(x,y,z,s)/2147483647.5-1;
        public static double Noise(D3 p,uint seed)
        {
            int x=(int)Math.Floor(p.x),y=(int)Math.Floor(p.y),z=(int)Math.Floor(p.z);
            double u=Fade(p.x-x),v=Fade(p.y-y),w=Fade(p.z-z);
            return Mix(Mix(Mix(Lattice(x,y,z,seed),Lattice(x+1,y,z,seed),u),Mix(Lattice(x,y+1,z,seed),Lattice(x+1,y+1,z,seed),u),v),Mix(Mix(Lattice(x,y,z+1,seed),Lattice(x+1,y,z+1,seed),u),Mix(Lattice(x,y+1,z+1,seed),Lattice(x+1,y+1,z+1,seed),u),v),w);
        }
        public static double Fbm(D3 p,double frequency,uint seed,int octaves)
        { double sum=0,amp=.5;for(int i=0;i<octaves;i++){sum+=Noise(p*frequency,unchecked(seed+(uint)(i*1013)))*amp;frequency*=2.03;amp*=.49;}return sum; }
        public static D3 Face(int f,double u,double v)
        { switch(f){case 0:return new D3(1,v,-u).Unit;case 1:return new D3(-1,v,u).Unit;case 2:return new D3(u,1,-v).Unit;case 3:return new D3(u,-1,v).Unit;case 4:return new D3(u,v,1).Unit;default:return new D3(-u,v,-1).Unit;} }
        public static void FaceUV(D3 d,out int face,out double u,out double v)
        {
            double x=Math.Abs(d.x),y=Math.Abs(d.y),z=Math.Abs(d.z),m=Math.Max(x,Math.Max(y,z));
            if(x==m){face=d.x>0?0:1;u=(d.x>0?-d.z:d.z)/m;v=d.y/m;}
            else if(y==m){face=d.y>0?2:3;u=d.x/m;v=(d.y>0?-d.z:d.z)/m;}
            else{face=d.z>0?4:5;u=(d.z>0?d.x:-d.x)/m;v=d.y/m;}
        }
    }
}
