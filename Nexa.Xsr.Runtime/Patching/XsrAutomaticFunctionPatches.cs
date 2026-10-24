using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Nexa.Core.Patching;
using Nexa.Sidecar.Protocol;

namespace Nexa.Xsr.Runtime;

public sealed class XsrAutomaticFunctionAdmission
{
    private readonly Func<PatchTarget, bool>? _policy;
    public XsrAutomaticFunctionAdmission(Func<PatchTarget, bool>? policy = null) => _policy = policy;
    internal static bool IsProgram(ReadOnlySpan<byte> payload) => payload.Length >= 4 && payload[..4].SequenceEqual("NFP3"u8);
    internal XsrPreparedAutomaticFunction[] Prepare(IReadOnlyList<SidecarExtensionRegistration> declarations, SidecarFeatures features)
    {
        List<XsrPreparedAutomaticFunction> prepared = [];
        foreach (var declaration in declarations)
        {
            if (declaration.Kind != SidecarRegistrationKind.FunctionPatch || !IsProgram(declaration.Payload.Span)) continue;
            if (!features.HasFlag(SidecarFeatures.AutomaticFunctions) || declaration.Flags != 0)
                throw new SidecarProtocolException("Automatic function patches require negotiated support and zero flags.");
            var target = Patch.Find(declaration.Target.Value);
            if (target is null || target.Status != PatchTargetStatus.Instrumented || _policy?.Invoke(target) == false)
                throw new SidecarProtocolException("Automatic function target has no instrumented managed body.");
            prepared.Add(new(target, AutomaticFunctionProgram.Decode(declaration.Payload.Span, target)));
            if (prepared.Count > 256) throw new SidecarProtocolException("Session automatic program budget exceeded.");
        }
        if (prepared.GroupBy(item => item.Target.Id).Any(group => group.Count() > 32))
            throw new SidecarProtocolException("Session automatic point budget exceeded.");
        return prepared.ToArray();
    }
    internal static IDisposable Activate(XsrPreparedAutomaticFunction[] prepared)
    {
        var batch = Patch.BeginBatch();
        try
        {
            foreach (var item in prepared) batch.Add(item.Target, item.Program.Phase, item.Program.Apply);
            batch.Commit(); return batch;
        }
        catch { batch.Dispose(); throw; }
    }
}

internal sealed record XsrPreparedAutomaticFunction(PatchTarget Target, AutomaticFunctionProgram Program);
internal sealed class AutomaticFunctionProgram(PatchPhase phase, AutomaticFunctionProgram.Operation[] operations)
{
    internal PatchPhase Phase { get; } = phase;
    internal sealed record Operation(byte Code, ushort Argument = 0, object? Constant = null);
    private readonly record struct StackType(string Name, bool Reference = false, bool Default = false);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static string ValueType(string type) => type.EndsWith('&') ? type[..^1] : type;
    private static SidecarProtocolException Invalid() => new("Invalid, unsafe or mismatched NFP3 program.");

