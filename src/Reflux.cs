using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;

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
        HookProcessor processor = new HookProcessor(original, [prefix], [postfix], [finalizer]);
        Hooks.Add(processor.Process());
    }

    public void Patch(
        MethodBase original,
        Delegate?[]? prefix = null,
        Delegate?[]? postfix = null,
        Delegate?[]? finalizer = null
    )
    {
        prefix ??= [];
        postfix ??= [];
        finalizer ??= [];
        HookProcessor processor = new HookProcessor(original, prefix, postfix, finalizer);
        Hooks.Add(processor.Process());
    }

    public static void Dump(MethodBase orig)
    {
        new MethodDiff(orig).PrintToConsole();
    }
}
