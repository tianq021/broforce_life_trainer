using System;
using System.IO;
using System.Reflection;
class InspectAiTypes {
 static void Main(string[] args) {
  string dir=@"F:\SteamLibrary\steamapps\common\Broforce\Broforce_beta_Data\Managed";
  AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += delegate(object s, ResolveEventArgs e) { return Assembly.ReflectionOnlyLoadFrom(Path.Combine(dir,new AssemblyName(e.Name).Name+".dll")); };
  Assembly a=Assembly.ReflectionOnlyLoadFrom(Path.Combine(dir,"Assembly-CSharp.dll"));
  foreach(string name in args) {
   Type t=a.GetType(name); Console.WriteLine("TYPE "+t+" base="+t.BaseType);
   foreach(FieldInfo f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static)) Console.WriteLine("FIELD "+f+" public="+f.IsPublic);
   foreach(MethodInfo m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)) Console.WriteLine("METHOD "+m+" public="+m.IsPublic+" virtual="+m.IsVirtual+" final="+m.IsFinal);
  }
 }
}
