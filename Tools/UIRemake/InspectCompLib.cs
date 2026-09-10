using System;
using System.Reflection;
class InspectCompLib {
  static void Main(string[] args) {
    foreach(var a in args) foreach(var t in Assembly.LoadFrom(a).GetExportedTypes()) {
      Console.WriteLine(t.FullName);
      foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly)) { Console.WriteLine("  "+m); foreach(var p in m.GetParameters()) Console.WriteLine("    "+p.Name+" "+p.ParameterType); }
    }
  }
}
