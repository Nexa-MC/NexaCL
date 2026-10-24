using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Nexa.Xsr.Patch.Compiler;

internal sealed partial class AssemblyInstrumenter
{
    private void Weave(MethodDefinition method, int slot)
    {
        method.Body.SimplifyMacros();
        var body = method.Body; var processor = body.GetILProcessor();
        var returns = body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ret).ToArray();
        Instruction entry = body.Instructions[0];
        if (method.IsConstructor && !method.IsStatic && !method.DeclaringType.IsValueType)
        {
            var initialize = body.Instructions.FirstOrDefault(instruction => instruction.OpCode == OpCodes.Call
                && instruction.Operand is MethodReference called && called.Name == ".ctor"
                && (called.DeclaringType.GetElementType().FullName == method.DeclaringType.FullName
                    || called.DeclaringType.GetElementType().FullName == method.DeclaringType.BaseType?.GetElementType().FullName));
            if (initialize?.Next is null) throw new InvalidDataException("Constructor has no verifiable initialization boundary: " + method.FullName);
            entry = initialize.Next;
        }
        var frame = new VariableDefinition(_frameType); body.Variables.Add(frame);
        var bitmap = new VariableDefinition(new ArrayType(_module.TypeSystem.Byte)); body.Variables.Add(bitmap);
        var boxed = new VariableDefinition(_module.TypeSystem.Object); body.Variables.Add(boxed);
        var saved = method.Parameters.Select(parameter => new VariableDefinition(ValueType(parameter.ParameterType))).ToArray();
        foreach (var variable in saved) body.Variables.Add(variable);
        bool hasResult = Unmodified(method.ReturnType).MetadataType != MetadataType.Void;
        VariableDefinition? result = hasResult ? new(method.ReturnType) : null;
        if (result is not null) body.Variables.Add(result);
        List<Instruction> prefix = [];
        var original = Instruction.Create(OpCodes.Nop);
        void Emit(OpCode code) => prefix.Add(Instruction.Create(code));
        void Number(int value) => prefix.Add(Instruction.Create(OpCodes.Ldc_I4, value));
        Emit(OpCodes.Ldnull); prefix.Add(Instruction.Create(OpCodes.Stloc, frame));
        prefix.Add(Instruction.Create(OpCodes.Ldsfld, _sites)); Emit(OpCodes.Volatile); prefix.Add(Instruction.Create(OpCodes.Ldfld, _enabled));
        prefix.Add(Instruction.Create(OpCodes.Stloc, bitmap)); prefix.Add(Instruction.Create(OpCodes.Ldloc, bitmap));
        prefix.Add(Instruction.Create(OpCodes.Brfalse, original)); prefix.Add(Instruction.Create(OpCodes.Ldsfld, _evaluating));
        prefix.Add(Instruction.Create(OpCodes.Brtrue, original)); prefix.Add(Instruction.Create(OpCodes.Ldloc, bitmap));
        Number(slot); Emit(OpCodes.Ldelem_U1); prefix.Add(Instruction.Create(OpCodes.Brfalse, original));
        for (int index = 0; index < saved.Length; index++)
        {
            var parameter = method.Parameters[index];
            if (parameter.IsOut) Default(prefix, saved[index].VariableType, saved[index]);
            else
            {
                prefix.Add(Instruction.Create(OpCodes.Ldarg, parameter));
                if (ByReference(parameter.ParameterType)) prefix.Add(Instruction.Create(OpCodes.Ldobj, saved[index].VariableType));
            }
            prefix.Add(Instruction.Create(OpCodes.Stloc, saved[index]));
        }
        prefix.Add(Instruction.Create(OpCodes.Ldsfld, _sites)); Number(slot);
        if (method.IsStatic || method.IsConstructor && method.DeclaringType.IsValueType || StackOnly(method.DeclaringType)) Emit(OpCodes.Ldnull);
        else
        {
            Emit(OpCodes.Ldarg_0);
            if (method.DeclaringType.IsValueType)
            {
                var self = SelfType(method.DeclaringType);
                prefix.Add(Instruction.Create(OpCodes.Ldobj, self)); prefix.Add(Instruction.Create(OpCodes.Box, self));
            }
        }
        Number(saved.Length); prefix.Add(Instruction.Create(OpCodes.Newarr, _module.TypeSystem.Object));
        for (int index = 0; index < saved.Length; index++)
        {
            Emit(OpCodes.Dup); Number(index);
            if (StackOnly(saved[index].VariableType)) { Number(index); prefix.Add(Instruction.Create(OpCodes.Call, _reference)); }
            else { prefix.Add(Instruction.Create(OpCodes.Ldloc, saved[index])); Box(prefix, saved[index].VariableType); }
            Emit(OpCodes.Stelem_Ref);
        }
        prefix.Add(Instruction.Create(OpCodes.Call, _enter)); prefix.Add(Instruction.Create(OpCodes.Stloc, frame));
        prefix.Add(Instruction.Create(OpCodes.Ldloc, frame)); prefix.Add(Instruction.Create(OpCodes.Brfalse, original));
        for (int index = 0; index < saved.Length; index++)
        {
            var parameter = method.Parameters[index];
            if (ByReference(parameter.ParameterType)) prefix.Add(Instruction.Create(OpCodes.Ldarg, parameter));
            if (StackOnly(saved[index].VariableType))
                SelectReference(prefix, method, frame, saved, saved[index].VariableType, index, saved[index]);
            else
            {
                prefix.Add(Instruction.Create(OpCodes.Ldloc, frame)); Number(index); prefix.Add(Instruction.Create(OpCodes.Callvirt, _argument));
                Unbox(prefix, saved[index].VariableType, boxed, saved[index]);
            }
            prefix.Add(ByReference(parameter.ParameterType) ? Instruction.Create(OpCodes.Stobj, saved[index].VariableType) : Instruction.Create(OpCodes.Starg, parameter));
        }
        prefix.Add(Instruction.Create(OpCodes.Ldloc, frame)); prefix.Add(Instruction.Create(OpCodes.Callvirt, _shouldSkip));
        prefix.Add(Instruction.Create(OpCodes.Brfalse, original));
        if (method.IsConstructor && !method.IsStatic && method.DeclaringType.IsValueType)
        { prefix.Add(Instruction.Create(OpCodes.Ldarg_0)); prefix.Add(Instruction.Create(OpCodes.Initobj, SelfType(method.DeclaringType))); }
        if (result is not null) LoadResult(prefix, method, frame, saved, result, boxed, originalValue: false);
        Complete(prefix, method, frame, saved, result, boxed);
        prefix.Add(original);
        foreach (var instruction in prefix) processor.InsertBefore(entry, instruction);

