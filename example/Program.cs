using System.Runtime.CompilerServices;
using RefluxLibrary;

var reflux = new Reflux();

// reflux.Patch(typeof(MakeMePatch).GetMethod("ToPatchWith")!, prefix: ToPatchWith_Prefix_Another);
reflux.Patch(typeof(MakeMePatch).GetMethod("ToPatchWith")!, prefix: ToPatchWith_Prefix, postfix: ToPatchWith_Postfix, finalizer: ToPatchWith_Finalizer);

var patching = new MakeMePatch();
patching.ToPatchWith("OKKK");
MakeMePatch.SomeStatic();
Console.WriteLine(patching.Add(4, 2));


// static void ToPatchWith_Prefix_Another(ref string name, MakeMePatch __instance) 
// {
//     name = "Will do";
// }

static bool ToPatchWith_Prefix(ref string name, MakeMePatch __instance) 
{
    if (name == "OKKK")
    {
        return true;
    }
    Console.WriteLine(name);
    return false;
}

static void ToPatchWith_Postfix(ref string name, MakeMePatch __instance) 
{
    Console.WriteLine("Postfix called!");
}

static void ToPatchWith_Finalizer(Exception __exception) 
{

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