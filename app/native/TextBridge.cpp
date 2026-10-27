#define UNICODE
#define _UNICODE
#include <windows.h>
#include <msctf.h>
#include <richedit.h>
#include <wrl/client.h>
#include <string>
#include <algorithm>
#include <cwctype>
#include <stdint.h>
#include "TextChanges.h"
using Microsoft::WRL::ComPtr;
static HINSTANCE module;
static const WCHAR ClassName[] = L"VoxFix.TextBridge." VOXFIX_BRIDGE_VERSION;
struct Header { uint32_t version, final, chars, previousChars; uint64_t target, focus, segment; };
static_assert(sizeof(Header) == 40, "wire size");
class Bridge;
static thread_local Bridge* bridge = nullptr;

class CompositionSink final:public ITfCompositionSink {
    LONG refs_=1;
public:
    bool terminated=false;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(id==IID_IUnknown || id==IID_ITfCompositionSink)*result=static_cast<ITfCompositionSink*>(this);
        if(!*result)return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&refs_);}
    ULONG STDMETHODCALLTYPE Release() override{ULONG count=InterlockedDecrement(&refs_);if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE OnCompositionTerminated(TfEditCookie,ITfComposition*) override{terminated=true;return S_OK;}
};

class Edit final : public ITfEditSession {
    LONG refs_ = 1;
    Bridge* owner_;
public:
    explicit Edit(Bridge* owner):owner_(owner) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        if (!result) return E_POINTER; *result=nullptr;
        if(id==IID_IUnknown || id==IID_ITfEditSession) *result=static_cast<ITfEditSession*>(this);
        if(!*result)return E_NOINTERFACE; AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return InterlockedIncrement(&refs_);}
    ULONG STDMETHODCALLTYPE Release() override {ULONG n=InterlockedDecrement(&refs_);if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE DoEditSession(TfEditCookie cookie) override;
};

class Bridge {
public:
    HWND owner = nullptr, window = nullptr;
    ComPtr<ITfThreadMgr> manager;
    ComPtr<ITfContext> context;
    ComPtr<ITfRange> range;
    ComPtr<ITfComposition> composition;
    ComPtr<CompositionSink> compositionSink;
    TfClientId client = 0;
    uint64_t segment = 0;
    uint64_t finishedSegment = 0;
    std::wstring current, next, expected;
    bool final = false, mutated = false, initialized = false;
    int result = 2;
    HWND focus = nullptr;
    DWORD ownerPid = 0;
    ULONGLONG touched = GetTickCount64();
    bool legacyOwned = false;
    DWORD legacyStart = 0, legacyEnd = 0;
    HWND ownedFocus = nullptr;
    bool ending=false;