    internal static AutomaticFunctionProgram Decode(ReadOnlySpan<byte> bytes, PatchTarget target)
    {
        if (bytes.Length is < 9 or > 16384 || !XsrAutomaticFunctionAdmission.IsProgram(bytes) || bytes[4] != 1
            || !Enum.IsDefined((PatchPhase)bytes[5])) throw Invalid();
        var phase = (PatchPhase)bytes[5]; int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        if (count is < 1 or > 128) throw Invalid();
        Operation[] operations = new Operation[count]; StackType[] stack = new StackType[16];
        int cursor = 8, top = 0; bool resultSet = false, skip = false;
        for (int index = 0; index < count; index++)
        {
            if (cursor >= bytes.Length) throw Invalid();
            byte code = bytes[cursor++]; ushort argument = 0; object? constant = null;
            switch (code)
            {
                case 1: // Value of argument, including opaque local values.
                case 9: // Typed reference to an argument (needed for reference returns).
                    if (cursor + 2 > bytes.Length || top == 16) throw Invalid();
                    argument = BinaryPrimitives.ReadUInt16LittleEndian(bytes[cursor..]); cursor += 2;
                    if (argument >= target.Parameters.Count) throw Invalid();
                    stack[top++] = code == 9 ? new(target.Parameters[argument].Type, true)
                        : new(ValueType(target.Parameters[argument].Type));
                    break;
                case 2:
                    if (phase is not (PatchPhase.Tail or PatchPhase.Return) || target.ResultType == "System.Void" || top == 16) throw Invalid();
                    stack[top++] = new(target.ResultType, target.ResultFlags.HasFlag(PatchTypeTraits.ByReference)); break;
                case 3:
                    if (cursor + 3 > bytes.Length || top == 16) throw Invalid();
                    byte kind = bytes[cursor++]; int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes[cursor..]); cursor += 2;
                    if (length > 4096 || cursor + length > bytes.Length) throw Invalid();
                    var literal = Constant(kind, bytes.Slice(cursor, length)); cursor += length;
                    constant = literal.Value; stack[top++] = new(literal.Type); break;
                case 4:
                    if (top < 2 || stack[top - 1] != stack[top - 2] || !Addable(stack[top - 1])) throw Invalid();
                    top--; break;
                case 5:
                    if (phase != PatchPhase.Args || cursor + 2 > bytes.Length || top == 0) throw Invalid();
                    argument = BinaryPrimitives.ReadUInt16LittleEndian(bytes[cursor..]); cursor += 2;
                    if (argument >= target.Parameters.Count || !Assignable(stack[--top], ValueType(target.Parameters[argument].Type), false)) throw Invalid();
                    break;
                case 6:
                    if (phase == PatchPhase.Args || top == 0 || target.ResultType == "System.Void"
                        || !Assignable(stack[--top], target.ResultType, target.ResultFlags.HasFlag(PatchTypeTraits.ByReference))) throw Invalid();
                    resultSet = true; break;
                case 7:
                    if (phase is not (PatchPhase.Head or PatchPhase.Replace) || skip
                        || target.ResultType != "System.Void" && !resultSet) throw Invalid();
                    skip = true; break;
                case 8:
                    if (index != count - 1 || top != 0 || cursor != bytes.Length) throw Invalid();
                    break;
                case 10:
                    if (top == 16) throw Invalid(); stack[top++] = new("*", Default: true); constant = Patch.DefaultValue; break;
                case 11:
                    if (top < 2 || stack[top - 1] != stack[top - 2] || !Equatable(stack[top - 1])) throw Invalid();
                    top--; stack[top - 1] = new("System.Boolean"); break;
                default: throw Invalid();
            }
            operations[index] = new(code, argument, constant);
        }
        if (operations[^1].Code != 8 || cursor != bytes.Length) throw Invalid();
        return new(phase, operations);
    }

    private static bool Assignable(StackType value, string target, bool reference) => value.Default ? !reference
        : value.Name == target && value.Reference == reference;
    private static bool Addable(StackType value) => !value.Reference && !value.Default && value.Name is
        "System.String" or "System.Int32" or "System.Int64" or "System.Double" or "System.Single" or "System.UInt32" or "System.UInt64";
    private static bool Equatable(StackType value) => !value.Reference && !value.Default && value.Name is
        "System.String" or "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16"
        or "System.Int32" or "System.Int64" or "System.Double" or "System.Single" or "System.UInt32" or "System.UInt64";
    private static (string Type, object Value) Constant(byte kind, ReadOnlySpan<byte> bytes)
    {
        try
        {
            return kind switch
            {
                0 => ("System.String", StrictUtf8.GetString(bytes)),
                1 when bytes.Length == 1 && bytes[0] <= 1 => ("System.Boolean", bytes[0] != 0),
                2 when bytes.Length == 4 => ("System.Int32", BinaryPrimitives.ReadInt32LittleEndian(bytes)),
                3 when bytes.Length == 8 => ("System.Int64", BinaryPrimitives.ReadInt64LittleEndian(bytes)),
                4 when bytes.Length == 8 && double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(bytes)) => ("System.Double", BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
                8 when bytes.Length == 1 => ("System.Byte", bytes[0]),
                9 when bytes.Length == 1 => ("System.SByte", unchecked((sbyte)bytes[0])),
                10 when bytes.Length == 2 => ("System.Int16", BinaryPrimitives.ReadInt16LittleEndian(bytes)),
                11 when bytes.Length == 2 => ("System.UInt16", BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
                12 when bytes.Length == 4 => ("System.UInt32", BinaryPrimitives.ReadUInt32LittleEndian(bytes)),
                13 when bytes.Length == 8 => ("System.UInt64", BinaryPrimitives.ReadUInt64LittleEndian(bytes)),
                14 when bytes.Length == 4 && float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes)) => ("System.Single", BinaryPrimitives.ReadSingleLittleEndian(bytes)),
                15 when bytes.Length == 2 => ("System.Char", (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes)),
                _ => throw Invalid(),
            };
        }
        catch (DecoderFallbackException) { throw Invalid(); }
    }

    internal void Apply(PatchFrame frame)
    {
        var stack = ArrayPool<object?>.Shared.Rent(16); int top = 0;
        try
        {
            foreach (var operation in operations)
            {
                switch (operation.Code)
                {
                    case 1: stack[top++] = frame.Arguments[operation.Argument]; break;
                    case 2: stack[top++] = frame.Result; break;
                    case 3 or 10: stack[top++] = operation.Constant; break;
                    case 4: { object? right = stack[--top]; stack[top - 1] = Add(stack[top - 1], right); break; }
                    case 5: frame.Arguments[operation.Argument] = stack[--top]; break;
                    case 6: frame.Result = stack[--top]; break;
                    case 7: frame.SkipOriginal = true; break;
                    case 8: return;
                    case 9: stack[top++] = Patch.Reference(operation.Argument); break;
                    case 11: { object? right = stack[--top]; stack[top - 1] = Equal(stack[top - 1], right); break; }
                }
            }
        }
        finally { ArrayPool<object?>.Shared.Return(stack, clearArray: true); }
    }
    private static object Add(object? left, object? right) => (left, right) switch
    {
        (string a, string b) when b.Length <= 4096 && a.Length <= 4096 - b.Length => a + b,
        (int a, int b) => checked(a + b),
        (long a, long b) => checked(a + b),
        (uint a, uint b) => checked(a + b),
        (ulong a, ulong b) => checked(a + b),
        (double a, double b) when double.IsFinite(a + b) => a + b,
        (float a, float b) when float.IsFinite(a + b) => a + b,
        _ => throw new InvalidOperationException("NFP3 value operation failed."),
    };
    private static bool Equal(object? left, object? right) => left switch
    {
        string value => right is string other && value == other,
        int value => right is int other && value == other,
        long value => right is long other && value == other,
        bool value => right is bool other && value == other,
        double value => right is double other && value == other,
        float value => right is float other && value == other,
        uint value => right is uint other && value == other,
        ulong value => right is ulong other && value == other,
        byte value => right is byte other && value == other,
        sbyte value => right is sbyte other && value == other,
        short value => right is short other && value == other,
        ushort value => right is ushort other && value == other,
        char value => right is char other && value == other,
        _ => throw new InvalidOperationException("NFP3 equality requires a known primitive type."),
    };
}
