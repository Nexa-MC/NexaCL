#pragma once
#include <windows.h>
#include <string>
inline constexpr CLSID NexaCommandId{0x522c4ed3,0x0a1f,0x47e8,{0xb0,0x18,0x96,0x16,0x52,0xd6,0x38,0x16}};
inline constexpr GUID ModpackCommandId{0x586b27cd,0x8203,0x4126,{0x99,0x40,0xf5,0x1d,0x71,0x51,0xbc,0x9c}};
inline constexpr GUID ThemeCommandId{0x3e89242d,0x2eb4,0x408e,{0xab,0xb3,0x68,0x9e,0x5c,0x74,0x46,0x13}};
// CommandLineToArgvW / CRT quoting, including trailing slashes before a closing quote.
inline std::wstring QuoteArgument(const std::wstring& value) {
    std::wstring result = L"\""; size_t slashes = 0;
    for (wchar_t c : value) {
        if (c == L'\\') { ++slashes; continue; }
        result.append(c == L'\"' ? slashes * 2 + 1 : slashes, L'\\'); slashes = 0;
        result += c;
    }
    result.append(slashes * 2, L'\\'); result += L'\"'; return result;
}
