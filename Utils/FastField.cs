using System;
using System.Reflection;
using System.Reflection.Emit;

namespace MTFVoiceTools.Utils;

public static class FastField<TTarget, TField>
{
    public static Func<TTarget, TField> CreateGetter(string fieldName)
    {
        var fi = typeof(TTarget).GetField(fieldName,
                     BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                 ?? throw new MissingFieldException(typeof(TTarget).FullName, fieldName);

        var dm = new DynamicMethod(
            name: "get_" + fieldName,
            returnType: typeof(TField),
            parameterTypes: new[] { typeof(TTarget) },
            owner: typeof(TTarget),               // owner (helps access checks)
            skipVisibility: true              // allow non-public
        );

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);            // load target
        il.Emit(OpCodes.Ldfld, fi);          // load field
        il.Emit(OpCodes.Ret);

        return (Func<TTarget, TField>)dm.CreateDelegate(typeof(Func<TTarget, TField>));
    }

    public static Action<TTarget, TField> CreateSetter(string fieldName)
    {
        var fi = typeof(TTarget).GetField(fieldName,
                     BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                 ?? throw new MissingFieldException(typeof(TTarget).FullName, fieldName);

        var dm = new DynamicMethod(
            name: "set_" + fieldName,
            returnType: typeof(void),
            parameterTypes: new[] { typeof(TTarget), typeof(TField) },
            owner: typeof(TTarget),
            skipVisibility: true
        );

        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);            // target
        il.Emit(OpCodes.Ldarg_1);            // value
        il.Emit(OpCodes.Stfld, fi);          // store field
        il.Emit(OpCodes.Ret);

        return (Action<TTarget, TField>)dm.CreateDelegate(typeof(Action<TTarget, TField>));
    }
}