    static void Close(Bridge* self) {
        if(self->composition && self->context && self->client && !self->compositionSink->terminated){
            self->ending=true;Edit* edit=new Edit(self);HRESULT outcome=E_FAIL;
            self->context->RequestEditSession(self->client,edit,TF_ES_SYNC|TF_ES_READWRITE,&outcome);edit->Release();
        }
        HWND window=self->window;self->window=nullptr;
        SetWindowLongPtrW(window,GWLP_USERDATA,0);bridge=nullptr;
        DestroyWindow(window);delete self;
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        try{return WindowMessage(window,message,wparam,lparam);}catch(...){return message==WM_COPYDATA?5:0;}
    }
    static LRESULT WindowMessage(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        Bridge* self = reinterpret_cast<Bridge*>(GetWindowLongPtrW(window,GWLP_USERDATA));
        if(message==WM_NCCREATE) {self=static_cast<Bridge*>(reinterpret_cast<CREATESTRUCT*>(lparam)->lpCreateParams);SetWindowLongPtrW(window,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}
        if(message==WM_COPYDATA && self) {
            const COPYDATASTRUCT* packet=reinterpret_cast<COPYDATASTRUCT*>(lparam);
            if(reinterpret_cast<HWND>(wparam)==self->owner && packet && packet->dwData==0x56465832 && packet->cbData==0) {Close(self);return 1;}
            return self->Receive(reinterpret_cast<HWND>(wparam),packet);
        }
        if(message==WM_TIMER && wparam==1 && self) {
            DWORD pid=0;GetWindowThreadProcessId(self->owner,&pid);
            if(pid!=self->ownerPid || GetTickCount64()-self->touched>15000){Close(self);return 0;}
        }
        return DefWindowProcW(window,message,wparam,lparam);
    }
    HRESULT Start(HWND source, uint32_t token) {
        owner=source;
        GetWindowThreadProcessId(owner,&ownerPid);
        WNDCLASSW type={};type.hInstance=module;type.lpfnWndProc=WindowProc;type.lpszClassName=ClassName;RegisterClassW(&type);
        WCHAR title[96];swprintf_s(title,L"%lu:%p:%08X",GetCurrentThreadId(),source,token);
        window=CreateWindowExW(0,ClassName,title,0,0,0,0,0,HWND_MESSAGE,nullptr,module,this);
        if(window)SetTimer(window,1,1000,nullptr);
        return window?S_OK:HRESULT_FROM_WIN32(GetLastError());
    }
    ~Bridge() {
        composition.Reset();compositionSink.Reset();
        range.Reset();context.Reset();
        if(window)DestroyWindow(window);
        if(client)manager->Deactivate();manager.Reset();
        if(initialized)CoUninitialize();
    }
    int Receive(HWND source,const COPYDATASTRUCT* packet) {
        if(source!=owner || !IsWindow(owner) || !packet || packet->dwData!=0x56465831 || packet->cbData<sizeof(Header) || packet->cbData>65536 || !packet->lpData)return 2;
        Header header;memcpy(&header,packet->lpData,sizeof header);
        if(header.version!=1 || header.final>1 || header.chars>8192 || header.previousChars>8192 || packet->cbData!=sizeof(Header)+2*(header.chars+header.previousChars))return 2;
        if(!header.segment || header.segment<=finishedSegment)return 4;
        touched=GetTickCount64();
        HWND target=reinterpret_cast<HWND>(static_cast<uintptr_t>(header.target));focus=reinterpret_cast<HWND>(static_cast<uintptr_t>(header.focus));
        GUITHREADINFO info={sizeof info};
        if(GetForegroundWindow()!=target || !GetGUIThreadInfo(GetCurrentThreadId(),&info) || info.hwndFocus!=focus || GetWindowThreadProcessId(focus,nullptr)!=GetCurrentThreadId())return 4;
        const WCHAR* text=reinterpret_cast<const WCHAR*>(static_cast<const BYTE*>(packet->lpData)+sizeof header);
        next.assign(text,header.chars);expected.assign(text+header.chars,header.previousChars);
        if(next.find(L'\0')!=std::wstring::npos || expected.find(L'\0')!=std::wstring::npos)return 2;
        final=header.final!=0;mutated=false;result=2;
        if(compositionSink && compositionSink->terminated)return 4;
        if((range || legacyOwned) && (header.segment!=segment || focus!=ownedFocus))return 4;
        WCHAR type[256];if(!GetClassNameW(focus,type,ARRAYSIZE(type)))return 2;
        std::wstring control=type;std::transform(control.begin(),control.end(),control.begin(),[](WCHAR value){return static_cast<WCHAR>(towlower(value));});
        if(control.find(L"richedit")!=std::wstring::npos && (GetWindowLongPtrW(focus,GWL_STYLE)&ES_READONLY))return 4;
        if(control==L"edit" || control.find(L"windowsforms10.edit.")==0) {
            if(!legacyOwned){if(!expected.empty())return 4;segment=header.segment;ownedFocus=focus;}
            return ApplyLegacy();
        }
        // Legacy IMM contexts may acknowledge writes to an invisible proxy document.
        // Add other host families only after verifying their real text store.
        if(control.find(L"richedit")==std::wstring::npos && control.find(L"chrome_")!=0 && control.find(L"mozilla")!=0)return 2;
        // Legacy EDIT uses its own replacement API and needs no TSF client.
        if(!manager) {
            if(!initialized){HRESULT hr=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(hr))return 2;initialized=true;}
            HRESULT hr=CoCreateInstance(CLSID_TF_ThreadMgr,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&manager));
            if(FAILED(hr))return 2;
            hr=manager->Activate(&client);if(FAILED(hr)){manager.Reset();return 2;}
        }
        ComPtr<ITfDocumentMgr> document;ComPtr<ITfContext> focused;
        if(FAILED(manager->GetFocus(&document)) || !document || FAILED(document->GetTop(&focused)) || !focused)return 2;
        ComPtr<ITfContextView> view;HWND viewWindow=nullptr;
        if(FAILED(focused->GetActiveView(&view)) || !view || FAILED(view->GetWnd(&viewWindow)) || !viewWindow ||
            (viewWindow!=focus && !IsChild(viewWindow,focus) && !IsChild(focus,viewWindow)))return 2;
        TF_STATUS status={};
        if(FAILED(focused->GetStatus(&status)))return 2;
        if(status.dwDynamicFlags&TF_SD_READONLY)return 4;
        if(range && (header.segment!=segment || focused.Get()!=context.Get()))return 4;
        if(!range){if(!expected.empty())return 4;context=focused;segment=header.segment;ownedFocus=focus;}
        Edit* edit=new Edit(this);HRESULT outcome=E_FAIL;
        HRESULT hr=context->RequestEditSession(client,edit,TF_ES_SYNC|TF_ES_READWRITE,&outcome);edit->Release();
        if(FAILED(hr) || FAILED(outcome))return result==4?4:(mutated || composition)?5:result;
        // Chromium must flush the updated composition before a separate lock commits it.
        if(final && result==1 && composition){
            ending=true;Edit* finish=new Edit(this);outcome=E_FAIL;
            hr=context->RequestEditSession(client,finish,TF_ES_SYNC|TF_ES_READWRITE,&outcome);finish->Release();ending=false;
            if(FAILED(hr) || FAILED(outcome))return 5;
            finishedSegment=segment;range.Reset();current.clear();
        }
        return result;
    }
    static bool ReadControl(HWND window,std::wstring& text) {
        int length=GetWindowTextLengthW(window);if(length<0 || length>2000000)return false;
        std::wstring buffer(static_cast<size_t>(length)+1,L'\0');
        int copied=GetWindowTextW(window,&buffer[0],length+1);if(copied!=length)return false;
        buffer.resize(copied);text.swap(buffer);return true;
    }
    int ApplyLegacy() {
        if(GetWindowLongPtrW(focus,GWL_STYLE)&ES_READONLY)return 4;
        DWORD start=0,end=0;SendMessageW(focus,EM_GETSEL,reinterpret_cast<WPARAM>(&start),reinterpret_cast<LPARAM>(&end));
        bool initial=!legacyOwned;
        if(!initial && start!=end)return 4;
        if(initial){legacyStart=start;legacyEnd=end;}
        std::wstring original;if(!ReadControl(focus,original))return 2;
        if(end!=legacyEnd || legacyStart>legacyEnd || legacyEnd>original.size() || current!=expected)return 4;
        std::wstring old=original.substr(legacyStart,legacyEnd-legacyStart);
        if(!initial && old!=expected)return 4;
        std::vector<TextChange> changes;if(!TextChanges(old,next,changes))return 2;
        if(old!=next || start!=end) {
            bool hidden=HideCaret(focus)!=FALSE;
            SendMessageW(focus,WM_SETREDRAW,FALSE,0);
            for(const auto& change:changes){
                SendMessageW(focus,EM_SETSEL,legacyStart+change.start,legacyStart+change.end);
                SendMessageW(focus,EM_REPLACESEL,TRUE,reinterpret_cast<LPARAM>(change.text.c_str()));
            }
            DWORD caret=legacyStart+static_cast<DWORD>(next.size());
            SendMessageW(focus,EM_SETSEL,caret,caret);
            SendMessageW(focus,WM_SETREDRAW,TRUE,0);
            if(hidden)ShowCaret(focus);
            RedrawWindow(focus,nullptr,nullptr,RDW_INVALIDATE|RDW_ERASE|RDW_ALLCHILDREN);
        }
        std::wstring actual;
        std::wstring wanted=original.substr(0,legacyStart)+next+original.substr(legacyEnd);
        SendMessageW(focus,EM_GETSEL,reinterpret_cast<WPARAM>(&start),reinterpret_cast<LPARAM>(&end));
        if(!ReadControl(focus,actual) || actual!=wanted || start!=legacyStart+next.size() || end!=start)return 5;
        legacyEnd=static_cast<DWORD>(start);legacyOwned=!final;current=final?L"":next;
        if(final)finishedSegment=segment;
        return 1;
    }
    HRESULT Apply(TfEditCookie cookie) {
        if(ending){HRESULT hr=composition?composition->EndComposition(cookie):S_OK;if(SUCCEEDED(hr)){composition.Reset();compositionSink.Reset();}return hr;}
        TF_SELECTION selection={};ULONG count=0;
        HRESULT hr=context->GetSelection(cookie,TF_DEFAULT_SELECTION,1,&selection,&count);
        if(FAILED(hr) || count!=1)return E_FAIL;
        ComPtr<ITfRange> selected;selected.Attach(selection.range);
        bool initial=!range;
        BOOL empty=FALSE;if(FAILED(selected->IsEmpty(cookie,&empty)) || (!initial && !empty)){result=4;return E_FAIL;}
        if(initial){
            ComPtr<ITfContextComposition> service;hr=context.As(&service);if(FAILED(hr))return hr;
            ComPtr<IEnumITfCompositionView> existing;hr=service->EnumCompositions(&existing);if(FAILED(hr))return hr;
            ComPtr<ITfCompositionView> other;ULONG found=0;hr=existing->Next(1,&other,&found);if(FAILED(hr))return hr;
            if(found){result=4;return E_FAIL;}
            compositionSink.Attach(new CompositionSink());
            hr=service->StartComposition(cookie,selected.Get(),compositionSink.Get(),&composition);if(FAILED(hr))return hr;
            hr=composition->GetRange(&range);if(FAILED(hr))return hr;
            hr=range->SetGravity(cookie,TF_GRAVITY_BACKWARD,TF_GRAVITY_FORWARD);if(FAILED(hr))return hr;
        }
        else {
            LONG comparison=1;
            if(FAILED(range->CompareEnd(cookie,selected.Get(),TF_ANCHOR_END,&comparison)) || comparison!=0){result=4;return E_FAIL;}
        }
        ComPtr<ITfRange> check;hr=range->Clone(&check);if(FAILED(hr))return hr;
        std::wstring actual;WCHAR buffer[512];ULONG length=0;
        do {hr=check->GetText(cookie,TF_TF_MOVESTART,buffer,ARRAYSIZE(buffer),&length);if(FAILED(hr))return hr;actual.append(buffer,length);if(actual.size()>8192)return E_FAIL;}while(length);
        if((!initial && actual!=expected) || current!=expected){result=4;return E_FAIL;}
        std::vector<TextChange> changes;if(!TextChanges(actual,next,changes))return E_FAIL;
        for(const auto& edit:changes){
            ComPtr<ITfRange> change;hr=range->Clone(&change);if(FAILED(hr))return hr;
            hr=change->Collapse(cookie,TF_ANCHOR_START);if(FAILED(hr))return hr;
            LONG moved=0;
            hr=change->ShiftEnd(cookie,static_cast<LONG>(edit.end),&moved,nullptr);if(FAILED(hr) || moved!=static_cast<LONG>(edit.end))return E_FAIL;
            hr=change->ShiftStart(cookie,static_cast<LONG>(edit.start),&moved,nullptr);if(FAILED(hr) || moved!=static_cast<LONG>(edit.start))return E_FAIL;
            mutated=true;hr=change->SetText(cookie,0,edit.text.data(),static_cast<LONG>(edit.text.size()));if(FAILED(hr))return hr;
        }
        // Keep the caret separate from the span retained for the next correction.
        hr=composition->ShiftStart(cookie,range.Get());if(FAILED(hr))return hr;
        hr=composition->ShiftEnd(cookie,range.Get());if(FAILED(hr))return hr;
        ComPtr<ITfRange> caret;hr=range->Clone(&caret);if(FAILED(hr))return hr;
        hr=caret->Collapse(cookie,TF_ANCHOR_END);if(FAILED(hr))return hr;
        TF_SELECTION end={caret.Get(),{TF_AE_NONE,FALSE}};
        hr=context->SetSelection(cookie,1,&end);if(FAILED(hr))return hr;
        current=next;result=1;
        return S_OK;
    }
};
HRESULT Edit::DoEditSession(TfEditCookie cookie){try{return owner_->Apply(cookie);}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}catch(...){return E_FAIL;}}

