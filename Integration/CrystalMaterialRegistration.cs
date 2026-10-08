using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta;
using UnityEngine;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

public static class CrystalMaterialRegistration
{
    public const uint CrystalMaterialHash = IngotCatalog.CrystalMaterialHash;
    private const string AppearanceTemplateName = "Iron";
    private static readonly BindingFlags MaterialFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(object).FullName, "MemberwiseClone");

    private static readonly Dictionary<uint, PhysicalMaterial> RegisteredMaterials = new Dictionary<uint, PhysicalMaterial>();
    public static PhysicalMaterial? RegisteredMaterial { get; private set; }

    public static PhysicalMaterial? FindRegistered(uint hash) => RegisteredMaterials.TryGetValue(hash, out var material) ? material : null;

    public static PhysicalMaterial CreateAndRegister() => CreateAndRegister(IngotCatalog.Crystal, CrystalMaterialDefaults.ApplyCrystal);

    public static PhysicalMaterial CreateAndRegister(IngotDefinition definition, Action<PhysicalMaterial, PhysicalMaterial>? applyStats = null)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (RegisteredMaterials.TryGetValue(definition.MaterialHash, out var existing)) return existing;

        PhysicalMaterial.CheckItems();
        var ironAppearance = PhysicalMaterial.All.FirstOrDefault(candidate =>
            string.Equals(candidate.name, AppearanceTemplateName, StringComparison.Ordinal));
        if (ironAppearance == null)
            throw new InvalidOperationException("Could not resolve the vanilla Iron PhysicalMaterial used by the RepairHammer crystal appearance.");

        var scaling = definition.StatScaling;
        var sourceMaterial = scaling == null
            ? ironAppearance
            : PhysicalMaterial.All.FirstOrDefault(candidate => candidate.Hash == scaling.SourceMaterialHash)
                ?? throw new InvalidOperationException("Could not resolve source PhysicalMaterial hash "
                    + scaling.SourceMaterialHash + " for " + definition.ItemName + ".");

        Core.Logger.Msg("Crysteel material template for " + definition.ItemName + ": PhysicalMaterial '"
            + sourceMaterial.name + "'(" + sourceMaterial.Hash + ").");

        var material = UnityEngine.Object.Instantiate(sourceMaterial);
        material.name = definition.MaterialName;
        var showInListField = typeof(PhysicalMaterial).GetField("isShowingInList", MaterialFields);
        if (showInListField?.FieldType == typeof(bool)) showInListField.SetValue(material, true);
        CopyAndTintAppearance(material, ironAppearance, definition);
        if (scaling != null) ApplyStatScaling(material, sourceMaterial, scaling);
        applyStats?.Invoke(material, sourceMaterial);
        if (scaling != null)
            Core.Logger.Msg("Crysteel " + definition.ItemName + " effective stats: damage="
                + material.DamageMultiplier + " (source " + sourceMaterial.DamageMultiplier + "), durability="
                + material.DurabilityMultiplier + " (source " + sourceMaterial.DurabilityMultiplier + ").");
        AssignStableHash(material, definition.MaterialHash, definition.MaterialName);
        Register(material, definition);
        Core.Logger.Msg("Registered Crysteel material '" + material.name + "' with stable hash " + material.Hash + ".");
        return material;
    }

    private static void ApplyStatScaling(PhysicalMaterial target, PhysicalMaterial source, IngotStatScaling scaling)
    {
        SetScaledField(target, source, "damageMultiplier", scaling.DamageScale);
        SetScaledField(target, source, "durabilityMultiplier", scaling.DurabilityScale);
    }

    private static void SetScaledField(PhysicalMaterial target, PhysicalMaterial source, string fieldName, float scale)
    {
        var field = typeof(PhysicalMaterial).GetField(fieldName, MaterialFields);
        if (field == null || field.FieldType != typeof(float))
            throw new MissingFieldException(typeof(PhysicalMaterial).FullName, fieldName + " (expected Single)");
        var baseValue = (float)(field.GetValue(source) ?? 0f);
        var scaled = baseValue * scale;
        if (float.IsNaN(scaled) || float.IsInfinity(scaled) || scaled <= 0f)
            throw new InvalidOperationException("Scaled " + fieldName + " is invalid for " + target.name + ".");
        field.SetValue(target, scaled);
    }

    private static void CopyAndTintAppearance(PhysicalMaterial material, PhysicalMaterial appearanceTemplate, IngotDefinition definition)
    {
        var clonedMaterials = new Dictionary<Material, Material>();
        foreach (var field in typeof(PhysicalMaterial).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(Material) && field.GetValue(appearanceTemplate) is Material sourceMaterial)
            {
                field.SetValue(material, CloneAndTintMaterial(sourceMaterial, clonedMaterials, definition));
            }
        }

        var channelsField = typeof(PhysicalMaterial).GetField("materialChannels", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(PhysicalMaterial).FullName, "materialChannels");
        if (channelsField.GetValue(appearanceTemplate) is not Array channels)
        {
            throw new InvalidOperationException("Crysteel requires Iron PhysicalMaterial.materialChannels to be an array.");
        }

        var elementType = channels.GetType().GetElementType()
            ?? throw new InvalidOperationException("Crysteel material channel type is unavailable.");
        var clonedChannels = Array.CreateInstance(elementType, channels.Length);
        for (var index = 0; index < channels.Length; index++)
        {
            var channel = channels.GetValue(index);
            if (channel == null) continue;

            var clonedChannel = MemberwiseCloneMethod.Invoke(channel, null)
                ?? throw new InvalidOperationException("Crysteel failed to clone an Iron material channel.");
            foreach (var field in channel.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(Material) && field.GetValue(clonedChannel) is Material sourceMaterial)
                {
                    field.SetValue(clonedChannel, CloneAndTintMaterial(sourceMaterial, clonedMaterials, definition));
                }
            }

            clonedChannels.SetValue(clonedChannel, index);
        }

        channelsField.SetValue(material, clonedChannels);
        Core.Logger.Msg("Crysteel appearance: copied vanilla Iron renderer materials/channels for "
            + definition.ItemName + ", cloned " + clonedMaterials.Count + " Unity materials, and applied its configured tint/emission.");
    }

    private static Material CloneAndTintMaterial(Material source, IDictionary<Material, Material> clones, IngotDefinition definition)
    {
        if (clones.TryGetValue(source, out var existing)) return existing;

        var clone = new Material(source) { name = definition.ItemName + " " + source.name };
        var tint = definition.Tint;
        var emission = definition.Emission;

        foreach (var propertyName in new[] { "_ColorA", "_ColorB", "_Color" })
        {
            if (CrystalAppearancePolicy.ShouldTint(propertyName) && clone.HasProperty(propertyName)) clone.SetColor(propertyName, tint);
        }

        foreach (var propertyName in new[] { "_Emission", "_EmissionColor" })
        {
            if (!CrystalAppearancePolicy.ShouldSetEmission(propertyName) || !clone.HasProperty(propertyName)) continue;
            clone.EnableKeyword("_EMISSION");
            clone.SetColor(propertyName, emission);
        }

        clones.Add(source, clone);
        return clone;
    }

    private static void Register(PhysicalMaterial material, IngotDefinition definition)
    {
        var registry = GetRegistry();
        if (registry.TryGetValue(material.Hash, out var existing))
        {
            if (ReferenceEquals(existing, material))
            {
                RegisteredMaterials[material.Hash] = material;
                if (definition.MaterialHash == CrystalMaterialHash) RegisteredMaterial = material;
                return;
            }

            throw new InvalidOperationException("A different PhysicalMaterial is already registered for Crystal material hash " + material.Hash + ".");
        }

        registry.Add(material.Hash, material);
        RegisteredMaterials.Add(material.Hash, material);
        if (definition.MaterialHash == CrystalMaterialHash) RegisteredMaterial = material;
    }

    private static void AssignStableHash(HashedGeneralValue value, uint hash, string name)
    {
        var hashField = typeof(HashedGeneralValue).GetField("hash", BindingFlags.Instance | BindingFlags.NonPublic);
        if (hashField == null || hashField.FieldType != typeof(int))
        {
            throw new MissingFieldException(typeof(HashedGeneralValue).FullName, "hash (expected serialized Int32 hash)");
        }

        hashField.SetValue(value, unchecked((int)hash));
        if (value.Hash != hash) throw new InvalidOperationException("Could not assign stable hash " + hash + " to " + name + ".");
    }

    private static Dictionary<uint, PhysicalMaterial> GetRegistry()
    {
        var registryType = typeof(HashedGeneralValue<PhysicalMaterial>);
        var registryField = registryType.GetField("items", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(registryType.FullName, "items");
        return registryField.GetValue(null) as Dictionary<uint, PhysicalMaterial>
            ?? throw new InvalidOperationException("PhysicalMaterial registry is unavailable.");
    }
}
