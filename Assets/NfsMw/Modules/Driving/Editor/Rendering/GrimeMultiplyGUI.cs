using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving.Editor.Rendering
{
    // Keep native HDRP passes and validation, with the brush's existing multiplicative compositing.
    public sealed class GrimeMultiplyGUI : UnlitShaderGraphGUI
    {
        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            Configure(material);
        }
        public static void Configure(Material material)
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.DstColor);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        }
    }
}
