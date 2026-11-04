using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PBZL3Installer {
    static class Core {
        internal const string Owner = "PBZL3Sprint.Distribution.1";
        internal const string LoaderUrl = "https://github.com/KintaroModding/ue4ss/releases/download/v3.0.1-1195-g1ceefac8/phantombladezero_ue4ss_v3.0.1-1195-g1ceefac8.zip";
        internal const string LoaderHash = "0B6B7F9430502C1FBEADB09740E586063DCE42FE4F7D7A3EE7BE51C9BD6CA304";
        internal const string BackendUrl = "https://www.nexusmods.com/phantombladezero/mods/112?tab=files&file_id=873";
        internal static string TestFailurePoint;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        internal static string Hash(byte[] data) { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""); }
        internal static byte[] Resource(string name) {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if(stream==null) throw new IOException("安装器资源缺失："+name);
                using(var output=new MemoryStream()) {stream.CopyTo(output);return output.ToArray();}
            }
        }
        internal static string Mod(string root) {return Path.Combine(root,@"platform\steam\ue4ss\Mods\PBZL3Sprint");}
        internal static string Library(string root) {return Path.Combine(root,@"platform\steam\ue4ss\PBZL3SprintDependencies");}
        internal static string FullRoot(string path, bool checkBuild, bool test) {
            string root=Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
            if(!Directory.Exists(root) || !File.Exists(Path.Combine(root,@"platform\steam\PBZ.exe"))) throw new IOException("请选择 PBZ 游戏根目录，其中应有 platform\\steam\\PBZ.exe。");
            if(checkBuild && !test) {
                if(root.Any(c=>c>127)) throw new IOException("当前框架要求游戏路径仅含英文字母、数字及常用符号。请使用不含中文的 Steam 游戏库路径。");
                GameCompatibility.Verify(Path.Combine(root,@"platform\steam\PBZ.exe"));
            }
            if(test && !root.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) throw new IOException("测试只能使用系统临时目录。");
            return root;
        }
        internal static void Stopped(bool test) {if(!test && Process.GetProcessesByName("PBZ").Length>0) throw new IOException("请先从游戏菜单正常退出游戏，再安装或卸载。");}
        internal static void Guard(string root,string target) {
            string boundary=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(target);
            if(!full.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)) throw new IOException("路径越界，操作已停止。");
            string current=full;
            while(!String.Equals(current,root,StringComparison.OrdinalIgnoreCase) && current!=null) {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw new IOException("目标含目录链接，已停止操作："+current);
                current=Path.GetDirectoryName(current);
            }
        }
        static byte[] ZipFileBytes(ZipArchive zip,string name,int limit) {
            var entry=zip.GetEntry(name);
            if(entry==null || entry.Length>limit || entry.Length<0) throw new IOException("依赖压缩包内容不符合要求："+name);
            using(var stream=entry.Open()) using(var output=new MemoryStream()) {
                var buffer=new byte[8192]; int read;
                while((read=stream.Read(buffer,0,buffer.Length))>0) {
                    if(output.Length+read>limit) throw new IOException("压缩包文件超出大小限制。");
                    output.Write(buffer,0,read);
                }
                return output.ToArray();
            }
        }
        internal static Dictionary<string,byte[]> ReadBackendZip(string file) {
            var result=new Dictionary<string,byte[]>();
            using(var zip=ZipFile.OpenRead(file)) foreach(var pair in VendorInfo.Backend) {
                byte[] bytes=ZipFileBytes(zip,"PBZControllerCustomizer/Scripts/"+pair.Key,1024*1024);
                if(Hash(bytes)!=pair.Value) throw new IOException("框架校验失败："+pair.Key+"。请下载 PBZControllerCustomizer 2.1 完整包（不是 Update）。");
                result.Add(pair.Key,bytes);
            }
            return result;
        }
        static Dictionary<string,byte[]> ReadBackendFolder(string folder) {
            var result=new Dictionary<string,byte[]>();
            foreach(var pair in VendorInfo.Backend) {
                string path=Path.Combine(folder,pair.Key);
                if(!File.Exists(path) || new FileInfo(path).Length>1024*1024) throw new IOException("未找到兼容框架。");
                byte[] bytes=File.ReadAllBytes(path);
                if(Hash(bytes)!=pair.Value) throw new IOException("框架版本不匹配。");
                result.Add(pair.Key,bytes);
            }
            return result;
        }
        internal static Dictionary<string,byte[]> FindBackend(string root,string zipPath) {
            if(!String.IsNullOrWhiteSpace(zipPath)) return ReadBackendZip(zipPath);
            foreach(var folder in new[]{Path.Combine(Library(root),@"PBZControllerCustomizer\Scripts"),Path.Combine(Mod(root),"Scripts"),Path.Combine(root,@"platform\steam\ue4ss\Mods\PBZControllerCustomizer\Scripts")}) {
                try {return ReadBackendFolder(folder);} catch(IOException){} catch(UnauthorizedAccessException){}
            }
            foreach(var folder in new[]{AppDomain.CurrentDomain.BaseDirectory,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")}) {
                if(!Directory.Exists(folder)) continue;
                foreach(var file in Directory.GetFiles(folder,"PBZControllerCustomizer*.zip").OrderByDescending(File.GetLastWriteTime).Take(30)) {
                    try {return ReadBackendZip(file);} catch(IOException){} catch(InvalidDataException){}
                }
            }
            throw new IOException("请先从作者页面下载 PBZControllerCustomizer 2.1 完整 ZIP，然后通过“选择框架 ZIP”导入。安装器不能代替作者分发框架。");
        }
        static void CheckBackend(Dictionary<string,byte[]> files) {
            foreach(var pair in VendorInfo.Backend) if(!files.ContainsKey(pair.Key)||Hash(files[pair.Key])!=pair.Value) throw new IOException("框架文件校验失败："+pair.Key);
        }
        internal static bool LoaderExists(string root) {
            string platform=Path.Combine(root,@"platform\steam");
            bool any=File.Exists(Path.Combine(platform,"dwmapi.dll"))||Directory.Exists(Path.Combine(platform,"ue4ss"));
            if(!any) return false;
            foreach(var pair in VendorInfo.Loader) {
                if(pair.Key.EndsWith("UE4SS-settings.ini")||pair.Key.EndsWith("LICENSE")) continue;
                string file=Path.Combine(platform,pair.Key.Replace('/', '\\'));
                if(!File.Exists(file)||Hash(File.ReadAllBytes(file))!=pair.Value) throw new IOException("已有 UE4SS 与支持版本不一致，或安装不完整。安装器不会覆盖现有加载器，请先处理已有版本。");
            }
            return true;
        }
        internal static byte[] DownloadLoader(Action<string> log) {
            string cache=Path.Combine(Path.GetTempPath(),"PBZL3Sprint-Installer","loader-1195.zip");
            if(File.Exists(cache) && new FileInfo(cache).Length<20*1024*1024) {var cached=File.ReadAllBytes(cache);if(Hash(cached)==LoaderHash)return cached;}
            log("从发布者 GitHub 下载适配的 UE4SS 加载器……");
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(LoaderUrl);request.Timeout=45000;request.ReadWriteTimeout=30000;request.UserAgent="PBZL3SprintInstaller/1.2";
            byte[] bytes;
            using(var response=(HttpWebResponse)request.GetResponse()) {
                if(response.ResponseUri.Scheme!="https") throw new IOException("下载未使用 HTTPS，已停止。");
                using(var stream=response.GetResponseStream()) using(var memory=new MemoryStream()) {
                    byte[] buffer=new byte[65536];int n;
                    while((n=stream.Read(buffer,0,buffer.Length))>0) {if(memory.Length+n>20*1024*1024)throw new IOException("加载器下载大小异常。");memory.Write(buffer,0,n);}
                    bytes=memory.ToArray();
                }
            }
            if(Hash(bytes)!=LoaderHash) throw new IOException("加载器 SHA-256 校验失败，未安装。");
            Directory.CreateDirectory(Path.GetDirectoryName(cache));File.WriteAllBytes(cache,bytes);return bytes;
        }
        internal static bool IsOwn(string folder) {
            string receipt=Path.Combine(folder,"installer-receipt.json");
            if(File.Exists(receipt)) {
                try {var obj=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(receipt));if(obj.ContainsKey("owner")&&(string)obj["owner"]==Owner)return true;} catch(ArgumentException){}
            }
            string main=Path.Combine(folder,@"Scripts\main.lua");
            if(!File.Exists(main))return false;
            string hash=Hash(File.ReadAllBytes(main));
            return hash=="17E7E0132D0AE00039C90CB764A6F329AED5A52183DBB6BB1239B2E4C9ABA618"||hash==Hash(Resource("payload.main.lua"));
        }
        static string Backup(string root) {string p=Path.Combine(root,"PBZL3SprintBackups",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Guard(root,p);Directory.CreateDirectory(p);return p;}
        static void Move(string root,string source,string destination) {Guard(root,source);Guard(root,destination);Directory.CreateDirectory(Path.GetDirectoryName(destination));if(Directory.Exists(source))Directory.Move(source,destination);else if(File.Exists(source))File.Move(source,destination);}
        internal static string Install(string input,Dictionary<string,byte[]> backend,byte[] loaderZip,bool test,Action<string> log) {
            string root=FullRoot(input,true,test);Stopped(test);CheckBackend(backend);
            string platform=Path.Combine(root,@"platform\steam"),mods=Path.Combine(platform,@"ue4ss\Mods"),target=Mod(root),library=Library(root),modsFile=Path.Combine(mods,"mods.txt");
            foreach(var path in new[]{target,library,modsFile,Path.Combine(platform,"dwmapi.dll")})Guard(root,path);
            foreach(var name in new[]{"PBZControllerCustomizer","PBZCommentPreset","PBZPS5R1TapHold","PBZSmartControls"}) if(Directory.Exists(Path.Combine(mods,name))) throw new IOException("检测到其他改键脚本："+name+"。请先停用并移出 Mods，避免两个脚本同时接管按键。");
            if(Directory.Exists(target)&&!IsOwn(target))throw new IOException("同名 Mod 不属于本安装器，未覆盖。");
            byte[] settings=Resource("payload.settings.lua");
            string previousSettings=Path.Combine(target,@"Scripts\settings.lua");
            Guard(root,previousSettings);
            if(File.Exists(previousSettings)) {
                if(new FileInfo(previousSettings).Length>1024*1024)throw new IOException("已有配置超过大小限制，已停止更新。");
                settings=File.ReadAllBytes(previousSettings);
            }
            if(Directory.Exists(library)) {
                if(!File.Exists(Path.Combine(library,"owner.txt"))||File.ReadAllText(Path.Combine(library,"owner.txt"))!=Owner)throw new IOException("依赖目录已存在且不属于本安装器，未覆盖。");
                ReadBackendFolder(Path.Combine(library,@"PBZControllerCustomizer\Scripts"));
            }
            bool existingLoader=LoaderExists(root);
            Dictionary<string,byte[]> loaderFiles=new Dictionary<string,byte[]>();
            if(!existingLoader) {
                if(loaderZip==null||Hash(loaderZip)!=LoaderHash)throw new IOException("需要校验通过的 UE4SS 加载器。");
                using(var memory=new MemoryStream(loaderZip))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read))foreach(var pair in VendorInfo.Loader) {
                    var bytes=ZipFileBytes(zip,pair.Key,25*1024*1024);if(Hash(bytes)!=pair.Value)throw new IOException("加载器文件校验失败："+pair.Key);loaderFiles.Add(pair.Key,bytes);
                }
            }
            Stopped(test);
            string backup=Backup(root),oldMod=Path.Combine(backup,"previous-PBZL3Sprint");
            string sav=Path.Combine(root,@"ProjectPBZ\Saved\EnhancedInputUserSettings.sav");
            string before=File.Exists(sav)?Hash(File.ReadAllBytes(sav)):null;
            if(before!=null){File.Copy(sav,Path.Combine(backup,"EnhancedInputUserSettings.sav"));if(Hash(File.ReadAllBytes(Path.Combine(backup,"EnhancedInputUserSettings.sav")))!=before)throw new IOException("键位备份校验失败。");}
            byte[] originalMods=File.Exists(modsFile)?File.ReadAllBytes(modsFile):null;
            if(originalMods!=null)File.WriteAllBytes(Path.Combine(backup,"mods.txt"),originalMods);
            bool loaderCreated=false,proxyCreated=false,libCreated=false,modCreated=false,oldMoved=false,modsChanged=false;
            try {
                if(!existingLoader) {
                    loaderCreated=true;
                    foreach(var pair in loaderFiles.Where(p=>p.Key!="dwmapi.dll")) {
                        string path=Path.Combine(platform,pair.Key.Replace('/','\\'));Guard(root,path);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,pair.Value);
                    }
                    log("加载器已校验并安装，仅启用本改键 Mod。");
                }
                if(!Directory.Exists(library)) {
                    libCreated=true;
                    string script=Path.Combine(library,@"PBZControllerCustomizer\Scripts");Directory.CreateDirectory(script);
                    foreach(var pair in backend)File.WriteAllBytes(Path.Combine(script,pair.Key),pair.Value);
                    File.WriteAllText(Path.Combine(library,"owner.txt"),Owner,Utf8);
                }
                if(Directory.Exists(target)){Move(root,target,oldMod);oldMoved=true;}
                if(test&&TestFailurePoint=="after-backup")throw new IOException("Injected rollback test");
                modCreated=true;Directory.CreateDirectory(Path.Combine(target,"Scripts"));
                File.WriteAllBytes(Path.Combine(target,@"Scripts\main.lua"),Resource("payload.main.lua"));
                File.WriteAllBytes(Path.Combine(target,@"Scripts\settings.lua"),settings);
                File.WriteAllBytes(Path.Combine(target,@"Scripts\native_guard.lua"),Resource("payload.native_guard.lua"));
                File.WriteAllText(Path.Combine(target,"enabled.txt"),"",Utf8);
                File.WriteAllText(Path.Combine(target,"installer-receipt.json"),new JavaScriptSerializer().Serialize(new{owner=Owner,version="1.2.0",backend="PBZControllerCustomizer 2.1",backup=backup,sav_sha256=before,installed_at=DateTime.Now.ToString("o")}),Utf8);
                Directory.CreateDirectory(mods);
                string oldText=originalMods==null?"":Utf8.GetString(originalMods).TrimStart('\uFEFF');
                string newText=Regex.Replace(oldText,@"(?m)^\s*PBZL3Sprint\s*:\s*[01]\s*\r?\n?","").TrimEnd()+"\r\nPBZL3Sprint : 1\r\n";
                modsChanged=true;File.WriteAllText(modsFile,newText,Utf8);
                if(!existingLoader){proxyCreated=true;File.WriteAllBytes(Path.Combine(platform,"dwmapi.dll"),loaderFiles["dwmapi.dll"]);}
                CheckBackend(ReadBackendFolder(Path.Combine(library,@"PBZControllerCustomizer\Scripts")));
                if(Hash(File.ReadAllBytes(Path.Combine(target,@"Scripts\main.lua")))!=Hash(Resource("payload.main.lua")))throw new IOException("Mod 复制校验失败。");
                if(Hash(File.ReadAllBytes(Path.Combine(target,@"Scripts\settings.lua")))!=Hash(settings))throw new IOException("配置复制校验失败。");
                if(Hash(File.ReadAllBytes(Path.Combine(target,@"Scripts\native_guard.lua")))!=Hash(Resource("payload.native_guard.lua")))throw new IOException("原生版本校验模块复制失败。");
                if(before!=null&&Hash(File.ReadAllBytes(sav))!=before)throw new IOException("安装期间原键位发生变化，已撤销 Mod 安装。");
                log("安装完成，原键位未改写。备份："+backup);return backup;
            } catch(Exception failure) {
                var rollbackErrors=new List<string>();
                Action<Action> undo=a=>{try{a();}catch(Exception e){rollbackErrors.Add(e.Message);}};
                if(modCreated&&Directory.Exists(target))undo(()=>Move(root,target,Path.Combine(backup,"failed-new-mod")));
                if(oldMoved)undo(()=>Move(root,oldMod,target));
                if(modsChanged)undo(()=>{if(originalMods!=null)File.WriteAllBytes(modsFile,originalMods);else if(File.Exists(modsFile))Move(root,modsFile,Path.Combine(backup,"failed-mods.txt"));});
                if(libCreated&&Directory.Exists(library))undo(()=>Move(root,library,Path.Combine(backup,"failed-dependencies")));
                if(proxyCreated&&File.Exists(Path.Combine(platform,"dwmapi.dll")))undo(()=>Move(root,Path.Combine(platform,"dwmapi.dll"),Path.Combine(backup,"failed-dwmapi.dll")));
                if(loaderCreated&&Directory.Exists(Path.Combine(platform,"ue4ss")))undo(()=>Move(root,Path.Combine(platform,"ue4ss"),Path.Combine(backup,"failed-loader")));
                if(rollbackErrors.Count>0)throw new IOException(failure.Message+"\n部分撤销操作失败："+String.Join("; ",rollbackErrors)+"\n备份："+backup,failure);
                throw;
            }
        }
        internal static string Uninstall(string input,bool test,Action<string> log) {
            string root=FullRoot(input,false,test);Stopped(test);string target=Mod(root),library=Library(root);
            Guard(root,target);Guard(root,library);
            if(!Directory.Exists(target)){log("此目录未安装 PBZL3Sprint。");return null;}
            if(!IsOwn(target))throw new IOException("同名 Mod 不属于本安装器，未移动。");
            string modsFile=Path.Combine(root,@"platform\steam\ue4ss\Mods\mods.txt");Guard(root,modsFile);
            string backup=Backup(root);byte[] originalMods=File.Exists(modsFile)?File.ReadAllBytes(modsFile):null;
            if(originalMods!=null)File.WriteAllBytes(Path.Combine(backup,"mods.txt"),originalMods);
            string savedMod=Path.Combine(backup,"uninstalled-PBZL3Sprint"),savedLibrary=Path.Combine(backup,"uninstalled-dependencies");bool modMoved=false,libraryMoved=false;
            try {
                Move(root,target,savedMod);modMoved=true;
                if(Directory.Exists(library)&&File.Exists(Path.Combine(library,"owner.txt"))&&File.ReadAllText(Path.Combine(library,"owner.txt"))==Owner){Move(root,library,savedLibrary);libraryMoved=true;}
                if(originalMods!=null)File.WriteAllText(modsFile,Regex.Replace(Utf8.GetString(originalMods).TrimStart('\uFEFF'),@"(?m)^\s*PBZL3Sprint\s*:\s*[01]\s*\r?\n?",""),Utf8);
                log("已卸载并保留可恢复备份。共享 UE4SS 和其他 Mod 保持原样。");return backup;
            } catch {
                if(libraryMoved)Move(root,savedLibrary,library);
                if(modMoved)Move(root,savedMod,target);
                if(originalMods!=null)File.WriteAllBytes(modsFile,originalMods);
                throw;
            }
        }
        internal static List<string> SteamRoots() {
            var roots=new List<string>();
            foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})foreach(var keyPath in new[]{@"Software\Valve\Steam",@"SOFTWARE\WOW6432Node\Valve\Steam"}) {
                using(var key=hive.OpenSubKey(keyPath)){if(key==null)continue;var path=key.GetValue("SteamPath")??key.GetValue("InstallPath");if(path!=null&&!roots.Contains(path.ToString()))roots.Add(path.ToString());}
            }
            var libraries=new List<string>(roots);
            foreach(var root in roots){string file=Path.Combine(root,@"steamapps\libraryfolders.vdf");if(File.Exists(file))foreach(Match m in Regex.Matches(File.ReadAllText(file),"\"path\"\\s+\"([^\"]+)\"")){string path=m.Groups[1].Value.Replace("\\\\","\\");if(!libraries.Contains(path))libraries.Add(path);}}
            return libraries.Select(p=>Path.Combine(p,@"steamapps\common\PBZ")).Where(p=>File.Exists(Path.Combine(p,@"platform\steam\PBZ.exe"))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
    sealed class InstallerForm:Form {
        bool busy;
        readonly TextBox game=new TextBox(),dependency=new TextBox(),log=new TextBox();
        readonly Button install=new Button(),uninstall=new Button(),pickGame=new Button(),pickDependency=new Button(),download=new Button();
        public InstallerForm() {
            Text="影之刃零 · 手柄键位安装器 1.2";ClientSize=new Size(740,570);MinimumSize=new Size(740,610);Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterScreen;
            var title=new Label{Text="L3 冲刺 · R3 长按下蹲",Font=new Font(Font.FontFamily,19,FontStyle.Bold),AutoSize=true,Location=new Point(24,20)};Controls.Add(title);
            Controls.Add(new Label{Text="○ 闪避  ·  R1 处决  ·  × 交互\n按 L3 切换冲刺；R3 短按锁定，按住 0.5 秒切换下蹲。",AutoSize=true,Location=new Point(26,70)});
            Controls.Add(new Label{Text="游戏根目录（PBZ）",AutoSize=true,Location=new Point(26,127)});
            game.SetBounds(26,150,578,28);pickGame.Text="选择目录";pickGame.SetBounds(614,148,100,31);
            Controls.Add(game);Controls.Add(pickGame);
            Controls.Add(new Label{Text="框架完整 ZIP（已有兼容依赖时可留空）",AutoSize=true,Location=new Point(26,192)});
            dependency.SetBounds(26,216,468,28);pickDependency.Text="选择框架 ZIP";pickDependency.SetBounds(504,214,115,31);download.Text="作者下载页";download.SetBounds(629,214,85,31);
            Controls.AddRange(new Control[]{dependency,pickDependency,download});
            Controls.Add(new Label{Text="支持 Steam build 26266538 / 26367565；更新保留已有 Mod 配置。\n框架需使用已校验的 2.1 原件；缺少 UE4SS 时从发布者 GitHub 下载并校验。",AutoSize=true,Location=new Point(26,260)});
            install.Text="安装 / 更新";install.SetBounds(26,316,196,40);install.BackColor=Color.FromArgb(38,79,61);install.ForeColor=Color.White;install.FlatStyle=FlatStyle.Flat;
            uninstall.Text="卸载本 Mod";uninstall.SetBounds(236,316,150,40);
            Controls.AddRange(new Control[]{install,uninstall});
            log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.SetBounds(26,374,688,168);log.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right|AnchorStyles.Bottom;Controls.Add(log);
            pickGame.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){d.Description="选择影之刃零 PBZ 游戏根目录";if(d.ShowDialog(this)==DialogResult.OK)game.Text=d.SelectedPath;}};
            pickDependency.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="框架完整 ZIP|*.zip",Title="选择 PBZControllerCustomizer 2.1 完整包"}){if(d.ShowDialog(this)==DialogResult.OK)dependency.Text=d.FileName;}};
            download.Click+=(s,e)=>Process.Start(new ProcessStartInfo(Core.BackendUrl){UseShellExecute=true});
            install.Click+=async(s,e)=>await Run(false);uninstall.Click+=async(s,e)=>await Run(true);
            var candidates=Core.SteamRoots();if(candidates.Count==1)game.Text=candidates[0];
            Write("先退出游戏。确认目录后点击“安装 / 更新”。原键位文件会备份，安装过程不改写它。");
            Write("框架 ZIP 可放在本安装器旁或系统 Downloads 目录；也可手动选择。");
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;Write("操作尚未完成，请等日志显示结果后再关闭。");}};
        }
        void Write(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(Write),text);return;}log.AppendText(text+Environment.NewLine);}
        async Task Run(bool remove){
            busy=true;
            string root=game.Text,zip=dependency.Text;
            foreach(var b in new[]{install,uninstall,pickGame,pickDependency,download})b.Enabled=false;
            game.Enabled=dependency.Enabled=false;
            try {await Task.Run(()=>{
                root=Core.FullRoot(root,!remove,false);Core.Stopped(false);
                if(remove){Core.Uninstall(root,false,Write);return;}
                Write("检查框架文件与加载器……");var backend=Core.FindBackend(root,zip);
                byte[] loader=Core.LoaderExists(root)?null:Core.DownloadLoader(Write);
                Core.Install(root,backend,loader,false,Write);
            });Write(remove?"卸载完成。下次启动游戏后恢复原有键位。":"完成。现在可以启动游戏测试手柄；无需替换存档或手动复制文件。");}
            catch(UnauthorizedAccessException){Write("写入权限不足。请关闭安装器，右键选择“以管理员身份运行”后重试。");}
            catch(Exception ex){Write("未完成："+ex.Message);}
            finally{busy=false;if(!IsDisposed){foreach(var b in new[]{install,uninstall,pickGame,pickDependency,download})b.Enabled=true;game.Enabled=dependency.Enabled=true;}}
        }
    }
    static class Program {
        [STAThread]static int Main(string[] args) {
            if(args.Length>0&&args[0]=="--self-test")return SelfTests.Run(args);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==2&&args[0]=="--preview") {
                using(var form=new InstallerForm()) {
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);
                    form.Show();Application.DoEvents();
                    using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}
                    form.Close();
                }
                return 0;
            }
            try{Application.Run(new InstallerForm());return 0;}catch(Exception e){MessageBox.Show(e.Message,"安装器启动失败");return 1;}
        }
    }
}
