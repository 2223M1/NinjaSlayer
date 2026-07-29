using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace NinjaSlayer.Code.ExternalAnimations;

internal sealed class BossDeathWhiteoutLease : IDisposable
{
    public const string ShaderPath =
        "res://NinjaSlayer/shaders/vfx/boss_death_whiteout.gdshader";

    private static readonly StringName WhiteMixParameter = new("white_mix");

    private readonly MegaSprite _sprite;
    private readonly Material? _originalNormalMaterial;
    private readonly ShaderMaterial _whiteoutMaterial;
    private readonly List<SlotColorState> _slotColors;
    private int _disposed;

    private BossDeathWhiteoutLease(
        MegaSprite sprite,
        Material? originalNormalMaterial,
        ShaderMaterial whiteoutMaterial,
        List<SlotColorState> slotColors)
    {
        _sprite = sprite;
        _originalNormalMaterial = originalNormalMaterial;
        _whiteoutMaterial = whiteoutMaterial;
        _slotColors = slotColors;
    }

    public static IEnumerable<string> AssetPaths => [ShaderPath];

    public static bool TryAcquire(
        NCreature boss,
        out BossDeathWhiteoutLease? lease,
        out string failureReason)
    {
        lease = null;
        failureReason = string.Empty;
        ShaderMaterial? material = null;
        List<SlotColorState>? slotColors = null;
        try
        {
            MegaSprite? sprite = boss.Visuals.SpineBody;
            if (sprite == null)
            {
                failureReason = "the Boss has no active Spine body";
                return false;
            }

            Shader shader = ResourceLoader.Load<Shader>(ShaderPath)
                ?? throw new InvalidOperationException("the whiteout shader is unavailable");
            material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter(WhiteMixParameter, 0f);
            slotColors = CaptureSlotColors(sprite);
            Material? originalMaterial = sprite.GetNormalMaterial();
            sprite.SetNormalMaterial(material);
            lease = new BossDeathWhiteoutLease(
                sprite,
                originalMaterial,
                material,
                slotColors);
            return true;
        }
        catch (Exception exception)
        {
            if (slotColors != null)
            {
                DisposeSlots(slotColors);
            }

            material?.Dispose();
            failureReason = exception.Message;
            return false;
        }
    }

    public void SetMix(float whiteMix)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        float mix = Math.Clamp(whiteMix, 0f, 1f);
        _whiteoutMaterial.SetShaderParameter(WhiteMixParameter, mix);
        foreach (SlotColorState state in _slotColors)
        {
            if (!GodotObject.IsInstanceValid(state.Slot))
            {
                continue;
            }

            Color white = new(1f, 1f, 1f, state.Original.A);
            Color color = state.Original.Lerp(white, mix);
            color.A = state.Original.A;
            state.Slot.Call("set_color", color);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (GodotObject.IsInstanceValid(_sprite.BoundObject))
            {
                _sprite.SetNormalMaterial(_originalNormalMaterial!);
            }
        }
        catch (Exception exception)
        {
            Scripts.Entry.Logger.Warn(
                $"Boss death whiteout material could not be restored: {exception.Message}");
        }

        foreach (SlotColorState state in _slotColors)
        {
            try
            {
                if (GodotObject.IsInstanceValid(state.Slot))
                {
                    state.Slot.Call("set_color", state.Original);
                }
            }
            catch (Exception exception)
            {
                Scripts.Entry.Logger.Warn(
                    $"Boss death whiteout slot color could not be restored: {exception.Message}");
            }
        }

        DisposeSlots(_slotColors);
        _whiteoutMaterial.Dispose();
    }

    private static List<SlotColorState> CaptureSlotColors(MegaSprite sprite)
    {
        using MegaSkeleton skeleton = sprite.GetSkeleton()
            ?? throw new InvalidOperationException("the Boss Spine skeleton is unavailable");
        if (!skeleton.BoundObject.HasMethod("get_slots"))
        {
            throw new MissingMethodException("SpineSkeleton.get_slots is unavailable");
        }

        Godot.Collections.Array<GodotObject> slots = skeleton.BoundObject
            .Call("get_slots")
            .AsGodotArray<GodotObject>();
        var result = new List<SlotColorState>(slots.Count);
        try
        {
            foreach (GodotObject slot in slots)
            {
                if (!slot.HasMethod("get_color") || !slot.HasMethod("set_color"))
                {
                    slot.Dispose();
                    continue;
                }

                result.Add(new SlotColorState(slot, slot.Call("get_color").AsColor()));
            }

            return result;
        }
        catch
        {
            DisposeSlots(result);
            foreach (GodotObject slot in slots)
            {
                if (GodotObject.IsInstanceValid(slot)
                    && result.All(state => !ReferenceEquals(state.Slot, slot)))
                {
                    slot.Dispose();
                }
            }

            throw;
        }
    }

    private static void DisposeSlots(IEnumerable<SlotColorState> slotColors)
    {
        foreach (SlotColorState state in slotColors)
        {
            if (GodotObject.IsInstanceValid(state.Slot))
            {
                state.Slot.Dispose();
            }
        }
    }

    private sealed record SlotColorState(GodotObject Slot, Color Original);
}
