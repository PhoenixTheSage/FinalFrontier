// Planet-local metres; world anchored noise is advected, never camera anchored.
float FfFogHash(float3 p) { p=frac(p*.1031); p+=dot(p,p.yzx+33.33); return frac((p.x+p.y)*p.z); }
float FfFogNoise(float3 p)
{
    float3 i=floor(p),f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(lerp(FfFogHash(i),FfFogHash(i+float3(1,0,0)),f.x),lerp(FfFogHash(i+float3(0,1,0)),FfFogHash(i+float3(1,1,0)),f.x),f.y),
        lerp(lerp(FfFogHash(i+float3(0,0,1)),FfFogHash(i+float3(1,0,1)),f.x),lerp(FfFogHash(i+float3(0,1,1)),FfFogHash(i+1),f.x),f.y),f.z);
}
AnomalyMediumSample EvaluateMedium(float3 p)
{
    AnomalyMediumSample m=AnomalyEmptyMedium();
    float radius=length(p),ground=MediumUniforms[0].x,air=MediumUniforms[0].y;
    if(air<=ground || radius>=air) return m;
    float height=max(radius-ground-MediumUniforms[4].x,0);
    float density=MediumUniforms[0].w*exp(-height/max(MediumUniforms[0].z,1));
    float3 noisePosition=(p-MediumUniforms[3].xyz*MediumUniforms[2].w)/max(MediumUniforms[2].x,1);
    float noise=FfFogNoise(noisePosition)*.7+FfFogNoise(noisePosition*2.03)*.3;
    density*=lerp(1,.15+noise*1.7,saturate(MediumUniforms[2].y));
    density*=1-smoothstep(max(ground,air-500),air,radius);
    float distance=length(p-MediumCameraPosition);
    density*=1-smoothstep(MediumUniforms[4].y*.8,MediumUniforms[4].y,distance);
    m.extinction=max(density,0);
    m.scattering=m.extinction*saturate(MediumUniforms[1].rgb);
    // Contrast narrows the phase lobe; it cannot invent sunlight in umbra.
    m.anisotropy=clamp(MediumUniforms[1].w*MediumUniforms[3].w,0,.95);
    m.velocity=MediumVelocity;
    return m;
}
