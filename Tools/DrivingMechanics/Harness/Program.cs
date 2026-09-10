using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using NfsMwRemaster.Driving.Tests;
using NUnit.Framework;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string game = null, output = null, project = null; bool selfTest = args.Length == 0; var vehicles = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string Next() { if (++i >= args.Length) throw new ArgumentException("Missing value for " + args[i - 1]); return args[i]; }
                switch (args[i])
                {
                    case "--self-test": selfTest = true; break;
                    case "--game": game = Next(); break;
                    case "--out": output = Next(); break;
                    case "--project": project = Next(); break;
                    case "--vehicle": vehicles.Add(Next()); break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (selfTest)
            {
                int failures = 0, passed = 0; var instance = new MostWantedHandlingReaderTests();
                foreach (var method in instance.GetType().GetMethods().Where(method => method.IsDefined(typeof(TestAttribute), false)
                    && !method.IsDefined(typeof(ExplicitAttribute), false)).OrderBy(method => method.Name))
                {
                    try { method.Invoke(instance, null); passed++; Console.WriteLine("PASS " + method.Name); }
                    catch (TargetInvocationException error) { failures++; Console.Error.WriteLine("FAIL " + method.Name + "\n" + error.InnerException); }
                }
                Console.WriteLine("Synthetic reader tests: " + passed + " passed, " + failures + " failed. No Unity process or game execution.");
                if (failures != 0) return 1;
            }
            if (game == null)
            { if (!selfTest) throw new ArgumentException("Supply --self-test or --game, --out and --project."); return 0; }
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(project)) throw new ArgumentException("Decoding requires --out and --project.");
            string destination = Path.GetFullPath(output); string root = Path.GetFullPath(project);
            string evidence = Path.Combine(root, "Tools", "DrivingMechanics", "Evidence") + Path.DirectorySeparatorChar;
            string temporary = Path.Combine(root, "Library", "DrivingMechanics") + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(evidence, StringComparison.Ordinal) && !destination.StartsWith(temporary, StringComparison.Ordinal))
                throw new ArgumentException("Evidence output must be under this project's Tools/DrivingMechanics/Evidence or Library/DrivingMechanics.");
            if (destination.StartsWith(Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Never write inside the source installation.");
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(destination)); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Output ancestry contains a symbolic link; refusing ambiguous destination.");
            if (File.Exists(destination)) throw new IOException("Evidence already exists and will not be overwritten: " + destination);
            var report = MostWantedHandlingReader.Decode(game, vehicles.Count == 0 ? null : vehicles.ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
            Console.WriteLine("HANDLING_EVIDENCE vehicles=" + report.vehicles.Count + " records=" + report.records.Count + " issues=" + report.issues.Count + " output=" + destination);
            foreach (var source in report.sources) Console.WriteLine(source.relativePath + " SHA256=" + source.sha256 + " unchanged=" + source.unchanged);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
