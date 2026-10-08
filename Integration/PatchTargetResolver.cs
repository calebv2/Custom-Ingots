using System.Reflection;
using System.Runtime.CompilerServices;
using Alta.Blacksmithing;

namespace CustomIngots.Config.Integration;

internal static class PatchTargetResolver
{
    internal static MethodBase? FindSmelterCompletionMoveNext()
    {
        var trySmelt = typeof(Smelter).GetMethod(
            "TrySmelt",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(bool) },
            null);
        var stateMachine = trySmelt?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        return stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
