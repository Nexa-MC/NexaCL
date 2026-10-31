#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <shellapi.h>
#include <wrl.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <vector>
#include "CommandIdentity.h"
using Microsoft::WRL::ComPtr;
static void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
static ComPtr<IShellItemArray> Selection(const std::vector<std::filesystem::path>& paths) {
    std::vector<PIDLIST_ABSOLUTE> ids;
    for (const auto& path : paths) { PIDLIST_ABSOLUTE id{}; Require(SUCCEEDED(SHParseDisplayName(path.c_str(), nullptr, &id, 0, nullptr)), "parse selection"); ids.push_back(id); }
    ComPtr<IShellItemArray> items;
    HRESULT hr = SHCreateShellItemArrayFromIDLists(static_cast<UINT>(ids.size()), const_cast<PCIDLIST_ABSOLUTE*>(ids.data()), &items);
    for (auto id : ids) CoTaskMemFree(id);
    Require(SUCCEEDED(hr), "selection array"); return items;
}
int wmain(int argc, wchar_t** argv) {
    if (argc == 2 && std::wstring(argv[1]) == L"--registered") {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        ComPtr<IExplorerCommand> command;
        HRESULT hr = CoCreateInstance(NexaCommandId, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&command));
        if (FAILED(hr)) { std::cerr << "Registered activation failed: " << std::hex << hr << '\n'; return 1; }
        ComPtr<IEnumExplorerCommand> children;
        Require(SUCCEEDED(command->EnumSubCommands(&children)), "registered flyout");
        ComPtr<IExplorerCommand> child;
        Require(children->Next(1, &child, nullptr) == S_OK, "registered child");
        std::cout << "PASS: registered COM surrogate activation and flyout\n"; return 0;
    }
    if (argc == 3 && (std::wstring(argv[1]) == L"--install-modpack" || std::wstring(argv[1]) == L"--install-theme")) {
        wchar_t capture[32768]; if (!GetEnvironmentVariableW(L"NEXA_SHELL_TEST_RESULT", capture, ARRAYSIZE(capture))) return 5;
        std::wstring text = std::wstring(argv[1]) + L"\n" + argv[2];
        std::ofstream(std::filesystem::path(std::wstring(capture) + L".pid")) << GetCurrentProcessId();
        std::ofstream output(std::filesystem::path(capture), std::ios::binary);
        output.write(reinterpret_cast<const char*>(text.data()), static_cast<std::streamsize>(text.size() * sizeof(wchar_t))); return 0;
    }
    try {
        Require(argc == 2, "DLL argument"); Require(SUCCEEDED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)), "COM");
        auto root = std::filesystem::temp_directory_path() / (L"nexacl-shell-test-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
        std::filesystem::create_directories(root / L"ShellIntegration");
        auto dll = root / L"ShellIntegration/Nexa.ExplorerCommand.dll";
        std::filesystem::copy_file(argv[1], dll);
        wchar_t own[32768]; GetModuleFileNameW(nullptr, own, ARRAYSIZE(own));
        std::filesystem::copy_file(own, root / L"Nexa.Desktop.exe");
        auto zip = root / L"整合包 & ' 100% test.ZIP"; auto other = root / L"test.txt";
        std::ofstream(zip).put('a'); std::ofstream(other).put('b');
        HMODULE module = LoadLibraryExW(dll.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        Require(module != nullptr, "load DLL");
        auto factoryEntry = reinterpret_cast<HRESULT(__stdcall*)(REFCLSID, REFIID, void**)>(GetProcAddress(module, "DllGetClassObject"));
        auto unload = reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(module, "DllCanUnloadNow"));
        Require(factoryEntry && unload && unload() == S_OK, "exports and initial lifetime");
        {
            ComPtr<IClassFactory> factory; Require(SUCCEEDED(factoryEntry(NexaCommandId, IID_PPV_ARGS(&factory))), "factory");
            Require(unload() == S_FALSE, "factory lifetime");
            factory->LockServer(TRUE); factory->LockServer(FALSE);
            ComPtr<IExplorerCommand> command; Require(SUCCEEDED(factory->CreateInstance(nullptr, IID_PPV_ARGS(&command))), "command");
            GUID id{}; command->GetCanonicalName(&id); Require(id == NexaCommandId, "root identity");
            EXPCMDFLAGS flags{}; command->GetFlags(&flags); Require(flags == ECF_HASSUBCOMMANDS, "flyout");
            EXPCMDSTATE state{}; auto selected = Selection({zip}); command->GetState(selected.Get(), FALSE, &state); Require(state == ECS_ENABLED, "ZIP enabled");
            command->GetState(nullptr, FALSE, &state); Require(state == ECS_HIDDEN, "empty hidden");
            auto wrong = Selection({other}); command->GetState(wrong.Get(), FALSE, &state); Require(state == ECS_HIDDEN, "non-ZIP hidden");
            auto multiple = Selection({zip, other}); command->GetState(multiple.Get(), FALSE, &state); Require(state == ECS_HIDDEN, "multi hidden");
            ComPtr<IEnumExplorerCommand> children; Require(SUCCEEDED(command->EnumSubCommands(&children)), "enumerator");
            ComPtr<IExplorerCommand> child; ULONG fetched{};
            for (int index = 0; index < 2; ++index) {
                child.Reset(); Require(children->Next(1, &child, &fetched) == S_OK && fetched == 1, "child");
                child->GetCanonicalName(&id); Require(id == (index == 0 ? ModpackCommandId : ThemeCommandId), "child identity");
                auto capture = root / (index == 0 ? L"modpack-result" : L"theme-result");
                SetEnvironmentVariableW(L"NEXA_SHELL_TEST_RESULT", capture.c_str());
                Require(SUCCEEDED(child->Invoke(selected.Get(), nullptr)), "launch fixture");
                std::wstring expected = (index == 0 ? L"--install-modpack\n" : L"--install-theme\n") + zip.wstring();
                std::wstring actual;
                for (int attempt = 0; attempt < 250; ++attempt) {
                    Sleep(20); if (!std::filesystem::exists(capture)) continue;
                    std::ifstream input(capture, std::ios::binary); actual.resize(expected.size()); input.read(reinterpret_cast<char*>(actual.data()), static_cast<std::streamsize>(actual.size() * sizeof(wchar_t)));
                    if (actual == expected) break;
                }
                Require(actual == expected, "exact mode and Unicode argument delivery");
                DWORD pid{}; std::ifstream(std::filesystem::path(capture.wstring() + L".pid")) >> pid;
                HANDLE childProcess = OpenProcess(SYNCHRONIZE, FALSE, pid);
                if (childProcess) { Require(WaitForSingleObject(childProcess, 5000) == WAIT_OBJECT_0, "fixture exit"); CloseHandle(childProcess); }
            }
            child.Reset(); Require(children->Next(1, &child, &fetched) == S_FALSE && fetched == 0, "enumeration end");
            children->Reset(); children->Skip(1); ComPtr<IEnumExplorerCommand> clone; children->Clone(&clone);
            Require(clone->Next(1, &child, nullptr) == S_OK, "clone position"); child->GetCanonicalName(&id); Require(id == ThemeCommandId, "clone child");
            for (auto text : {L"a b", L"C:\\trailing\\", L"with\"quote", L"& % ' 中文"}) {
                int count{}; auto line = L"test " + QuoteArgument(text); PWSTR* args = CommandLineToArgvW(line.c_str(), &count);
                Require(args && count == 2 && std::wstring(args[1]) == text, "quoting round trip"); LocalFree(args);
            }
        }
        Require(unload() == S_OK, "all references released"); FreeLibrary(module); CoUninitialize();
        std::filesystem::remove_all(root);
        std::cout << "PASS: native flyout, filtering, enumeration, lifecycle, both launch paths and quoting\n"; return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
