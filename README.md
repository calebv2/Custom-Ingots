# Custom Ingots API

A reusable .NET API for registering custom ingots in A Township Tale. The same ingot definitions can be registered on the server and each client so item hashes, network prefabs, materials, and appearance stay aligned.

## Use the API

Download [`CustomIngots.API.dll`](https://github.com/calebv2/Custom-Ingots/releases/latest/download/CustomIngots.API.dll) from Releases. Install it in the `UserLibs` folder on the server and every client that uses a mod built against the API.

Reference the DLL from a mod, add `using CustomIngots.API;`, then register definitions during `OnInitializeMelon` on the server and every client, before [Crysteel](https://github.com/calebv2/Crysteel) begins late initialization:

```csharp
IngotCatalog.Register(new IngotDefinition(
    "Example Ingot", "Iron Ingot",
    itemHash, prefabHash, recipeHash, materialHash, "Example Material",
    new[] { new IngotIngredient(sourceItemHash, "Source Ingot", 1) },
    tint, emission));
```

An `IngotDefinition` provides a unique item name and item, prefab, recipe, and material hashes; a source ingot name; one or more recipe ingredients; and tint/emission colors. Network prefab hashes must fit in 16 bits. An optional `IngotStatScaling` can scale damage and durability from a source material; definitions without it inherit Iron gameplay stats.

Registration closes when Crysteel starts late initialization. The catalog rejects late registrations and duplicate item, prefab, recipe, or material hashes. The same definition must be registered on the server and all clients. The API registers ingot data; Crysteel adds the server-side smelting and mould recipes.

## Build

The project targets .NET Framework 4.7.2 and references `UnityEngine.CoreModule.dll` from the installed game. Set `GAME_PATH` if the game files are elsewhere, or `DOTNET` to select a .NET SDK executable.

```bash
./build.sh
```

The Release assembly is written to `bin/CustomIngots.API.dll`. This repository tracks source only; the downloadable DLL is attached to the matching GitHub release.
