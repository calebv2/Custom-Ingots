# Custom Ingots API

Create custom ingots for *A Township Tale* by editing a JSON file. The shared config loader reads your definitions and registers them through this API, so you do not need to write, compile, or install a separate mod DLL for each ingot.

## What you need

- [`CustomIngots.API.dll`](https://github.com/calebv2/Custom-Ingots/releases/latest/download/CustomIngots.API.dll) in `UserLibs`.
- [`CustomIngots.Config.dll`](https://github.com/calebv2/Custom-Ingots/releases/latest/download/CustomIngots.Config.dll) in `Mods`. The loader reads `ingots.json` and registers the ingot items, materials, smelting recipes, forge mould recipes, and client appearances.

Install both DLLs on the server and every client. The server's `ingots.json` is sent to clients when they join and saved to `UserData/CustomIngots/ingots.json`. Individual ingots only need JSON definitions; you do not need to build or install a separate DLL for each one.

## Create an ingot

1. Start the game once with the config loader installed. It creates `UserData/CustomIngots/ingots.json` with an example ingot.
2. Edit the JSON file. You can define one or more ingots in the `ingots` array.
3. Replace the example hashes and ingredient item hash with IDs that are valid for your mod and game setup.
4. Put the finished JSON file on the server and restart it. When a client joins, the server sends its config and the client saves it in the correct `UserData/CustomIngots/ingots.json` location. The client must restart once after receiving a new or changed config so the ingots can be registered before joining a world.

The server's config is authoritative. The client's existing file is backed up as `ingots.json.before-server-sync.bak` the first time it is replaced. Keep a separate backup of any client-only edits you need; syncing replaces the active client config with the server copy. Clients need both Custom Ingots DLLs installed, but they do not need to edit or manually copy the JSON file.

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

### How the ingredients make the ingot

The `ingredients` list is the input list for the ingot's smelting recipe. Add one entry for each item the recipe consumes; `count` sets how many of that item are required. A recipe can use one ingredient, two ingredients, or more. For example, two entries can require 2 Iron Ingots and 1 Coal to smelt into 1 Example Alloy Ingot.

```json
"ingredients": [
  { "itemHash": "12345", "itemName": "Iron Ingot", "count": 2 },
  { "itemHash": "12346", "itemName": "Coal", "count": 1 }
]
```

The hashes in this example are placeholders; replace them with the actual item hashes. The ingot integration adds this as a smelting recipe. Forge mould recipes use the completed ingot as their material to craft other items.

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
| `gradient` | Optional two-color gradient running along the ingot or forged metal part's longest axis. |
| `emissionPulse` | Optional client-side glow animation. Use `beatsPerMinute` for a heartbeat or `fadeCycle` for timed holds and fades. |
| `legacyPrefabHashes` | Optional trailing prefab IDs to keep recognizing older IDs when migrating an existing ingot. |

All hashes should remain the same across updates and match on server and clients. Use IDs reserved for your mod; do not copy the example IDs. The catalog rejects duplicate item, prefab, recipe, or material hashes and duplicate item names. It also rejects two definitions that use the same set of ingredient item hashes, even if their ingredient counts differ. An ingot cannot be one of its own ingredients.

### Optional animated emission

Add `emissionPulse` to animate the configured `emission` color on clients. For a slow rise to full glow followed by a quicker fade back to no glow, use `fadeCycle`:

```json
"emissionPulse": {
  "lowMultiplier": 0.0,
  "highMultiplier": 2.5,
  "fadeCycle": {
    "offHoldSeconds": 1.5,
    "fadeInSeconds": 1.2,
    "glowHoldSeconds": 0.8,
    "fadeOutSeconds": 0.4
  }
}
```

This cycle stays dark for 1.5 seconds, fades to full glow over 1.2 seconds, stays bright for 0.8 seconds, then fades back over 0.4 seconds. It repeats every 3.9 seconds. Increase `offHoldSeconds` or `glowHoldSeconds` to make either state last longer. Increase `fadeInSeconds` or `fadeOutSeconds` to slow that transition. Hold durations can be zero; fade durations must be greater than zero.

[Death Steel's complete JSON definition](examples/death-steel.json) shows these fade controls with its current timing values. Add its entry to your server's `ingots` array if you already have other ingots configured.

For the original double-beat heartbeat, use `beatsPerMinute` instead:

```json
"emissionPulse": {
  "lowMultiplier": 0.0,
  "highMultiplier": 1.0,
  "beatsPerMinute": 60
}
```

The multiplier is applied to the configured emission color. A low multiplier of `0` removes the glow in the dark state; a high multiplier of `1` reaches the configured color. Values above `1` make the peak brighter than the configured emission. With `fadeCycle`, `beatsPerMinute` is ignored. With `beatsPerMinute` alone, the double-beat pattern repeats once per beat; at `60` beats per minute, that is once per second. This changes only the client glow; it does not change damage or durability. Every client needs the updated Custom Ingots DLLs. The server sends the config to clients when they join.

### Optional lengthwise color gradient

Add `gradient` to blend two colors along the longest dimension of each metal mesh. The same setting applies to the loose ingot and generated metal blades or tool heads. It follows the item as it rotates; handles keep their own materials. The configured emission grows from zero at `start` to full brightness at `end`.

```json
"gradient": {
  "start": { "r": 0.08, "g": 0.06, "b": 0.12, "a": 1.0 },
  "end": { "r": 0.90, "g": 0.30, "b": 0.05, "a": 1.0 },
  "reverse": false
}
```

`start` and `end` are the colors at opposite ends of the mesh's longest local axis. Set `reverse` to `true` if you want the colors swapped on a particular ingot or blade. Keep `tint` as a fallback color for any renderer that cannot use the gradient. This is a client appearance setting and does not change gameplay stats. It can be combined with `emissionPulse`.

Gradient-enabled metal uses a separate client material to show the full color blend and glow mask. Its shine and heated appearance can differ from the game's usual metal shader.

[Light & Dark Steel](examples/light-dark-steel.json) is a complete example: 1 Darksteel Ingot and 1 Silver Ingot create a black-to-white ingot with white glow at its bright end. Its damage and durability sit between Crysteel and Death Steel in the supplied server setup.

## Optional gameplay stat scaling

Add a `statScaling` object to an ingot to base gameplay stats on another material:

```json
"statScaling": {
  "sourceMaterialHash": "16222",
  "damageScale": 1.2,
  "durabilityScale": 0.9
}
```

The integration uses the selected source material's gameplay profile. The resulting damage multiplier is the source material's damage multiplier times `damageScale`; durability works the same way. For example, a source with damage `2.0` and durability `1.5` becomes `2.5` and `1.8` with scales of `1.25` and `1.2`. These are material multipliers; a finished item's stats also depend on its item type and crafting quality. Both scales must be positive, finite numbers. Without `statScaling`, the integration uses its default gameplay stats for the ingot.

## Troubleshooting

- **The ingot is missing on a first join:** the client saves the server config after joining. Restart the client once, then reconnect.
- **The client does not receive the server config:** confirm the API and config-loader DLLs are installed on both the server and client, and check the logs for a config delivery or save error.
- **Registration throws an exception:** check for duplicate hashes or item names, repeated ingredient sets, a zero hash, or a prefab hash above `65535`.
- **The catalog is already sealed:** the config loader must run during initialization, before the ingot integration reads the catalog. Check that the loader is installed and enabled.
- **The ingot is missing:** check the server and client logs for a registration error, confirm every ingredient item name and hash exists in the game, and make sure both Custom Ingots DLLs are installed. After the server sends a changed config, restart the client once.

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
