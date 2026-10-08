using System;
using System.Collections.Generic;
using System.Reflection;
using Alta;
using Alta.Blacksmithing;
using Alta.Heat;
using HarmonyLib;
using UnityEngine;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

internal static class CrystalClientAppearancePatch
{
    private static readonly int ForcedTemperaturePropertyId = Shader.PropertyToID("_ForcedTemperature");
    private static readonly FieldInfo RenderersField = typeof(PhysicalMaterialPart)
        .GetField("renderers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(PhysicalMaterialPart).FullName, "renderers");
    private static readonly FieldInfo PhysicalMaterialField = typeof(PhysicalMaterialPart)
        .GetField("physicalMaterial", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(PhysicalMaterialPart).FullName, "physicalMaterial");
    private static readonly FieldInfo TargetRenderersField = typeof(TemperatureToMaterial)
        .GetField("targetRenderers", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TemperatureToMaterial).FullName, "targetRenderers");
    private static readonly FieldInfo TemperatureToValueField = typeof(TemperatureToMaterial)
        .GetField("temperatureToValue", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TemperatureToMaterial).FullName, "temperatureToValue");
    private static readonly HashSet<int> LoggedForgedModels = new HashSet<int>();

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var setMaterial = AccessTools.Method(typeof(PhysicalMaterialPart), nameof(PhysicalMaterialPart.SetMaterial), new[] { typeof(PhysicalMaterial) });
        var updateTemperature = AccessTools.Method(typeof(TemperatureToMaterial), "UpdateValue", new[] { typeof(HeatPoint) });
        var updateForgedMaterial = AccessTools.Method(typeof(ForgedModel), "UpdateMaterial", new[] { typeof(PhysicalMaterial), typeof(PhysicalMaterialChannel) });
        if (setMaterial == null || updateTemperature == null || updateForgedMaterial == null)
        {
            throw new MissingMethodException("Could not resolve all Crystal renderer appearance patch targets, including the generated forged-mesh material update.");
        }

        harmony.Patch(setMaterial, postfix: new HarmonyMethod(typeof(CrystalClientAppearancePatch), nameof(SetMaterialPostfix)));
        harmony.Patch(updateTemperature, postfix: new HarmonyMethod(typeof(CrystalClientAppearancePatch), nameof(TemperaturePostfix)));
        harmony.Patch(updateForgedMaterial, postfix: new HarmonyMethod(typeof(CrystalClientAppearancePatch), nameof(ForgedMaterialPostfix)));
    }

    private static void SetMaterialPostfix(PhysicalMaterialPart __instance, PhysicalMaterial physicalMaterial)
    {
        var appearance = physicalMaterial == null ? null : IngotCatalog.FindByMaterialHash(physicalMaterial.Hash);
        if (RenderersField.GetValue(__instance) is not Renderer[] renderers) return;
        if (appearance == null)
        {
            foreach (var renderer in renderers)
                if (renderer != null) IngotLengthGradientController.Apply(renderer, null);
            return;
        }

        var tint = appearance.Tint;
        var emission = appearance.Emission;
        var rendererCount = 0;
        var emissiveRendererCount = 0;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var gradientApplied = IngotLengthGradientController.Apply(renderer, appearance.Gradient);
            var material = gradientApplied ? renderer.sharedMaterial : renderer.material;
            if (material == null) continue;
            if (!gradientApplied)
            {
                foreach (var propertyName in new[] { "_ColorA", "_ColorB", "_Color" })
                {
                    if (!CrystalAppearancePolicy.ShouldTint(propertyName) || !material.HasProperty(propertyName)) continue;
                    material.SetColor(propertyName, tint);
                }
            }

            var hasEmission = false;
            foreach (var propertyName in new[] { "_Emission", "_EmissionColor" })
            {
                if (!CrystalAppearancePolicy.ShouldSetEmission(propertyName) || !material.HasProperty(propertyName)) continue;
                material.EnableKeyword("_EMISSION");
                material.SetColor(propertyName, emission);
                hasEmission = true;
            }

            rendererCount++;
            if (hasEmission) emissiveRendererCount++;
        }

        BindEmissionPulse(__instance.gameObject, renderers, appearance);
        var forcedValue = FindHeatEndpoint(__instance, renderers) ?? 1f;
        EnsurePersistentPartController(__instance, forcedValue);
        ApplyForcedTemperature(__instance, forcedValue);
        LogMaterialAppearance(__instance, renderers, rendererCount, emissiveRendererCount, forcedValue);
        BindHeatComponents(__instance, renderers, forcedValue);
    }

    private static void TemperaturePostfix(TemperatureToMaterial __instance)
    {
        var part = FindCrystalMaterialPart(__instance);
        if (part == null) return;

        var forcedValue = GetHeatEndpoint(__instance) ?? 1f;
        EnsurePersistentPartController(part, forcedValue);
        ApplyForcedTemperature(__instance, part);
        ApplyForcedTemperature(part, forcedValue);
    }

    private static void ForgedMaterialPostfix(object __instance, PhysicalMaterial physicalMaterial)
    {
        if (physicalMaterial == null
            || IngotCatalog.FindByMaterialHash(physicalMaterial.Hash) == null
            || __instance is not ForgedModel forgedModel)
        {
            return;
        }

        var controller = forgedModel.GetComponent<CrystalForgedMeshAppearanceController>();
        if (controller == null) controller = forgedModel.gameObject.AddComponent<CrystalForgedMeshAppearanceController>();
        controller.Bind(forgedModel);
        ApplyForgedMeshAppearance(forgedModel);
    }

    internal static void ApplyForgedMeshAppearance(ForgedModel forgedModel)
    {
        var physicalMaterial = forgedModel.PhysicalMaterial;
        var appearance = physicalMaterial == null ? null : IngotCatalog.FindByMaterialHash(physicalMaterial.Hash);

        var meshFilter = forgedModel.MeshFilter;
        var renderer = meshFilter == null ? null : meshFilter.GetComponent<Renderer>();
        if (renderer == null) renderer = forgedModel.GetComponent<Renderer>();
        if (renderer == null) return;

        if (appearance == null)
        {
            IngotLengthGradientController.Apply(renderer, null);
            return;
        }

        var gradientApplied = IngotLengthGradientController.Apply(renderer, appearance.Gradient);
        var material = gradientApplied ? renderer.sharedMaterial : renderer.material;
        if (material == null) return;
        var tint = appearance.Tint;
        var emission = appearance.Emission;
        if (!gradientApplied)
        {
            foreach (var propertyName in new[] { "_ColorA", "_ColorB", "_Color" })
            {
                if (CrystalAppearancePolicy.ShouldTint(propertyName) && material.HasProperty(propertyName))
                    material.SetColor(propertyName, tint);
            }
        }
        foreach (var propertyName in new[] { "_Emission", "_EmissionColor" })
        {
            if (!CrystalAppearancePolicy.ShouldSetEmission(propertyName) || !material.HasProperty(propertyName)) continue;
            material.EnableKeyword("_EMISSION");
            material.SetColor(propertyName, emission);
        }

        BindEmissionPulse(forgedModel.gameObject, new[] { renderer }, appearance);
        ApplyForcedTemperature(new[] { renderer }, 1f);
        if (LoggedForgedModels.Add(forgedModel.GetInstanceID()))
        {
            Core.Logger.Msg("Crystal generated forged mesh appearance applied: item='" + forgedModel.name
                + "', material='" + material.name + "', shader='" + (material.shader == null ? "<null>" : material.shader.name)
                + "', emission=" + material.HasProperty("_EmissionColor")
                + ", forcedTemperature=" + material.HasProperty(ForcedTemperaturePropertyId) + ".");
        }
    }

    private static void BindEmissionPulse(GameObject owner, Renderer[] renderers, IngotDefinition definition)
    {
        var controller = owner.GetComponent<IngotEmissionPulseController>();
        if (definition.EmissionPulse == null)
        {
            if (controller != null && controller.IsPulsing) controller.Unbind(renderers, definition.Emission);
            return;
        }

        if (controller == null) controller = owner.AddComponent<IngotEmissionPulseController>();
        if (!controller.Matches(renderers, definition.Emission, definition.EmissionPulse))
            controller.Bind(renderers, definition.Emission, definition.EmissionPulse);
        else
            controller.ApplyNow();
    }

    private static void LogMaterialAppearance(
        PhysicalMaterialPart part,
        Renderer[] renderers,
        int rendererCount,
        int emissiveRendererCount,
        float forcedValue)
    {
        var forcedPropertyRenderers = 0;
        foreach (var renderer in renderers)
        {
            var material = renderer == null ? null : renderer.sharedMaterial;
            if (material != null && material.HasProperty(ForcedTemperaturePropertyId)) forcedPropertyRenderers++;
        }

        Core.Logger.Msg("Crystal appearance applied: item='" + part.transform.root.name
            + "', renderers=" + rendererCount
            + ", emissiveRenderers=" + emissiveRendererCount
            + ", forcedTemperaturePropertyRenderers=" + forcedPropertyRenderers
            + ", forcedTemperature=" + forcedValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    private static void BindHeatComponents(PhysicalMaterialPart part, Renderer[] partRenderers, float fallbackValue)
    {
        var root = part.transform.root;
        var heatComponents = root == null ? Array.Empty<TemperatureToMaterial>() : root.GetComponentsInChildren<TemperatureToMaterial>(true);
        var matchedCount = 0;
        foreach (var heatComponent in heatComponents)
        {
            if (heatComponent == null || !SharesRenderer(partRenderers, GetTargetRenderers(heatComponent))) continue;
            var componentValue = GetHeatEndpoint(heatComponent) ?? fallbackValue;
            EnsurePersistentPartController(part, componentValue);
            ApplyForcedTemperature(heatComponent, part);
            matchedCount++;
        }

        Core.Logger.Msg("Crystal heat component scan: item='" + (root == null ? part.name : root.name)
            + "', components=" + heatComponents.Length + ", matchingCrystalRenderers=" + matchedCount + ".");
    }

    private static void EnsurePersistentPartController(PhysicalMaterialPart part, float forcedValue)
    {
        var controller = part.GetComponent<CrystalHeatedAppearanceController>();
        if (controller == null) controller = part.gameObject.AddComponent<CrystalHeatedAppearanceController>();
        controller.Bind(part, forcedValue);
    }

    private static PhysicalMaterialPart? FindCrystalMaterialPart(TemperatureToMaterial component)
    {
        var targetRenderers = GetTargetRenderers(component);
        var root = component.transform.root;
        if (root != null)
        {
            foreach (var candidate in root.GetComponentsInChildren<PhysicalMaterialPart>(true))
            {
                if (IsCrystalMaterial(candidate)
                    && SharesRenderer(targetRenderers, RenderersField.GetValue(candidate) as Renderer[]))
                {
                    return candidate;
                }
            }
        }

        var ancestor = component.GetComponentInParent<PhysicalMaterialPart>();
        return IsCrystalMaterial(ancestor) ? ancestor : null;
    }

    private static bool IsCrystalMaterial(PhysicalMaterialPart? part)
    {
        var physicalMaterial = part == null ? null : PhysicalMaterialField.GetValue(part) as PhysicalMaterial;
        return physicalMaterial != null && IngotCatalog.FindByMaterialHash(physicalMaterial.Hash) != null;
    }

    private static Renderer[]? GetTargetRenderers(TemperatureToMaterial component)
    {
        return TargetRenderersField.GetValue(component) as Renderer[];
    }

    private static bool SharesRenderer(Renderer[]? left, Renderer[]? right)
    {
        if (left == null || right == null) return false;
        foreach (var leftRenderer in left)
        {
            if (leftRenderer == null) continue;
            foreach (var rightRenderer in right)
            {
                if (ReferenceEquals(leftRenderer, rightRenderer)) return true;
            }
        }

        return false;
    }

    private static float? FindHeatEndpoint(PhysicalMaterialPart part, Renderer[] partRenderers)
    {
        var root = part.transform.root;
        if (root == null) return null;
        foreach (var component in root.GetComponentsInChildren<TemperatureToMaterial>(true))
        {
            if (component != null
                && SharesRenderer(partRenderers, GetTargetRenderers(component))
                && GetHeatEndpoint(component) is float endpoint)
            {
                return endpoint;
            }
        }

        return null;
    }

    private static float? GetHeatEndpoint(TemperatureToMaterial component)
    {
        var curve = TemperatureToValueField.GetValue(component) as AnimationCurve;
        if (curve == null || curve.length == 0) return null;
        var keys = curve.keys;
        return keys[keys.Length - 1].value;
    }

    private static void ApplyForcedTemperature(TemperatureToMaterial component, PhysicalMaterialPart part)
    {
        var renderers = GetTargetRenderers(component);
        if (renderers == null) return;
        var forcedValue = GetHeatEndpoint(component) ?? 1f;
        ApplyForcedTemperature(renderers, forcedValue);
    }

    internal static void ApplyForcedTemperature(PhysicalMaterialPart part, float forcedValue)
    {
        if (!IsCrystalMaterial(part)) return;
        if (RenderersField.GetValue(part) is Renderer[] renderers)
            ApplyForcedTemperature(renderers, forcedValue);
    }

    private static void ApplyForcedTemperature(Renderer[] renderers, float forcedValue)
    {
        var props = new MaterialPropertyBlock();
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(props);
            props.SetFloat(ForcedTemperaturePropertyId, forcedValue);
            renderer.SetPropertyBlock(props);
        }
    }
}

internal sealed class CrystalHeatedAppearanceController : MonoBehaviour
{
    private PhysicalMaterialPart? materialPart;
    private float forcedValue = 1f;

    internal void Bind(PhysicalMaterialPart crystalPart, float value)
    {
        materialPart = crystalPart;
        forcedValue = value;
    }

    private void LateUpdate()
    {
        if (materialPart != null)
            CrystalClientAppearancePatch.ApplyForcedTemperature(materialPart, forcedValue);
    }
}

internal sealed class CrystalForgedMeshAppearanceController : MonoBehaviour
{
    private ForgedModel? forgedModel;

    internal void Bind(ForgedModel model)
    {
        forgedModel = model;
    }

    private void LateUpdate()
    {
        if (forgedModel != null)
            CrystalClientAppearancePatch.ApplyForgedMeshAppearance(forgedModel);
    }
}
