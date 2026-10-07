# Custom Ingots API

Create custom ingots for *A Township Tale* by editing a JSON file. The shared config loader reads your definitions and registers them through this API, so you do not need to write, compile, or install a separate mod DLL for each ingot.

## What you need

- The `CustomIngots.API.dll` from the [latest release](https://github.com/calebv2/Custom-Ingots/releases/latest).
- The generic `CustomIngots.Config.dll` loader.
- A compatible ingot integration mod installed on the server and clients. This API provides the definitions and registration catalog; on its own it does not add game recipes, prefabs, or materials.

Install `CustomIngots.API.dll` in `UserLibs` on the server and each client. Install `CustomIngots.Config.dll` and the compatible ingot integration mod in the appropriate `Mods` folders. Put the same `ingots.json` file on the server and every client so they agree on item and network identifiers.

## Create an ingot

1. Start the game once with the config loader installed. It creates `UserData/CustomIngots/ingots.json` with an example ingot.
2. Edit the JSON file. You can define one or more ingots in the `ingots` array.
3. Replace the example hashes and ingredient item hash with IDs that are valid for your mod and game setup.
4. Copy the same JSON file to the server and every client, then restart.

Example:

```json
{
  "ingots": [
    {
      "itemName": "Example Alloy Ingot",
      "sourceItemName": "Iron Ingot",
      "itemHash": "0x45584901",
      "prefabHash": "0x5101",
      "recipeHash": "0x45585201",
      "materialHash": "0x45584D01",
      "materialName": "Example Alloy",
      "ingredients": [
        { "itemHash": "12345", "itemName": "Iron Ingot", "count": 2 }
      ],
      "tint": { "r": 0.35, "g": 0.75, "b": 0.55, "a": 1.0 },
      "emission": { "r": 0.05, "g": 0.15, "b": 0.08, "a": 1.0 }
    }
  ]
}
```

The sample's `12345` ingredient hash is a placeholder. Hashes can be decimal strings or hexadecimal strings prefixed with `0x`. Keep your actual IDs stable and use the same config on all runtimes.

## Definition fields

| Field | Meaning |
| --- | --- |
| `itemName` | Display name for the ingot. Must be unique in the catalog. |
| `sourceItemName` | Existing item used as the base/source ingot. |
| `itemHash` | Stable item identifier. Must be nonzero and unique. |
| `prefabHash` | Stable network prefab identifier. Must be unique and fit in 16 bits (`1` to `65535`). |
| `recipeHash` | Stable recipe identifier. Must be nonzero and unique. |
| `materialHash` | Stable material identifier. Must be nonzero and unique. |
| `materialName` | Name for the custom material. |
| `ingredients` | One or more `IngotIngredient(itemHash, itemName, count)` entries. Counts must be positive. |
| `tint`, `emission` | Material colors, using Unity's `Color` type. |
| `legacyPrefabHashes` | Optional trailing prefab IDs to keep recognizing older IDs when migrating an existing ingot. |

All hashes should remain the same across updates and match on server and clients. Use IDs reserved for your mod; do not copy the example IDs. The catalog rejects duplicate item, prefab, recipe, or material hashes and duplicate item names. It also rejects two definitions that use the same set of ingredient item hashes, even if their ingredient counts differ. An ingot cannot be one of its own ingredients.

## Optional gameplay stat scaling

Add a `statScaling` object to an ingot to base gameplay stats on another material:

```json
"statScaling": {
  "sourceMaterialHash": "16222",
  "damageScale": 1.2,
  "durabilityScale": 0.9
}
```

The integration uses the selected source material's gameplay profile and applies the damage and durability multipliers. Both multipliers must be positive, finite numbers. Without `IngotStatScaling`, the API leaves the integration to use its default gameplay stats for the ingot.

## Troubleshooting

- **The ingot is missing or differs between players:** confirm the API and config-loader DLLs are installed on the server and every client, and that each runtime has the same `ingots.json` with matching hashes.
- **Registration throws an exception:** check for duplicate hashes or item names, repeated ingredient sets, a zero hash, or a prefab hash above `65535`.
- **The catalog is already sealed:** the config loader must run during initialization, before the ingot integration reads the catalog. Check that the loader is installed and enabled.
- **The API is installed but no ingot appears:** make sure a compatible ingot integration mod is installed. The API catalog alone does not create in-game recipes or assets.

## Building the config loader

Most ingot creators only need the released loader and an `ingots.json` file. To build the generic loader from source, first build the API, then run:

```bash
./ConfigLoader/build.sh
```

The loader assembly is written to `ConfigLoader/bin/CustomIngots.Config.dll`. It is a shared loader; it does not need to be rebuilt for each ingot.

## Building the API

The API project targets .NET Framework 4.7.2 and references `UnityEngine.CoreModule.dll` from the game installation. Set `GAME_PATH` if the game files are elsewhere, or `DOTNET` to select a .NET SDK executable.

```bash
./build.sh
```

The Release assembly is written to `bin/CustomIngots.API.dll`.
