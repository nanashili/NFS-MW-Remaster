using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;

internal static class VisualOffsets
{
    public static void Main()
    {
        var db = new MostWantedAudioDatabase();
        const string root = "/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted";
        foreach (var path in new[] { "GLOBAL/attributes.bin", "GLOBAL/FE_ATTRIB.bin", "GLOBAL/gameplay.bin" })
            db.LoadSource(File.ReadAllBytes(Path.Combine(root, path)), path, new[] { "ecar", "pvehicle" });
        var labels = new[] { "CollectionName", "MODEL", "TireOffsets", "ExtraRearTireOffset", "KitWheelOffsetFront", "KitWheelOffsetRear", "FrontCamber", "RearCamber", "TireSkidWidth", "WheelSpokeCount", "RideHeight" };
        var rows = db.RowsOf("ecar").Select(row => new {
            key = row.Key.ToString("x8"),
            fields = db.Resolve(row).Where(pair => labels.Any(name => MostWantedAudioDatabase.Hash(name) == pair.Key))
                .Select(pair => new {
                    name = labels.First(name => MostWantedAudioDatabase.Hash(name) == pair.Key),
                    type = pair.Value.Definition.Type.ToString("x8"),
                    vector4 = pair.Value.Definition.Type == MostWantedAudioDatabase.Hash("Attrib::Types::Vector4") && pair.Value.Definition.Size == 16
                        ? pair.Value.Items().Select(v => Enumerable.Range(0,4).Select(i => BitConverter.ToSingle(v.RawBytes(16),i*4)).ToArray()).ToArray() : null,
                    values = pair.Value.Items().Select(MostWantedHandlingReader.Decode).ToArray()
                }).ToArray()
        }).ToArray();
        File.WriteAllText("Art/Cars/visual-attributes.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        Console.WriteLine("Recovered " + rows.Length + " visual attribute records.");
    }
}
