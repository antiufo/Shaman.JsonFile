#if STANDALONE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    internal static class DynamicAnonymousType
    {


        public static Type CreateAnonymousType(IEnumerable<KeyValuePair<string, Type>> defs)
        {
            var name = "DynamicAnonymousType<" + string.Join(",", defs.Select(x => x.Key + ":" + x.Value.FullName)) + ">";

            var asmname = new System.Reflection.AssemblyName("_DynamicAnonymousTypes");
#if CORECLR
            var asm = AssemblyBuilder.DefineDynamicAssembly(asmname, AssemblyBuilderAccess.Run);
#else
            var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(asmname, System.Reflection.Emit.AssemblyBuilderAccess.Run);
#endif
            var module = asm.DefineDynamicModule("_DynamicAnonymousTypes");

            if (name.Length >= 1024) name = "DynamicLongHashedAnonymousType<" + (name.GetHashCode().ToString() + ("x" + name).GetHashCode()).Replace("-", string.Empty) + ">";
            var builder = module.DefineType(name, TypeAttributes.Public);

            var i = 0;
            var fields = new List<FieldBuilder>();
            foreach (var item in defs)
            {
                fields.Add(builder.DefineField(item.Key, item.Value, System.Reflection.FieldAttributes.Public));
                i++;
            }

            var emptyconstr = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new Type[] { });
            var il = emptyconstr.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(new Type[] { }));
            il.Emit(OpCodes.Ret);


            var constr = builder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, fields.Select(x => x.FieldType).ToArray());
            il = constr.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(new Type[] { }));

            i = 1;
            foreach (var field in fields)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Stfld, field);
                i++;
            }
            il.Emit(OpCodes.Ret);
            return builder.AsType();
        }
    }
}

#endif