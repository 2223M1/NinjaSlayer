using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Helpers;
using System.Reflection;
using System.Collections;
using System.Text.Json;
using System.Diagnostics;

[ModInitializer(nameof(Init))]
public static class SelectPosterProbe
{
    static string output="";
    static readonly BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
    static Type Type(string n)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(n)).First(t=>t!=null)!;
    public static void Init()
    {
        output=CommandLineHelper.GetValue("poster-output")??"";
        if(output.Length==0)return;
        Directory.CreateDirectory(output);
        File.Delete(Path.Combine(output,"error.txt"));File.Delete(Path.Combine(output,"result.json"));
        DenyTelemetry();
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus,true);DisplayServer.WindowSetPosition(new(-5000,-5000));
        _=Run();
    }
    static void DenyTelemetry()
    {
        var registry=Type("STS2RitsuLib.Telemetry.TelemetryRegistry");
        var framework=Type("STS2RitsuLib.RitsuLibFramework");
        var deny=Enum.Parse(Type("STS2RitsuLib.Telemetry.TelemetryConsentState"),"Denied");
        var setter=framework.GetMethods(Static).Single(m=>m.Name=="SetTelemetryApplicantConsent");
        foreach(var a in (IEnumerable)registry.GetMethod("GetApplicants",Static)!.Invoke(null,null)!)
        {
            var ps=setter.GetParameters();var args=new object?[ps.Length];args[0]=a.GetType().GetProperty("ApplicantId")!.GetValue(a);args[1]=deny;
            for(int i=2;i<args.Length;i++)args[i]=ps[i].DefaultValue;setter.Invoke(null,args);
        }
        Type("NinjaSlayer.Content.NinjaSlayerTelemetryConsent").GetMethod("SetEnabled",Static)!.Invoke(null,new object[]{false});
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task Run()
    {
        try
        {
            var tree=(SceneTree)Engine.GetMainLoop();
            async Task Frame()=>await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
            async Task Frames(int n){for(int i=0;i<n;i++)await Frame();}
            async Task<Image> Capture(string? name=null)
            {
                await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var im=tree.Root.GetTexture().GetImage();im.Convert(Image.Format.Rgba8);
                if(name!=null)im.SavePng(Path.Combine(output,name+".png"));return im;
            }
            for(int i=0;i<12000&&NGame.Instance?.MainMenu==null;i++)await Frame();
            var game=NGame.Instance??throw new Exception("Game unavailable");await Frames(120);DenyTelemetry();await Frames(360);
            SaveManager.Instance.SetFtuesEnabled(false);
            var settings=Type("NinjaSlayer.Content.NinjaSlayerSettings");
            var binding=settings.GetField("_mangaSelectPortrait",Static)!.GetValue(null)!;
            bool Read()=>(bool)binding.GetType().GetMethod("Read")!.Invoke(binding,null)!;
            void Write(bool value){binding.GetType().GetMethod("Write")!.Invoke(binding,new object[]{value});binding.GetType().GetMethod("Save")!.Invoke(binding,null);}
            Require(!Read(),"Manga must be off by default");
            var screen=game.MainMenu!.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();screen.InitializeSingleplayer();game.MainMenu.SubmenuStack.Push(screen);await Frames(100);
            var buttons=screen.GetNode<Control>("CharSelectButtons/ButtonContainer");
            var model=ModelDb.AllCharacters.Single(c=>c.Id.Entry.Contains("NINJA",StringComparison.OrdinalIgnoreCase));
            var button=buttons.GetChildren().OfType<NCharacterSelectButton>().Single(b=>b.Character==model);
            button.Select();await Frames(100);
            Type("STS2RitsuLib.Ui.Toast.RitsuToastService").GetMethod("CloseAll",Static)!.Invoke(null,new object[]{true});await Frames(10);
            var parent=screen.GetNode<Control>("AnimatedBg");
            var bg=parent.GetChild<Control>(0);var backgroundId=bg.GetNode("Background").GetInstanceId();
            void Check(string expected)
            {
                var names=bg.GetChildren().Select(n=>n.Name.ToString()).ToArray();
                Require(names.SequenceEqual(new[]{"Background",expected,"ash2","ash3"}),"Wrong portrait/foreground particle order: "+string.Join(',',names));
                Require(bg.GetNode("Background").GetInstanceId()==backgroundId,"Background was restarted by toggle");
                foreach(var name in new[]{"ash2","ash3"})Require(bg.GetNode<CpuParticles2D>(name).Emitting,"Native particles stopped");
            }
            Check("OfficialFrontPortrait");
            var ch=bg.GetNode<Node2D>("OfficialFrontPortrait/Character");
            Require(ch.Position.IsEqualApprox(new(1052.18409f,36.76727f))&&ch.Scale.IsEqualApprox(new(.956175f,.956175f)),"Wrong bottom-anchored 5% reduction");
            var fixedSourcePoint=(bg.GetGlobalTransform().AffineInverse()*new Vector2(960,1080)-new Vector2(1013.59091f,-15.61818f))/1.0065f;
            Require(ch.ToGlobal(fixedSourcePoint).DistanceTo(new(1002,1080))<.001f,"Bottom-center screen anchor drifted");
            var characterTransform=ch.GetGlobalTransform();
            File.WriteAllText(Path.Combine(output,"composition-transform.json"),JsonSerializer.Serialize(new{position=new[]{ch.Position.X,ch.Position.Y},scale=new[]{ch.Scale.X,ch.Scale.Y},origin=new[]{characterTransform.Origin.X,characterTransform.Origin.Y},basis_x=new[]{characterTransform.X.X,characterTransform.X.Y},basis_y=new[]{characterTransform.Y.X,characterTransform.Y.Y},post_scale_screen_translation=new[]{-50,0},bottom_anchor_error_px=ch.ToGlobal(fixedSourcePoint).DistanceTo(new(1002,1080))},new JsonSerializerOptions{WriteIndented=true}));
            await Capture("official-default-real-ui");
            // Short real-time viewport recording, including native foreground particles.
            var info=new ProcessStartInfo("ffmpeg"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
            foreach(var arg in new[]{"-y","-v","error","-f","rawvideo","-pix_fmt","rgba","-s","1920x1080","-r","30","-i","pipe:0","-an","-c:v","libx264","-preset","fast","-crf","17","-threads","8","-pix_fmt","yuv420p","-movflags","+faststart",Path.Combine(output,"official-default-real-ui.mp4")})info.ArgumentList.Add(arg);
            using(var encoder=Process.Start(info)!)
            {
                var start=Time.GetTicksMsec();
                for(int i=0;i<240;i++)
                {
                    while(Time.GetTicksMsec()-start<(ulong)(i*1000/30))await Frame();
                    using var image=await Capture();encoder.StandardInput.BaseStream.Write(image.GetData());
                }
                encoder.StandardInput.Close();encoder.WaitForExit();Require(encoder.ExitCode==0,"Video encode failed");
            }
            // Freeze the native Spine player at both scarf rotation extrema.
            ch.Call("set_update_mode",2);
            var animationState=ch.Call("get_animation_state").AsGodotObject();
            foreach(var phase in new[]{(43.0/12,"scarf-swing-positive-real-ui"),(91.0/12,"scarf-swing-negative-real-ui")})
            {
                animationState.Call("get_current",0).AsGodotObject().Call("set_track_time",phase.Item1);
                ch.Call("update_skeleton",0.0);
                await Capture(phase.Item2);
            }
            Write(true);await Frames(30);Require(Read(),"Setting write failed");Check("MangaPortrait");
            var hand=bg.GetNode<AnimationPlayer>("MangaPortrait/AnimationPlayer");Require(hand.IsPlaying(),"Original hand animation not playing");
            await Capture("manga-option-real-ui");
            for(int i=0;i<12;i++){Write(i%2==0);await Frames(2);Check(i%2==0?"MangaPortrait":"OfficialFrontPortrait");}
            Write(true);
            // Destroy/reopen the real selection scene, proving persistence and event unsubscription.
            screen.SelectCharacter(button,model);await Frames(30);bg=parent.GetChild<Control>(0);backgroundId=bg.GetNode("Background").GetInstanceId();Check("MangaPortrait");
            Write(false);await Frames(10);Check("OfficialFrontPortrait");
            game.MainMenu.SubmenuStack.Pop();await Frames(60);
            var nav=Type("STS2RitsuLib.Settings.ModSettingsNavigator");
            var pages=(IEnumerable)Type("STS2RitsuLib.Settings.ModSettingsRegistry").GetMethod("GetPages",Static)!.Invoke(null,null)!;
            var page=pages.Cast<object>().Single(p=>(string)p.GetType().GetProperty("ModId")!.GetValue(p)! == "NinjaSlayer");
            var pageId=page.GetType().GetProperty("Id")!.GetValue(page);
            var task=(Task)nav.GetMethod("OpenByIdsAsync",Static)!.Invoke(null,new object?[]{"NinjaSlayer",pageId,"appearance","manga_select_portrait",null})!;
            await task;var result=task.GetType().GetProperty("Result")!.GetValue(task)!;
            File.WriteAllText(Path.Combine(output,"settings-navigation.json"),JsonSerializer.Serialize(result,result.GetType(),new JsonSerializerOptions{WriteIndented=true}));
            Require((bool)result.GetType().GetProperty("Success")!.GetValue(result)!,"Settings navigation failed: "+File.ReadAllText(Path.Combine(output,"settings-navigation.json")));
            await Frames(60);await Capture("manga-setting-default-off");
            File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{status="pass",default_manga=false,live_toggle_cycles=12,reopened_scene_persisted=true,background_preserved=true,particle_order="Background,Portrait,ash2,ash3",position=new[]{1052.18409,36.76727},scale=.956175,composition_factor=1.045,screen_left_shift=50,final_bottom_scale=.95,final_screen_right_shift=42,settings_navigation=true,installed_game_modified=false},new JsonSerializerOptions{WriteIndented=true}));
            game.Quit();
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());((SceneTree)Engine.GetMainLoop()).Quit(2);}
    }
}
