using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Nexa.Services.Minecraft;
using Nexa.Services.Minecraft.Java;
using Nexa.Services.Minecraft.Launch;
using Nexa.Services.Minecraft.Libraries;
using Nexa.Services.Minecraft.Process;
using Nexa.Xsr;
using Nexa.Xsr.State;

namespace Nexa.Services.Tests;

// XSR-606: Wave 6 acceptance hardening — Java range conflicts, full launch token coverage,
// client JAR on the classpath, ARM64 native classification, Mojang rule semantics, natives
// extraction, process state in the host store, and manifest-controlled path containment.
internal static partial class Program
{
}
