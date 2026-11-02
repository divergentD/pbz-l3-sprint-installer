using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PBZL3Installer {
    static class SelfTests {
        static readonly List<string> Passed=new List<string>();
        static void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);Passed.Add(label);}
        static void Refused(Action action,string label){bool refused=false;try{action();}catch(IOException){refused=true;}Check(refused,label);}
        static string Game(string suite,string name) {
            string root=Path.Combine(suite,name);Directory.CreateDirectory(Path.Combine(root,@"platform\steam"));Directory.CreateDirectory(Path.Combine(root,@"ProjectPBZ\Saved"));
            File.WriteAllText(Path.Combine(root,@"platform\steam\PBZ.exe"),"test fixture; not executable");
            File.WriteAllBytes(Path.Combine(root,@"ProjectPBZ\Saved\EnhancedInputUserSettings.sav"),new byte[]{0,255,128,32,13,10,42});return root;
        }
        static string Sav(string root){return Path.Combine(root,@"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");}
        internal static int Run(string[] args) {
            string suite=Path.Combine(Path.GetTempPath(),"PBZL3Sprint-selftest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(suite);
            try {
                if(args.Length!=4)throw new Exception("Usage: --self-test report.json backend-folder loader.zip");
                var backend=new Dictionary<string,byte[]>();foreach(var pair in VendorInfo.Backend)backend.Add(pair.Key,File.ReadAllBytes(Path.Combine(args[2],pair.Key)));
                byte[] loader=File.ReadAllBytes(args[3]);Action<string> log=s=>{};
                string root=Game(suite,"install-update-uninstall"),originalSav=Core.Hash(File.ReadAllBytes(Sav(root)));
                string firstBackup=Core.Install(root,backend,loader,true,log);
                Check(File.Exists(Path.Combine(Core.Mod(root),@"Scripts\main.lua")),"fresh install creates own entry point");
                Check(Core.LoaderExists(root),"loader files match publisher hashes");
                Check(Core.Hash(File.ReadAllBytes(Sav(root)))==originalSav,"fresh install leaves binary SAV identical");
                Check(Core.Hash(File.ReadAllBytes(Path.Combine(firstBackup,"EnhancedInputUserSettings.sav")))==originalSav,"input backup matches original bytes");
                Check(!Directory.Exists(Path.Combine(root,@"platform\steam\ue4ss\Mods\CheatManagerEnablerMod")),"example and cheat mods are not installed");
                string modsFile=Path.Combine(root,@"platform\steam\ue4ss\Mods\mods.txt");
                File.AppendAllText(modsFile,"OtherMod : 1\r\n# Keep my comment\r\n");
                byte[] modsBefore=File.ReadAllBytes(modsFile),mainBefore=File.ReadAllBytes(Path.Combine(Core.Mod(root),@"Scripts\main.lua"));
                Core.TestFailurePoint="after-backup";
                Refused(()=>Core.Install(root,backend,null,true,log),"failed update is rejected");Core.TestFailurePoint=null;
                Check(Core.Hash(mainBefore)==Core.Hash(File.ReadAllBytes(Path.Combine(Core.Mod(root),@"Scripts\main.lua"))),"rollback restores previous mod");
                Check(Core.Hash(modsBefore)==Core.Hash(File.ReadAllBytes(modsFile)),"rollback preserves mods.txt byte-for-byte");
                string updated=Core.Install(root,backend,null,true,log);
                Check(Directory.Exists(Path.Combine(updated,"previous-PBZL3Sprint")),"update backs up previous owned mod");
                Check(File.ReadAllText(modsFile).Split(new[]{"PBZL3Sprint : 1"},StringSplitOptions.None).Length==2,"update keeps exactly one enable entry");
                Check(File.ReadAllText(modsFile).Contains("OtherMod : 1")&&File.ReadAllText(modsFile).Contains("# Keep my comment"),"unrelated mod entries and comments survive update");
                string removed=Core.Uninstall(root,true,log);
                Check(!Directory.Exists(Core.Mod(root))&&!Directory.Exists(Core.Library(root)),"uninstall moves owned mod and dependency out of active locations");
                Check(Directory.Exists(Path.Combine(removed,"uninstalled-PBZL3Sprint")),"uninstall is reversible");
                Check(Core.LoaderExists(root)&&File.ReadAllText(modsFile).Contains("OtherMod : 1"),"uninstall retains shared loader and other mods");
                Check(Core.Hash(File.ReadAllBytes(Sav(root)))==originalSav,"update and uninstall leave original SAV identical");
                string broken=Game(suite,"new-install-rollback");Core.TestFailurePoint="after-backup";
                Refused(()=>Core.Install(broken,backend,loader,true,log),"fresh-install failure triggers rollback");Core.TestFailurePoint=null;
                Check(!Directory.Exists(Path.Combine(broken,@"platform\steam\ue4ss"))&&!File.Exists(Path.Combine(broken,@"platform\steam\dwmapi.dll")),"failed fresh install leaves no active loader or partial mod");
                string foreign=Game(suite,"foreign-mod");Directory.CreateDirectory(Path.Combine(Core.Mod(foreign),"Scripts"));File.WriteAllText(Path.Combine(Core.Mod(foreign),@"Scripts\main.lua"),"other author's code");
                Refused(()=>Core.Install(foreign,backend,loader,true,log),"foreign same-name mod is never overwritten");
                Check(File.ReadAllText(Path.Combine(Core.Mod(foreign),@"Scripts\main.lua"))=="other author's code","foreign mod remains unchanged");
                string invalid=Game(suite,"invalid-dependency");var tampered=new Dictionary<string,byte[]>(backend);tampered["runtime.lua"]=new byte[]{1,2,3};
                Refused(()=>Core.Install(invalid,tampered,loader,true,log),"modified dependency is rejected before installation");
                Refused(()=>Core.Install(invalid,backend,new byte[]{0},true,log),"corrupt loader archive is rejected");
                Check(!Directory.Exists(Path.Combine(invalid,@"platform\steam\ue4ss")),"invalid inputs create no game files");
                Refused(()=>Core.FullRoot(invalid,true,false),"unsupported game executable is rejected");
                Refused(()=>Core.Guard(invalid,Path.Combine(suite,"outside")),"paths outside selected game root are rejected");
                string importZip=Path.Combine(suite,"dependency-with-extra.zip");
                using(var zip=ZipFile.Open(importZip,ZipArchiveMode.Create)) {
                    foreach(var pair in backend){var entry=zip.CreateEntry("PBZControllerCustomizer/Scripts/"+pair.Key);using(var stream=entry.Open())stream.Write(pair.Value,0,pair.Value.Length);}
                    using(var writer=new StreamWriter(zip.CreateEntry("../../should-not-extract.txt").Open()))writer.Write("must not be extracted");
                }
                var imported=Core.ReadBackendZip(importZip);
                Check(imported.Count==5&&!File.Exists(Path.Combine(suite,"should-not-extract.txt")),"ZIP importer reads only five named dependency files");
                var resources=typeof(Program).Assembly.GetManifestResourceNames();
                Check(resources.Length==2&&resources.All(n=>n=="payload.main.lua"||n=="payload.settings.lua"),"executable embeds only our two Lua files; no third-party framework");
                string json=new JavaScriptSerializer().Serialize(new{passed=true,checks=Passed.Count,results=Passed,fixture_root=suite,game_installation_changed=false,network_used=false});
                File.WriteAllText(args[1],json,new UTF8Encoding(false));return 0;
            } catch(Exception e) {
                Core.TestFailurePoint=null;
                if(args.Length>1)File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{passed=false,error=e.ToString(),completed=Passed,fixture_root=suite}),new UTF8Encoding(false));
                return 1;
            }
        }
    }
}
