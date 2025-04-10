using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;

namespace RefluxLibrary;

public class Reflux : IDisposable
{
    public List<ILHook> Hooks { get; } = new List<ILHook>();

    public void Dispose()
    {
        foreach (var hook in Hooks)
        {
            hook.Dispose();
        }
    }

    public void Patch(
        MethodBase original,
        Delegate? prefix = null,
        Delegate? postfix = null,
        Delegate? finalizer = null
    )
    {
        HookProcessor processor = new HookProcessor(original, prefix, postfix, finalizer);
        Hooks.Add(processor.Process());
    }
}

public class HookProcessor 
{
    private MethodBase original;
    private ILCursor cursor = null!;
    private Delegate? prefix;
    private Delegate? postfix;
    private Delegate? finalizer;

    private Dictionary<string, ParameterIdentity> parameters = new Dictionary<string, ParameterIdentity>();

    public HookProcessor(MethodBase orig, Delegate? prefix, Delegate? postfix, Delegate? finalizer)
    {
        original = orig;
        this.prefix = prefix;
        this.postfix = postfix;
        this.finalizer = finalizer;

        var parameters = original.GetParameters();
        bool isStatic = original.IsStatic;
        for (int i = 0; i < parameters.Length; i++)
        {
            string name = parameters[i].Name!;
            int index = i;
            if (!isStatic)
            {
                index += 1;
            }
            this.parameters.Add(name, new ParameterIdentity(name, index));
        }
    }

    public ILHook Process()
    {
        return new ILHook(original, InitManipulator);
    }

    private void InitManipulator(ILContext ctx)
    {
        cursor = new ILCursor(ctx);

        var label = cursor.MarkLabel();

        cursor.GotoNext(MoveType.Before, instr => instr.MatchRet());
        var retLabel = cursor.MarkLabel();
        cursor.Goto(0);

        if (prefix != null)
        {
            WritePrefix(prefix, retLabel);
        }

        if (postfix != null)
        {
            WritePostfix(postfix);
        }

        // reset position
        cursor.GotoLabel(label);

        if (finalizer != null)
        {
            WriteFinalizer(finalizer, retLabel);
        }
    }

