namespace Nexa.Services.Capabilities;

public enum MinecraftEnvironmentProvider { Java = 1, Files = 2, Loader = 3, Mods = 4 }
public enum MinecraftCapabilityValueKind { Boolean = 1, SmallInteger = 2, LargeInteger = 3, Text = 4, TextList = 5 }
public sealed record MinecraftEnvironmentFactsQuery(MinecraftEnvironmentProvider Provider, string? DefaultRoot, MachineCapabilityQuery Scope, DateTimeOffset Timestamp);
public sealed record MinecraftEnvironmentFact(string Id, MinecraftCapabilityValueKind Kind, bool Boolean, int SmallInteger, long LargeInteger,
    string? Text, IReadOnlyList<string>? TextList, CapabilityAvailability Availability, CapabilityConfidence Confidence,
    CapabilityPermissionState Permission, DateTimeOffset Timestamp, string Source, string Reason);
public sealed record MinecraftEnvironmentFacts(IReadOnlyList<MinecraftEnvironmentFact> Items);
