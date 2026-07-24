using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;

namespace NinjaSlayer.Code.Transition;

internal readonly record struct TransitionManagedCodePrewarmResult(
    int PreparedMethodCount,
    int FailedMethodCount);

internal static class TransitionManagedCodePrewarmer
{
    private static readonly HashSet<string> ColdPathMethodNames = new(StringComparer.Ordinal)
    {
        "_EnterTree",
        "_Notification",
        "_Ready",
        "Initialize",
        "InitializeVisuals",
        "InvokeGodotClassMethod",
        "SetEvent",
        "SetRunState",
        "UpdateMusic"
    };

    public static TransitionManagedCodePrewarmResult Prepare(
        Node root,
        ISet<Type> visitedTypes)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(visitedTypes);

        var prepared = 0;
        var failed = 0;
        var pending = new Stack<Node>();
        pending.Push(root);

        while (pending.TryPop(out Node? node))
        {
            Type type = node.GetType();
            if (type.Assembly == typeof(MegaCrit.Sts2.Core.Nodes.NRun).Assembly
                && visitedTypes.Add(type))
            {
                foreach (MethodInfo method in type.GetMethods(
                             BindingFlags.Instance
                             | BindingFlags.Static
                             | BindingFlags.Public
                             | BindingFlags.NonPublic
                             | BindingFlags.DeclaredOnly))
                {
                    if (!ColdPathMethodNames.Contains(method.Name)
                        || method.IsAbstract
                        || method.ContainsGenericParameters)
                    {
                        continue;
                    }

                    try
                    {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                        prepared++;
                    }
                    catch
                    {
                        // Pre-JIT is an optional hint. The normal runtime path remains authoritative.
                        failed++;
                    }
                }
            }

            foreach (Node child in node.GetChildren())
            {
                pending.Push(child);
            }
        }

        return new TransitionManagedCodePrewarmResult(prepared, failed);
    }
}
