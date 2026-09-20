using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
class InspectVillager {
 static string dir = @"F:\SteamLibrary\steamapps\common\Broforce\Broforce_beta_Data\Managed";
 static Dictionary<short,OpCode> ops = new Dictionary<short,OpCode>();
 static void Main(string[] args) {
  foreach(FieldInfo f in typeof(OpCodes).GetFields()) if(f.FieldType==typeof(OpCode)) { OpCode o=(OpCode)f.GetValue(null); ops[o.Value]=o; }
  AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += delegate(object sender, ResolveEventArgs e) { string p=Path.Combine(dir,new AssemblyName(e.Name).Name+".dll"); return File.Exists(p)?Assembly.ReflectionOnlyLoadFrom(p):Assembly.ReflectionOnlyLoad(e.Name); };
  Assembly a=Assembly.ReflectionOnlyLoadFrom(Path.Combine(dir,"Assembly-CSharp.dll"));
  foreach(Type t in a.GetTypes()) {
   foreach(MethodInfo m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly)) {
    bool wanted=args.Length>0 ? (t.Name+"."+m.Name).Contains(args[0]) : (m.Name.Contains("SpawnVillager")||m.Name.Contains("ArmVillager")||m.Name.Contains("FreeVillager"));
    if(!wanted) continue;
    Console.WriteLine("\nMETHOD "+t.FullName+" :: "+m+" public="+m.IsPublic+" static="+m.IsStatic);
    foreach(ParameterInfo p in m.GetParameters()) Console.WriteLine("PARAM "+p.Position+" "+p.ParameterType+" "+p.Name);
    MethodBody b=m.GetMethodBody(); if(b==null)continue; byte[] il=b.GetILAsByteArray();
    for(int i=0;i<il.Length;) { int start=i; short key=il[i++]; if(key==254)key=(short)(0xfe00|il[i++]); OpCode o=ops[key]; object v=""; int size=0;
     switch(o.OperandType) {
      case OperandType.InlineNone: break;
      case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size=1;v=il[i];break;
      case OperandType.ShortInlineBrTarget: size=1;v="IL_"+(i+1+(sbyte)il[i]).ToString("x4");break;
      case OperandType.InlineVar:size=2;v=BitConverter.ToUInt16(il,i);break;
      case OperandType.InlineI:size=4;v=BitConverter.ToInt32(il,i);break;
      case OperandType.InlineBrTarget:size=4;v="IL_"+(i+4+BitConverter.ToInt32(il,i)).ToString("x4");break;
      case OperandType.ShortInlineR:size=4;v=BitConverter.ToSingle(il,i);break;
      case OperandType.InlineI8:size=8;v=BitConverter.ToInt64(il,i);break;
      case OperandType.InlineR:size=8;v=BitConverter.ToDouble(il,i);break;
      case OperandType.InlineSwitch: size=4+4*BitConverter.ToInt32(il,i);v="switch";break;
      default: size=4;int token=BitConverter.ToInt32(il,i);try{if(o.OperandType==OperandType.InlineString)v=m.Module.ResolveString(token);else {MemberInfo member=m.Module.ResolveMember(token);v=member.DeclaringType+" :: "+member;}}catch{v=token.ToString("x8");}break;
     }
     i+=size;Console.WriteLine("IL_"+start.ToString("x4")+" "+o.Name+" "+v);
    }
   }
  }
 }
}
