using System.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using NinjaSlayer.Scripts;

namespace NinjaSlayer.Code.Transition;

internal enum TransitionScenePrewarmStatus
{
    Ready,
    Degraded
}

internal readonly record struct TransitionScenePrewarmResult(
    TransitionScenePrewarmStatus Status,
    int InstantiatedSceneCount,
    int RenderedSceneCount,
    int PreparedManagedMethodCount,
    int FailedManagedMethodCount,
    TimeSpan Elapsed,
    string? Diagnostic);

internal static class NinjaSlayerTransitionScenePrewarmer
{
    private const int RenderWidth = 1920;
    private const int RenderHeight = 1080;
    private const int RenderFramesPerScene = 4;
    private static readonly TimeSpan AssetWaitTimeout = TimeSpan.FromSeconds(8);
    private static readonly object SyncRoot = new();
    private static Task<TransitionScenePrewarmResult>? _completion;

    public static void TryStart()
    {
        lock (SyncRoot)
        {
            if (_completion is not null)
            {
                return;
            }

            NGame? game = NGame.Instance;
            if (game is null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree())
            {
                return;
            }

            _completion = WarmAsync(game);
        }
    }

    public static async Task<TransitionScenePrewarmResult> AwaitReadyAsync(
        CancellationToken cancellationToken)
    {
        TryStart();
        Task<TransitionScenePrewarmResult>? completion;
        lock (SyncRoot)
        {
            completion = _completion;
        }

        if (completion is null)
        {
            return new TransitionScenePrewarmResult(
                TransitionScenePrewarmStatus.Degraded,
                0,
                0,
                0,
                0,
                TimeSpan.Zero,
                "NGame was unavailable when scene prewarming was requested.");
        }

        return await completion.WaitAsync(cancellationToken);
    }

