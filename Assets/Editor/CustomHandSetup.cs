using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Bombanana.Interaction;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace Bombanana.Editor
{
    // Editor-only preparation. No model-specific paths or external mapping files.
    public static class CustomHandSetup
    {
        static readonly string[][] Fingers = {
            new[]{"thumb"}, new[]{"index"}, new[]{"middle", "mid"},
            new[]{"ring"}, new[]{"pinky", "little"}
        };
        static readonly int[] Starts = {2, 6, 11, 16, 21};
        static readonly int[] Metacarpals = {6, 11, 16, 21};

        static string Key(string name)
        {
            name = name.Substring(name.LastIndexOf(':') + 1);
            return Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
        }
        static int Depth(Transform t) { int n=0; while(t.parent!=null){n++;t=t.parent;} return n; }
        static CustomHandVisual.JointBinding Copy(CustomHandVisual.JointBinding b) =>
            new CustomHandVisual.JointBinding { id=b.id, bone=b.bone, reference=b.reference,
                keepRestRotation=b.keepRestRotation, terminalDirection=b.terminalDirection };

        public static string Prepare(CustomHandVisual visual)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before preparing a hand.");
            if (EditorUtility.IsPersistent(visual)) throw new InvalidOperationException("Open the prefab in Prefab Mode before preparing its hand.");
            var root=visual.Root!=null?visual.Root:visual.transform;
            var renderer=visual.Renderer;
            if(renderer==null)
            {
                var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if(renderers.Length!=1)throw new InvalidOperationException("Assign Renderer: the selected Root must identify one hand mesh.");
                renderer=renderers[0];
            }
            var state=new SerializedObject(visual);
            var side=(Handedness)state.FindProperty("_handedness").enumValueIndex;
            var reference=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.meta.xr.sdk.interaction/Runtime/Meshes/OpenXR"+side+"Hand.fbx");
            if(reference==null)throw new InvalidOperationException("Meta OpenXR reference asset is unavailable.");
            var source=reference.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name);
            var targets=root.GetComponentsInChildren<Transform>(true).Where(t=>t!=root).ToArray();
            var messages=new List<string>();
            var joints=new List<CustomHandVisual.JointBinding>();
            for(int i=0;i<26;i++)
            {
                var old=visual.Joints.Where(b=>b!=null&&(int)b.id==i).ToArray();
                if(old.Length>1)throw new InvalidOperationException("Duplicate joint ID: "+i);
                var b=old.Length==1?Copy(old[0]):new CustomHandVisual.JointBinding{id=(HandJointId)i};
                b.reference=source["XRHand_"+CustomHandVisualEditor.OpenXRNames[i]];
                if(b.bone!=null&&!b.bone.IsChildOf(root))throw new InvalidOperationException(b.id+": existing bone is outside Root; choose the intended Root or correct this mapping.");
                joints.Add(b);
            }
            Action<int,IEnumerable<Transform>> assign=(id,candidates)=>{
                if(joints[id].bone!=null)return;
                var found=candidates.Distinct().ToArray();
                if(found.Length==1&&!joints.Any(b=>b.bone==found[0]))joints[id].bone=found[0];
                else if(found.Length>1)messages.Add(CustomHandVisualEditor.OpenXRNames[id]+": ambiguous names; assign Bone manually.");
            };
            // Prefer explicit semantic names over numbered finger conventions.
            for(int i=0;i<26;i++)
            {
                string semantic=Key(CustomHandVisualEditor.OpenXRNames[i]);
                assign(i,targets.Where(t=>Key(t.name)=="xrhand"+semantic||Key(t.name)==semantic));
            }
            string suffix=side==Handedness.Right?"r":"l",sideName=side.ToString().ToLowerInvariant();
            assign(1,targets.Where(t=>new[]{"hand"+suffix,"handbase","wrist"+suffix,"wrist","hand"+sideName}.Contains(Key(t.name))));
            var wrist=joints[1].bone;
            if(wrist!=null)
            {
                for(int f=0;f<5;f++)
                {
                    int start=Starts[f],tip=f==0?5:start+4;
                    var candidates=targets.Where(t=>t.IsChildOf(wrist)&&Fingers[f].Any(word=>Key(t.name).Contains(word))&&
                        !new[]{"control","target","pole","ik"}.Any(word=>Key(t.name).Contains(word))).ToArray();
                    var ends=candidates.Where(t=>Key(t.name).Contains("tip")||Key(t.name).Contains("end")).ToArray();
                    assign(tip,ends);
                    var chain=candidates.Except(ends).OrderBy(Depth).ToArray();
                    bool linear=chain.Skip(1).Select((t,i)=>t.IsChildOf(chain[i])).All(x=>x);
                    if(!linear||chain.Length<2||chain.Length>4||(f==0&&chain.Length>3))
                    {
                        if(chain.Length>0)messages.Add(Fingers[f][0]+": nonstandard chain; verify joint semantics manually.");
                        continue;
                    }
                    int first=f==0?(chain.Length==3?2:3):(chain.Length==4?start:start+1);
                    if(f!=0&&chain.Length!=3&&chain.Length!=4)continue;
                    for(int j=0;j<chain.Length;j++)assign(first+j,new[]{chain[j]});
                }
            }
            foreach(int id in new[]{4,9,14,19,24})
            {
                var b=joints[id];var previous=joints[id-1];
                if(b.bone==null||joints[id+1].bone!=null||b.terminalDirection.sqrMagnitude>1e-12f)continue;
                if(previous.bone==null)continue;
                var d=b.bone.position-previous.bone.position;
                if(d.sqrMagnitude<1e-12f)continue;
                b.terminalDirection=b.bone.InverseTransformDirection(d).normalized;
                messages.Add(b.id+": estimated Terminal Direction from preceding segment; inspect the fingertip direction.");
            }
            visual.Configure(root,renderer,side,joints,
                state.FindProperty("_materialProperties").objectReferenceValue as Oculus.Interaction.MaterialPropertyBlockEditor);
            var missing=new[]{1,3,4,7,8,9,12,13,14,17,18,19,22,23,24}.Where(id=>joints[id].bone==null).ToArray();
            if(missing.Length>0)messages.Add("Assign required joints: "+string.Join(", ",missing.Select(id=>CustomHandVisualEditor.OpenXRNames[id])));
            var weights=renderer.sharedMesh.GetAllBoneWeights();
            try
            {
                foreach(int id in new[]{5,10,15,20,25})
                {
                    int skinIndex=Array.IndexOf(renderer.bones,joints[id].bone);
                    if(joints[id].bone!=null&&skinIndex>=0&&weights.Any(w=>w.boneIndex==skinIndex&&w.weight>0))
                        messages.Add(joints[id].id+": weighted Tip; confirm this is an endpoint, not a distal phalanx.");
                }
            }
            finally{weights.Dispose();}
            bool valid=visual.TryValidate(out var error,false);
            if(!valid)messages.Add("Needs attention: "+error);
            messages.Insert(0,$"Mapped {joints.Count(b=>b.bone!=null)}/26 joints. Existing Bone, Terminal Direction and Keep Rest Rotation settings retained.");
            return string.Join("\n",messages);
        }

        public static string Bake(CustomHandVisual visual,bool align)
        {
            string report=Prepare(visual);
            if(!visual.TryValidate(out var error,false))throw new InvalidOperationException(error);
            if(align)CustomHandVisualEditor.AlignFingerReferencePose(visual.Joints);
            visual.BakeCalibration();
            return report+"\n"+(align?"Reference pose aligned and calibration saved.":"Current pose calibrated without changing bone rotations.")+" Visual/VR acceptance is still required.";
        }

        public static void PreserveFingerRoots(CustomHandVisual visual,bool preserve)
        {
            foreach(var b in visual.Joints.Where(b=>b!=null&&b.bone!=null&&Metacarpals.Contains((int)b.id)))
                b.keepRestRotation=preserve;
            var state=new SerializedObject(visual);state.FindProperty("_calibrated").boolValue=false;state.ApplyModifiedProperties();
        }
    }
}
