using System.Runtime.CompilerServices;
using RefluxLibrary;

var reflux = new Reflux();

reflux.Patch(
    typeof(MakeMePatch).GetMethod("Add")!,
    prefix: [Add_Prefix],
    postfix: [Add_Postfix],
    finalizer: [Add_Finalizer]
);

var patching = new MakeMePatch();
patching.ToPatchWith("OKKK");
MakeMePatch.SomeStatic();
Console.WriteLine(patching.Add(4, 2));

Reflux.Dump(typeof(MakeMePatch).GetMethod("Add")!);

static void Add_Prefix(int a, int b)
{
    Console.WriteLine($"{a} + {b}");
}

static void Add_Postfix(in int __result)
{
    Console.WriteLine($"The results are: {__result}");
}

static Exception? Add_Finalizer(ref int __result, ref Exception __exception)
{
    if (__exception == null)
    {
        Console.WriteLine("There were no errors in this results.");
    }
    return __exception;
}


public class MakeMePatch 
{
    private int hello;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ToPatchWith(string name)
    {
        hello = 5;
        Console.WriteLine(hello);
        Console.WriteLine(name + "ORIG");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Add(int a, int b)
    {
        return (a + b);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SomeStatic() 
    {
        Console.WriteLine("Im static");
    }
}