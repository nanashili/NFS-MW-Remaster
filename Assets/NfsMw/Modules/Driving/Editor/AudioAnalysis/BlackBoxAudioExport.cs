using System;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.AudioAnalysis
{
    public static class BlackBoxAudioExport
    {
        public static string ExportRecordings(string parent, BlackBoxDocument[] documents, CancellationToken cancel = default)
        {
            if (documents == null || !documents.Any(d => d.Report.Recordings.Any(r => r.Decoded && r.Pcm != null)))
                throw new InvalidOperationException("No decoded recordings to export.");
            string folder = Path.Combine(parent, "Audio-recordings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllLines(Path.Combine(folder, "source-inventory.txt"), documents.Select(d => d.Report.Source.Sha256 + "\t" + d.Attachment.relativePath + "\tDeclared context: " + d.Attachment.declaredContext));
                var written = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var doc in documents)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (doc.IsStale || BlackBoxSourceAccess.Hash(BlackBoxSourceAccess.Read(doc.Attachment)) != doc.Report.Source.Sha256)
                        throw new IOException("A source changed; reinspect it before exporting.");
                    if (!written.Add(doc.Report.Source.Sha256)) continue;
                    string sourceFolder = Path.Combine(folder, doc.Report.Source.Sha256); Directory.CreateDirectory(sourceFolder);
                    File.WriteAllText(Path.Combine(sourceFolder, "source-evidence.json"), JsonUtility.ToJson(BlackBoxEvidenceExport.From(doc), true));
                    for (int i = 0; i < doc.Report.Recordings.Count; i++)
                    {
                        var rec = doc.Report.Recordings[i];
                        if (rec.Decoded && rec.Pcm != null) BlackBoxWaveExport.ExportNew(Path.Combine(sourceFolder, "recording-" + i + ".wav"), doc, rec, 0, rec.ValidFrames, cancel);
                    }
                }
                File.WriteAllText(Path.Combine(folder, "README.txt"), "Decoded PCM and per-recording source manifests. source-evidence.json retains unavailable recordings, tables, references and decode limitations. Create and review RPM/load mappings in Black Box Audio, then save an engine profile for vehicle playback.");
                cancel.ThrowIfCancellationRequested(); return folder;
            }
            catch { Directory.Delete(folder, true); throw; }
        }

        public static string[] PackageAssets(BlackBoxSession session, BlackBoxDocument[] documents)
        {
            var plan = BlackBoxNativeMapping.Prepare(session, documents, true);
            if (!plan.Unchanged) throw new InvalidOperationException("Save the current mapping as an engine profile before packaging.");
            var assets = AssetDatabase.GetDependencies(session.generatedProfilePath, true)
                .Concat(new[] { session.outputFolder + "/Analysis/native-mapping.json" })
                .Concat(Directory.GetFiles(session.outputFolder + "/Decoded", "*.wav.json")).Distinct().ToArray();
            foreach (string path in assets)
            {
                if (path.Contains("/Editor/")) throw new InvalidOperationException("Runtime package unexpectedly references editor assets: " + path);
                // Only generated JSON may still be outside the asset database.
                // Reimporting script dependencies here schedules an unrelated compilation.
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return assets.Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
        }

        public static void ExportPackage(string path, BlackBoxSession session, BlackBoxDocument[] documents)
        {
            if (File.Exists(path)) throw new IOException("Choose a new package filename.");
            AssetDatabase.ExportPackage(PackageAssets(session, documents), path, ExportPackageOptions.Default);
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Unity did not create the audio package.");
        }
    }
}
