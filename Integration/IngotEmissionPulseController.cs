using CustomIngots.API;
using UnityEngine;

namespace CustomIngots.Config.Integration;

internal sealed class IngotEmissionPulseController : MonoBehaviour
{
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    private Renderer[] renderers = System.Array.Empty<Renderer>();
    private bool[] hasEmission = System.Array.Empty<bool>();
    private bool[] hasEmissionColor = System.Array.Empty<bool>();
    private Color emission;
    private IngotEmissionPulse? pulse;

    internal bool IsPulsing => pulse != null;

    internal bool Matches(Renderer[] targetRenderers, Color baseEmission, IngotEmissionPulse settings)
    {
        if (!ReferenceEquals(pulse, settings) || !emission.Equals(baseEmission)
            || targetRenderers == null || targetRenderers.Length != renderers.Length)
            return false;

        for (var index = 0; index < renderers.Length; index++)
        {
            if (!ReferenceEquals(renderers[index], targetRenderers[index])) return false;
        }

        return true;
    }

    internal void Bind(Renderer[] targetRenderers, Color baseEmission, IngotEmissionPulse settings)
    {
        renderers = targetRenderers ?? System.Array.Empty<Renderer>();
        hasEmission = new bool[renderers.Length];
        hasEmissionColor = new bool[renderers.Length];
        emission = baseEmission;
        pulse = settings;

        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (renderer == null) continue;
            var material = renderer.material;
            if (material == null) continue;
            hasEmission[index] = material.HasProperty(EmissionId);
            hasEmissionColor[index] = material.HasProperty(EmissionColorId);
        }

        ApplyNow();
    }

    internal void Unbind(Renderer[] targetRenderers, Color baseEmission)
    {
        renderers = targetRenderers ?? System.Array.Empty<Renderer>();
        hasEmission = new bool[renderers.Length];
        hasEmissionColor = new bool[renderers.Length];
        emission = baseEmission;
        pulse = null;

        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (renderer == null) continue;
            var material = renderer.material;
            if (material == null) continue;
            hasEmission[index] = material.HasProperty(EmissionId);
            hasEmissionColor[index] = material.HasProperty(EmissionColorId);

            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock);
            if (hasEmission[index]) propertyBlock.SetColor(EmissionId, emission);
            if (hasEmissionColor[index]) propertyBlock.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    internal void ApplyNow()
    {
        if (pulse == null) return;

        float glow;
        var fadeCycle = pulse.FadeCycle;
        if (fadeCycle == null)
        {
            var phase = Mathf.Repeat(Time.unscaledTime * pulse.BeatsPerMinute / 60f, 1f);
            var firstBeat = Mathf.Exp(-Mathf.Pow((phase - 0.16f) / 0.035f, 2f));
            var secondBeat = 0.68f * Mathf.Exp(-Mathf.Pow((phase - 0.29f) / 0.052f, 2f));
            glow = Mathf.Clamp01(firstBeat + secondBeat);
        }
        else
        {
            var seconds = Mathf.Repeat(Time.unscaledTime, fadeCycle.TotalSeconds);
            if (seconds < fadeCycle.OffHoldSeconds)
                glow = 0f;
            else if (seconds < fadeCycle.OffHoldSeconds + fadeCycle.FadeInSeconds)
                glow = Mathf.SmoothStep(0f, 1f, (seconds - fadeCycle.OffHoldSeconds) / fadeCycle.FadeInSeconds);
            else if (seconds < fadeCycle.OffHoldSeconds + fadeCycle.FadeInSeconds + fadeCycle.GlowHoldSeconds)
                glow = 1f;
            else
                glow = Mathf.SmoothStep(1f, 0f,
                    (seconds - fadeCycle.OffHoldSeconds - fadeCycle.FadeInSeconds - fadeCycle.GlowHoldSeconds)
                    / fadeCycle.FadeOutSeconds);
        }

        var multiplier = Mathf.Lerp(pulse.LowMultiplier, pulse.HighMultiplier, glow);
        var animatedEmission = new Color(
            emission.r * multiplier,
            emission.g * multiplier,
            emission.b * multiplier,
            emission.a);

        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (renderer == null || (!hasEmission[index] && !hasEmissionColor[index])) continue;

            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock);
            if (hasEmission[index]) propertyBlock.SetColor(EmissionId, animatedEmission);
            if (hasEmissionColor[index]) propertyBlock.SetColor(EmissionColorId, animatedEmission);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private void LateUpdate() => ApplyNow();
}
