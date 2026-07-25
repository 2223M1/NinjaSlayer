using Godot;

namespace NinjaSlayer.Code.Transition;

internal sealed class TransitionNodeProcessLease : IDisposable
{
    private readonly Dictionary<Node, Node.ProcessModeEnum> _overrides =
        new(ReferenceEqualityComparer.Instance);
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
            foreach (Node child in _root.GetChildren())
            {
                DisablePresentationSubtree(child);
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

        if (ReferenceEquals(node.GetParent(), _root))
        {
            DisablePresentationSubtree(node);
            return;
        }

        DisableExplicitProcessOverrides(node);
    }

    private void DisablePresentationSubtree(Node node)
    {
        CaptureAndDisable(node);
        foreach (Node child in node.GetChildren())
        {
            DisableExplicitProcessOverrides(child);
        }
    }

    private void DisableExplicitProcessOverrides(Node node)
    {
        if (node.ProcessMode is not Node.ProcessModeEnum.Inherit
            and not Node.ProcessModeEnum.Disabled)
        {
            CaptureAndDisable(node);
        }

        foreach (Node child in node.GetChildren())
        {
            DisableExplicitProcessOverrides(child);
        }
    }

    private void CaptureAndDisable(Node node)
    {
        if (_overrides.TryAdd(node, node.ProcessMode))
        {
            node.ProcessMode = Node.ProcessModeEnum.Disabled;
        }
    }

    private void RestoreOverrides(ICollection<Exception>? failures = null)
    {
        foreach ((Node node, Node.ProcessModeEnum processMode) in _overrides.Reverse())
        {
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
    }
}
