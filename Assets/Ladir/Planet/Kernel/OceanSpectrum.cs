using System;
using System.Globalization;

namespace Ladir.Planet
{
    // Original 64 x 64 Tessendorf spectrum. All rounding at the storage boundary
    // matches the Float32Array data used by render/water.mjs.
    public sealed class OceanSpectrum
    {
        public const int Size=64,Count=Size*Size;
        public readonly float[] packed=new float[Count*4],frequency=new float[Count];
        readonly float[] a=new float[Count*2],b=new float[Count*2];
        uint state;
        public OceanSpectrum(uint seed)
        {
            state=PM.HashString(seed.ToString(CultureInfo.InvariantCulture)+"/ocean-spectrum");if(state==0)state=1;
            var h0=new float[Count*2];var omega=new float[Count];
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                int id=y*Size+x;double kx=2*Math.PI*(x<32?x:x-64)/640,ky=2*Math.PI*(y<32?y:y-64)/640,k=PM.Hypot(kx,ky);
                double ph=k==0?0:Math.Exp(-1/(k*k*180*180))/Math.Pow(k,4)*Math.Pow((kx*.86+ky*.50)/k,2)*Math.Exp(-k*k*.4),amplitude=Math.Sqrt(ph/2)*.004;
                h0[id*2]=(float)(Gaussian()*amplitude);h0[id*2+1]=(float)(Gaussian()*amplitude);omega[id]=(float)Math.Sqrt(9.81*k);
            }
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                int i=y*Size+x,j=Reverse(y)*Size+Reverse(x),neg=((Size-Reverse(y))%Size)*Size+(Size-Reverse(x))%Size;
                packed[i*4]=h0[j*2];packed[i*4+1]=h0[j*2+1];packed[i*4+2]=h0[neg*2];packed[i*4+3]=-h0[neg*2+1];frequency[i]=omega[j];
            }
        }
        double Random(){unchecked{state^=state<<13;state^=state>>17;state^=state<<5;}return state/4294967296.0;}
        double Gaussian()=>Math.Sqrt(-2*Math.Log(Math.Max(1e-9,Random())))*Math.Cos(2*Math.PI*Random());
        static int Reverse(int i){int o=0;for(int b=0;b<6;b++){o=o*2+(i&1);i>>=1;}return o;}
        // Allocation-free CPU reference and fallback, also used to validate the
        // GPU butterfly stages against an independent direct DFT fixture.
        public void Evaluate(double time,float[] output)
        {
            if(output.Length!=Count)throw new ArgumentException("Expected 64 x 64 output.");
            for(int i=0;i<Count;i++){double angle=frequency[i]*time,c=Math.Cos(angle),s=Math.Sin(angle);int q=i*4;a[i*2]=(float)(packed[q]*c-packed[q+1]*s+packed[q+2]*c+packed[q+3]*s);a[i*2+1]=(float)(packed[q]*s+packed[q+1]*c-packed[q+2]*s+packed[q+3]*c);}
            var src=a;var dst=b;
            for(int axis=0;axis<2;axis++)for(int stage=1;stage<=6;stage++)
            {
                int length=1<<stage,half=length/2;
                for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
                {int q=axis==0?x:y,other=axis==0?y:x,k=q%half,first=q/length*length+k,i0=axis==0?other*Size+first:first*Size+other,i1=i0+(axis==0?half:half*Size),i=y*Size+x;double angle=k*2*Math.PI/length,c=Math.Cos(angle),s=Math.Sin(angle),sign=q%length<half?1:-1;dst[i*2]=(float)(src[i0*2]+sign*(src[i1*2]*c-src[i1*2+1]*s));dst[i*2+1]=(float)(src[i0*2+1]+sign*(src[i1*2]*s+src[i1*2+1]*c));}
                var swap=src;src=dst;dst=swap;
            }
            for(int i=0;i<Count;i++)output[i]=src[i*2]/Count*22;
        }
    }
}
