namespace Meitou.Rendering;

/// <summary>
/// The switch for the water's work gated on water being in view (docs/render-water.md "Runs whether or not water is in view"; <c>--ab water-gate</c>:
/// A on, B off). On: <see cref="ReflectionPass.WaterRect"/> leaves out water past <see cref="ReflectionPass.WaterDistance"/> (the haze has stopped
/// growing there, so nothing it reflects can show), and the water pass reads the rect's nearest-water distance: the ocean's FFT is recorded only while
/// water within <see cref="WaterRenderer.OceanFadeEnd"/> is in view, and the refraction copy only while water within the transmitting distance is.
/// Off: the pass as before, the FFT every frame the clock advances and the copy for every near slice.
/// </summary>
public static class WaterGate
{
    public static bool Enabled { get; set; } = true;
}
