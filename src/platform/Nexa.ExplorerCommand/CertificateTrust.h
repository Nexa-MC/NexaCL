#pragma once
#include <windows.h>
#include <wincrypt.h>
#include <winrt/base.h>
#include <filesystem>
#include <fstream>
#include <vector>
#include <string>
#ifndef NEXA_SHELL_CERT_SHA256
#define NEXA_SHELL_CERT_SHA256 ""
#endif
namespace ShellTrust {
inline void Check(bool ok) { if (!ok) winrt::throw_last_error(); }
struct Certificate {
    PCCERT_CONTEXT value{};
    explicit Certificate(PCCERT_CONTEXT context) : value(context) { Check(value != nullptr); }
    ~Certificate() { if (value) CertFreeCertificateContext(value); }
};
struct Store {
    HCERTSTORE value{};
    explicit Store(HCERTSTORE handle) : value(handle) { Check(value != nullptr); }
    ~Store() { CertCloseStore(value, 0); }
};
struct Key {
    HKEY value{};
    ~Key() { if (value) RegCloseKey(value); }
};
inline void Registry(LSTATUS status) { winrt::check_win32(status); }
inline std::wstring Digest(const BYTE* data, DWORD size) {
    BYTE hash[32]; DWORD length = sizeof(hash);
    Check(CryptHashCertificate2(BCRYPT_SHA256_ALGORITHM, 0, nullptr, data, size, hash, &length));
    static constexpr wchar_t digits[] = L"0123456789ABCDEF";
    std::wstring text;
    for (BYTE b : hash) { text += digits[b >> 4]; text += digits[b & 15]; }
    return text;
}
inline bool Contains(HCERTSTORE store, PCCERT_CONTEXT certificate) {
    PCCERT_CONTEXT found = CertFindCertificateInStore(store, X509_ASN_ENCODING, 0, CERT_FIND_EXISTING, certificate, nullptr);
    if (!found) return false;
    bool exact = found->cbCertEncoded == certificate->cbCertEncoded &&
        memcmp(found->pbCertEncoded, certificate->pbCertEncoded, found->cbCertEncoded) == 0;
    CertFreeCertificateContext(found); return exact;
}
// Store and registry handles are injectable so tests never change system trust.
inline void Apply(HCERTSTORE store, HKEY journal, PCCERT_CONTEXT certificate, const std::wstring& installation, bool install) {
    Key references;
    Registry(RegCreateKeyExW(journal, L"Installations", 0, nullptr, 0, KEY_READ | KEY_WRITE, nullptr, &references.value, nullptr));
    std::wstring reference = Digest(reinterpret_cast<const BYTE*>(installation.data()), static_cast<DWORD>(installation.size() * sizeof(wchar_t)));
    DWORD owned = 0, bytes = sizeof(owned), kind = 0;
    LSTATUS read = RegQueryValueExW(journal, L"AddedByNexa", nullptr, &kind, reinterpret_cast<BYTE*>(&owned), &bytes);
    if (read != ERROR_SUCCESS && read != ERROR_FILE_NOT_FOUND) Registry(read);
    if (read == ERROR_SUCCESS && (kind != REG_DWORD || bytes != sizeof(owned) || owned > 1)) winrt::throw_hresult(E_INVALIDARG);
    if (install) {
        // Record the owner first. An interrupted addition can then be removed safely.
        if (!Contains(store, certificate)) {
            owned = 1;
            Registry(RegSetValueExW(journal, L"AddedByNexa", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&owned), sizeof(owned)));
            Registry(RegFlushKey(journal));
            Check(CertAddCertificateContextToStore(store, certificate, CERT_STORE_ADD_NEW, nullptr));
        }
        Registry(RegSetValueExW(references.value, reference.c_str(), 0, REG_SZ,
            reinterpret_cast<const BYTE*>(installation.c_str()), static_cast<DWORD>((installation.size() + 1) * sizeof(wchar_t))));
    } else {
        LSTATUS removed = RegDeleteValueW(references.value, reference.c_str());
        if (removed != ERROR_SUCCESS && removed != ERROR_FILE_NOT_FOUND) Registry(removed);
        DWORD remaining{};
        Registry(RegQueryInfoKeyW(references.value, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, &remaining, nullptr, nullptr, nullptr, nullptr));
        if (remaining == 0 && owned == 1) {
            PCCERT_CONTEXT found = CertFindCertificateInStore(store, X509_ASN_ENCODING, 0, CERT_FIND_EXISTING, certificate, nullptr);
            if (found) {
                if (found->cbCertEncoded != certificate->cbCertEncoded || memcmp(found->pbCertEncoded, certificate->pbCertEncoded, certificate->cbCertEncoded)) {
                    CertFreeCertificateContext(found); winrt::throw_hresult(E_INVALIDARG);
                }
                Check(CertDeleteCertificateFromStore(found));
            }
            Registry(RegDeleteValueW(journal, L"AddedByNexa"));
        }
    }
}
inline void Change(const std::filesystem::path& directory, bool install) {
    std::ifstream input(directory / L"signer.cer", std::ios::binary);
    std::vector<BYTE> encoded;
    for (char byte; input.get(byte);) {
        if (encoded.size() >= 65536) winrt::throw_hresult(E_INVALIDARG);
        encoded.push_back(static_cast<BYTE>(byte));
    }
    if (encoded.empty()) { if (!install && !std::filesystem::exists(directory / L"signer.cer")) return; winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)); }
    std::wstring fingerprint = Digest(encoded.data(), static_cast<DWORD>(encoded.size()));
    if (fingerprint != winrt::to_hstring(NEXA_SHELL_CERT_SHA256)) winrt::throw_hresult(TRUST_E_SUBJECT_NOT_TRUSTED);
    Certificate certificate(CertCreateCertificateContext(X509_ASN_ENCODING, encoded.data(), static_cast<DWORD>(encoded.size())));
    // Only a code-signing end-entity certificate is permitted; never a root CA.
    auto basic = CertFindExtension(szOID_BASIC_CONSTRAINTS2, certificate.value->pCertInfo->cExtension, certificate.value->pCertInfo->rgExtension);
    CERT_BASIC_CONSTRAINTS2_INFO* decoded = nullptr; DWORD length = 0;
    if (!basic || !CryptDecodeObjectEx(X509_ASN_ENCODING, X509_BASIC_CONSTRAINTS2, basic->Value.pbData, basic->Value.cbData,
        CRYPT_DECODE_ALLOC_FLAG, nullptr, &decoded, &length)) winrt::throw_hresult(E_INVALIDARG);
    bool ca = decoded->fCA != FALSE; LocalFree(decoded);
    if (ca) winrt::throw_hresult(E_INVALIDARG);
    DWORD usageLength{}; Check(CertGetEnhancedKeyUsage(certificate.value, CERT_FIND_EXT_ONLY_ENHKEY_USAGE_FLAG, nullptr, &usageLength));
    std::vector<BYTE> usageData(usageLength);
    auto usage = reinterpret_cast<PCERT_ENHKEY_USAGE>(usageData.data());
    Check(CertGetEnhancedKeyUsage(certificate.value, CERT_FIND_EXT_ONLY_ENHKEY_USAGE_FLAG, usage, &usageLength));
    if (usage->cUsageIdentifier != 1 || strcmp(usage->rgpszUsageIdentifier[0], szOID_PKIX_KP_CODE_SIGNING)) winrt::throw_hresult(E_INVALIDARG);
    HANDLE mutex = CreateMutexW(nullptr, FALSE, L"Global\\NexaCL.ExplorerCertificateTrust"); Check(mutex != nullptr);
    DWORD wait = WaitForSingleObject(mutex, 30000);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) { CloseHandle(mutex); winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_TIMEOUT)); }
    try {
        Key journal;
        std::wstring path = L"SOFTWARE\\NexaCL\\ExplorerTrust\\" + fingerprint;
        Registry(RegCreateKeyExW(HKEY_LOCAL_MACHINE, path.c_str(), 0, nullptr, 0, KEY_READ | KEY_WRITE | KEY_WOW64_64KEY, nullptr, &journal.value, nullptr));
        Store store(CertOpenStore(CERT_STORE_PROV_SYSTEM_W, 0, 0, CERT_SYSTEM_STORE_LOCAL_MACHINE | CERT_STORE_OPEN_EXISTING_FLAG, L"TrustedPeople"));
        std::wstring root = std::filesystem::weakly_canonical(directory.parent_path()).wstring();
        for (auto& c : root) c = static_cast<wchar_t>(towlower(c));
        try { Apply(store.value, journal.value, certificate.value, root, install); }
        catch (...) { if (install) { try { Apply(store.value, journal.value, certificate.value, root, false); } catch (...) {} } throw; }
    } catch (...) { ReleaseMutex(mutex); CloseHandle(mutex); throw; }
    ReleaseMutex(mutex); CloseHandle(mutex);
}
}
