#include "CertificateTrust.h"
#include <iostream>
using namespace ShellTrust;
void Require(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
int main(int argc, char** argv) {
    std::wstring path = L"Software\\NexaCL.Tests\\ShellTrust-" + std::to_wstring(GetCurrentProcessId());
    try {
        Require(argc == 2, "certificate fixture");
        std::ifstream input(argv[1], std::ios::binary);
        std::vector<BYTE> bytes((std::istreambuf_iterator<char>(input)), {});
        Certificate certificate(CertCreateCertificateContext(X509_ASN_ENCODING, bytes.data(), static_cast<DWORD>(bytes.size())));
        Store store(CertOpenStore(CERT_STORE_PROV_MEMORY, 0, 0, CERT_STORE_CREATE_NEW_FLAG, nullptr));
        Key journal; Registry(RegCreateKeyExW(HKEY_CURRENT_USER, path.c_str(), 0, nullptr, 0, KEY_ALL_ACCESS, nullptr, &journal.value, nullptr));
        Apply(store.value, journal.value, certificate.value, L"a", true);
        Require(Contains(store.value, certificate.value), "install adds certificate");
        Apply(store.value, journal.value, certificate.value, L"a", true);
        Apply(store.value, journal.value, certificate.value, L"b", true);
        Apply(store.value, journal.value, certificate.value, L"a", false);
        Require(Contains(store.value, certificate.value), "another install preserves trust");
        Apply(store.value, journal.value, certificate.value, L"b", false);
        Require(!Contains(store.value, certificate.value), "last uninstall removes owned trust");
        Apply(store.value, journal.value, certificate.value, L"b", false);
        Check(CertAddCertificateContextToStore(store.value, certificate.value, CERT_STORE_ADD_NEW, nullptr));
        Apply(store.value, journal.value, certificate.value, L"a", true);
        Apply(store.value, journal.value, certificate.value, L"a", false);
        Require(Contains(store.value, certificate.value), "preexisting trust is never removed");
        RegCloseKey(journal.value); journal.value = nullptr;
        Registry(RegDeleteTreeW(HKEY_CURRENT_USER, path.c_str()));
        std::cout << "PASS: isolated trust install, repair, shared ownership, rollback, preexisting certificate preservation\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what(); }
      catch (const winrt::hresult_error& error) { std::cerr << std::hex << error.code().value << ": " << winrt::to_string(error.message()); }
    RegDeleteTreeW(HKEY_CURRENT_USER, path.c_str()); return 1;
}
