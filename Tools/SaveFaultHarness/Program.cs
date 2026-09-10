using System;
using System.Diagnostics;
using System.IO;
using NfsMwRemaster.Driving;
using Newtonsoft.Json;

internal static class Program
{
    private static string Profile(int cash)
    {
        // Construct DTO fields only: no Unity engine calls are needed by the backend.
        var profile = new CareerProfileData { profileId = "career", playerName = "Kill test", activeVehicleId = "player_vehicle" };
        profile.wallet.balance = cash;
        return JsonConvert.SerializeObject(profile);
    }
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "bench") return Benchmark();
        if (args.Length > 0 && args[0] == "child")
        {
            string directory = args[1]; int target = int.Parse(args[2]); int cash = int.Parse(args[3]);
            var repository = new CareerSaveRepository(directory, 3, stage =>
            {
                if ((int)stage != target) return;
                Console.WriteLine("READY"); Console.Out.Flush(); Console.ReadLine();
            });
            var old = repository.Load("career"); repository.Commit("career", Profile(cash), old.HeadStamp);
            return 0;
        }
        int iterations = args.Length == 0 ? 180 : int.Parse(args[0]);
        if (iterations < 1 || iterations > 10000) throw new ArgumentOutOfRangeException(nameof(iterations));
        string root = Path.Combine(Path.GetTempPath(), "nfs-save-kill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Isolated evidence directory: " + root);
        var reader = new CareerSaveRepository(root); reader.Commit("career", Profile(0), null);
        int expectedCash = 0;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            int stage = i % 9;
            var start = new ProcessStartInfo(Environment.ProcessPath)
            { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("child"); start.ArgumentList.Add(root); start.ArgumentList.Add(stage.ToString());
            start.ArgumentList.Add((expectedCash + 1).ToString());
            using (var process = Process.Start(start))
            {
                var ready = process.StandardOutput.ReadLineAsync();
                if (!ready.Wait(10000) || ready.Result != "READY")
                {
                    if (!process.HasExited) process.Kill(true);
                    throw new Exception("Child failed before checkpoint: " + process.StandardError.ReadToEnd());
                }
                process.Kill(true); process.WaitForExit();
            }
            if (stage >= (int)SaveWriteStage.Promoted) expectedCash++;
            var loaded = reader.Load("career");
            int cash = (int)CareerSaveCodec.Parse(loaded.Payload)["wallet"]["balance"];
            if (cash != expectedCash) throw new Exception("Incoherent state after kill at " + (SaveWriteStage)stage);
        }
        Console.WriteLine("PASS: " + iterations + " process kills across all 9 checkpoints in " + watch.Elapsed.TotalSeconds.ToString("F2") + "s");
        Console.WriteLine("Evidence retained. Pending files are uncommitted and never auto-promoted.");
        return 0;
    }

    private static int Benchmark()
    {
        var profile = new CareerProfileData { profileId = "career", playerName = "Large-save benchmark", activeVehicleId = "player_vehicle" };
        for (int i = 0; i < 10000; i++)
        {
            var vehicle = new CareerVehicleData { vehicleId = "vehicle_" + i };
            for (int j = 0; j < 10; j++) vehicle.performanceUpgradeIds.Add("upgrade_" + j);
            profile.vehicles.Add(vehicle);
        }
        var clock = Stopwatch.StartNew(); string payload = JsonConvert.SerializeObject(profile);
        double serialize = clock.Elapsed.TotalMilliseconds;
        string root = Path.Combine(Path.GetTempPath(), "nfs-save-bench-" + Guid.NewGuid().ToString("N"));
        var repository = new CareerSaveRepository(root); SaveReadResult previous = null;
        long allocated = GC.GetTotalAllocatedBytes(true);
        double maximum = 0, total = 0;
        for (int i = 0; i < 20; i++)
        {
            profile.wallet.balance = i;
            payload = JsonConvert.SerializeObject(profile);
            clock.Restart(); previous = repository.Commit("career", payload, previous?.HeadStamp);
            maximum = Math.Max(maximum, clock.Elapsed.TotalMilliseconds); total += clock.Elapsed.TotalMilliseconds;
            if (repository.Load("career").Payload != payload) throw new Exception("Large-save round trip changed state.");
        }
        Console.WriteLine("PASS: 10000 vehicles / 100000 installed upgrade IDs; " + CareerSaveCodec.Utf8.GetByteCount(payload) + " bytes");
        Console.WriteLine(".NET serialization " + serialize.ToString("F2") + "ms; storage mean " + (total / 20).ToString("F2")
            + "ms, max " + maximum.ToString("F2") + "ms; managed allocations across 20 commits + loads "
            + (GC.GetTotalAllocatedBytes(true) - allocated) + " bytes (not peak memory)");
        Console.WriteLine("Evidence: " + root + "; this is not a Unity main-thread snapshot or Windows benchmark.");
        return 0;
    }
}
