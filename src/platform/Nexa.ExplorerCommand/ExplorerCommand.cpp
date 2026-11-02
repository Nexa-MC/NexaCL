#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wrl.h>
#include <atomic>
#include <filesystem>
#include <new>
#include "CommandIdentity.h"

using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;
using Microsoft::WRL::ClassicCom;
namespace {
HMODULE module;
std::atomic<long> objects{0};
std::atomic<long> locks{0};
struct Lifetime { Lifetime() { ++objects; } ~Lifetime() { --objects; } };
enum class Kind { Root, Modpack, Theme };
HRESULT SelectedZip(IShellItemArray* items, PWSTR* path) noexcept {
    *path = nullptr; DWORD count = 0;
    if (!items || FAILED(items->GetCount(&count)) || count != 1) return E_INVALIDARG;
    ComPtr<IShellItem> item;
    HRESULT hr = items->GetItemAt(0, &item); if (FAILED(hr)) return hr;
    hr = item->GetDisplayName(SIGDN_FILESYSPATH, path); if (FAILED(hr)) return hr;
    if (_wcsicmp(PathFindExtensionW(*path), L".zip") || PathIsRelativeW(*path)) {
        CoTaskMemFree(*path); *path = nullptr; return E_INVALIDARG;
    }
    SFGAOF attributes = 0;
    hr = item->GetAttributes(SFGAO_FOLDER | SFGAO_STREAM, &attributes);
    // Windows' compressed-folder namespace marks real ZIP files as folders AND streams.
    if (FAILED(hr) || ((attributes & SFGAO_FOLDER) && !(attributes & SFGAO_STREAM))) { CoTaskMemFree(*path); *path = nullptr; return E_INVALIDARG; }
    return S_OK;
}
std::filesystem::path InstallRoot() {
    wchar_t path[32768]; DWORD length = GetModuleFileNameW(module, path, ARRAYSIZE(path));
    if (!length || length >= ARRAYSIZE(path)) throw std::runtime_error("Module path unavailable");
    return std::filesystem::path(path).parent_path().parent_path();
}
class Command;
class Enumerator final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>, private Lifetime {
    ULONG position = 0;
public:
    explicit Enumerator(ULONG index = 0) noexcept : position(index) {}
    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override;
    IFACEMETHODIMP Skip(ULONG count) override { ULONG available = 2 - position; position += (count < available ? count : available); return count <= available ? S_OK : S_FALSE; }
    IFACEMETHODIMP Reset() override { position = 0; return S_OK; }
    IFACEMETHODIMP Clone(IEnumExplorerCommand** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        auto copy = Make<Enumerator>(position); return copy ? copy.CopyTo(result) : E_OUTOFMEMORY;
    }
};
class Command final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>, private Lifetime {
    Kind kind;
public:
    explicit Command(Kind commandKind = Kind::Root) noexcept : kind(commandKind) {}
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override {
        if (!title) return E_POINTER;
        bool chinese = PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_CHINESE;
        return SHStrDupW(kind == Kind::Root ? L"NexaCL" : kind == Kind::Modpack ?
            (chinese ? L"作为整合包安装" : L"Install as modpack") : (chinese ? L"作为主题包安装" : L"Install as theme"), title);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        if (!icon) return E_POINTER; *icon = nullptr;
        try {
            auto path = InstallRoot() / (kind == Kind::Root ? L"Nexa.Desktop.exe" : kind == Kind::Modpack ? L"Assets/FileTypes/nmp.ico" : L"Assets/FileTypes/ntp.ico");
            return SHStrDupW((QuoteArgument(path.wstring()) + L",0").c_str(), icon);
        } catch (...) { return E_FAIL; }
    }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* text) override { if (!text) return E_POINTER; *text = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* id) override {
        if (!id) return E_POINTER; *id = kind == Kind::Root ? NexaCommandId : kind == Kind::Modpack ? ModpackCommandId : ThemeCommandId; return S_OK;
    }
    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        if (!state) return E_POINTER; PWSTR path = nullptr;
        *state = SUCCEEDED(SelectedZip(items, &path)) ? ECS_ENABLED : ECS_HIDDEN;
        CoTaskMemFree(path); return S_OK;
    }
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override {
        if (kind == Kind::Root) return E_NOTIMPL;
        PWSTR raw = nullptr; HRESULT hr = SelectedZip(items, &raw); if (FAILED(hr)) return hr;
        try {
            std::wstring selected(raw); CoTaskMemFree(raw); raw = nullptr;
            auto root = InstallRoot(); auto executable = root / L"Nexa.Desktop.exe";
            std::wstring line = QuoteArgument(executable.wstring()) + (kind == Kind::Theme ? L" --install-theme " : L" --install-modpack ") + QuoteArgument(selected);
            STARTUPINFOW startup{sizeof(startup)}; PROCESS_INFORMATION process{};
            // Never shell-evaluate user-controlled file names, and never search PATH for the launcher.
            if (!CreateProcessW(executable.c_str(), line.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process)) return HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(process.hThread); CloseHandle(process.hProcess); return S_OK;
        } catch (...) { CoTaskMemFree(raw); return E_FAIL; }
    }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { if (!flags) return E_POINTER; *flags = kind == Kind::Root ? ECF_HASSUBCOMMANDS : ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** result) override {
        if (!result) return E_POINTER; *result = nullptr; if (kind != Kind::Root) return E_NOTIMPL;
        auto enumeration = Make<Enumerator>(); return enumeration ? enumeration.CopyTo(result) : E_OUTOFMEMORY;
    }
};
HRESULT Enumerator::Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) {
    if (fetched) *fetched = 0;
    if (!commands || (!fetched && count != 1)) return E_POINTER;
    ULONG read = 0;
    while (read < count && position < 2) {
        auto command = Make<Command>(position == 0 ? Kind::Modpack : Kind::Theme);
        if (!command) return E_OUTOFMEMORY;
        commands[read++] = command.Detach(); ++position;
    }
    if (fetched) *fetched = read; return read == count ? S_OK : S_FALSE;
}
class Factory final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IClassFactory>, private Lifetime {
public:
    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID id, void** result) override {
        if (!result) return E_POINTER; *result = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
        auto command = Make<Command>(); return command ? command->QueryInterface(id, result) : E_OUTOFMEMORY;
    }
    IFACEMETHODIMP LockServer(BOOL lock) override { if (lock) ++locks; else --locks; return S_OK; }
};
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) module = instance; return TRUE; }
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID id, void** result) {
    if (!result) return E_POINTER; *result = nullptr; if (clsid != NexaCommandId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = Make<Factory>(); return factory ? factory->QueryInterface(id, result) : E_OUTOFMEMORY;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() { return objects == 0 && locks == 0 ? S_OK : S_FALSE; }
