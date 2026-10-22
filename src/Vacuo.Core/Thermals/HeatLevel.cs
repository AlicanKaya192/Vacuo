namespace Vacuo.Core.Thermals;

/// <summary>
/// How hot the graphics card is, from 0 (cool: the black hole draws its heat away) to 1 (hot: the sun
/// burns at its brightest). Alican, 22 October 2026: "sıcaklık arttıkça güneşin efekti parlaklığı artsın
/// eğer soğutma iyi ise karadelik güneşten ısıyı çekiyormuş gibi görünsün".
/// </summary>
public static class HeatLevel
{
    // An idle card sits around 40 °C; many cards start holding themselves back near 90 °C.
    private const double CoolCelsius = 40;
    private const double HotCelsius = 90;

    public static float From(double celsius) =>
        (float)Math.Clamp((celsius - CoolCelsius) / (HotCelsius - CoolCelsius), 0, 1);
}
