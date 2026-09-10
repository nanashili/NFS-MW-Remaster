using System;
using System.IO;
using System.Reflection;
class DecodeHuff {
  static int Main(string[] args) {
    if(args.Length!=2) throw new Exception("Usage: DecodeHuff Common.dll texture-cache-directory");
    var method=Assembly.LoadFrom(args[0]).GetType("Common.Compression").GetMethod("Decompress");
    int count=0;
    // Only process the texture allowlist created by stage_huff.py. Front-end
    // package HUFF variants are incompatible with this legacy native decoder.
    foreach(var name in File.ReadAllLines(Path.Combine(args[1],"texture-streams.txt"))) {
      if(name.Length!=69 || Path.GetFileName(name)!=name || !name.EndsWith(".huff")) throw new Exception("Invalid texture cache name");
      var path=Path.Combine(args[1],name);
      byte[] input=File.ReadAllBytes(path);
      if(input.Length<16 || System.Text.Encoding.ASCII.GetString(input,0,4)!="HUFF") throw new Exception("Invalid HUFF header");
      int length=BitConverter.ToInt32(input,8);
      if(length<=0 || length>67108864) throw new Exception("Invalid output size");
      byte[] output=new byte[length];
      method.Invoke(null,new object[]{input,output});
      File.WriteAllBytes(Path.ChangeExtension(path,"raw"),output);
      count++;
    }
    Console.WriteLine("Decoded "+count+" HUFF blocks using existing Common.Compression");
    return 0;
  }
}
