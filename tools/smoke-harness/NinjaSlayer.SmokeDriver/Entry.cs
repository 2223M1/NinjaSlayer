using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;

namespace NinjaSlayer.SmokeDriver;

[ModInitializer(nameof(Init))]
public static class Entry
{
    public static void Init()
    {
        string? configurationPath = CommandLineHelper.GetValue("ninjaslayer-smoke-config");
        if (string.IsNullOrWhiteSpace(configurationPath))
        {
            return;
        }

        SmokeConfiguration configuration = SmokeConfiguration.Load(configurationPath);
        new Harmony("NinjaSlayer.SmokeDriver").PatchAll(typeof(Entry).Assembly);
        var tree = (SceneTree)Engine.GetMainLoop();
        var controller = new SmokeController(configuration, tree);
        controller.Start();
    }
}
