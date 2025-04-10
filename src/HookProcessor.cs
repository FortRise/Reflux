using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace RefluxLibrary;

public class HookProcessor 
{
    private MethodBase original;
    private ILCursor cursor = null!;
    private Delegate?[] prefixes;
    private Delegate?[] postfixes;
    private Delegate?[] finalizers;

    private Dictionary<string, VariableDefinition> variables = new Dictionary<string, VariableDefinition>();

    private Dictionary<string, ParameterIdentity> parameters = new Dictionary<string, ParameterIdentity>();

    public HookProcessor(MethodBase orig, Delegate?[] prefixes, Delegate?[] postfixes, Delegate?[] finalizers)
    {
        original = orig;
        this.prefixes = prefixes;
        this.postfixes = postfixes;
        this.finalizers = finalizers;

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

        if (prefixes.Length > 0)
        {
            WritePrefix(retLabel);
        }

        bool needLoad = false;

        if (needLoad = postfixes.Length > 0)
        {
            WritePostfix();
        }

        // reset position
        cursor.GotoLabel(label);

        if (finalizers.Length > 0)
        {
            WriteFinalizer(needLoad);
        }
    }

    private void WritePrefix(ILLabel retLabel)
    {
        foreach (var prefix in prefixes)
        {
            if (prefix is null)
            {
                continue;
            }
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
    }

    private void WritePostfix()
    {
        cursor.Index = cursor.Instrs.Count - 1;
        var info = (original as MethodInfo)!;

        if (info.ReturnType == typeof(void))
        {
            foreach (var postfix in postfixes)
            {
                if (postfix is null)
                {
                    continue;
                }

                var parameters = CompareAndBuildParameter(postfix.Method);
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
            }

            return;
        }

        ref var retVal = ref CollectionsMarshal.GetValueRefOrAddDefault(variables, "retVal", out bool exists)!;

        if (!exists)
        {
            var type = cursor.Context.Import(info.ReturnType);
            retVal = new VariableDefinition(type);
            cursor.IL.Body.Variables.Add(retVal);
            variables["retVal"] = retVal;
        }

        // store
        cursor.Emit(OpCodes.Stloc, retVal);

        foreach (var postfix in postfixes)
        {
            if (postfix is null)
            {
                continue;
            }

            var parameters = CompareAndBuildParameter(postfix.Method);

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
    }

    private void WriteFinalizer(bool needLoad)
    {
        var type = cursor.Context.Import(typeof(Exception));
        var cException = new VariableDefinition(type);
        cursor.IL.Body.Variables.Add(cException);

        var exceptionHandler = new ExceptionHandler(ExceptionHandlerType.Catch);
        cursor.IL.Body.ExceptionHandlers.Insert(0, exceptionHandler);

        exceptionHandler.CatchType = type;
        cursor.EmitNop();
        exceptionHandler.TryStart = cursor.Next;

        cursor.Index = cursor.Instrs.Count - 1;


        VariableDefinition retVal = null!;

        var info = (original as MethodInfo)!;
        if (info.ReturnType != typeof(void))
        {
            ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(variables, "retVal", out bool exists)!;

            if (!exists)
            {
                var rtype = cursor.Context.Import(info.ReturnType);
                val = new VariableDefinition(rtype);
                cursor.IL.Body.Variables.Add(retVal);
                variables["retVal"] = retVal;
            }

            retVal = val;

            if (needLoad)
            {
                cursor.Emit(OpCodes.Ldloc, retVal);
            }

            cursor.Emit(OpCodes.Stloc, retVal);
        }

        EmitFinalizerCall();

        var leaveBlock = cursor.DefineLabel();

        cursor.Emit(OpCodes.Leave, leaveBlock);
        cursor.Emit(OpCodes.Stloc, cException);
        exceptionHandler.TryEnd = cursor.Prev;
        exceptionHandler.HandlerStart = cursor.Prev;

        EmitFinalizerCall();

        cursor.Emit(OpCodes.Leave, leaveBlock);

        if (retVal != null)
        {
            cursor.Emit(OpCodes.Ldloc, retVal);
        }
        else 
        {
            cursor.Emit(OpCodes.Nop);
        }

        exceptionHandler.HandlerEnd = cursor.Prev;
        leaveBlock.Target = cursor.Prev;

        void EmitFinalizerCall()
        {
            foreach (var finalizer in finalizers)
            {
                if (finalizer is null)
                {
                    continue;
                }
                if (finalizer.Method.ReturnType == typeof(void))
                {
                    var parameters = CompareAndBuildParameter(finalizer.Method);
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        var p = parameters[i];
                        if (p.Name == "__result" && retVal != null)
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
                        else if (p.Name == "__exception")
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

                    cursor.Emit(OpCodes.Call, finalizer.Method);
                    var throwLabel = cursor.DefineLabel();
                    cursor.Emit(OpCodes.Ldloc, cException);
                    cursor.Emit(OpCodes.Brfalse, throwLabel);
                    cursor.Emit(OpCodes.Ldloc, cException);
                    cursor.Emit(OpCodes.Throw);
                    cursor.MarkLabel(throwLabel);
                }
                else 
                {
                    var parameters = CompareAndBuildParameter(finalizer.Method);

                    for (int i = 0; i < parameters.Count; i++)
                    {
                        var p = parameters[i];
                        if (p.Name == "__result" && retVal != null)
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

                    // call the method and store the exception
                    cursor.Emit(OpCodes.Call, finalizer.Method);
                    cursor.Emit(OpCodes.Stloc, cException);
                    // check if the exception is null
                    var throwLabel = cursor.DefineLabel();
                    cursor.Emit(OpCodes.Ldloc, cException);
                    cursor.Emit(OpCodes.Brfalse, throwLabel);
                    cursor.Emit(OpCodes.Ldloc, cException);
                    cursor.Emit(OpCodes.Throw);
                    cursor.MarkLabel(throwLabel);
                }
            }
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