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

internal static partial class Program
{
    internal static void ConflictingJavaRangesAreRejected()
    {
        JavaVersionRange java21 = JavaVersionRange.ForMajor(21);
        JavaVersionRange java8 = JavaVersionRange.ForMajor(8);

        AssertFalse(java21.TryIntersect(java8, out JavaVersionRange conflict));
        AssertEqual(JavaVersionRange.Any, conflict);

        JavaRequirementResolution resolution = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasCleanroom = true,
            CleanroomVersion = "1.0.0",
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(8, 0),
            HasForge = true,
            ForgeVersion = "14.23.5.2859",
        });
        AssertFalse(resolution.Success);
        AssertEqual(JavaRequirementFailureReason.ConflictingRequirements, resolution.FailureReason);
    }

    internal static void OverlappingJavaRangesNarrowCorrectly()
    {
        JavaVersionRange any = JavaVersionRange.Any;
        AssertTrue(any.TryIntersect(JavaVersionRange.ForMajor(21), out JavaVersionRange narrowed));
        AssertEqual(JavaVersionRange.ForMajor(21), narrowed);

        JavaVersionRange legacy = new(new Version(1, 8), new Version(1, 8, 0, 512));
        AssertTrue(legacy.TryIntersect(JavaVersionRange.ForMajor(8), out JavaVersionRange eight));
        AssertEqual(new Version(1, 8), eight.Minimum);
        AssertEqual(new Version(1, 8, 0, 512), eight.Maximum);
    }

    internal static void MinecraftJavaGatesUseNormalizedCoordinates()
    {
        JavaRequirementResolution modern = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 20, 5),
        });
        AssertTrue(modern.Success);
        AssertEqual(JavaVersionRange.ForMajor(21), modern.Range);

        JavaRequirementResolution preModern = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 20, 4),
        });
        AssertTrue(preModern.Success);
        AssertEqual(JavaVersionRange.ForMajor(17), preModern.Range);

        JavaRequirementResolution shorthand = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(20, 5),
        });
        AssertTrue(shorthand.Success);
        AssertEqual(JavaVersionRange.ForMajor(21), shorthand.Range);

        JavaRequirementResolution cleanroom25 = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasCleanroom = true,
            CleanroomVersion = "0.5.1-beta",
        });
        AssertTrue(cleanroom25.Success);
        AssertEqual(JavaVersionRange.ForMajor(25), cleanroom25.Range);

        JavaRequirementResolution cleanroomWithLegacyJava8 = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasCleanroom = true,
            CleanroomVersion = "0.5.1-beta",
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 8, 9),
        });
        AssertFalse(cleanroomWithLegacyJava8.Success);
        AssertEqual(JavaRequirementFailureReason.ConflictingRequirements, cleanroomWithLegacyJava8.FailureReason);
    }

    internal static void MinecraftJavaEraMatrixMatchesReleaseLine()
    {
        (string Version, int JavaMajor)[] matrix =
        [
            ("1.7.10", 8),
            ("1.12.2", 8),
            ("1.16.5", 8),
            ("1.17.1", 16),
            ("1.18.2", 17),
            ("1.20.1", 17),
            ("1.20.4", 17),
            ("1.20.5", 21),
            ("1.21.1", 21),
            ("26.1", 25),
            ("26.2", 25),
        ];

        foreach ((string version, int javaMajor) in matrix)
        {
            JavaRequirementResolution resolution = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
            {
                HasReliableVanillaVersion = true,
                VanillaVersion = Version.Parse(version),
            });
            AssertTrue(resolution.Success);
            AssertEqual(JavaVersionRange.ForMajor(javaMajor), resolution.Range);
        }
    }

    internal static void MinecraftCalendarVersionsRetainTheirScheme()
    {
        MinecraftGameVersion calendar = MinecraftGameVersion.FromVersion(new Version(26, 1));
        AssertEqual(MinecraftVersionScheme.Calendar, calendar.Scheme);
        AssertTrue(calendar.IsCalendar);
        AssertEqual(26, calendar.Major);
        AssertEqual(1, calendar.Minor);
        AssertEqual(0, calendar.Patch);
        AssertEqual(new Version(26, 1, 0), calendar.ToVersion());
        AssertEqual(new Version(26, 1, 0), MinecraftJavaRequirementResolver.NormalizeVanilla(new Version(26, 1)));

        MinecraftGameVersion shorthand = MinecraftGameVersion.FromVersion(new Version(20, 5));
        AssertEqual(MinecraftVersionScheme.Legacy, shorthand.Scheme);
        AssertEqual(new MinecraftGameVersion(MinecraftVersionScheme.Legacy, 1, 20, 5), shorthand);
        AssertTrue(calendar > shorthand);
    }

    internal static void ManifestJavaMetadataIsAuthoritative()
    {
        // Calendar 26.1 uses Java 25. The manifest contract must remain exact even when the
        // fallback table is changed or when a caller supplies the legacy Version compatibility input.
        JavaRequirementResolution calendar = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(26, 1),
            ManifestJavaMajorVersion = 25,
            ManifestJavaComponent = "java-runtime-delta",
        });
        AssertTrue(calendar.Success);
        AssertEqual(JavaVersionRange.ForMajor(25), calendar.Range);
        AssertEqual("java-runtime-delta", calendar.RecommendedComponent);

        // A modern 1.20.1 manifest explicitly declaring Java 17 must not inherit an old Any
        // fallback.
        JavaRequirementResolution modern = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 20, 1),
            ManifestJavaMajorVersion = 17,
        });
        AssertTrue(modern.Success);
        AssertEqual(JavaVersionRange.ForMajor(17), modern.Range);

        // Even an intentionally different manifest major wins over inference; only loader
        // constraints are intersected with this authoritative value.
        JavaRequirementResolution overridden = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 20, 1),
            ManifestJavaMajorVersion = 25,
        });
        AssertTrue(overridden.Success);
        AssertEqual(JavaVersionRange.ForMajor(25), overridden.Range);

        JavaRequirementResolution typedResolution = MinecraftJavaRequirementResolver.Resolve(new MinecraftJavaRequirementRequest
        {
            MinecraftVersion = MinecraftGameVersion.FromVersion(new Version(26, 1)),
            ManifestJavaMajorVersion = 25,
        });
        AssertTrue(typedResolution.Success);
        AssertEqual(JavaVersionRange.ForMajor(25), typedResolution.Range);
    }

    internal static async ValueTask Minecraft1165NeverSelectsJava7()
    {
        JavaRuntimeCandidate[] candidates =
        [
            Candidate("jdk-7", new Version(1, 7, 0, 321), JavaBrand.EclipseTemurin, false),
            Candidate("jdk-8", new Version(1, 8, 0, 392), JavaBrand.EclipseTemurin, false),
        ];
        JavaSelectionResult result = await new JavaSelectionService(new InMemoryJavaLocator(candidates)).SelectAsync(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 16, 5),
        });
        AssertTrue(result.Success);
        AssertEqual(8, result.SelectedJava!.Installation.MajorVersion);
        AssertFalse(result.Requirement.Range.Contains(new Version(1, 7, 0, 321)));
    }

    internal static async ValueTask Minecraft1201NeverSelectsJava8()
    {
        JavaRuntimeCandidate[] candidates =
        [
            Candidate("jdk-8", new Version(1, 8, 0, 392), JavaBrand.EclipseTemurin, false),
            Candidate("jdk-17", new Version(17, 0, 10), JavaBrand.EclipseTemurin, false),
            Candidate("jdk-21", new Version(21, 0, 2), JavaBrand.Microsoft, false),
        ];
        JavaSelectionResult result = await new JavaSelectionService(new InMemoryJavaLocator(candidates)).SelectAsync(new MinecraftJavaRequirementRequest
        {
            HasReliableVanillaVersion = true,
            VanillaVersion = new Version(1, 20, 1),
        });
        AssertTrue(result.Success);
        AssertEqual(17, result.SelectedJava!.Installation.MajorVersion);
        AssertFalse(result.Requirement.Range.Contains(new Version(1, 8, 0, 392)));
    }
}
