using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NfsMwRemaster.Driving.Editor.WorldValidation
{
    public static class WorldValidationNavigation
    {
        public static bool TrySelect(WorldValidationResult result, out string failure)
        {
            failure = string.Empty;
            if (result == null || !result.canNavigate)
            {
                failure = "This result has no stable source reference.";
                return false;
            }

            try
            {
                UnityEngine.Object target = ResolveGlobalObject(result.globalObjectId);
                if (target != null)
                {
                    Selection.activeObject = target;
                    EditorGUIUtility.PingObject(target);
                    return true;
                }

                if (string.IsNullOrEmpty(result.globalObjectId) && !string.IsNullOrEmpty(result.assetPath))
                {
                    target = AssetDatabase.LoadMainAssetAtPath(result.assetPath);
                    if (target != null)
                    {
                        Selection.activeObject = target;
                        EditorGUIUtility.PingObject(target);
                        return true;
                    }
                }

                if (!string.IsNullOrEmpty(result.scenePath))
                {
                    Scene scene = SceneManager.GetSceneByPath(result.scenePath);
                    if (!scene.IsValid() || !scene.isLoaded)
                        scene = EditorSceneManager.OpenScene(result.scenePath, OpenSceneMode.Additive);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        failure = "The source scene could not be opened: " + result.scenePath;
                        return false;
                    }

                    // A position is useful for framing, but it is not a stable
                    // identity. Never select a nearest object as a substitute
                    // for a missing/renamed source reference.
                    target = ResolveGlobalObject(result.globalObjectId);
                    if (target != null)
                    {
                        Selection.activeObject = target;
                        EditorGUIUtility.PingObject(target);
                        SceneView.lastActiveSceneView?.FrameSelected();
                        return true;
                    }
                    SceneView.lastActiveSceneView?.Frame(new Bounds(result.hasWorldPosition ? result.worldPosition : Vector3.zero, Vector3.one * 10));
                    failure = "Scene opened, but the stable source object no longer exists; no nearest-object substitution was made.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }

            failure = "The source object or asset no longer exists.";
            return false;
        }

        private static UnityEngine.Object ResolveGlobalObject(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            try
            {
                if (!GlobalObjectId.TryParse(value, out GlobalObjectId id)) return null;
                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            }
            catch { return null; }
        }
    }
}