    private void WritePrefix(Delegate prefix, ILLabel retLabel)
    {
        bool canCallOriginalOrNot = prefix.Method.ReturnType == typeof(bool);
        VariableDefinition? exists = null;

        if (canCallOriginalOrNot)
        {
            var type = cursor.Context.Import(typeof(bool));
            exists = new VariableDefinition(type);
            cursor.IL.Body.Variables.Add(exists);
        }

        var parameters = CompareAndBuildParameter(prefix.Method);
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Name == "__result")
            {
                throw new Exception("__result is not supported in 'prefix' operation.");
            }
            else 
            {
                if (p.ByRef)
                {
                    cursor.Emit(OpCodes.Ldarga, (ushort)p.Index);
                }
                else 
                {
                    cursor.Emit(OpCodes.Ldarg, (ushort)p.Index);
                }
            }
        }

        cursor.Emit(OpCodes.Call, prefix.Method);

        if (exists != null)
        {
            cursor.Emit(OpCodes.Stloc, exists);
            if (retLabel != null)
            {
                cursor.Emit(OpCodes.Ldloc, exists);
                cursor.Emit(OpCodes.Brfalse, retLabel);
            }
        }
    }

    private void WritePostfix(Delegate postfix)
    {
        cursor.Index = cursor.Instrs.Count - 1;
        var parameters = CompareAndBuildParameter(postfix.Method);

        var info = (original as MethodInfo)!;
        if (info.ReturnType == typeof(void))
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (p.Name == "__result")
                {
                    throw new Exception("__result is not supported in 'postfix' without a return type.");
                }
                cursor.Emit(OpCodes.Ldarg, (ushort)p.Index);
            }

            cursor.Emit(OpCodes.Call, postfix.Method);
            return;
        }
        var type = cursor.Context.Import(info.ReturnType);
        var retVal = new VariableDefinition(type);
        cursor.IL.Body.Variables.Add(retVal);

        // store
        cursor.Emit(OpCodes.Stloc, retVal);

        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (p.Name == "__result")
            {
                if (p.ByRef)
                {
                    cursor.Emit(OpCodes.Ldloca, retVal);
                }
                else 
                {
                    cursor.Emit(OpCodes.Ldloc, retVal);
                }
            }
            else 
            {
                if (p.ByRef)
                {
                    cursor.Emit(OpCodes.Ldarga, (ushort)p.Index);
                }
                else 
                {
                    cursor.Emit(OpCodes.Ldarg, (ushort)p.Index);
                }
            }
        }

        cursor.Emit(OpCodes.Call, postfix.Method);
    }

    private void WriteFinalizer(Delegate postfix, ILLabel retLabel)
    {
        var parameters = CompareAndBuildParameter(postfix.Method);

        var type = cursor.Context.Import(typeof(Exception));
        var cException = new VariableDefinition(type);
        cursor.IL.Body.Variables.Add(cException);

        var exceptionHandler = new ExceptionHandler(ExceptionHandlerType.Catch);
        cursor.IL.Body.ExceptionHandlers.Insert(0, exceptionHandler);

        exceptionHandler.CatchType = cursor.IL.Import(typeof(Exception));
        cursor.EmitNop();
        exceptionHandler.TryStart = cursor.Next;

        cursor.Index = cursor.Instrs.Count - 1;

        var leaveBlock = cursor.DefineLabel();

        VariableDefinition? retVal = null;

        if (postfix.Method.ReturnType == typeof(void))
        {
            cursor.Emit(OpCodes.Leave, leaveBlock);
            cursor.Emit(OpCodes.Stloc, cException);
            exceptionHandler.TryEnd = cursor.Prev;
            exceptionHandler.HandlerStart = cursor.Prev;

            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (p.Name == "__result")
                {
                    throw new Exception("__result is not supported in 'postfix' without a return type.");
                }
                if (p.Name == "__exception")
                {
                    if (p.ByRef)
                    {
                        cursor.Emit(OpCodes.Ldloca, cException);
                    }
                    else 
                    {
                        cursor.Emit(OpCodes.Ldloc, cException);
                    }
                }
                cursor.Emit(OpCodes.Ldarg, (ushort)p.Index);
            }

            cursor.Emit(OpCodes.Call, postfix.Method);
        }
        else 
        {
            var info = (original as MethodInfo)!;
            var rtype = cursor.Context.Import(info.ReturnType);
            retVal = new VariableDefinition(rtype);
            cursor.IL.Body.Variables.Add(retVal);

            cursor.Emit(OpCodes.Leave, leaveBlock);
            cursor.Emit(OpCodes.Stloc, cException);
            exceptionHandler.TryEnd = cursor.Prev;
            exceptionHandler.HandlerStart = cursor.Prev;

            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (p.Name == "__result")
                {
                    if (p.ByRef)
                    {
                        cursor.Emit(OpCodes.Ldloca, retVal);
                    }
                    else 
                    {
                        cursor.Emit(OpCodes.Ldloc, retVal);
                    }
                }
                if (p.Name == "__exception")
                {
                    if (p.ByRef)
                    {
                        cursor.Emit(OpCodes.Ldloca, cException);
                    }
                    else 
                    {
                        cursor.Emit(OpCodes.Ldloc, cException);
                    }
                }
                else 
                {
                    if (p.ByRef)
                    {
                        cursor.Emit(OpCodes.Ldarga, (ushort)p.Index);
                    }
                    else 
                    {
                        cursor.Emit(OpCodes.Ldarg, (ushort)p.Index);
                    }
                }
            }
        }

        cursor.Emit(OpCodes.Call, postfix.Method);
        cursor.Emit(OpCodes.Leave, leaveBlock);
        cursor.Emit(OpCodes.Nop);
        exceptionHandler.HandlerEnd = cursor.Prev;
        leaveBlock.Target = cursor.Prev;

        if (retVal != null)
        {
            cursor.Emit(OpCodes.Ldloc, retVal);
        }
    }

    private List<ParameterArgument> CompareAndBuildParameter(MethodInfo info)
    {
        var list = new List<ParameterArgument>();
        var parameters = info.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            string name = p.Name!;
            bool byRef = p.ParameterType.IsByRef;
            if (this.parameters.TryGetValue(parameters[i].Name!, out ParameterIdentity val))
            {
                list.Add(new ParameterArgument(name, val.Index, byRef));
                continue;
            }

            if (name == "__instance")
            {
                list.Add(new ParameterArgument("__instance", 0, byRef));
                continue;
            }

            if (name == "__result")
            {
                list.Add(new ParameterArgument("__result", 0, byRef));
                continue;
            }

            if (name == "__exception")
            {
                list.Add(new ParameterArgument("__exception", 0, byRef));
                continue;
            }

            throw new Exception($"Parameter name: '{parameters[i].Name}' does not exists on method '{original.Name}'");
        }

        return list;
    }

    private record struct ParameterArgument(string Name, int Index, bool ByRef);
    private record struct ParameterIdentity(string Name, int Index);
}