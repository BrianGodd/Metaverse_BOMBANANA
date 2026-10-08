using System;
using System.Collections.Generic;
using System.Linq;
using Bombanana.Interaction;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace Bombanana.Editor
{
    [CustomEditor(typeof(CustomHandVisual))]
    public sealed class CustomHandVisualEditor : UnityEditor.Editor
    {
        public static readonly string[] OpenXRNames = {
            "Palm", "Wrist", "ThumbMetacarpal", "ThumbProximal", "ThumbDistal", "ThumbTip",
            "IndexMetacarpal", "IndexProximal", "IndexIntermediate", "IndexDistal", "IndexTip",
            "MiddleMetacarpal", "MiddleProximal", "MiddleIntermediate", "MiddleDistal", "MiddleTip",
            "RingMetacarpal", "RingProximal", "RingIntermediate", "RingDistal", "RingTip",
            "LittleMetacarpal", "LittleProximal", "LittleIntermediate", "LittleDistal", "LittleTip"
        };

        public static List<CustomHandVisual.JointBinding> Map(Transform root, GameObject reference)
        {
            var targets = root.GetComponentsInChildren<Transform>(true);
            var sources = reference.GetComponentsInChildren<Transform>(true);
            var result = new List<CustomHandVisual.JointBinding>();
            for (int i = 0; i < OpenXRNames.Length; i++)
            {
                string name = "XRHand_" + OpenXRNames[i];
                result.Add(new CustomHandVisual.JointBinding {
                    id = (HandJointId)i,
                    bone = targets.SingleOrDefault(t => t.name == name),
                    reference = sources.Single(t => t.name == name)
                });
            }
            return result;
        }

        // Editor preparation only: retain bindposes, bone lengths, scales and palm width.
        public static void AlignFingerReferencePose(IReadOnlyList<CustomHandVisual.JointBinding> joints)
        {
            var byId=joints.ToDictionary(j=>(int)j.id);
            foreach(int id in new[]{1,7,12,22})
                if(!byId.TryGetValue(id,out var b)||b.bone==null||b.reference==null)
                    throw new InvalidOperationException("Assign Wrist, Index/Middle/Little Proximal and their references first.");
            var forward=byId[12].bone.position-byId[1].bone.position;
            var across=byId[7].bone.position-byId[22].bone.position;
            var sourceForward=byId[12].reference.position-byId[1].reference.position;
            var sourceAcross=byId[7].reference.position-byId[22].reference.position;
            if(Vector3.Cross(forward,across).sqrMagnitude<1e-12f||Vector3.Cross(sourceForward,sourceAcross).sqrMagnitude<1e-12f)
                throw new InvalidOperationException("Degenerate palm reference pose.");
            var sourceToModel=Quaternion.LookRotation(forward,Vector3.Cross(forward,across))*
                Quaternion.Inverse(Quaternion.LookRotation(sourceForward,Vector3.Cross(sourceForward,sourceAcross)));
            var moving=joints.Where(j=>j.bone!=null&&!j.keepRestRotation&&new[]{2,3,4,7,8,9,12,13,14,17,18,19,22,23,24}.Contains((int)j.id)).OrderBy(j=>(int)j.id).ToArray();
            foreach(var j in moving)
            {
                var next=byId[(int)j.id+1];
                if(j.reference==null||next.reference==null||(next.reference.position-j.reference.position).sqrMagnitude<1e-12f||
                    (next.bone==null?j.terminalDirection.sqrMagnitude:(next.bone.position-j.bone.position).sqrMagnitude)<1e-12f)
                    throw new InvalidOperationException("Missing reference endpoint or terminal direction: "+j.id);
            }
            foreach(var j in moving)
            {
                var next=byId[(int)j.id+1];
                var current=next.bone!=null?next.bone.position-j.bone.position:j.bone.TransformDirection(j.terminalDirection);
                var desired=sourceToModel*(next.reference.position-j.reference.position);
                j.bone.rotation=Quaternion.FromToRotation(current,desired)*j.bone.rotation;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            // Runtime update switches do not alter the baked reference pose.
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_updateRootPose"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_updateRootScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_updateVisibility"));
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_updateRootPose", "_updateRootScale", "_updateVisibility");
            if (EditorGUI.EndChangeCheck()) serializedObject.FindProperty("_calibrated").boolValue = false;
            serializedObject.ApplyModifiedProperties();
            var visual = (CustomHandVisual)target;
            EditorGUILayout.HelpBox("Auto Map fills missing mappings using semantic names and common finger chains. Review uncertain joints. Align changes the reference pose; Bake only calibrates the current pose. Neither changes mesh weights or bone lengths.", MessageType.Info);
            if(EditorUtility.IsPersistent(visual))
            {
                EditorGUILayout.HelpBox("Open this prefab in Prefab Mode to prepare its hand safely.",MessageType.Info);
                if(GUILayout.Button("Open Prefab"))AssetDatabase.OpenAsset(visual.gameObject);
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying||EditorUtility.IsPersistent(visual)))
            {
                if (GUILayout.Button("Auto Map")) Run(visual, () => _setupReport=CustomHandSetup.Prepare(visual));
                if (GUILayout.Button("Bake Reference Pose Calibration")) Run(visual, () => {
                    _setupReport=CustomHandSetup.Bake(visual,false);
                });
                if (GUILayout.Button("Align Finger Reference Pose and Bake")) Run(visual, () => {
                    _setupReport=CustomHandSetup.Bake(visual,true);
                });
                bool hasMetacarpals=visual.Joints.Any(b=>b!=null&&b.bone!=null&&new[]{6,11,16,21}.Contains((int)b.id));
                using(new EditorGUI.DisabledScope(!hasMetacarpals))
                {
                    if(GUILayout.Button("Preserve Finger Roots")) Run(visual,()=>{
                        CustomHandSetup.PreserveFingerRoots(visual,true);_setupReport="Four finger metacarpals keep their reference rotation. Finger motion is retained; palm cupping is reduced. Bake again in the reference pose.";
                    });
                    if(GUILayout.Button("Track Finger Metacarpals")) Run(visual,()=>{
                        CustomHandSetup.PreserveFingerRoots(visual,false);_setupReport="Four finger metacarpals follow tracking. Inspect finger-root stretching. Bake again in the reference pose.";
                    });
                }
                if(!hasMetacarpals)EditorGUILayout.HelpBox("No mapped finger metacarpals. No palm-lock setting is needed for this rig.",MessageType.None);
            }
            if(!string.IsNullOrEmpty(_setupReport))EditorGUILayout.HelpBox(_setupReport,MessageType.Info);
            if (GUILayout.Button("Validate"))
                Debug.Log(visual.TryValidate(out var error) ? "CustomHandVisual configuration is valid." : error, visual);
            if (!serializedObject.FindProperty("_calibrated").boolValue)
                EditorGUILayout.HelpBox("Calibration is required after configuration changes.", MessageType.Warning);
        }
        private string _setupReport;
        private void Run(CustomHandVisual visual, Action action)
        {
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Prepare custom hand");
            var root=visual.Root!=null?visual.Root:visual.transform;
            var objects=root.GetComponentsInChildren<Transform>(true).Cast<UnityEngine.Object>().Concat(new UnityEngine.Object[]{visual}).ToArray();
            Undo.RecordObjects(objects,"Prepare custom hand");
            try
            {
                action();
                EditorUtility.SetDirty(visual);
                foreach(var o in objects)PrefabUtility.RecordPrefabInstancePropertyModifications(o);
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
                serializedObject.Update();
            }
            catch(Exception e)
            {
                Undo.FlushUndoRecordObjects();Undo.RevertAllDownToGroup(group);
                _setupReport=e.Message;Debug.LogWarning(e.Message,visual);serializedObject.Update();
            }
        }
    }
}
