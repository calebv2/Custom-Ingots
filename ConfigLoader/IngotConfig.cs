using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CustomIngots.API;
using Newtonsoft.Json;
using UnityEngine;

namespace CustomIngots.Config;

public sealed class IngotConfigFile
{
    public IngotConfig[] Ingots { get; set; } = Array.Empty<IngotConfig>();

    public static IngotConfig[] Load(string path)
    {
        var config = JsonConvert.DeserializeObject<IngotConfigFile>(File.ReadAllText(path));
        if (config == null || config.Ingots == null)
            throw new InvalidDataException("The config must contain an 'ingots' array.");
        return config.Ingots;
    }
}

public sealed class IngotConfig
{
    public string ItemName { get; set; } = "";
    public string SourceItemName { get; set; } = "";
    public string ItemHash { get; set; } = "";
    public string PrefabHash { get; set; } = "";
    public string RecipeHash { get; set; } = "";
    public string MaterialHash { get; set; } = "";
    public string MaterialName { get; set; } = "";
    public IngredientConfig[] Ingredients { get; set; } = Array.Empty<IngredientConfig>();
    public ColorConfig Tint { get; set; } = new ColorConfig();
    public ColorConfig Emission { get; set; } = new ColorConfig();
    public EmissionPulseConfig? EmissionPulse { get; set; }
    public GradientConfig? Gradient { get; set; }
    public string[] LegacyPrefabHashes { get; set; } = Array.Empty<string>();
    public StatScalingConfig? StatScaling { get; set; }

    public IngotDefinition ToDefinition()
    {
        var ingredients = (Ingredients ?? Array.Empty<IngredientConfig>())
            .Select(ingredient => ingredient.ToIngredient()).ToArray();
        var legacyHashes = (LegacyPrefabHashes ?? Array.Empty<string>())
            .Select(ParseHash).ToArray();
        var tint = (Tint ?? new ColorConfig()).ToColor();
        var emission = (Emission ?? new ColorConfig()).ToColor();
        var emissionPulse = EmissionPulse?.ToPulse();
        var gradient = Gradient?.ToGradient();

        if (StatScaling == null)
        {
            return new IngotDefinition(ItemName, SourceItemName, ParseHash(ItemHash), ParseHash(PrefabHash),
                ParseHash(RecipeHash), ParseHash(MaterialHash), MaterialName, ingredients, tint, emission,
                emissionPulse, gradient, legacyHashes);
        }

        var scaling = new IngotStatScaling(ParseHash(StatScaling.SourceMaterialHash),
            StatScaling.DamageScale, StatScaling.DurabilityScale);
        return new IngotDefinition(scaling, ItemName, SourceItemName, ParseHash(ItemHash), ParseHash(PrefabHash),
            ParseHash(RecipeHash), ParseHash(MaterialHash), MaterialName, ingredients, tint, emission,
            emissionPulse, gradient, legacyHashes);
    }

    internal static uint ParseHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Hashes must be strings containing decimal values or 0x-prefixed hexadecimal values.");
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}

public sealed class IngredientConfig
{
    public string ItemHash { get; set; } = "";
    public string ItemName { get; set; } = "";
    public int Count { get; set; }

    public IngotIngredient ToIngredient() => new IngotIngredient(IngotConfig.ParseHash(ItemHash), ItemName, Count);
}

public sealed class ColorConfig
{
    public float R { get; set; } = 1f;
    public float G { get; set; } = 1f;
    public float B { get; set; } = 1f;
    public float A { get; set; } = 1f;

    public Color ToColor() => new Color(R, G, B, A);
}

public sealed class StatScalingConfig
{
    public string SourceMaterialHash { get; set; } = "";
    public float DamageScale { get; set; } = 1f;
    public float DurabilityScale { get; set; } = 1f;
}

public sealed class EmissionPulseConfig
{
    public float LowMultiplier { get; set; } = 0f;
    public float HighMultiplier { get; set; } = 1f;
    public float BeatsPerMinute { get; set; } = 60f;
    public EmissionFadeCycleConfig? FadeCycle { get; set; }

    public IngotEmissionPulse ToPulse() => FadeCycle == null
        ? new IngotEmissionPulse(LowMultiplier, HighMultiplier, BeatsPerMinute)
        : new IngotEmissionPulse(LowMultiplier, HighMultiplier, FadeCycle.ToFadeCycle());
}

public sealed class EmissionFadeCycleConfig
{
    public float OffHoldSeconds { get; set; }
    public float FadeInSeconds { get; set; }
    public float GlowHoldSeconds { get; set; }
    public float FadeOutSeconds { get; set; }

    public IngotEmissionFadeCycle ToFadeCycle() => new IngotEmissionFadeCycle(
        OffHoldSeconds, FadeInSeconds, GlowHoldSeconds, FadeOutSeconds);
}

public sealed class GradientConfig
{
    public ColorConfig? Start { get; set; }
    public ColorConfig? End { get; set; }
    public bool Reverse { get; set; }

    public IngotGradient ToGradient()
    {
        if (Start == null || End == null)
            throw new InvalidDataException("A gradient needs both 'start' and 'end' colors.");
        return new IngotGradient(Start.ToColor(), End.ToColor(), Reverse);
    }
}
