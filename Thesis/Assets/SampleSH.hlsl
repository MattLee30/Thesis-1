#ifndef SAMPLESH_INCLUDED
#define SAMPLESH_INCLUDED

void SampleSH_float(float3 WorldNormal, out float3 Color)
{
    // Unity's built-in SH evaluation from UnityCG / Core RP Library
    // Uses unity_SHAr/g/b (L1) and unity_SHBr/g/b + unity_SHC (L2)
    real4 SHCoefficients[7];
    SHCoefficients[0] = unity_SHAr;
    SHCoefficients[1] = unity_SHAg;
    SHCoefficients[2] = unity_SHAb;
    SHCoefficients[3] = unity_SHBr;
    SHCoefficients[4] = unity_SHBg;
    SHCoefficients[5] = unity_SHBb;
    SHCoefficients[6] = unity_SHC;

    Color = max(float3(0,0,0), SampleSH9(SHCoefficients, WorldNormal));
}

#endif