    private static async Task<TransitionScenePrewarmResult> WarmAsync(NGame game)
    {
        long startedAt = Stopwatch.GetTimestamp();
        int instantiatedSceneCount = 0;
        int renderedSceneCount = 0;
        int preparedManagedMethodCount = 0;
        int failedManagedMethodCount = 0;
        var failures = new List<string>();
        var visitedManagedTypes = new HashSet<Type>();
        SubViewport? viewport = null;

        try
        {
            string[] instantiatePaths = BuildInstantiationPaths();
            string[] renderPaths = BuildRenderPaths();
            string[] requiredPaths = instantiatePaths
                .Concat(renderPaths)
                .Concat(NAncientNameBanner.AssetPaths)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            long waitStartedAt = Stopwatch.GetTimestamp();
            while (!AreAllCached(requiredPaths))
            {
                if (Stopwatch.GetElapsedTime(waitStartedAt) >= AssetWaitTimeout)
                {
                    string[] missing = GetMissingPaths(requiredPaths);
                    failures.Add(
                        $"timed out waiting for {missing.Length} scene asset(s): " +
                        string.Join(',', missing));
                    break;
                }

                await game.AwaitProcessFrame();
            }

            foreach (string path in instantiatePaths)
            {
                if (!IsCached(path))
                {
                    continue;
                }

                try
                {
                    Node instance = PreloadManager.Cache
                        .GetScene(path)
                        .Instantiate(PackedScene.GenEditState.Disabled);
                    instantiatedSceneCount++;
                    TransitionManagedCodePrewarmResult managedCode =
                        TransitionManagedCodePrewarmer.Prepare(instance, visitedManagedTypes);
                    preparedManagedMethodCount += managedCode.PreparedMethodCount;
                    failedManagedMethodCount += managedCode.FailedMethodCount;
                    instance.Free();
                }
                catch (Exception ex)
                {
                    failures.Add($"instantiate {path}: {ex.GetType().Name}: {ex.Message}");
                }

                await game.AwaitProcessFrame();
            }

            viewport = CreateRenderViewport();
            game.AddChildSafely(viewport);
            foreach (string path in renderPaths)
            {
                if (!IsCached(path))
                {
                    continue;
                }

                Node? visual = null;
                try
                {
                    visual = PreloadManager.Cache
                        .GetScene(path)
                        .Instantiate(PackedScene.GenEditState.Disabled);
                    viewport.AddChildSafely(visual);
                    renderedSceneCount++;
                    for (int frame = 0; frame < RenderFramesPerScene; frame++)
                    {
                        await game.AwaitProcessFrame();
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"render {path}: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (visual is not null && GodotObject.IsInstanceValid(visual))
                    {
                        visual.Free();
                    }
                }
            }

            if (AreAllCached(NAncientNameBanner.AssetPaths))
            {
                NAncientNameBanner? banner = null;
                try
                {
                    banner = NAncientNameBanner.Create(ModelDb.Event<Neow>());
                    if (banner is null)
                    {
                        failures.Add("render ancient name banner: factory returned null");
                    }
                    else
                    {
                        TransitionManagedCodePrewarmResult managedCode =
                            TransitionManagedCodePrewarmer.Prepare(banner, visitedManagedTypes);
                        preparedManagedMethodCount += managedCode.PreparedMethodCount;
                        failedManagedMethodCount += managedCode.FailedMethodCount;
                        viewport.AddChildSafely(banner);
                        renderedSceneCount++;
                        for (int frame = 0; frame < RenderFramesPerScene; frame++)
                        {
                            await game.AwaitProcessFrame();
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"render ancient name banner: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (banner is not null && GodotObject.IsInstanceValid(banner))
                    {
                        banner.Free();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"prewarm: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (viewport is not null && GodotObject.IsInstanceValid(viewport))
            {
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                viewport.Free();
            }
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
        string? diagnostic = failures.Count == 0 ? null : string.Join(" | ", failures);
        var result = new TransitionScenePrewarmResult(
            failures.Count == 0
                ? TransitionScenePrewarmStatus.Ready
                : TransitionScenePrewarmStatus.Degraded,
            instantiatedSceneCount,
            renderedSceneCount,
            preparedManagedMethodCount,
            failedManagedMethodCount,
            elapsed,
            diagnostic);
        Entry.Logger.Info(
            $"NinjaSlayer transition scene prewarm: status={result.Status}, " +
            $"instantiated={result.InstantiatedSceneCount}, rendered={result.RenderedSceneCount}, " +
            $"managed_methods={result.PreparedManagedMethodCount}/{result.FailedManagedMethodCount}, " +
            $"elapsed={result.Elapsed.TotalMilliseconds:F1}ms" +
            (diagnostic is null ? "." : $", diagnostic={diagnostic}."));
        return result;
    }

    private static string[] BuildInstantiationPaths() =>
    [
        .. NRun.AssetPaths,
        .. NEventRoom.AssetPaths,
        NAncientEventLayout.ancientScenePath
    ];

    private static string[] BuildRenderPaths()
    {
        string ancientLayoutPath = NAncientEventLayout.ancientScenePath;
        string neowBackgroundPath = ModelDb.Event<Neow>()
            .GetAssetPaths(NullRunState.Instance)
            .Single(path =>
                path.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, ancientLayoutPath, StringComparison.Ordinal));
        return [neowBackgroundPath];
    }

    private static SubViewport CreateRenderViewport() => new()
    {
        Name = "NinjaSlayerTransitionScenePrewarmViewport",
        Size = new Vector2I(RenderWidth, RenderHeight),
        Size2DOverride = new Vector2I(RenderWidth, RenderHeight),
        TransparentBg = true,
        Disable3D = true,
        GuiDisableInput = true,
        HandleInputLocally = false,
        ProcessMode = Node.ProcessModeEnum.Always,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Always
    };

    private static bool AreAllCached(IEnumerable<string> paths)
    {
        IReadOnlySet<string> loaded = PreloadManager.Cache.GetLoadedCacheAssets();
        return paths.All(loaded.Contains);
    }

    private static bool IsCached(string path) =>
        PreloadManager.Cache.GetLoadedCacheAssets().Contains(path);

    private static string[] GetMissingPaths(IEnumerable<string> paths)
    {
        IReadOnlySet<string> loaded = PreloadManager.Cache.GetLoadedCacheAssets();
        return paths.Where(path => !loaded.Contains(path)).ToArray();
    }
}
