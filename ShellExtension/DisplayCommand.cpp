#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wrl.h>
#include <string>
#include <vector>
#include <atomic>
#include <algorithm>
using namespace Microsoft::WRL;
static HMODULE module;
static std::atomic<long> objects{0};
static const CLSID CLSID_Display = {0x725d32b1,0xbe50,0x4c32,{0x90,0x32,0xa0,0x91,0x33,0x22,0x56,0x81}};
struct Lifetime { Lifetime(){ ++objects; } ~Lifetime(){ --objects; } };
static std::wstring AppPath() {
    wchar_t path[32768]{}; GetModuleFileNameW(module,path,32768);
    std::wstring p(path); return p.substr(0,p.find_last_of(L"\\/")+1)+L"SoundSwitchQuick.exe";
}
static std::wstring Quote(const std::wstring& value) {
    std::wstring out=L"\""; size_t slashes=0;
    for(wchar_t c:value) {
        if(c==L'\\') { ++slashes; continue; }
        if(c==L'\"') out.append(slashes*2+1,L'\\'); else out.append(slashes,L'\\');
        slashes=0; out+=c;
    }
    out.append(slashes*2,L'\\'); return out+L"\"";
}
static HRESULT SelectedPath(IShellItemArray* items,std::wstring& path) {
    if(!items) return E_INVALIDARG;
    DWORD count=0; if(FAILED(items->GetCount(&count))||count!=1) return E_INVALIDARG;
    ComPtr<IShellItem> item; HRESULT hr=items->GetItemAt(0,&item); if(FAILED(hr)) return hr;
    PWSTR text=nullptr; hr=item->GetDisplayName(SIGDN_FILESYSPATH,&text); if(FAILED(hr)) return hr;
    path=text; CoTaskMemFree(text);
    const auto ext=PathFindExtensionW(path.c_str());
    return (_wcsicmp(ext,L".exe")==0||_wcsicmp(ext,L".lnk")==0||_wcsicmp(ext,L".url")==0)? S_OK:E_INVALIDARG;
}
struct Target { std::wstring id,name; };
static BOOL CALLBACK Collect(HMONITOR monitor,HDC,LPRECT,LPARAM param) {
    auto& targets=*reinterpret_cast<std::vector<Target>*>(param);
    MONITORINFOEXW info{}; info.cbSize=sizeof(info);
    if(GetMonitorInfoW(monitor,reinterpret_cast<LPMONITORINFO>(&info))) {
        DISPLAY_DEVICEW device{}; device.cb=sizeof(device);
        bool ok=EnumDisplayDevicesW(info.szDevice,0,&device,EDD_GET_DEVICE_INTERFACE_NAME)!=FALSE;
        std::wstring name=info.szDevice; name+=L" — "; name+=ok?device.DeviceString:L"Экран";
        targets.push_back({ok&&device.DeviceID[0]?device.DeviceID:info.szDevice,name});
    }
    return TRUE;
}
class Command;
class CommandEnum final: public RuntimeClass<RuntimeClassFlags<ClassicCom>,IEnumExplorerCommand>,private Lifetime {
    std::vector<ComPtr<IExplorerCommand>> items; ULONG index=0;
public:
    explicit CommandEnum(std::vector<ComPtr<IExplorerCommand>> commands,ULONG offset=0):items(std::move(commands)),index(offset){}
    IFACEMETHODIMP Next(ULONG count,IExplorerCommand** result,ULONG* fetched) override {
        if(!result||(!fetched&&count!=1)) return E_POINTER;
        if(fetched)*fetched=0; ULONG n=0;
        while(n<count&&index<items.size()) { result[n]=items[index++].Get(); result[n++]->AddRef(); }
        if(fetched)*fetched=n; return n==count?S_OK:S_FALSE;
    }
    IFACEMETHODIMP Skip(ULONG count) override { ULONG n=std::min(count,static_cast<ULONG>(items.size())-index);index+=n;return n==count?S_OK:S_FALSE; }
    IFACEMETHODIMP Reset() override {index=0;return S_OK;}
    IFACEMETHODIMP Clone(IEnumExplorerCommand** result) override { if(!result)return E_POINTER;return Make<CommandEnum>(items,index).CopyTo(result); }
};
class Command final:public RuntimeClass<RuntimeClassFlags<ClassicCom>,IExplorerCommand>,private Lifetime {
    bool root=true, borderless=false; Target target;
public:
    Command()=default;
    Command(Target display,bool full):root(false),borderless(full),target(std::move(display)){}
    IFACEMETHODIMP GetTitle(IShellItemArray*,PWSTR* result) override {
        return SHStrDupW(root?L"Открыть на экране":(target.name+(borderless?L" · без рамок":L" · обычное окно")).c_str(),result);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*,PWSTR* result) override { return SHStrDupW((AppPath()+L",0").c_str(),result); }
    IFACEMETHODIMP GetToolTip(IShellItemArray*,PWSTR* result) override { return SHStrDupW(L"Запустить и запомнить экран для приложения",result); }
    IFACEMETHODIMP GetCanonicalName(GUID* result) override {if(!result)return E_POINTER;*result=root?CLSID_Display:GUID_NULL;return S_OK;}
    IFACEMETHODIMP GetState(IShellItemArray* items,BOOL,EXPCMDSTATE* state) override {
        if(!state)return E_POINTER;std::wstring path;*state=SUCCEEDED(SelectedPath(items,path))?ECS_ENABLED:ECS_HIDDEN;return S_OK;
    }
    IFACEMETHODIMP Invoke(IShellItemArray* items,IBindCtx*) override {
        if(root)return E_NOTIMPL;
        std::wstring path;HRESULT hr=SelectedPath(items,path);if(FAILED(hr))return hr;
        auto exe=AppPath(); auto args=Quote(exe)+L" --display-launch "+Quote(path)+L" "+Quote(target.id)+(borderless?L" borderless":L" window");
        STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION process{};
        if(!CreateProcessW(exe.c_str(),args.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&process))return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(process.hThread);CloseHandle(process.hProcess);return S_OK;
    }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override {if(!flags)return E_POINTER;*flags=root?ECF_HASSUBCOMMANDS:ECF_DEFAULT;return S_OK;}
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** result) override {
        if(!result)return E_POINTER;*result=nullptr;if(!root)return E_NOTIMPL;
        std::vector<Target> targets;EnumDisplayMonitors(nullptr,nullptr,Collect,reinterpret_cast<LPARAM>(&targets));
        std::vector<ComPtr<IExplorerCommand>> commands;
        for(const auto& t:targets)for(bool full:{false,true})commands.push_back(Make<Command>(t,full));
        return Make<CommandEnum>(std::move(commands)).CopyTo(result);
    }
};
class Factory final:public RuntimeClass<RuntimeClassFlags<ClassicCom>,IClassFactory>,private Lifetime {
public:
    IFACEMETHODIMP CreateInstance(IUnknown* outer,REFIID iid,void** result) override {if(outer)return CLASS_E_NOAGGREGATION;return Make<Command>()->QueryInterface(iid,result);}
    IFACEMETHODIMP LockServer(BOOL lock) override {if(lock)++objects;else --objects;return S_OK;}
};
STDAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** result) {
    if(!result)return E_POINTER;*result=nullptr;if(clsid!=CLSID_Display)return CLASS_E_CLASSNOTAVAILABLE;
    return Make<Factory>()->QueryInterface(iid,result);
}
STDAPI DllCanUnloadNow(){return objects==0?S_OK:S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
