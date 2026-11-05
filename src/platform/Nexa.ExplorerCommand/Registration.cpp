#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <filesystem>
#include <vector>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.Management.Deployment.h>
#include "CertificateTrust.h"
using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Management::Deployment;
namespace {
Uri FileUri(const std::filesystem::path& path) {
    wchar_t uri[32768]; DWORD size = ARRAYSIZE(uri);
    check_hresult(UrlCreateFromPathW(path.c_str(), uri, &size, 0)); return Uri(uri);
}
void CheckDeployment(const DeploymentResult& result) { check_hresult(result.ExtendedErrorCode()); }
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    try {
        init_apartment(apartment_type::multi_threaded);
        int count{}; PWSTR* arguments = CommandLineToArgvW(GetCommandLineW(), &count);
        std::wstring mode = arguments && count == 2 ? arguments[1] : L""; LocalFree(arguments);
        if (mode != L"--register" && mode != L"--development" && mode != L"--unregister" && mode != L"--unregister-all" && mode != L"--install-trust" && mode != L"--remove-trust") return ERROR_INVALID_PARAMETER;
        wchar_t file[32768]; DWORD size = GetModuleFileNameW(nullptr, file, ARRAYSIZE(file));
        if (!size || size >= ARRAYSIZE(file)) return ERROR_BAD_PATHNAME;
        auto directory = std::filesystem::path(file).parent_path(); auto root = directory.parent_path();
        if (mode == L"--install-trust" || mode == L"--remove-trust") { ShellTrust::Change(directory, mode == L"--install-trust"); return 0; }
        PackageManager manager;
        if (mode == L"--unregister" || mode == L"--unregister-all") {
            bool all = mode == L"--unregister-all";
            // Collect first: removing packages invalidates a live deployment enumeration.
            std::vector<hstring> packages;
            auto found = all ? manager.FindPackages() : manager.FindPackagesForUser(L"");
            for (const auto& package : found)
                if (package.Id().Name() == L"NexaCL.ExplorerIntegration") packages.push_back(package.Id().FullName());
            for (const auto& name : packages)
                CheckDeployment(manager.RemovePackageAsync(name, all ? RemovalOptions::RemoveForAllUsers : RemovalOptions::None).get());
        } else if (mode == L"--development") {
            RegisterPackageOptions options; options.ExternalLocationUri(FileUri(root));
            options.DeveloperMode(true); options.StageInPlace(true);
            CheckDeployment(manager.RegisterPackageByUriAsync(FileUri(directory / L"package/AppxManifest.xml"), options).get());
        } else {
            AddPackageOptions options; options.ExternalLocationUri(FileUri(root));
            // Launcher channel changes may also change the independent identity-package version.
            // Windows still verifies the package signature and publisher on every registration.
            options.ForceUpdateFromAnyVersion(true);
            CheckDeployment(manager.AddPackageByUriAsync(FileUri(directory / L"NexaCL.ExplorerIntegration.msix"), options).get());
        }
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
        return 0;
    } catch (const hresult_error& error) {
        OutputDebugStringW(error.message().c_str());
        auto message = to_string(error.message()); DWORD written{};
        WriteFile(GetStdHandle(STD_ERROR_HANDLE), message.data(), static_cast<DWORD>(message.size()), &written, nullptr);
        return error.code();
    }
      catch (...) { return E_FAIL; }
}
