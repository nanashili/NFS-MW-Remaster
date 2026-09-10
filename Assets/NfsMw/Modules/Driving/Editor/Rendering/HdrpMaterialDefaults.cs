using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace NfsMwRemaster.Driving.Editor.Rendering
{
    public static class HdrpMaterialDefaults
    {
        public static Material Road()
        {
            var template=AssetDatabase.LoadAssetAtPath<Material>("Assets/NfsMw/Modules/Driving/Data/Rendering/Materials/Asphalt dry to wet.mat");
            if(!template)throw new InvalidOperationException("The HDRP asphalt material template is missing.");
            return new Material(template);
        }
        public static void Validate(Material material) => HDMaterial.ValidateMaterial(material);
    }
}
