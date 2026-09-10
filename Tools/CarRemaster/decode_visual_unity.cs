using System;
using System.IO;
using System.Linq;
using UnityEngine;
using NfsMwRemaster.Driving.AudioAnalysis;
using NfsMwRemaster.Driving.Editor.DrivingMechanics;
using Newtonsoft.Json;
internal class CommandScript : IRunCommand {
 public void Execute(ExecutionResult result) {
  var db=new MostWantedAudioDatabase();
  var root="/Users/tihan-nico/Library/Application Support/CrossOver/Bottles/NFS MW/drive_c/Program Files (x86)/NFS Most Wanted";
  foreach(var p in new[]{"GLOBAL/attributes.bin","GLOBAL/FE_ATTRIB.bin","GLOBAL/gameplay.bin"}) db.LoadSource(File.ReadAllBytes(Path.Combine(root,p)),p,new[]{"ecar","pvehicle"});
  var labels=new[]{"CollectionName","MODEL","TireOffsets","KitWheelOffsetFront","KitWheelOffsetRear","FrontCamber","RearCamber","TireSkidWidth","WheelSpokeCount","RideHeight"};
  var rows=db.RowsOf("ecar").Select(r=>new {key=r.Key.ToString("x8"),fields=db.Resolve(r).Where(k=>labels.Any(n=>MostWantedAudioDatabase.Hash(n)==k.Key)).Select(k=>new{name=labels.First(n=>MostWantedAudioDatabase.Hash(n)==k.Key),values=k.Value.Items().Select(MostWantedHandlingReader.Decode).ToArray()}).ToArray()}).ToArray();
  File.WriteAllText("Art/Cars/visual-attributes.json",JsonConvert.SerializeObject(rows,Formatting.Indented));
  result.Log("Recovered "+rows.Length+" ecar visual records");
 }
}
