using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using NfsMwRemaster.Driving.Editor.Rendering;

namespace NfsMwRemaster.Driving.Tests
{
    public sealed class HdrpTexturePackingTests
    {
        string folder;
        [SetUp] public void Setup(){folder="Assets/HdrpPackingTest_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",folder.Substring(7));}
        [TearDown] public void Cleanup(){AssetDatabase.DeleteAsset(folder);}
        [Test] public void PackedChannelsAndLinearImporterSurviveAssetReload()
        {
            var source=new Texture2D(16,16,TextureFormat.RGBA32,false,true);
            try
            {
                var colors=new Color[256];Array.Fill(colors,new Color(.2f,.4f,.6f,.8f));source.SetPixels(colors);source.Apply();
                string path=folder+"/Mask.png";
                HdrpTexturePacking.Pack(path,16,new MaskChannel{texture=source,channel=TextureChannel.B},new MaskChannel{texture=source,channel=TextureChannel.R},new MaskChannel{fallback=1},new MaskChannel{texture=source,channel=TextureChannel.A,invert=true});
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.sRGBTexture,Is.False);Assert.That(importer.mipmapEnabled,Is.True);Assert.That(importer.streamingMipmaps,Is.True);Assert.That(importer.isReadable,Is.False);
                var decoded=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                try { decoded.LoadImage(File.ReadAllBytes(path));var c=decoded.GetPixel(8,8);Assert.That(c.r,Is.EqualTo(.6f).Within(.01));Assert.That(c.g,Is.EqualTo(.2f).Within(.01));Assert.That(c.b,Is.EqualTo(1).Within(.01));Assert.That(c.a,Is.EqualTo(.2f).Within(.01)); }
                finally { UnityEngine.Object.DestroyImmediate(decoded); }
                Assert.Throws<ArgumentException>(()=>HdrpTexturePacking.Pack(path,16,default,default,default,default));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
        [Test] public void SrgbSourcesAreRejectedWithoutMutatingImports()
        {
            string path=folder+"/Color.png";var t=new Texture2D(16,16);
            try{File.WriteAllBytes(path,t.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(t);}
            AssetDatabase.ImportAsset(path);var source=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.Throws<ArgumentException>(()=>HdrpTexturePacking.Pack(folder+"/Mask.png",16,new MaskChannel{texture=source},default,default,default));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).sRGBTexture,Is.True);
            Assert.That(File.Exists(folder+"/Mask.png"),Is.False);
        }
    }
}