static bool IsSource(HWND source) {
    DWORD pid=0;GetWindowThreadProcessId(source,&pid);
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(!process)return false;
    WCHAR image[32768],library[32768];DWORD length=ARRAYSIZE(image);
    bool valid=QueryFullProcessImageNameW(process,0,image,&length)!=FALSE;CloseHandle(process);
    DWORD count=GetModuleFileNameW(module,library,ARRAYSIZE(library));if(!valid || !count || count>=ARRAYSIZE(library))return false;
    WCHAR* slash=wcsrchr(library,L'\\');if(!slash)return false;*(slash+1)=0;
    std::wstring expected=std::wstring(library)+L"VoxFix Desktop.exe";
    return _wcsicmp(image,expected.c_str())==0;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK BridgeHook(int code, WPARAM wparam, LPARAM lparam) {
    Bridge* created=nullptr;
    try{
    if(code>=0 && wparam==PM_REMOVE){
        static const UINT bootstrap=RegisterWindowMessageW(L"VoxFix.TextBridge." VOXFIX_BRIDGE_VERSION L".Bootstrap");
        MSG* message=reinterpret_cast<MSG*>(lparam);
        if(message->message==bootstrap && !bridge && IsSource(reinterpret_cast<HWND>(message->wParam))){
            // Window procedures must stay valid after the bootstrap hook is removed.
            HMODULE pinned=nullptr;if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,reinterpret_cast<LPCWSTR>(&BridgeHook),&pinned))return CallNextHookEx(nullptr,code,wparam,lparam);
            created=new Bridge();bridge=created;
            if(FAILED(created->Start(reinterpret_cast<HWND>(message->wParam),static_cast<uint32_t>(message->lParam)))){bridge=nullptr;delete created;created=nullptr;}
            message->message=WM_NULL;
        }
    }
    }catch(...){if(created){bridge=nullptr;delete created;}}
    return CallNextHookEx(nullptr,code,wparam,lparam);
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)module=instance;return TRUE;}
