#include <windows.h>
#include <shobjidl.h>
#include <wrl.h>
#include <iostream>
using Microsoft::WRL::ComPtr;
static void Check(bool result){if(!result){std::cerr<<"Shell test failed\n";ExitProcess(1);}}
int wmain(int argc,wchar_t** argv){
    Check(argc==2);CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    auto dll=LoadLibraryW(argv[1]);Check(dll!=nullptr);
    auto factoryProc=reinterpret_cast<HRESULT(__stdcall*)(REFCLSID,REFIID,void**)>(GetProcAddress(dll,"DllGetClassObject"));Check(factoryProc!=nullptr);
    CLSID id={0x725d32b1,0xbe50,0x4c32,{0x90,0x32,0xa0,0x91,0x33,0x22,0x56,0x81}};
    ComPtr<IClassFactory> factory;Check(SUCCEEDED(factoryProc(id,IID_PPV_ARGS(&factory))));
    ComPtr<IExplorerCommand> command;Check(SUCCEEDED(factory->CreateInstance(nullptr,IID_PPV_ARGS(&command))));
    PWSTR title=nullptr;Check(SUCCEEDED(command->GetTitle(nullptr,&title))&&title&&wcslen(title)>0);CoTaskMemFree(title);
    EXPCMDSTATE state;Check(SUCCEEDED(command->GetState(nullptr,FALSE,&state))&&state==ECS_HIDDEN);
    EXPCMDFLAGS flags;Check(SUCCEEDED(command->GetFlags(&flags))&&flags==ECF_HASSUBCOMMANDS);
    ComPtr<IEnumExplorerCommand> children;Check(SUCCEEDED(command->EnumSubCommands(&children)));
    ULONG total=0,fetched=0;ComPtr<IExplorerCommand> child;
    while(children->Next(1,&child,&fetched)==S_OK){Check(fetched==1);Check(SUCCEEDED(child->GetTitle(nullptr,&title)));CoTaskMemFree(title);child.Reset();++total;}
    Check(total>=2&&total%2==0);Check(children->Reset()==S_OK);
    ComPtr<IEnumExplorerCommand> clone;Check(children->Clone(&clone)==S_OK);Check(clone->Next(1,&child,&fetched)==S_OK);child.Reset();
    children.Reset();clone.Reset();command.Reset();factory.Reset();
    auto unload=reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(dll,"DllCanUnloadNow"));Check(unload&&unload()==S_OK);
    FreeLibrary(dll);CoUninitialize();std::cout<<"Shell COM tests passed\n";return 0;
}
