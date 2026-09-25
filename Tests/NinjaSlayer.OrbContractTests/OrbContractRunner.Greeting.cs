using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using NinjaSlayer.Orbs;

namespace NinjaSlayer.OrbContractTests;

public partial class OrbContractRunner
{
    private async Task VerifyGreetingBarrier(string role, string directory)
    {
        Type type = typeof(ShurikenOrb).Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.BossGreetingSync", true)!;
        object Create(string key, bool brief, bool present = true) => Activator.CreateInstance(type,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
            [_network!, key, new ulong[] { 1, 2 }, brief, present], null)!;
        void Call(object sync, string method) => AccessTools.Method(type, method).Invoke(sync, null);
        bool Read(object sync, string property) => (bool)AccessTools.Property(type, property).GetValue(sync)!;
        void Send(string room, string signal)
        {
            Type messageType = type.Assembly.GetType("NinjaSlayer.Code.ExternalAnimations.BossGreetingMessage", true)!;
            object message = Activator.CreateInstance(messageType)!;
            AccessTools.Field(messageType, "RoomKey").SetValue(message, room);
            var field = AccessTools.Field(messageType, "Signal");
            field.SetValue(message, Enum.Parse(field.FieldType, signal));
            typeof(MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService).GetMethods()
                .Single(method => method.Name == "SendMessage" && method.GetParameters().Length == 1)
                .MakeGenericMethod(messageType).Invoke(_network, [message]);
        }
        bool host = role == "host";
        for (int index = 0; index < 3; index++)
        {
            string key = "greeting-room-" + index;
            object sync = Create(key, host ? index == 1 : index != 1, !host || index != 2);
            using var lease = (IDisposable)sync;
            await WaitNetwork(() => { Call(sync, "Ready"); return Read(sync, "Started"); }, key + " ready/start");
            Require(Read(sync, "Brief") == (index == 1), "Client preference changed the host's greeting mode.");
            Require(Read(sync, "Present") == (index != 2), "Reloaded host must suppress replay on clients.");
            if (index == 0)
            {
                if (!host)
                {
                    Call(sync, "RequestBrief");
                    Send(key, "Shorten");
                    Send(key, "Release");
                    Require(!Read(sync, "Shortened"), "Client Space must not shorten greetings.");
                    File.WriteAllText(Path.Combine(directory, "client-space-tested"), "ready");
                }
                else
                {
                    await WaitNetwork(() => File.Exists(Path.Combine(directory, "client-space-tested")), "client Space");
                    Require(!Read(sync, "Shortened"), "Client shortcut was broadcast.");
                    Call(sync, "RequestBrief");
                    Call(sync, "RequestBrief");
                }
                await WaitNetwork(() => Read(sync, "Shortened"), "host shortcut broadcast");
                Call(sync, "Ready");
                Require(Read(sync, "Shortened"), "Duplicate Ready reset the current presentation.");
            }
            if (index == 1 && host) Send("greeting-room-0", "Release");
            if (!host) await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            if (!host) Require(!Read(sync, "Released"), "Stale room release bypassed the active barrier.");
            Call(sync, "Complete");
            if (host) Require(!Read(sync, "Released"), "Host released opening hooks before client completion.");
            await WaitNetwork(() => Read(sync, "Released"), key + " done/release");
            if (index == 2 && !host)
            {
                lease.Dispose();
                object resumed = Create(key, true);
                using var resumedLease = (IDisposable)resumed;
                await WaitNetwork(() => { Call(resumed, "Ready"); return Read(resumed, "Released"); }, "rejoin released room");
                File.WriteAllText(Path.Combine(directory, "rejoin-tested"), "ready");
            }
            if (index == 2 && host)
                await WaitNetwork(() => File.Exists(Path.Combine(directory, "rejoin-tested")), "client rejoin");
            GD.Print($"PASS {key}: host mode, ready/start/done/release, duplicate messages and reload");
        }
        object interrupted = Create("greeting-disconnect", false);
        using var cleanup = (IDisposable)interrupted;
        await WaitNetwork(() => { Call(interrupted, "Ready"); return Read(interrupted, "Started"); }, "disconnect room start");
        if (host)
        {
            Call(interrupted, "Complete");
            await WaitNetwork(() => Read(interrupted, "Released"), "disconnected peer removed from barrier");
        }
        else
        {
            _network!.Disconnect(NetError.Quit);
            Require(Read(interrupted, "Cancelled"), "Disconnected client retained a live greeting wait.");
        }
        GD.Print("PASS greeting disconnect cleanup");
    }
}
