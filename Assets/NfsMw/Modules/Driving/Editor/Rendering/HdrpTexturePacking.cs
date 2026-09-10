using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NfsMwRemaster.Driving.Editor.Rendering
{
    public enum TextureChannel { R, G, B, A }
    [Serializable]
    public struct MaskChannel
    {
        public Texture2D texture;
        public TextureChannel channel;
        public bool invert;
        [Range(0,1)] public float fallback;
        public float Sample(Color pixel) => Mathf.Clamp01(invert ? 1-pixel[(int)channel] : pixel[(int)channel]);
    }

    public static class HdrpTexturePacking
    {
        // Sources must be linear data maps. Readback never changes source import settings or Read/Write flags.
        public static Texture2D Pack(string path, int size, MaskChannel metal, MaskChannel ao, MaskChannel detail, MaskChannel smooth)
        {
            if(size<16 || size>4096 || !Mathf.IsPowerOfTwo(size)) throw new ArgumentOutOfRangeException(nameof(size));
            if(!path.StartsWith("Assets/",StringComparison.Ordinal) || !path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)
                || File.Exists(path)) throw new ArgumentException("Choose a new PNG path inside Assets.");
            var channels=new[]{metal,ao,detail,smooth};
            foreach(var channel in channels)
            {
                if(!channel.texture)continue;
                var importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(channel.texture)) as TextureImporter;
                if(importer!=null && (importer.sRGBTexture || importer.textureType==TextureImporterType.NormalMap))
                    throw new ArgumentException(channel.texture.name+": use a linear data texture, not an sRGB or normal map.");
            }
            var pixels=new Color[size*size];
            for(int c=0;c<4;c++)
            {
                var source=channels[c].texture?Read(channels[c].texture,size):null;
                for(int i=0;i<pixels.Length;i++)pixels[i][c]=source==null?Mathf.Clamp01(channels[c].fallback):channels[c].Sample(source[i]);
            }
            var result=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
            try { result.SetPixels(pixels);result.Apply();File.WriteAllBytes(path,result.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(result); }
            AssetDatabase.ImportAsset(path);
            var output=(TextureImporter)AssetImporter.GetAtPath(path);
            output.sRGBTexture=false;output.alphaSource=TextureImporterAlphaSource.FromInput;
            output.mipmapEnabled=true;output.streamingMipmaps=true;output.isReadable=false;
            output.anisoLevel=8;output.filterMode=FilterMode.Trilinear;output.maxTextureSize=size;
            output.textureCompression=TextureImporterCompression.CompressedHQ;output.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Color[] Read(Texture2D source,int size)
        {
            var prior=RenderTexture.active;var target=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var copy=new Texture2D(size,size,TextureFormat.RGBA32,false,true);
            try { Graphics.Blit(source,target);RenderTexture.active=target;copy.ReadPixels(new Rect(0,0,size,size),0,0);copy.Apply();return copy.GetPixels(); }
            finally { RenderTexture.active=prior;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(copy); }
        }
    }
}