        foreach (var instruction in returns)
        {
            instruction.OpCode = result is null ? OpCodes.Nop : OpCodes.Stloc;
            instruction.Operand = result;
            List<Instruction> tail = [];
            var end = Instruction.Create(OpCodes.Nop);
            tail.Add(Instruction.Create(OpCodes.Ldloc, frame)); tail.Add(Instruction.Create(OpCodes.Brfalse, end));
            RefreshArguments(tail, method, frame, saved);
            tail.Add(Instruction.Create(OpCodes.Ldloc, frame));
            if (result is null) tail.Add(Instruction.Create(OpCodes.Ldnull));
            else if (ByReference(method.ReturnType) || StackOnly(method.ReturnType)) tail.Add(Instruction.Create(OpCodes.Call, _originalReturn));
            else { tail.Add(Instruction.Create(OpCodes.Ldloc, result)); Box(tail, method.ReturnType); }
            tail.Add(Instruction.Create(OpCodes.Callvirt, _complete));
            if (result is not null) LoadResult(tail, method, frame, saved, result, boxed, originalValue: true);
            tail.Add(end); if (result is not null) tail.Add(Instruction.Create(OpCodes.Ldloc, result)); tail.Add(Instruction.Create(OpCodes.Ret));
            var current = instruction;
            foreach (var added in tail) { processor.InsertAfter(current, added); current = added; }
        }
        foreach (var instruction in body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Tail)) instruction.OpCode = OpCodes.Nop;
        body.MaxStackSize = Math.Max(body.MaxStackSize, 16);
        body.OptimizeMacros();
    }

    private void Complete(List<Instruction> output, MethodDefinition method, VariableDefinition frame,
        VariableDefinition[] saved, VariableDefinition? result, VariableDefinition boxed)
    {
        RefreshArguments(output, method, frame, saved);
        output.Add(Instruction.Create(OpCodes.Ldloc, frame));
        if (result is null) output.Add(Instruction.Create(OpCodes.Ldnull));
        else if (ByReference(method.ReturnType) || StackOnly(method.ReturnType))
            output.Add(Instruction.Create(OpCodes.Call, _originalReturn));
        else { output.Add(Instruction.Create(OpCodes.Ldloc, result)); Box(output, method.ReturnType); }
        output.Add(Instruction.Create(OpCodes.Callvirt, _complete));
        if (result is not null)
        {
            LoadResult(output, method, frame, saved, result, boxed, originalValue: true);
            output.Add(Instruction.Create(OpCodes.Ldloc, result));
        }
        output.Add(Instruction.Create(OpCodes.Ret));
    }

    private void RefreshArguments(List<Instruction> output, MethodDefinition method, VariableDefinition frame, VariableDefinition[] saved)
    {
        foreach (var parameter in method.Parameters)
        {
            var type = ValueType(parameter.ParameterType);
            output.Add(Instruction.Create(OpCodes.Ldarg, parameter));
            if (ByReference(parameter.ParameterType)) output.Add(Instruction.Create(OpCodes.Ldobj, type));
            output.Add(Instruction.Create(OpCodes.Stloc, saved[parameter.Index]));
            output.Add(Instruction.Create(OpCodes.Ldloc, frame)); output.Add(Instruction.Create(OpCodes.Callvirt, _arguments));
            output.Add(Instruction.Create(OpCodes.Ldc_I4, parameter.Index));
            if (StackOnly(type))
            { output.Add(Instruction.Create(OpCodes.Ldc_I4, parameter.Index)); output.Add(Instruction.Create(OpCodes.Call, _reference)); }
            else
            {
                output.Add(Instruction.Create(OpCodes.Ldloc, saved[parameter.Index]));
                Box(output, type);
            }
            output.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }
    }

    private void LoadResult(List<Instruction> output, MethodDefinition method, VariableDefinition frame,
        VariableDefinition[] saved, VariableDefinition result, VariableDefinition boxed, bool originalValue)
    {
        if (ByReference(method.ReturnType) || StackOnly(method.ReturnType))
            SelectReference(output, method, frame, saved, method.ReturnType, -1, result, originalValue);
        else
        {
            output.Add(Instruction.Create(OpCodes.Ldloc, frame)); output.Add(Instruction.Create(OpCodes.Callvirt, _result));
            Unbox(output, method.ReturnType, boxed, result);
        }
        output.Add(Instruction.Create(OpCodes.Stloc, result));
    }

    private void SelectReference(List<Instruction> output, MethodDefinition method, VariableDefinition frame,
        VariableDefinition[] saved, TypeReference type, int argument, VariableDefinition fallback, bool originalValue = true)
    {
        var done = Instruction.Create(OpCodes.Nop);
        for (int index = 0; index < saved.Length; index++)
        {
            if (ShapeKey(type) != ShapeKey(ByReference(type) ? method.Parameters[index].ParameterType : saved[index].VariableType)) continue;
            var next = Instruction.Create(OpCodes.Nop);
            output.Add(Instruction.Create(OpCodes.Ldloc, frame));
            if (argument >= 0) output.Add(Instruction.Create(OpCodes.Ldc_I4, argument));
            output.Add(Instruction.Create(OpCodes.Callvirt, argument >= 0 ? _argumentSource : _returnSource));
            output.Add(Instruction.Create(OpCodes.Ldc_I4, index)); output.Add(Instruction.Create(OpCodes.Bne_Un, next));
            output.Add(ByReference(type) ? Instruction.Create(OpCodes.Ldarg, method.Parameters[index]) : Instruction.Create(OpCodes.Ldloc, saved[index]));
            output.Add(Instruction.Create(OpCodes.Br, done)); output.Add(next);
        }
        if (originalValue && !ByReference(type))
        {
            var retain = Instruction.Create(OpCodes.Nop);
            output.Add(Instruction.Create(OpCodes.Ldloc, frame));
            if (argument >= 0) output.Add(Instruction.Create(OpCodes.Ldc_I4, argument));
            output.Add(Instruction.Create(OpCodes.Callvirt, argument >= 0 ? _argumentSource : _returnSource));
            output.Add(Instruction.Create(OpCodes.Ldc_I4, -2)); output.Add(Instruction.Create(OpCodes.Bne_Un, retain));
            Default(output, type, fallback); output.Add(Instruction.Create(OpCodes.Br, done)); output.Add(retain);
            output.Add(Instruction.Create(OpCodes.Ldloc, fallback));
        }
        else if (originalValue) output.Add(Instruction.Create(OpCodes.Ldloc, fallback));
        else Default(output, type, fallback);
        output.Add(done);
    }

    private void Unbox(List<Instruction> output, TypeReference type, VariableDefinition boxed, VariableDefinition scratch)
    {
        type = Unmodified(type);
        output.Add(Instruction.Create(OpCodes.Stloc, boxed));
        var empty = Instruction.Create(OpCodes.Nop); var done = Instruction.Create(OpCodes.Nop);
        output.Add(Instruction.Create(OpCodes.Ldloc, boxed)); output.Add(Instruction.Create(OpCodes.Brfalse, empty));
        output.Add(Instruction.Create(OpCodes.Ldloc, boxed)); output.Add(Instruction.Create(OpCodes.Call, _default));
        output.Add(Instruction.Create(OpCodes.Beq, empty));
        output.Add(Instruction.Create(OpCodes.Ldloc, boxed));
        output.Add(Instruction.Create(type.IsValueType || type is GenericParameter ? OpCodes.Unbox_Any : OpCodes.Castclass, type));
        output.Add(Instruction.Create(OpCodes.Br, done)); output.Add(empty); Default(output, type, scratch); output.Add(done);
    }
    private static void Default(List<Instruction> output, TypeReference type, VariableDefinition scratch)
    {
        type = Unmodified(type);
        if (type.IsByReference || type is PointerType or FunctionPointerType)
        { output.Add(Instruction.Create(OpCodes.Ldc_I4_0)); output.Add(Instruction.Create(OpCodes.Conv_U)); }
        else if (!type.IsValueType && type is not GenericParameter) output.Add(Instruction.Create(OpCodes.Ldnull));
        else
        { output.Add(Instruction.Create(OpCodes.Ldloca, scratch)); output.Add(Instruction.Create(OpCodes.Initobj, type)); output.Add(Instruction.Create(OpCodes.Ldloc, scratch)); }
    }
    private static void Box(List<Instruction> output, TypeReference type)
    { type = Unmodified(type); if (type.IsValueType || type is GenericParameter) output.Add(Instruction.Create(OpCodes.Box, type)); }
    private static TypeReference SelfType(TypeDefinition type)
    {
        if (!type.HasGenericParameters) return type;
        var self = new GenericInstanceType(type); foreach (var parameter in type.GenericParameters) self.GenericArguments.Add(parameter); return self;
    }
}
