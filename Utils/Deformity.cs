using System.Reflection;
using System.Reflection.Emit;

namespace ReciteHelper.Utils;

public static class Deformity
{
    public static void HorribleMethod()
    {
        // Build builders
        var asmName = new AssemblyName("InvalidCode");
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(
            asmName, AssemblyBuilderAccess.RunAndCollect);
        var modBuilder = asmBuilder.DefineDynamicModule("Main");

        var typeBuilder = modBuilder.DefineType(
            "InvalidMethodHolder",
            TypeAttributes.Public);
       var methodBuilder = typeBuilder.DefineMethod(
            "InvalidMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);

        // Generate invalid IL code
        ILGenerator il = methodBuilder.GetILGenerator();

        // A legal but dangerous sequence of instructions
        // makes dnSpy's stack trace logic confusing.
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br_S, (byte)0);
        il.Emit(OpCodes.Newobj,
            typeof(object).GetConstructor(Type.EmptyTypes));
        il.Emit(OpCodes.Ldloc, 999);

        il.MarkLabel(il.DefineLabel());
        il.Emit(OpCodes.Ret);

        // Completion type
        typeBuilder.CreateType();
    }

    public class InvalidMethodHolder { }
}
