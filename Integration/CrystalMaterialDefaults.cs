using System;
using System.Reflection;

namespace CustomIngots.Config.Integration;

internal static class CrystalMaterialDefaults
{
    private const float CrystalDamageMultiplier = 0.85f;
    private const float CrystalDurabilityMultiplier = 0.85f;
    private static readonly BindingFlags MaterialFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void ApplyCrystal(PhysicalMaterial target, PhysicalMaterial iron)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (iron == null) throw new ArgumentNullException(nameof(iron));

        // The target is an Iron clone. Its remaining gameplay properties stay on Iron's values.
        SetFloatField(target, "damageMultiplier", CrystalDamageMultiplier);
        SetFloatField(target, "durabilityMultiplier", CrystalDurabilityMultiplier);
    }

    private static void SetFloatField(PhysicalMaterial material, string name, float value)
    {
        var field = typeof(PhysicalMaterial).GetField(name, MaterialFields);
        if (field == null || field.FieldType != typeof(float))
            throw new MissingFieldException(typeof(PhysicalMaterial).FullName, name + " (expected Single)");
        field.SetValue(material, value);
    }
}
