#define UNICODE
#define _UNICODE
#include <windows.h>
#include <richedit.h>
#include <stdio.h>
#include <string>
#include <vector>
#include <stdint.h>
#include <random>
#include <assert.h>
#include "../app/native/TextChanges.h"
struct Header { uint32_t version, final, chars, previousChars; uint64_t target, focus, segment; };
static HWND editor;
static const WCHAR BridgeClass[]=L"VoxFix.TextBridge." VOXFIX_BRIDGE_VERSION;
static const WCHAR initial[]=L"用户原文：";
static const WCHAR preservedSuffix[]=L"，正确后文保留。";
static std::wstring prefix=initial;
static std::wstring suffix;
static LRESULT CALLBACK HostProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam){
    if(message==WM_DESTROY){PostQuitMessage(0);return 0;}
    if(message==WM_APP+1){SetWindowTextW(editor,(std::wstring(initial)+L"外部编辑").c_str());int end=GetWindowTextLengthW(editor);SendMessage(editor,EM_SETSEL,end,end);return 1;}
    if(message==WM_APP+2){SetWindowTextW(editor,(std::wstring(initial)+L"选中旧词"+preservedSuffix).c_str());SendMessage(editor,EM_SETSEL,wcslen(initial),wcslen(initial)+4);return 1;}
    if(message==WM_APP+3){SetWindowTextW(editor,(std::wstring(initial)+L"外部修改"+preservedSuffix).c_str());SendMessage(editor,EM_SETSEL,wcslen(initial),wcslen(initial)+1);return 1;}
    if(message==WM_APP+4){SetWindowTextW(editor,L"整段待替换的内容");SendMessage(editor,EM_SETSEL,0,-1);return 1;}
    return DefWindowProcW(window,message,wparam,lparam);
}
static int Host(){
    HMODULE rich=LoadLibraryW(L"Msftedit.dll");WNDCLASSW type={};type.hInstance=GetModuleHandle(nullptr);type.lpfnWndProc=HostProc;type.lpszClassName=L"VoxFixHookTestHost";RegisterClassW(&type);
    HWND window=CreateWindowExW(0,type.lpszClassName,L"VoxFix isolated in-place typing test",WS_OVERLAPPEDWINDOW|WS_VISIBLE,100,100,800,260,nullptr,nullptr,type.hInstance,nullptr);
    WCHAR option[8];const WCHAR* inputClass=GetEnvironmentVariableW(L"VOXFIX_TEST_STANDARD_EDIT",option,ARRAYSIZE(option))?L"EDIT":MSFTEDIT_CLASS;
    editor=CreateWindowExW(WS_EX_CLIENTEDGE,inputClass,initial,WS_VISIBLE|WS_CHILD|ES_MULTILINE|ES_AUTOVSCROLL,0,0,760,210,window,nullptr,type.hInstance,nullptr);
    if(_wcsicmp(inputClass,MSFTEDIT_CLASS)==0)SendMessage(editor,EM_SETEDITSTYLE,SES_USECTF,SES_USECTF);
    int end=GetWindowTextLengthW(editor);SendMessage(editor,EM_SETSEL,end,end);SetForegroundWindow(window);SetFocus(editor);
    SetTimer(window,1,25000,nullptr);
    MSG message;while(GetMessage(&message,nullptr,0,0)>0){if(message.message==WM_TIMER && message.hwnd==window && message.wParam==1)DestroyWindow(window);else{TranslateMessage(&message);DispatchMessage(&message);}}
    FreeLibrary(rich);return 0;
}
static void Pump(DWORD duration){DWORD end=GetTickCount()+duration;while(GetTickCount()<end){MSG message;while(PeekMessage(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessage(&message);}Sleep(10);}}
static std::wstring Text(HWND window){WCHAR text[2048]={};int count=static_cast<int>(SendMessageW(window,WM_GETTEXT,ARRAYSIZE(text),reinterpret_cast<LPARAM>(text)));return std::wstring(text,count);}
static int Update(HWND bridge,HWND owner,HWND host,HWND input,const std::wstring& previous,const std::wstring& text,bool final=false,uint64_t segment=1){
    Header header={1,final?1u:0u,static_cast<uint32_t>(text.size()),static_cast<uint32_t>(previous.size()),reinterpret_cast<uint64_t>(host),reinterpret_cast<uint64_t>(input),segment};
    std::vector<BYTE> bytes(sizeof header+2*(text.size()+previous.size()));memcpy(bytes.data(),&header,sizeof header);memcpy(bytes.data()+sizeof header,text.data(),2*text.size());memcpy(bytes.data()+sizeof header+2*text.size(),previous.data(),2*previous.size());
    COPYDATASTRUCT packet={0x56465831,static_cast<DWORD>(bytes.size()),bytes.data()};DWORD_PTR status=0;
    if(!SendMessageTimeoutW(bridge,WM_COPYDATA,reinterpret_cast<WPARAM>(owner),reinterpret_cast<LPARAM>(&packet),SMTO_ABORTIFHUNG|SMTO_BLOCK,1000,&status))return -1;
    LRESULT caret=SendMessage(input,EM_GETSEL,0,0);std::wstring actual=Text(input);
    std::wstring wanted=prefix+text+suffix;size_t tail=prefix.size()+text.size();
    printf("status=%lld expected=%zu actual=%zu caret=%u,%u exact=%d\n",static_cast<long long>(status),wanted.size(),actual.size(),LOWORD(caret),HIWORD(caret),actual==wanted);
    if(status==1 && (actual!=wanted || LOWORD(caret)!=tail || HIWORD(caret)!=tail))return -2;
    return static_cast<int>(status);
}
int wmain(int argc,WCHAR**){
    if(argc>1)return Host();
    std::mt19937 random(20261027);const std::wstring alphabet[]={L"a",L"b",L"你",L"。",L"🙂",L"😃"};
    for(int example=0;example<1000;++example){
        std::wstring oldText,newText;for(unsigned i=0,n=random()%25;i<n;++i)oldText+=alphabet[random()%ARRAYSIZE(alphabet)];for(unsigned i=0,n=random()%25;i<n;++i)newText+=alphabet[random()%ARRAYSIZE(alphabet)];
        std::vector<TextChange> changes;assert(TextChanges(oldText,newText,changes));std::wstring patched=oldText;size_t boundary=oldText.size();
        for(const auto& change:changes){assert(change.start<=change.end && change.end<=boundary);patched.replace(change.start,change.end-change.start,change.text);boundary=change.start;}
        assert(patched==newText);
    }
    std::vector<TextChange> separated;assert(TextChanges(L"错甲正确后文错乙。",L"对甲正确后文对乙！",separated) && separated.size()==3);
    std::vector<TextChange> limited;assert(!TextChanges(std::wstring(1500,L'甲'),std::wstring(1500,L'乙'),limited));
    puts("diff-1000-cases=PASS");
    WCHAR executable[32768];GetModuleFileNameW(nullptr,executable,ARRAYSIZE(executable));std::wstring command=L"\""+std::wstring(executable)+L"\" --host";
    STARTUPINFOW startup={sizeof startup};PROCESS_INFORMATION child={};if(!CreateProcessW(nullptr,&command[0],nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&child))return 10;
    struct Target {DWORD pid;HWND window;};Target found={child.dwProcessId,nullptr};
    for(int i=0;i<100 && !found.window;i++){
        EnumWindows([](HWND window,LPARAM parameter)->BOOL{Target* target=reinterpret_cast<Target*>(parameter);DWORD pid=0;GetWindowThreadProcessId(window,&pid);WCHAR type[128];if(pid==target->pid && GetClassNameW(window,type,ARRAYSIZE(type)) && wcscmp(type,L"VoxFixHookTestHost")==0){target->window=window;return FALSE;}return TRUE;},reinterpret_cast<LPARAM>(&found));
        if(!found.window)Pump(30);
    }
    HWND host=found.window;
    if(!host){CloseHandle(child.hProcess);CloseHandle(child.hThread);puts("FAIL: test host missing");return 11;}
    HWND input=nullptr;DWORD thread=GetWindowThreadProcessId(host,nullptr);
    for(int i=0;i<100;i++){input=FindWindowExW(host,nullptr,MSFTEDIT_CLASS,nullptr);if(!input)input=FindWindowExW(host,nullptr,L"EDIT",nullptr);GUITHREADINFO ready={sizeof ready};GetGUIThreadInfo(thread,&ready);if(input && ready.hwndFocus==input)break;Pump(30);}
    DWORD foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),nullptr), currentThread=GetCurrentThreadId();
    bool attached=foregroundThread && foregroundThread!=currentThread && AttachThreadInput(currentThread,foregroundThread,TRUE);
    SetForegroundWindow(host);
    if(attached)AttachThreadInput(currentThread,foregroundThread,FALSE);
    Pump(500);HKL before=GetKeyboardLayout(thread);
    if(!input || !thread){PostMessage(host,WM_CLOSE,0,0);WaitForSingleObject(child.hProcess,3000);CloseHandle(child.hProcess);CloseHandle(child.hThread);puts("FAIL: test input missing");return 12;}
    HWND owner=CreateWindowExW(0,L"STATIC",L"VoxFix test source",0,0,0,0,0,HWND_MESSAGE,nullptr,GetModuleHandle(nullptr),nullptr);
    std::wstring library=executable;library.resize(library.find_last_of(L'\\')+1);library+=L"VoxFix.TextBridge." VOXFIX_BRIDGE_VERSION L".dll";
    HMODULE module=LoadLibraryW(library.c_str());HOOKPROC callback=reinterpret_cast<HOOKPROC>(module?GetProcAddress(module,"BridgeHook"):nullptr);HHOOK hook=callback?SetWindowsHookExW(WH_GETMESSAGE,callback,module,thread):nullptr;
    printf("hook=%p error=%lu\n",hook,GetLastError());
    PostMessageW(host,RegisterWindowMessageW(L"VoxFix.TextBridge." VOXFIX_BRIDGE_VERSION L".Bootstrap"),reinterpret_cast<WPARAM>(owner),0x12345678);
    WCHAR title[96];swprintf_s(title,L"%lu:%p:%08X",thread,owner,0x12345678);
    HWND bridge=nullptr;for(int i=0;i<100 && !bridge;i++){Pump(30);bridge=FindWindowExW(HWND_MESSAGE,nullptr,BridgeClass,title);}
    printf("bridge=%p\n",bridge);bool pass=bridge!=nullptr;
    GUITHREADINFO info={sizeof info};GetGUIThreadInfo(thread,&info);printf("foreground=%p host=%p focus=%p input=%p\n",GetForegroundWindow(),host,info.hwndFocus,input);
    if(pass){
        Header invalid={99,0,0,0,0,0,1};COPYDATASTRUCT packet={0x56465831,sizeof invalid,&invalid};DWORD_PTR status=0;std::wstring unchanged=Text(input);
        bool rejected=SendMessageTimeoutW(bridge,WM_COPYDATA,reinterpret_cast<WPARAM>(owner),reinterpret_cast<LPARAM>(&packet),SMTO_ABORTIFHUNG|SMTO_BLOCK,1000,&status)!=0 && status==2 && Text(input)==unchanged;
        packet.cbData=sizeof invalid-1;status=0;
        rejected=rejected && SendMessageTimeoutW(bridge,WM_COPYDATA,reinterpret_cast<WPARAM>(owner),reinterpret_cast<LPARAM>(&packet),SMTO_ABORTIFHUNG|SMTO_BLOCK,1000,&status)!=0 && status==2 && Text(input)==unchanged;
        printf("malformed-packets-rejected=%d\n",rejected);pass=pass && rejected;
    }
    std::wstring previous;
    for(const std::wstring text:{L"我想买十五元的茶，后面这段正确文字要保留。",L"我想买15元的茶，后面这段正确文字要保留。",L"我想买15元的茶，后面这段正确文字要保留。继续说下一句。",L"我不想买15元的茶，后面这段正确文字要保留。继续说下一句。",L"我不想买1.5元的茶，后面这段正确文字要保留。继续说下一句。🙂",L"我不想买1.5元的茶，后面这段正确文字要保留。继续说下一句。😃"}){
        if(!pass)break;int status=Update(bridge,owner,host,input,previous,text);printf("returned=%d\n",status);pass=status==1;previous=text;Pump(50);
    }
    if(pass){
        pass=Update(bridge,owner,host,input,previous,previous,true)==1;std::wstring unchanged=Text(input);
        pass=pass && Update(bridge,owner,host,input,L"",L"不应重复送字",true)==4 && Text(input)==unchanged;printf("final-and-dedup=%d\n",pass);
    }
    if(pass){
        SendMessage(host,WM_APP+2,0,0);suffix=preservedSuffix;
        pass=Update(bridge,owner,host,input,L"",L"替换草稿",false,2)==1;
        pass=pass && Update(bridge,owner,host,input,L"替换草稿",L"替换后的新文字",false,2)==1;
        pass=pass && Update(bridge,owner,host,input,L"替换后的新文字",L"语音纠正",true,2)==1;
        printf("selection-replaced-and-suffix-preserved=%d\n",pass);
    }
    if(pass){
        SendMessage(host,WM_APP+4,0,0);prefix.clear();suffix.clear();
        pass=Update(bridge,owner,host,input,L"",L"整段语音替换",true,3)==1;
        printf("select-all-final-only-replaced=%d\n",pass);
        prefix=initial;suffix=preservedSuffix;
    }
    if(pass){
        std::wstring unchanged=Text(input);SendMessage(input,EM_SETREADONLY,TRUE,0);
        int status=Update(bridge,owner,host,input,L"",L"不应写入只读框",false,4);pass=status==4 && Text(input)==unchanged;SendMessage(input,EM_SETREADONLY,FALSE,0);printf("readonly-protected=%d\n",pass);
    }
    if(pass){
        SendMessage(host,WM_APP+2,0,0);pass=Update(bridge,owner,host,input,L"",L"当前草稿",false,4)==1;
        SendMessage(host,WM_APP+3,0,0);Pump(50);std::wstring changed=Text(input);
        pass=pass && Update(bridge,owner,host,input,L"当前草稿",L"不能覆盖新选区",false,4)==4 && Text(input)==changed;
        printf("external-edit-and-selection-protected=%d\n",pass);
    }
    if(bridge){COPYDATASTRUCT close={0x56465832,0,nullptr};DWORD_PTR status=0;bool closed=SendMessageTimeoutW(bridge,WM_COPYDATA,reinterpret_cast<WPARAM>(owner),reinterpret_cast<LPARAM>(&close),SMTO_ABORTIFHUNG|SMTO_BLOCK,1000,&status)!=0 && status==1 && !IsWindow(bridge);printf("bridge-closed=%d\n",closed);pass=pass && closed;}
    printf("keyboard-unchanged=%d before=%p after=%p\n",GetKeyboardLayout(thread)==before,before,GetKeyboardLayout(thread));pass=pass && GetKeyboardLayout(thread)==before;
    if(hook)UnhookWindowsHookEx(hook);DestroyWindow(owner);PostMessage(host,WM_CLOSE,0,0);WaitForSingleObject(child.hProcess,3000);CloseHandle(child.hProcess);CloseHandle(child.hThread);if(module)FreeLibrary(module);
    puts(pass?"PASS":"FAIL");return pass?0:2;
}
