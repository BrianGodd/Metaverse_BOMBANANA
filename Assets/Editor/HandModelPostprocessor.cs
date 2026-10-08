using UnityEditor;
using UnityEngine;

namespace Bombanana.Editor
{
    // Keep Blender's editable armature wrapper out of the runtime hierarchy on every reimport.
    public sealed class HandModelPostprocessor : AssetPostprocessor
    {
        private void OnPostprocessModel(GameObject model)
        {
            if (assetPath != "Assets/Art/Hand/Bombanana_LeftHand.fbx" &&
                assetPath != "Assets/Art/Hand/Bombanana_LeftHand_OpenXR.fbx") return;
            var armature = model.transform.Find("Bombanana_LeftHandRig");
            var mesh = model.transform.Find("Bombanana_LeftHandMesh");
            if (armature == null || mesh == null) return;
            var wrist = armature.Find("XRHand_Wrist");
            if (wrist == null || armature.childCount != 1) return;

            // Preserve the imported world matrices and bind poses when removing the wrapper.
            wrist.SetParent(model.transform, true);
            wrist.SetSiblingIndex(0);
            mesh.name = "LeftHand";
            Object.DestroyImmediate(armature.gameObject);
        }
    }
}
