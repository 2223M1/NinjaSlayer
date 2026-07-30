using Godot;

namespace NinjaSlayer.Code.Transition;

internal sealed class TransitionNodeProcessLease : IDisposable
{
    // Captures are restored children-before-parents, so they are kept in a list and walked
    // backwards. The set only dedupes. Reversing the dictionary with LINQ used to buffer the whole
    // staged node map into a temporary array on the reveal frame.
    private readonly List<(Node Node, Node.ProcessModeEnum ProcessMode)> _overrides = [];
    private readonly HashSet<Node> _captured = new(ReferenceEqualityComparer.Instance);
    private readonly Node _root;
    private readonly SceneTree _tree;
    private int _disposed;

    public TransitionNodeProcessLease(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _tree = (SceneTree)Engine.GetMainLoop();
        _tree.NodeAdded += OnNodeAdded;
        try
        {
            int childCount = _root.GetChildCount();
            for (int index = 0; index < childCount; index++)
            {
                DisablePresentationSubtree(_root.GetChild(index));
            }
        }
        catch
        {
            try
            {
                _tree.NodeAdded -= OnNodeAdded;
            }
            catch
            {
            }
            finally
            {
                RestoreOverrides();
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        try
        {
            if (GodotObject.IsInstanceValid(_tree))
            {
                _tree.NodeAdded -= OnNodeAdded;
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex);
        }

        RestoreOverrides(failures);
        if (failures.Count > 0)
        {
            throw new AggregateException(
                "One or more staged Transition nodes could not restore their ProcessMode.",
                failures);
        }
    }

    private void OnNodeAdded(Node node)
    {
        if (Volatile.Read(ref _disposed) != 0
            || !GodotObject.IsInstanceValid(_root)
            || (!ReferenceEquals(node, _root) && !_root.IsAncestorOf(node)))
        {
            return;
        }

        // Godot emits node_added for a parent and then for every descendant, so each node in a
        // freshly instantiated branch arrives on its own event. Recursing here re-walked the same
        // subtree once per level while the staged run was being built.
        if (ReferenceEquals(node.GetParent(), _root))
        {
            // Direct children are forced Disabled even when they inherit, which is what actually
            // freezes the staged presentation.
            CaptureAndDisable(node);
            return;
        }

        DisableExplicitProcessOverride(node);
    }

    private void DisablePresentationSubtree(Node node)
    {
        CaptureAndDisable(node);
        int childCount = node.GetChildCount();
        for (int index = 0; index < childCount; index++)
        {
            DisableExplicitProcessOverrides(node.GetChild(index));
        }
    }

    /// <summary>
    /// Walks an existing subtree at lease creation. Nodes that appear later arrive through
    /// <see cref="OnNodeAdded"/> instead and are handled one at a time.
    /// </summary>
    private void DisableExplicitProcessOverrides(Node node)
    {
        DisableExplicitProcessOverride(node);
        int childCount = node.GetChildCount();
        for (int index = 0; index < childCount; index++)
        {
            DisableExplicitProcessOverrides(node.GetChild(index));
        }
    }

    private void DisableExplicitProcessOverride(Node node)
    {
        if (node.ProcessMode is not Node.ProcessModeEnum.Inherit
            and not Node.ProcessModeEnum.Disabled)
        {
            CaptureAndDisable(node);
        }
    }

    private void CaptureAndDisable(Node node)
    {
        if (_captured.Add(node))
        {
            _overrides.Add((node, node.ProcessMode));
            node.ProcessMode = Node.ProcessModeEnum.Disabled;
        }
    }

    private void RestoreOverrides(List<Exception>? failures = null)
    {
        for (int index = _overrides.Count - 1; index >= 0; index--)
        {
            (Node node, Node.ProcessModeEnum processMode) = _overrides[index];
            try
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    node.ProcessMode = processMode;
                }
            }
            catch (Exception ex)
            {
                failures?.Add(ex);
            }
        }

        _overrides.Clear();
        _captured.Clear();
    }
}
