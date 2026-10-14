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
    static int Size = 33;
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
                SolarChecks(main, probe);
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
                uniforms[16] = guideStart + guide.FigureStart;
                uniforms[17] = guide.FigureCount;
                uniforms[18] = 0;
                uniforms[19] = 0;
                Check("constellation v2 figures loaded", guide.Format >= 4 && guide.FigureCount == 9);
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
                uniforms[22] = 1; // Delayed Pattern on
                uniforms[33] = 0; // anyPatternLive off → whole lines block skipped
                var delayedHidden = Draw(main);
                Check("delayed pattern hides lines until look-at fade", BaseFlux(delayedHidden) <= BaseFlux(plainMain) * 1.002);
                uniforms[22] = 0;
                uniforms[33] = 0;
                uniforms[20] = 1; // daylight-under-atmosphere occlusion
                var dayOccluded = Draw(main);
                Check("constellation overlays hide under daylight atmosphere", BaseFlux(dayOccluded) <= BaseFlux(plainMain) * 1.002);
                uniforms[20] = 0;
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
                // Look-at names/art: aim at first figure center with a wide FOV so glyphs/strokes hit pixels.
                var nameStar = MakeStar(0, 0, -1);
                guideStart = nameStar.Length / 4;
                star = ClientPlugin.Celestial.ConstellationGuide.Concatenate(nameStar, guide);
                uniforms[12] = guideStart;
                uniforms[15] = guideStart + guide.MemberStart;
                uniforms[16] = guideStart + guide.FigureStart;
                uniforms[17] = guide.FigureCount;
                int fig = (guideStart + guide.FigureStart) * 4;
                forward = Vector3.Normalize(new Vector3(star[fig], star[fig + 1], star[fig + 2]));
                right = Vector3.Normalize(Vector3.Cross(forward, Math.Abs(forward.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
                up = Vector3.Cross(right, forward);
                Write("Environment.inv_view_matrix", new float[] {
                    right.X, right.Y, right.Z, 0,
                    up.X, up.Y, up.Z, 0,
                    -forward.X, -forward.Y, -forward.Z, 0,
                    0, 0, 0, 1
                });
                projection[0] = projection[5] = 1.2f;
                Write("Environment.projection_matrix", projection);
                uniforms[10] = 0; uniforms[11] = 0; uniforms[18] = 0; uniforms[19] = 0;
                foreground = false;
                var focusPlain = BaseFlux(Draw(main));
                uniforms[18] = 1;
                var named = BaseFlux(Draw(main));
                Check("constellation names add look-at header radiance", named > focusPlain * 1.001);
                uniforms[18] = 0; uniforms[19] = 1;
                var arted = BaseFlux(Draw(main));
                Check("constellation art adds look-at fantasy radiance", arted > focusPlain * 1.001);
                uniforms[18] = 1; uniforms[19] = 1;
                var namedProbe = Center(Draw(probe));
                uniforms[18] = 0; uniforms[19] = 0;
                var clearProbe = Center(Draw(probe));
                Check("constellation names and art stay out of probes", Math.Abs(namedProbe - clearProbe) < 1e-4f);
                // Looking away should drop overlay contribution even if scene flux differs.
                uniforms[18] = 1; uniforms[19] = 1;
                double overlayOn = BaseFlux(Draw(main)) - focusPlain;
                Write("Environment.inv_view_matrix", new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 });
                uniforms[18] = 0; uniforms[19] = 0;
                double awayPlain = BaseFlux(Draw(main));
                uniforms[18] = 1; uniforms[19] = 1;
                double awayOverlay = BaseFlux(Draw(main)) - awayPlain;
                Check("constellation names/art fade when looking away", overlayOn > 0 && awayOverlay < overlayOn * 0.15);
                foreground = true;
                if (args.Length > 5) using (var glow = new PixelShader(device, File.ReadAllBytes(args[5]))) SolarOpticalChecks(main, glow);
                if (args.Length > 7)
                    using (var hdr = new PixelShader(device, File.ReadAllBytes(args[6])))
                    using (var sdr = new PixelShader(device, File.ReadAllBytes(args[7])))
                        SolarDisplayChecks(main, hdr, sdr, args[4] == "-" ? null : args[4]);
                if (args.Length > 4 && args[4] != "-") SolarPreview(main, args[4]);
                Console.WriteLine("PASS: " + checks + " D3D11 WARP checks");
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void SolarSetup(int size)
    {
        Size = size;
        foreground = false;
        context.Rasterizer.SetViewport(0, 0, Size, Size);
        Array.Clear(uniforms, 0, uniforms.Length);
        uniforms[1] = 1; uniforms[2] = .004653f; uniforms[3] = 1;
        uniforms[34] = 1; uniforms[35] = 1; uniforms[36] = .8f;
        uniforms[37] = .18f; uniforms[38] = 48; uniforms[39] = 1;
        uniforms[41] = 1; uniforms[42] = .18f; uniforms[43] = 3;
        uniforms[44] = 2; uniforms[45] = 1; uniforms[46] = .55f;
        Write("Environment.inv_view_matrix", new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 });
        float scale = 1 / (uniforms[2] * 1.48f);
        Write("Environment.projection_matrix", new float[] { scale,0,0,0, 0,scale,0,0, 0,0,1,0, 0,0,0,1 });
        Write("Screen.resolution", Size, Size);
        Write("Light.directionalLightVec", 0, 0, 1);
        Write("Light.directionalLightColor", 1, 1, 1);
        Write("Light.SunDiscColor", 1, 1, 1);
        Write("Light.SunDiscIntensity", 10);
        Write("Fog.sky", 0);
    }

    static float SolarRadiusAt(int x, int y)
    {
        float qx = ((x + .5f) / Size * 2 - 1) * 1.48f;
        float qy = ((y + .5f) / Size * 2 - 1) * 1.48f;
        return (float)Math.Sqrt(qx * qx + qy * qy);
    }

    static void SolarChecks(PixelShader main, PixelShader probe)
    {
        SolarSetup(161);
        Write("frameTime", 0);
        var start = Draw(main);
        Check("resolved plasma sun remains finite", Finite(start));
        double low = double.MaxValue, high = 0, goldGreen = 0, goldBlue = 0;
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            if (SolarRadiusAt(x, y) >= .75f) continue;
            int o = (y * Size + x) * 4;
            low = Math.Min(low, start[o]); high = Math.Max(high, start[o]);
            goldGreen += start[o + 1]; goldBlue += start[o + 2];
        }
        Check("solar texture has luminous contrast without dark spots", low > 5.5 && high / low > 1.10);
        Check("reference palette is golden", goldGreen > goldBlue * 1.5);
        Write("frameTime", 12);
        var later = Draw(main);
        double surfaceMotion = 0, exteriorMotion = 0;
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            float radius = SolarRadiusAt(x, y);
            int o = (y * Size + x) * 4;
            if (radius < .85f) surfaceMotion += Math.Abs(start[o] - later[o]);
            if (radius > 1.025f && radius < 1.32f) exteriorMotion += Math.Abs(start[o] - later[o]);
        }
        Check("sphere plasma evolves with time", surfaceMotion > 1);
        Check("raised flare bands animate outside photosphere", exteriorMotion > .1);
        uniforms[36] = 0;
        var withoutLoops = Draw(main);
        double exteriorLight = 0;
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            if (SolarRadiusAt(x, y) > 1.025f && SolarRadiusAt(x, y) < 1.32f)
                exteriorLight += Math.Max(0, later[(y * Size + x) * 4] - withoutLoops[(y * Size + x) * 4]);
        Check("corona control removes raised flare bands", exteriorLight > .1);
        uniforms[36] = .8f; uniforms[35] = 0;
        Write("frameTime", 0);
        var frozen = Draw(main);
        Write("frameTime", 100);
        Check("zero solar animation speed freezes surface and bands", Equal(frozen, Draw(main)));
        Check("animated sun remains absent from reflection probes", Center(Draw(probe)) == 0);
        foreground = true;
        var occluded = Draw(main);
        Check("solar surface and loops preserve foreground depth", occluded[(Size * (Size / 2) + 8) * 4] == .125f);
        foreground = false;
        uniforms[3] = 0;
        Check("sun toggle removes surface and corona together", Center(Draw(main)) == 0);
        uniforms[3] = 1; uniforms[1] = 0;
        Check("sun brightness controls every solar layer", Center(Draw(main)) == 0);
        uniforms[1] = 1;
        Write("Environment.projection_matrix", new float[] { 100000,0,0,0, 0,100000,0,0, 0,0,1,0, 0,0,0,1 });
        Check("extreme solar zoom stays finite", Finite(Draw(main)));
        Write("Light.directionalLightVec", 0, 0, -1);
        Check("backward sun does not leak plasma into view", Center(Draw(main)) == 0);
        SolarSetup(297);
        uniforms[34] = 0; uniforms[36] = 0;
        var circular = Draw(main);
        // Equal angular distances: an axis sample and a 3:4:5 diagonal sample.
        float axis = circular[((148 + 125) * Size + 148) * 4];
        float diagonal = circular[((148 + 100) * Size + 148 + 75) * 4];
        Check("analytic solar halo stays circular", axis > 0 && Math.Abs(axis - diagonal) / axis < .001f);
        Size = 33;
        context.Rasterizer.SetViewport(0, 0, Size, Size);
        Write("Screen.resolution", Size, Size);
        Write("Environment.projection_matrix", new float[] { 2,0,0,0, 0,2,0,0, 0,0,1,0, 0,0,0,1 });
        Write("Light.directionalLightVec", 0, 0, 1);
        Array.Clear(uniforms, 0, uniforms.Length);
        uniforms[1] = 1; uniforms[2] = .2f; uniforms[3] = 1;
        foreground = true;
    }

    static void SolarPreview(PixelShader main, string directory)
    {
        Directory.CreateDirectory(directory);
        int size = 513, count = 64;
        int requested;
        if (int.TryParse(Environment.GetEnvironmentVariable("FINAL_FRONTIER_SOLAR_PREVIEW_SIZE"), out requested)) size = Math.Max(33, Math.Min(4096, requested));
        if (int.TryParse(Environment.GetEnvironmentVariable("FINAL_FRONTIER_SOLAR_PREVIEW_FRAMES"), out requested)) count = Math.Max(1, Math.Min(240, requested));
        SolarSetup(size);
        uniforms[40] = Environment.GetEnvironmentVariable("FINAL_FRONTIER_SOLAR_PREVIEW_HDR") == "1" ? 1 : 0;
        for (int i = 0; i < count; i++)
        {
            Write("frameTime", i * .125f);
            var rgba = Draw(main);
            var bytes = new byte[rgba.Length * sizeof(float)];
            System.Buffer.BlockCopy(rgba, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(Path.Combine(directory, "solar-" + i.ToString("D3") + ".f32"), bytes);
        }
        File.WriteAllText(Path.Combine(directory, "preview.txt"), size + " " + size + " " + count + "\nHDR RGBA float32, little endian; 0.125 simulation seconds per frame.\n");
        Console.WriteLine("Solar shader HDR preview frames: " + Path.GetFullPath(directory));
    }

    static void SolarOpticalChecks(PixelShader main, PixelShader glow)
    {
        SolarSetup(297);
        uniforms[36] = 0; uniforms[39] = 0;
        float scale = 1 / (uniforms[2] * 5);
        Write("Environment.projection_matrix", new float[] { scale,0,0,0, 0,scale,0,0, 0,0,1,0, 0,0,0,1 });
        foreach (float air in new[] { 0f, .5f, 1f })
        foreach (float detail in new[] { 0f, .35f, 1f })
        {
            uniforms[21] = air; uniforms[34] = detail;
            var optical = Draw(glow);
            float previous = Center(optical);
            // WARP transcendental instructions allow small relative error.
            bool monotonic = Math.Abs(previous - uniforms[42]) < 1e-4f;
            for (int x = Size / 2 + 1; x < Size; x++)
            {
                float next = optical[(Size * (Size / 2) + x) * 4];
                monotonic &= next <= previous + 1e-6f && next >= 0;
                previous = next;
            }
            Check("filled halo fades outwards, air=" + air + ", detail=" + detail, monotonic && Finite(optical));
            if (detail != 0) continue; // Plasma knots deliberately vary across the sphere.
            var body = Draw(main);
            previous = Center(body); monotonic = true;
            for (int x = Size / 2 + 1; x < Size; x++)
            {
                float next = body[(Size * (Size / 2) + x) * 4];
                monotonic &= next <= previous + 1e-4f;
                previous = next;
            }
            Check("additive disc/glow has no bright annulus, air=" + air, monotonic);
        }
        uniforms[21] = 0; uniforms[34] = 1; uniforms[36] = .8f;
        var withGlow = Draw(main);
        uniforms[41] = 0;
        var noGlow = Draw(main);
        Check("glow disabled removes the whole smooth profile", Center(Draw(glow)) == 0);
        // Away from all arches, every exterior pixel must be black when glow is off.
        int outside = (Size * (Size / 2) + Size / 2 + 65) * 4;
        Check("no second rim halo remains with glow off", noGlow[outside] == 0 && withGlow[outside] > .01f);
        int middle = (Size * (Size / 2) + Size / 2) * 4;
        Check("glow adds light through the centre of the disc", withGlow[middle] > noGlow[middle] + .1f);
        uniforms[41] = 1; uniforms[42] = 0;
        Check("zero glow strength equals glow disabled", Equal(noGlow, Draw(main)));
        uniforms[42] = .18f;
        var small = Draw(glow);
        uniforms[43] = 6;
        var wide = Draw(glow);
        Check("glow radius widens profile without raising centre intensity", Math.Abs(Center(wide) - Center(small)) < 1e-4f && wide[outside] > small[outside]);
        uniforms[44] = 1;
        var soft = Draw(glow);
        uniforms[44] = 4;
        var steep = Draw(glow);
        Check("falloff changes profile shape without a centre hole", Math.Abs(Center(soft) - Center(steep)) < 1e-4f && Math.Abs(soft[outside] - steep[outside]) > .001f);
        uniforms[44] = 2; uniforms[43] = 3; uniforms[45] = 0;
        uniforms[21] = 0; var vacuum = Draw(glow);
        uniforms[21] = 1;
        Check("atmosphere expansion zero keeps a single space profile", Equal(vacuum, Draw(glow)));
        uniforms[45] = 1;
        var atmospheric = Draw(glow);
        Check("atmosphere broadens the same profile without raising its centre", Math.Abs(Center(vacuum) - Center(atmospheric)) < 1e-4f && atmospheric[outside] > vacuum[outside]);
        uniforms[39] = 1; uniforms[46] = 0; var whiteGlow = Draw(main);
        uniforms[46] = 1; var goldenGlow = Draw(main);
        Check("glow tint changes exterior colour independently of plasma", whiteGlow[outside + 1] / whiteGlow[outside] > goldenGlow[outside + 1] / goldenGlow[outside] + .1f);
    }

    static double Luma(float[] image, int offset) => image[offset] * .2126 + image[offset + 1] * .7152 + image[offset + 2] * .0722;

    static void SolarDisplayChecks(PixelShader main, PixelShader hdr, PixelShader sdr, string directory)
    {
        SolarSetup(297); Write("frameTime", 0); Write("Post.WhitePoint", 11.2f);
        var original = Draw(main);
        uniforms[40] = 1;
        var corrected = Draw(main);
        double maxLumaError = 0, red = 0, green = 0, blue = 0;
        for (int i = 0; i < original.Length; i += 4)
        {
            if (Luma(original, i) > .01)
                maxLumaError = Math.Max(maxLumaError, Math.Abs(Luma(original, i) - Luma(corrected, i)) / Luma(original, i));
            int pixel = i / 4;
            if (SolarRadiusAt(pixel % Size, pixel / Size) < .75f)
            { red += corrected[i]; green += corrected[i + 1]; blue += corrected[i + 2]; }
        }
        Check("HDR solar correction preserves scene luminance", maxLumaError < .001);
        Check("HDR solar energy is golden yellow rather than orange", green / red > .70 && green / red < .96 && blue / green < .30);
        foreach (float[] sunlight in new[] { new[] { 1f, .65f, .25f }, new[] { 1f, .3f, .02f } })
        {
            Write("Light.directionalLightColor", sunlight);
            uniforms[40] = 0; var warmOriginal = Draw(main);
            uniforms[40] = 1; var warmCorrected = Draw(main);
            double error = 0;
            for (int i = 0; i < warmOriginal.Length; i += 4)
                if (Luma(warmOriginal, i) > .01)
                    error = Math.Max(error, Math.Abs(Luma(warmOriginal, i) - Luma(warmCorrected, i)) / Luma(warmOriginal, i));
            Check("HDR correction preserves warm sunlight luminance, G=" + sunlight[1], error < .001);
        }
        Write("Light.directionalLightColor", 1, 1, 1);
        uniforms[40] = 0;
        Check("HDR off restores exact SDR surface radiance", Equal(original, Draw(main)));
        uniforms[39] = 0;
        var white = Draw(main); uniforms[40] = 1;
        Check("gold strength zero retains world colour in both outputs", Equal(white, Draw(main)));
        float[] grade = new float[64];
        grade[0] = 200f / 80; grade[2] = 5000f / 80;
        grade[8] = grade[9] = grade[10] = grade[11] = grade[12] = grade[13] = 1;
        var sdrDisplay = DrawDisplay(sdr, original, grade);
        float[] before = null, after = null;
        foreach (float peakNits in new[] { 600f, 1000f, 2000f })
        {
            grade[1] = peakNits / 80;
            before = DrawDisplay(hdr, original, grade);
            after = DrawDisplay(hdr, corrected, grade);
            int o = (Size * (Size / 2) + Size / 2) * 4;
            double oldRatio = before[o + 1] / before[o], newRatio = after[o + 1] / after[o];
            Check("actual HDR tone mapper retains golden hue at " + peakNits + " nits", Finite(after) && newRatio > .70 && newRatio > oldRatio + .20);
            Check("HDR yellow moves towards Keen SDR highlight chromaticity at " + peakNits + " nits",
                Math.Abs(newRatio - sdrDisplay[o + 1] / sdrDisplay[o]) < Math.Abs(oldRatio - sdrDisplay[o + 1] / sdrDisplay[o]));
        }
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            void Save(string name, float[] pixels)
            {
                byte[] bytes = new byte[pixels.Length * 4];
                System.Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(Path.Combine(directory, name + ".f32"), bytes);
            }
            Save("display-sdr", sdrDisplay); Save("display-hdr-before", before); Save("display-hdr-after", after);
            File.WriteAllText(Path.Combine(directory, "display.txt"), Size + " " + Size + "\nLinear RGB; HDR is scRGB (80 nits/unit), 200 nits paper white, 2000 nits peak.\n");
        }
    }

    // Execute the production HDR shader (or Keen's SDR shoulder) over solar
    // radiance. Neutral grade, zero exposure offset and no scene/bloom textures.
    static float[] DrawDisplay(PixelShader shader, float[] pixels, float[] grade)
    {
        var desc = new Texture2DDescription { Width = Size, Height = Size, MipLevels = 1, ArraySize = 1,
            Format = Format.R32G32B32A32_Float, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget };
        using (var output = new Texture2D(device, desc))
        using (var rtv = new RenderTargetView(device, output))
        using (var stream = DataStream.Create(pixels, true, false))
        using (var frameBuffer = Buffer.Create(device, BindFlags.ConstantBuffer, frame))
        using (var gradeBuffer = Buffer.Create(device, BindFlags.ConstantBuffer, grade))
        {
            var inputDesc = desc; inputDesc.BindFlags = BindFlags.ShaderResource;
            using (var input = new Texture2D(device, inputDesc, new DataRectangle(stream.DataPointer, Size * 16)))
            using (var srv = new ShaderResourceView(device, input))
            {
                context.OutputMerger.SetRenderTargets(rtv); context.PixelShader.Set(shader);
                context.PixelShader.SetConstantBuffer(0, frameBuffer); context.PixelShader.SetConstantBuffer(7, gradeBuffer);
                context.PixelShader.SetShaderResource(0, srv);
                context.Draw(3, 0);
                context.PixelShader.SetShaderResource(0, null); context.OutputMerger.SetRenderTargets((RenderTargetView)null);
                var stagingDesc = desc; stagingDesc.BindFlags = BindFlags.None;
                stagingDesc.Usage = ResourceUsage.Staging; stagingDesc.CpuAccessFlags = CpuAccessFlags.Read;
                using (var staging = new Texture2D(device, stagingDesc))
                {
                    context.CopyResource(output, staging);
                    var map = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                    var result = new float[pixels.Length];
                    try { for (int y = 0; y < Size; y++) System.Runtime.InteropServices.Marshal.Copy(map.DataPointer + y * map.RowPitch, result, y * Size * 4, Size * 4); }
                    finally { context.UnmapSubresource(staging, 0); }
                    return result;
                }
            }
        }
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
    static Texture2D artTexture;
    static ShaderResourceView artSrv;
    static SamplerState artSampler;
    static int artSlices;

    static void EnsureArt(int slices = 1)
    {
        slices = Math.Max(1, slices);
        if (artSrv != null && artSlices >= slices) return;
        artSrv?.Dispose();
        artTexture?.Dispose();
        artSampler?.Dispose();
        artSrv = null; artTexture = null; artSampler = null;
        const int n = 16;
        var pixels = new byte[n * n * 4 * slices];
        for (int s = 0; s < slices; s++)
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int o = ((s * n * n) + y * n + x) * 4;
                    float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                    bool on = u * u + v * v < 0.35f;
                    pixels[o] = on ? (byte)40 : (byte)0;
                    pixels[o + 1] = on ? (byte)220 : (byte)0;
                    pixels[o + 2] = on ? (byte)120 : (byte)0;
                    pixels[o + 3] = on ? (byte)230 : (byte)0;
                }
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var boxes = new DataBox[slices];
            for (int s = 0; s < slices; s++)
                boxes[s] = new DataBox(IntPtr.Add(handle.AddrOfPinnedObject(), s * n * n * 4), n * 4, 0);
            artTexture = new Texture2D(device, new Texture2DDescription
            {
                Width = n, Height = n, MipLevels = 1, ArraySize = slices,
                Format = Format.R8G8B8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable, BindFlags = BindFlags.ShaderResource
            }, boxes);
            artSrv = new ShaderResourceView(device, artTexture, new ShaderResourceViewDescription
            {
                Format = Format.R8G8B8A8_UNorm,
                Dimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = { MipLevels = 1, FirstArraySlice = 0, ArraySize = slices, MostDetailedMip = 0 }
            });
            artSampler = new SamplerState(device, new SamplerStateDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });
            artSlices = slices;
        }
        finally { handle.Free(); }
    }

    static float[] Draw(PixelShader shader)
    {
        view[12] = view[13] = Size;
        view[16] = star.Length / 4;
        EnsureArt(Math.Max(1, (int)uniforms[17]));
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
                    context.PixelShader.SetShaderResource(2, artSrv);
                    context.PixelShader.SetSampler(0, artSampler);
                    context.Draw(3, 0);
                    context.PixelShader.SetShaderResource(0, null);
                    context.PixelShader.SetShaderResource(1, null);
                    context.PixelShader.SetShaderResource(2, null);
                    context.PixelShader.SetSampler(0, null);
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
