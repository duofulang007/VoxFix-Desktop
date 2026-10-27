using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class WebViewProbe {
    static readonly string Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=10000000};
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string type,string title);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,IntPtr pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first,uint second,bool attach);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    [StructLayout(LayoutKind.Sequential)] struct GuiInfo {public uint size,flags;public IntPtr active,focus,capture,menu,move,caret;public System.Drawing.Rectangle rect;}
    static IntPtr Focus(IntPtr window){var info=new GuiInfo{size=(uint)Marshal.SizeOf(typeof(GuiInfo))};return GetGUIThreadInfo(GetWindowThreadProcessId(window,IntPtr.Zero),ref info)?info.focus:IntPtr.Zero;}
    static void Set(object value,string name,object field){value.GetType().GetField(name,Flags).SetValue(value,field);}
    static object Get(object value,string name){if(value==null)return null;var field=value.GetType().GetField(name,Flags);if(field!=null)return field.GetValue(value);var property=value.GetType().GetProperty(name,Flags);return property==null?null:property.GetValue(value,null);}
    static string StatePath(string run){return Path.Combine(Root,"work","webview-text-test","state-"+run+".json");}
    static string Caption(string run){return "VoxFix browser typing verification "+run;}

    sealed class Host:Form {
        readonly WebView2 browser=new WebView2{Dock=DockStyle.Fill};
        readonly Timer timer=new Timer{Interval=50};
        readonly string run,mode;readonly bool replaceSelection;bool sampling,ready;
        internal Host(string run,string mode,bool replaceSelection){
            this.run=run;this.mode=mode;this.replaceSelection=replaceSelection;Text=Caption(run);Width=850;Height=380;Controls.Add(browser);
            Shown+=async(s,e)=>{try{await Start();}catch(Exception error){File.WriteAllText(StatePath(run)+".error",error.ToString());Close();}};
            timer.Tick+=async(s,e)=>await Snapshot();
            FormClosed+=(s,e)=>timer.Dispose();
        }
        async Task Start(){
            var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(Root,"work","webview-text-test","profile-"+run));
            await browser.EnsureCoreWebView2Async(environment);
            browser.CoreWebView2.NavigationCompleted+=async(s,e)=>{
                browser.Focus();
                await browser.CoreWebView2.ExecuteScriptAsync(replaceSelection ? "const e=document.getElementById('voice');e.focus();if(e.setSelectionRange)e.setSelectionRange(5,10);else{const r=document.createRange();r.setStart(e.firstChild,5);r.setEnd(e.firstChild,10);const s=getSelection();s.removeAllRanges();s.addRange(r);}" : "const e=document.getElementById('voice');e.focus();if(e.setSelectionRange)e.setSelectionRange(e.value.length,e.value.length);else{const r=document.createRange();r.selectNodeContents(e);r.collapse(false);const s=getSelection();s.removeAllRanges();s.addRange(r);}");
                ready=true;timer.Start();await Snapshot();
            };
            string seed=replaceSelection?"用户原文：需要替换词，正确后文保留。":"用户原文：";
            string input=mode=="editable"?"<div id='voice' contenteditable='true'>"+seed+"</div>":"<textarea id='voice'>"+seed+"</textarea>";
            browser.CoreWebView2.NavigateToString("<!doctype html><meta charset='utf-8'><title>VoxFix isolated browser typing</title><style>body{font:20px sans-serif;margin:24px}#voice{font:24px sans-serif;width:740px;min-height:50px;white-space:pre-wrap;border:1px solid #777;overflow:auto}</style>"+input+@"<script>
window.visualFrames=[];window.voiceState=()=>{const e=document.getElementById('voice');let start=0,end=0;if(e.setSelectionRange){start=e.selectionStart;end=e.selectionEnd;}else{const s=getSelection();if(s.rangeCount){const r=s.getRangeAt(0),a=r.cloneRange(),b=r.cloneRange();a.selectNodeContents(e);a.setEnd(r.startContainer,r.startOffset);b.selectNodeContents(e);b.setEnd(r.endContainer,r.endOffset);start=a.toString().length;end=b.toString().length;}}return {text:e.value===undefined?e.textContent:e.value,start,end,active:document.activeElement===e};};let last='';function frame(){const state=voiceState(),key=JSON.stringify(state);if(state.active&&key!==last){visualFrames.push(state);last=key;}const e=document.getElementById('voice');if(e.setSelectionRange){e.style.height='auto';e.style.height=e.scrollHeight+'px';}requestAnimationFrame(frame);}requestAnimationFrame(frame);
</script>");
        }
        async Task Snapshot(){
            if(!ready || sampling || IsDisposed)return;sampling=true;
            try{
                string value=await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({...voiceState(),frames:visualFrames})");
                string state=Json.Deserialize<string>(value);string path=StatePath(run),pending=path+".tmp";File.WriteAllText(pending,state);
                if(File.Exists(path))File.Replace(pending,path,null);else File.Move(pending,path);
            }catch(Exception error){if(!IsDisposed)File.WriteAllText(StatePath(run)+".error",error.ToString());}
            finally{sampling=false;}
        }
    }

    static async Task<Dictionary<string,object>> ReadState(string run){
        await Task.Delay(10);string error=StatePath(run)+".error";if(File.Exists(error))throw new Exception(File.ReadAllText(error));
        try{using(var stream=new FileStream(StatePath(run),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))return Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());}catch(IOException){return null;}
    }
    static async Task Verify(string mode,string eventsPath,bool replaceSelection){
        string run=Guid.NewGuid().ToString("N");Process child=null;Form shell=null;
        try{
            var assembly=Assembly.LoadFile(Path.Combine(Root,"bin","VoxFix Desktop.exe"));
            shell=(Form)Activator.CreateInstance(assembly.GetType("Microphone"),new object[]{Root,"--stream-test"});var owner=shell.Handle;
            Set(shell,"delivery",Activator.CreateInstance(assembly.GetType("TextDelivery"),true));
            child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--host "+mode+" "+run+(replaceSelection?" selection":"")){UseShellExecute=false,CreateNoWindow=true});
            IntPtr target=IntPtr.Zero;var clock=Stopwatch.StartNew();Dictionary<string,object> state=null;
            while(clock.ElapsedMilliseconds<15000){target=FindWindow(null,Caption(run));state=await ReadState(run);if(target!=IntPtr.Zero && state!=null && Convert.ToBoolean(state["active"]))break;}
            if(state==null || target==IntPtr.Zero)throw new Exception("Browser test host did not initialize");
            uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero),current=GetCurrentThreadId();bool attached=foreground!=current && AttachThreadInput(current,foreground,true);
            try{SetForegroundWindow(target);}finally{if(attached)AttachThreadInput(current,foreground,false);}
            await Task.Delay(100);Set(shell,"target",target);Set(shell,"targetFocus",Focus(target));
            var events=Json.Deserialize<object[]>(File.ReadAllText(eventsPath));
            var apply=shell.GetType().GetMethod("ApplySegment",Flags);int updates=0;var allowed=new Dictionary<string,int>{{"用户原文：",5}};string confirmed="",suffix=replaceSelection?"，正确后文保留。":"";
            foreach(Dictionary<string,object> item in events){
                object[] key=(object[])item["key"];var segment=new Dictionary<string,object>{{"route",key[0]},{"start",key[1]},{"text",item["text"]}};
                bool final=Convert.ToBoolean(item["final"]);apply.Invoke(shell,new object[]{segment,final});
                var bootstrap=Stopwatch.StartNew();
                while(((string)Get(Get(shell,"delivery"),"Latest")).Length==0 && bootstrap.ElapsedMilliseconds<1500){await Task.Delay(10);apply.Invoke(shell,new object[]{segment,final});}
                string paused=(string)Get(Get(shell,"delivery"),"Paused");if(paused!="")throw new Exception("Bridge paused at "+item["at"]+": "+paused);
                string latest=(string)Get(Get(shell,"delivery"),"Latest"),expected="用户原文："+latest+suffix;int tail=5+latest.Length;
                if(final){confirmed+=(string)item["text"];if(expected!="用户原文："+confirmed+suffix)throw new Exception("Final recognition text changed at "+item["at"]);}
                allowed[expected]=tail;
                if(latest.Length==0)throw new Exception("No fast browser draft: "+Get(Get(shell,"textBridge"),"Diagnostic"));
                var wait=Stopwatch.StartNew();bool exact=false;
                while(wait.ElapsedMilliseconds<2000){state=await ReadState(run);if(state!=null && (string)state["text"]==expected && Convert.ToInt32(state["start"])==tail && Convert.ToInt32(state["end"])==tail){exact=true;break;}}
                if(!exact)throw new Exception("Browser DOM/caret mismatch at "+item["at"]+" expected length="+expected.Length+" actual="+Json.Serialize(state));
                ++updates;
            }
            await Task.Delay(100);
            var snapshot=Stopwatch.StartNew();do{state=await ReadState(run);}while(state==null && snapshot.ElapsedMilliseconds<2000);
            if(state==null)throw new Exception("Final browser snapshot unavailable");
            var frames=(System.Collections.IList)state["frames"];
            foreach(Dictionary<string,object> frame in frames){string text=(string)frame["text"];int start=Convert.ToInt32(frame["start"]),end=Convert.ToInt32(frame["end"]),tail;
                if(replaceSelection && text=="用户原文：需要替换词"+suffix && start==5 && end==10)continue;
                if(!allowed.TryGetValue(text,out tail) || start!=tail || end!=tail)throw new Exception("Transient browser frame/caret: "+Json.Serialize(frame));}
            File.WriteAllText(Path.Combine(Root,"work","webview-text-test",mode+"-result.json"),Json.Serialize(new{pass=true,mode,updates,frames=frames.Count,finalState=state}));
            Console.WriteLine("PASS: browser "+mode+" / "+updates+" recognition updates / "+frames.Count+" changed visual frames / exact DOM and tail caret");
        }finally{
            if(shell!=null)shell.Dispose();
            if(child!=null){IntPtr target=FindWindow(null,Caption(run));if(target!=IntPtr.Zero)PostMessage(target,0x10,IntPtr.Zero,IntPtr.Zero);if(!child.WaitForExit(3000))child.Kill();child.Dispose();}
        }
    }
    [STAThread]static void Main(string[] args){
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0 && args[0]=="--host"){Application.Run(new Host(args[2],args[1],args.Length>3 && args[3]=="selection"));return;}
        string mode=args.Length>0?args[0]:"textarea";
        string eventsPath=args.Length>1?Path.GetFullPath(args[1]):Path.Combine(Root,"tools","fixtures","text-bridge-updates.json");
        using(var controller=new Form{Opacity=0,ShowInTaskbar=false}){
            controller.Shown+=async(s,e)=>{controller.Hide();try{await Verify(mode,eventsPath,args.Length>2 && args[2]=="selection");}catch(Exception error){File.WriteAllText(Path.Combine(Root,"work","webview-text-test",mode+"-error.txt"),error.ToString());Console.WriteLine(error);Environment.ExitCode=1;}finally{controller.Close();}};
            Application.Run(controller);
        }
    }
}
