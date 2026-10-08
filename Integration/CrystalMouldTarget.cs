using Alta.Blacksmithing;
using Alta.Inventory;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

public sealed class CrystalMouldTarget
{
    public CrystalMouldTarget(MouldDefinition definition, Item product, int cost, int outputQuantity, SmeltingRecipe recipe)
        : this(definition, product, IngotCatalog.Crystal, cost, outputQuantity, recipe)
    {
    }

    public CrystalMouldTarget(MouldDefinition definition, Item product, IngotDefinition ingot, int cost, int outputQuantity, SmeltingRecipe recipe)
    {
        Definition = definition;
        Product = product;
        Ingot = ingot;
        Cost = cost;
        OutputQuantity = outputQuantity;
        Recipe = recipe;
    }

    public MouldDefinition Definition { get; }
    public Item Product { get; }
    public IngotDefinition Ingot { get; }
    public int Cost { get; }
    public int OutputQuantity { get; }
    public SmeltingRecipe Recipe { get; }
    public uint RecipeHash => Recipe.Hash;
}
