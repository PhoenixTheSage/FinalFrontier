using System;
using System.IO;
using Vector3 = VRageMath.Vector3;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

// Executes the real compiled provider on D3D11 WARP; no game or visible window.
static class CelestialSmokeTests
{
    const int Size = 33;
    static Device device;
    static DeviceContext context;
    static float[] frame;
    static ShaderReflectionVariable frameVariable;
    static float[] view = { 1,0,0,0, 0,1,0,0, 0,0,1,0, Size,Size,2,2, 1,0,0,0 };
    static float[] uniforms = new float[64];
    static float[] star = MakeStar(0, 0, -1);
    static int checks;
    static bool foreground = true;

    static int Main(string[] args)
    {
        try
        {
            var mainBytes = File.ReadAllBytes(args[0]);
            using (var reflection = new ShaderReflection(mainBytes))
            using (device = new Device(DriverType.Warp, DeviceCreationFlags.None))
            using (var main = new PixelShader(device, mainBytes))
            using (var probe = new PixelShader(device, File.ReadAllBytes(args[1])))
            using (var vertex = new VertexShader(device, File.ReadAllBytes(args[2])))
            {
                context = device.ImmediateContext;
                context.VertexShader.Set(vertex);
                context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
                context.Rasterizer.SetViewport(0, 0, Size, Size);
                frameVariable = reflection.GetVariable("frame_");
                frame = new float[(frameVariable.Description.StartOffset + frameVariable.Description.Size + 15) / 16 * 4];
                var identity = new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
                Write("Environment.inv_view_matrix", identity);
                var projection = (float[])identity.Clone(); projection[0] = projection[5] = 2;
                Write("Environment.projection_matrix", projection);
                Write("Screen.resolution", Size, Size);
                Write("Light.directionalLightVec", 0, 0, 1);
                Write("Light.directionalLightColor", 1, 1, 1);
                Write("Light.SunDiscColor", 1, 1, 1);
                Write("Light.SunDiscIntensity", 10);
                Write("Light.skyboxBrightness", 1); Write("Light.envSkyboxBrightness", 1);
                uniforms[1] = 1; uniforms[2] = 0.2f; uniforms[3] = 1;
                var sun = Draw(main);
                Check("sun at expected world direction", Center(sun) > 9.9f);
                Check("foreground pixels survive", sun[(Size * 16 + 8) * 4] == 0.125f);
                Check("background away from sun is black", sun[(Size * 2 + 30) * 4] < 0.001f);
                var noProbeSun = Draw(probe);
                Check("probe omits solar disc", Center(noProbeSun) == 0);
                Write("Fog.sky", 1); Write("Fog.color", 0.2f, 0.3f, 0.4f);
                var fog = Draw(main);
                Check("main view preserves sky fog", Math.Abs(Center(fog) - 0.2f) < 1e-5f);
                Check("fog leaves foreground untouched", fog[(Size * 16 + 8) * 4] == 0.125f);
                Write("Fog.sky", 0);
                uniforms[0] = 1; uniforms[3] = 0; uniforms[4] = 8; uniforms[5] = 1; uniforms[6] = 1.5f;
                var mainStars = Draw(main);
                var probeStars = Draw(probe);
                Check("main/probe star directions agree", Math.Abs(Center(mainStars) - Center(probeStars)) < 1e-4f && Center(mainStars) > 1);
                identity[3] = 100000000; identity[7] = -300000000; identity[11] = 500000000;
                Write("Environment.inv_view_matrix", identity);
                var translated = Draw(main);
                Check("large translations do not move infinite stars", Equal(mainStars, translated));
                projection[0] = projection[5] = 100000;
                Write("Environment.projection_matrix", projection);
                var zoomed = Draw(main);
                Check("extreme zoom stays finite", Finite(zoomed));
                Check("center star survives zoom", Center(zoomed) > 1);
                // Probe looking +X: row-vector local -Z becomes world +X.
                view[0] = 0; view[2] = -1; view[8] = 1; view[10] = 0;
                star = MakeStar(1, 0, 0);
                Check("rotated probe uses its own view", Center(Draw(probe)) > 1);
                uniforms[0] = 0;
                Check("live uniforms change output", Center(Draw(probe)) == 0);
                foreground = false;
                view[0] = view[5] = view[10] = 1; view[2] = view[8] = 0;
                uniforms[0] = 1;
                foreach (float width in new[] { 0.85f, 1.5f, 2.5f })
                {
                    uniforms[6] = width;
                double minimum = double.MaxValue, maximum = 0;
                foreach (float phase in new[] { -0.5f, -0.25f, 0f, 0.25f, 0.5f })
                {
                    var direction = Vector3.Normalize(new Vector3(phase/Size, 0, -1));
                    star = MakeStar(direction.X, direction.Y, direction.Z);
                    var pixels = Draw(probe);
                    double flux = 0;
                    for (int i = 0; i < pixels.Length; i += 4) flux += pixels[i];
                    minimum = Math.Min(minimum, flux); maximum = Math.Max(maximum, flux);
                }
                Check("subpixel flux conserved across cell boundary at width " + width, minimum > 15.8 && maximum/minimum < 1.01);
                }
                // Power-shaped CDF must keep the same integral (pin-prick core, soft wings).
                uniforms[6] = 1.5f;
                double triangularPeak = 0;
                foreach (float power in new[] { 1f, 3f, 8f })
                {
                    uniforms[7] = power;
                    double minimum = double.MaxValue, maximum = 0, centerPeak = 0;
                    foreach (float phase in new[] { -0.5f, -0.25f, 0f, 0.25f, 0.5f })
                    {
                        var direction = Vector3.Normalize(new Vector3(phase / Size, 0, -1));
                        star = MakeStar(direction.X, direction.Y, direction.Z);
                        var pixels = Draw(probe);
                        double flux = 0;
                        for (int i = 0; i < pixels.Length; i += 4) flux += pixels[i];
                        minimum = Math.Min(minimum, flux); maximum = Math.Max(maximum, flux);
                        if (phase == 0) centerPeak = Center(pixels);
                    }
                    Check("subpixel flux conserved at core sharpness " + power, minimum > 15.8 && maximum / minimum < 1.01);
                    if (power == 1) triangularPeak = centerPeak;
                    else Check("sharper core peaks above triangular at power " + power, centerPeak > triangularPeak * 1.05);
                }
                uniforms[7] = 0; // unset → triangular (power 1) for remaining checks
                star = MakeStar(0, 0, -1);
                uniforms[8] = 0; uniforms[9] = 1;
                Write("frameTime", 0f);
                var steadyMain = Center(Draw(main));
                var steadyProbe = Center(Draw(probe));
                uniforms[8] = 1f; uniforms[9] = 1f;
                Write("frameTime", 1.25f);
                var twinkleMain = Center(Draw(main));
                var twinkleProbe = Center(Draw(probe));
                Check("twinkle leaves probe radiance unchanged", Math.Abs(twinkleProbe - steadyProbe) < 1e-4f);
                Check("twinkle modulates main-view linear radiance", Math.Abs(twinkleMain - steadyMain) > 1e-4f);
                uniforms[8] = 1f; uniforms[9] = 0f;
                Write("frameTime", 0f);
                var frozenA = Center(Draw(main));
                Write("frameTime", 100f);
                var frozenB = Center(Draw(main));
                Check("twinkle speed zero ignores time", Math.Abs(frozenA - frozenB) < 1e-4f);
                uniforms[8] = 0; uniforms[9] = 1;
                uniforms[6] = 1.5f;
                star = MakeStar(0,0,-1); star[32769*4+7] = 7;
                uniforms[4] = 6.5f;
                Check("magnitude limit hides faint star", Center(Draw(probe)) == 0);
                uniforms[4] = 8;
                Check("magnitude limit reveals faint star", Center(Draw(probe)) > 1);
                star = ClientPlugin.Celestial.StarCatalogue.Load(args[3]);
                Check("real catalogue loaded", star[1] > 40000);
                uniforms[0] = 1;
                Check("real catalogue renders finite pixels", Finite(Draw(probe)));
                int brightest = 32769*4;
                for (int i = brightest; i < star.Length; i += 8)
                    if (star[i+3] > star[brightest+3]) brightest = i;
                var forward = new Vector3(star[brightest], star[brightest+1], star[brightest+2]);
                var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                var up = Vector3.Cross(right, forward);
                view[0] = right.X; view[1] = up.X; view[2] = -forward.X;
                view[4] = right.Y; view[5] = up.Y; view[6] = -forward.Y;
                view[8] = right.Z; view[9] = up.Z; view[10] = -forward.Z;
                Check("brightest catalogue star projects to expected center", Center(Draw(probe)) > 5);
                var guide = ClientPlugin.Celestial.ConstellationGuide.Load(
                    Path.Combine(Path.GetDirectoryName(args[3]), "Constellations.bin"));
                int guideStart = star.Length / 4;
                star = ClientPlugin.Celestial.ConstellationGuide.Concatenate(star, guide);
                int stamped = 0;
                for (int s = 0; s < (int)star[1]; s++)
                    if (star[(32769 + s * 2) * 4 + 7] > 50) stamped++;
                Check("constellation membership stamped into catalogue", stamped == guide.MemberCount);
                uniforms[12] = guideStart;
                uniforms[13] = guide.EdgeCount;
                uniforms[14] = guide.MemberCount;
                uniforms[15] = guideStart + guide.MemberStart;
                int member = (guideStart + guide.MemberStart) * 4;
                forward = Vector3.Normalize(new Vector3(star[member], star[member + 1], star[member + 2]));
                right = Vector3.Normalize(Vector3.Cross(forward, Math.Abs(forward.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
                up = Vector3.Cross(right, forward);
                // Main view reads frame inv_view, not the celestial probe basis.
                var look = new float[] {
                    right.X, right.Y, right.Z, 0,
                    up.X, up.Y, up.Z, 0,
                    -forward.X, -forward.Y, -forward.Z, 0,
                    0, 0, 0, 1
                };
                Write("Environment.inv_view_matrix", look);
                projection[0] = projection[5] = 2;
                Write("Environment.projection_matrix", projection);
                uniforms[10] = 0; uniforms[11] = 0;
                double BaseFlux(float[] pixels) { double s = 0; for (int i = 0; i < pixels.Length; i += 4) s += pixels[i]; return s; }
                var plainMain = Draw(main);
                var plainProbe = Center(Draw(probe));
                uniforms[10] = 1;
                var linedMain = Draw(main);
                Check("constellation lines add main-view guide radiance", BaseFlux(linedMain) > BaseFlux(plainMain) * 1.01);
                Check("constellation lines stay out of probes", Math.Abs(Center(Draw(probe)) - plainProbe) < 1e-4f);
                // Fantasy uses catalogue membership stamps; exercise it on the default main -Z star.
                var fantasyStar = MakeStar(0, 0, -1);
                fantasyStar[32769 * 4 + 7] = 2.5f + 100f;
                star = ClientPlugin.Celestial.ConstellationGuide.Concatenate(fantasyStar, guide);
                Write("Environment.inv_view_matrix", new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 });
                uniforms[10] = 0; uniforms[11] = 0;
                var unboosted = Center(Draw(main));
                uniforms[11] = 1;
                var fantasy = Center(Draw(main));
                Check("constellation fantasy brightens member stars", fantasy > unboosted * 1.5);
                uniforms[11] = 1;
                view[0] = view[5] = view[10] = 1; view[1] = view[2] = view[4] = view[6] = view[8] = view[9] = 0;
                var probeFantasy = Center(Draw(probe));
                uniforms[11] = 0;
                var probePlain = Center(Draw(probe));
                Check("constellation fantasy stays out of probes", Math.Abs(probeFantasy - probePlain) < 1e-4f && Math.Abs(probePlain - unboosted) < 1e-3f);
                Console.WriteLine("PASS: " + checks + " D3D11 WARP checks");
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static float[] MakeStar(float x, float y, float z)
    {
        var data = new float[(32769 + 2) * 4];
        data[0] = 32; data[1] = 1; data[2] = 2; data[3] = 1;
        int cx = Math.Min(31, (int)((x+1)*16)), cy = Math.Min(31, (int)((y+1)*16)), cz = Math.Min(31, (int)((z+1)*16));
        int h = (1+cx+32*(cy+32*cz))*4;
        data[h] = 32769; data[h+1] = 1;
        int p = 32769*4;
        data[p] = x; data[p+1] = y; data[p+2] = z; data[p+3] = 2;
        data[p+4] = data[p+5] = data[p+6] = 1;
        return data;
    }

    static void Write(string path, params float[] values)
    {
        var type = frameVariable.GetVariableType();
        var offset = frameVariable.Description.StartOffset;
        foreach (var part in path.Split('.')) { type = type.GetMemberType(part); offset += type.Description.Offset; }
        Array.Copy(values, 0, frame, offset / 4, values.Length);
    }
    static float[] Draw(PixelShader shader)
    {
        view[16] = star.Length / 4;
        var desc = new Texture2DDescription { Width = Size, Height = Size, MipLevels = 1, ArraySize = 1,
            Format = Format.R32G32B32A32_Float, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget };
        using (var output = new Texture2D(device, desc))
        using (var rtv = new RenderTargetView(device, output))
        using (var frameBuffer = Buffer.Create(device, BindFlags.ConstantBuffer, frame))
        using (var viewBuffer = Buffer.Create(device, BindFlags.ConstantBuffer, view))
        using (var uniformBuffer = Buffer.Create(device, BindFlags.ConstantBuffer, uniforms))
        using (var starBuffer = Buffer.Create(device, BindFlags.ShaderResource, star, 0,
            ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16))
        using (var starSrv = new ShaderResourceView(device, starBuffer))
        {
            var depthValues = new float[Size * Size];
            for (var y = 0; foreground && y < Size; y++) for (var x = 0; x < Size / 2; x++) depthValues[y * Size + x] = 0.5f;
            using (var stream = DataStream.Create(depthValues, true, false))
            {
                var dd = desc; dd.Format = Format.R32_Float; dd.BindFlags = BindFlags.ShaderResource;
                using (var depth = new Texture2D(device, dd, new DataRectangle(stream.DataPointer, Size * 4)))
                using (var depthSrv = new ShaderResourceView(device, depth))
                {
                    context.OutputMerger.SetRenderTargets(rtv);
                    context.ClearRenderTargetView(rtv, new SharpDX.Mathematics.Interop.RawColor4(0.125f, 0.25f, 0.5f, 1));
                    context.PixelShader.Set(shader);
                    context.PixelShader.SetConstantBuffer(0, frameBuffer);
                    context.PixelShader.SetConstantBuffer(6, viewBuffer);
                    context.PixelShader.SetConstantBuffer(7, uniformBuffer);
                    context.PixelShader.SetShaderResource(0, depthSrv);
                    context.PixelShader.SetShaderResource(1, starSrv);
                    context.Draw(3, 0);
                    context.PixelShader.SetShaderResource(0, null); context.PixelShader.SetShaderResource(1, null);
                    context.OutputMerger.SetRenderTargets((RenderTargetView)null);
                    var stagingDesc = desc; stagingDesc.BindFlags = BindFlags.None;
                    stagingDesc.Usage = ResourceUsage.Staging; stagingDesc.CpuAccessFlags = CpuAccessFlags.Read;
                    using (var staging = new Texture2D(device, stagingDesc))
                    {
                        context.CopyResource(output, staging);
                        var map = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                        var result = new float[Size * Size * 4];
                        try { for (var y = 0; y < Size; y++) System.Runtime.InteropServices.Marshal.Copy(map.DataPointer + y * map.RowPitch, result, y * Size * 4, Size * 4); }
                        finally { context.UnmapSubresource(staging, 0); }
                        return result;
                    }
                }
            }
        }
    }
    static float Center(float[] image) => image[(Size * (Size / 2) + Size / 2) * 4];
    static bool Equal(float[] a, float[] b) { for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
    static bool Finite(float[] a) { foreach (var v in a) if (float.IsNaN(v) || float.IsInfinity(v)) return false; return true; }
    static void Check(string name, bool passed) { if (!passed) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
}
