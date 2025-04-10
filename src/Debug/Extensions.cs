// https://github.com/JaThePlayer/CelesteMappingUtils/blob/main/Extensions.cs
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace RefluxLibrary;

internal static partial class Extensions
{
    static void AppendLabel(StringBuilder builder, Instruction instruction)
    {
        builder.Append("IL_");
        builder.Append(instruction.Offset.ToString("x4"));
    }


    public static string FixedToString(this Instruction self, bool printOffsets = true)
    {
        var operand = self.Operand;
        var opcode = self.OpCode;

        var instruction = new StringBuilder();

        if (printOffsets)
        {
            AppendLabel(instruction, self);
            instruction.Append(':');
            instruction.Append(' ');
        }
        instruction.Append(opcode.Name);

        if (operand == null)
            return instruction.ToString();

        instruction.Append(' ');

        switch (opcode.OperandType)
        {
        case OperandType.ShortInlineBrTarget:
        case OperandType.InlineBrTarget:
            //AppendLabel(instruction, (Instruction)operand);
            AppendLabel(instruction, operand switch
            {
                Instruction instr => instr,
                ILLabel label => label.Target!
            });
            break;
        case OperandType.InlineSwitch:
            var labels = (Instruction[])operand;
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0)
                    instruction.Append(',');

                AppendLabel(instruction, labels[i]);
            }
            break;
        case OperandType.InlineString:
            instruction.Append('\"');
            instruction.Append(operand);
            instruction.Append('\"');
            break;
        case OperandType.InlineMethod:
            // fix issues with instructions not being compared correctly due to things like this:
            //(newobj System.Void System.Nullable`1<Microsoft.Xna.Framework.Rectangle>::.ctor(T), newobj System.Void System.Nullable`1<Microsoft.Xna.Framework.Rectangle>::.ctor(!0))
            var s = operand.ToString()!;
            var sFixed = s.Contains('!') ? FixGenericNamesRegex().Replace(s, (m) =>
            {
                var number = int.Parse(m.ValueSpan[1..]);
                if (number == 0)
                    return "T";
                return $"T{number}";
            }) : s;

            //if (s != sFixed)
            //    Console.WriteLine($"Fixed generic name: {s} -> {sFixed}");

            instruction.Append(sFixed);
            break;
        default:
            instruction.Append(operand);
            break;
        }

        return instruction.ToString();
    }


    [GeneratedRegex(@"!\d+")]
    private static partial Regex FixGenericNamesRegex();
}