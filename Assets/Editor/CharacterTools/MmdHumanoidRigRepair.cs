using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Explicit mappings for the two converted MMD models. Does not modify meshes or scenes.
[InitializeOnLoad]
public static class MmdHumanoidRigRepair
{
    private const string Request = "Temp/MmdHumanoidRigRepair.request";
    private const string Report = "Temp/MmdHumanoidRigRepair.report.txt";
    private static readonly string[] Paths = {
        "Assets/Recourses/PlayerModels/汐/汐_Unity.fbx",
        "Assets/Recourses/PlayerModels/星绘/星绘泳装_Unity.fbx"
    };

    static MmdHumanoidRigRepair() { EditorApplication.delayCall += RunRequested; }
    private static void RunRequested()
    {
        if (!File.Exists(Request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RunRequested; return; }
        File.Move(Request, Request + ".started."+DateTime.UtcNow.Ticks);
        Repair();
    }

    [MenuItem("Tools/Characters/Repair Xi and Xinghui Humanoid Rigs")]
    public static void Repair()
    {
        var log = new List<string>();
        try
        {
            foreach (var path in Paths) RepairOne(path, log);
            log.Add("ALL_AVATARS_VALID");
        }
        catch (Exception e) { log.Add(e.ToString()); Debug.LogException(e); }
        finally { File.WriteAllLines(Report, log); AssetDatabase.SaveAssets(); }
    }

    private static Dictionary<string,string> Mapping()
    {
        var m = new Dictionary<string,string> {
            {"Hips","腰"}, {"Spine","上半身"}, {"Chest","上半身1"}, {"UpperChest","上半身2"},
            {"Neck","首"}, {"Head","頭"}, {"LeftEye","左目"}, {"RightEye","右目"}
        };
        foreach (var side in new[]{"Left","Right"})
        {
            var jp = side == "Left" ? "左" : "右";
            m[side+"UpperLeg"] = jp+"足D"; // D chain owns the actual leg skin weights.
            m[side+"LowerLeg"] = jp+"ひざD";
            m[side+"Foot"] = jp+"足首D";
            m[side+"Toes"] = jp+"足先EX";
            m[side+"Shoulder"] = jp+"肩";
            m[side+"UpperArm"] = jp+"腕";
            m[side+"LowerArm"] = jp+"ひじ";
            m[side+"Hand"] = jp+"手首";
            var fingers = new[]{"Thumb","Index","Middle","Ring","Little"};
            var japanese = new[]{"親指","人指","中指","薬指","小指"};
            var segments = new[]{"Proximal","Intermediate","Distal"};
            for (int f=0;f<5;f++) for (int s=0;s<3;s++)
                m[side+fingers[f]+segments[s]] = jp+japanese[f]+(char)('０'+s+(f==0?0:1));
        }
        return m;
    }

    private static void RepairOne(string path, List<string> log)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!importer) throw new Exception("Missing model: "+path);
        var backup = "Library/MmdHumanoidRigRepairBackup/"+Path.GetFileName(path)+".meta";
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        if (!File.Exists(backup)) File.Copy(path+".meta",backup);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) throw new Exception("Cannot load model hierarchy: "+path);
        var transforms = prefab.GetComponentsInChildren<Transform>(true);
        var map = Mapping();
        if (map.Values.Distinct().Count()!=map.Count) throw new Exception("Duplicate mapping");
        foreach (var pair in map)
            if (transforms.Count(t=>t.name==pair.Value)!=1) throw new Exception("Bone is missing or ambiguous: "+pair.Value);
        var desc = importer.humanDescription;
        desc.human = map.Select(p=>new HumanBone { humanName=HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones),p.Key)], boneName=p.Value, limit=new HumanLimit { useDefaultValues=true } }).ToArray();
        desc.skeleton = transforms.Select(t=>new SkeletonBone { name=t.name, position=t.localPosition, rotation=t.localRotation, scale=t.localScale }).ToArray();
        desc.upperArmTwist=0.5f; desc.lowerArmTwist=0.5f;
        desc.upperLegTwist=0.5f; desc.lowerLegTwist=0.5f;
        desc.armStretch=0.05f; desc.legStretch=0.05f;
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeBones=false;
        importer.humanDescription=desc;
        importer.SaveAndReimport();
        var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        log.Add(path+" | Avatar="+(avatar?avatar.name:"null")+" | isValid="+(avatar && avatar.isValid)+" | isHuman="+(avatar && avatar.isHuman));
        if (!avatar || !avatar.isValid || !avatar.isHuman) throw new Exception("Avatar validation failed: "+path);
        var instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        instance.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var animator=instance.GetComponent<Animator>();
            if (!animator) animator=instance.AddComponent<Animator>();
            animator.avatar=avatar;
            animator.Rebind();
            foreach(var pair in map)
            {
                var id=(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones),pair.Key);
                var bone=animator.GetBoneTransform(id);
                if (!bone || bone.name!=pair.Value) throw new Exception("Runtime mapping mismatch: "+pair.Key+" -> "+(bone?bone.name:"null")+" expected "+pair.Value);
                log.Add(pair.Key+" -> "+bone.name);
            }
            log.Add("VERIFIED: "+map.Count+" unique human mappings; blend shapes="+instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r=>r.sharedMesh.blendShapeCount));
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        Debug.Log("Humanoid rig repaired and verified: "+path);
    }
}
