using System;

namespace CustomIngots.Config.Integration;

internal static class CrystalAppearancePolicy
{
    internal const float IceTintRed = 0f;
    internal const float IceTintGreen = 210f / 255f;
    internal const float IceTintBlue = 1f;
    internal const float IceTintAlpha = 0.8f;
    internal const float IceEmissionRed = 185f / 255f;
    internal const float IceEmissionGreen = 1f;
    internal const float IceEmissionBlue = 254f / 255f;

    internal static bool ShouldTint(string propertyName)
    {
        return propertyName == "_ColorA"
            || propertyName == "_ColorB"
            || propertyName == "_Color";
    }

    internal static bool ShouldSetEmission(string propertyName)
    {
        return propertyName == "_Emission"
            || propertyName == "_EmissionColor";
    }
}
