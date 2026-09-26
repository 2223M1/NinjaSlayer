using System.Reflection;
using Godot;
using HarmonyLib;
using NinjaSlayer.Content;
using STS2RitsuLib.Patching.Models;

namespace NinjaSlayer.Code.Patches;

internal static class CookieCursorIntegration
{
    internal static DynamicPatchInfo[] CreatePatches()
    {
        Type? core = AccessTools.TypeByName("CookieCursor.Core");
        if (core == null) return [];
        MethodInfo cache = AccessTools.Method(core, "CacheCharCookies", Type.EmptyTypes)
            ?? throw new MissingMethodException(core.FullName, "CacheCharCookies");
        return [new DynamicPatchInfo("ninjaslayer_cookie_cursor", cache,
            prefix: new HarmonyMethod(typeof(CookieCursorIntegration), nameof(EnsureDefaultIcon)),
            isCritical: true, description: "Provide NinjaSlayer's default icon through CookieCursor's custom-image interface.")];
    }

    private static void EnsureDefaultIcon(MethodBase __originalMethod)
    {
        string modDirectory = Path.GetDirectoryName(__originalMethod.DeclaringType!.Assembly.Location)
            ?? throw new InvalidOperationException("CookieCursor has no installation directory.");
        string characterDirectory = Path.Combine(modDirectory, "Cursors", typeof(NinjaSlayerCharacter).Name.ToLowerInvariant());
        string icon = Path.Combine(characterDirectory, "icon.png");
        if (File.Exists(icon)) return;
        Directory.CreateDirectory(characterDirectory);
        Texture2D texture = ResourceLoader.Load<Texture2D>(NinjaSlayerAssetProfile.IconTexturePath);
        using Image image = texture.GetImage();
        Error error = image.SavePng(icon);
        if (error != Error.Ok) throw new IOException($"Could not write CookieCursor's NinjaSlayer default icon: {error}.");
    }
}
