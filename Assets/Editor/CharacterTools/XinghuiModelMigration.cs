using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine.Animations.Rigging;

// Explicit, one-shot editor request. Never runs a migration without a request file.
[InitializeOnLoad]
public static class XinghuiModelMigration
{
    const string ModelPath = "Assets/Recourses/PlayerModels/星绘/星绘泳装_Unity.fbx";
    const string Request = "Temp/XinghuiModelMigration.request";
    const string Report = "Temp/XinghuiModelMigration.report.txt";
    static double nextCheck;
    static XinghuiModelMigration() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var mode = File.ReadAllText(Request).Trim();
        File.Move(Request, Request + ".started." + DateTime.UtcNow.Ticks);
        var log = new List<string>();
        try { if (mode == "inspect") Inspect(log); else if (mode == "replace") Replace(log); else if (mode == "verify") Verify(Player(), log); else if(mode=="preview") Preview(log); else if(mode=="skin") Skin(log); else if(mode=="fit") Fit(log); else if(mode=="fit2") FitPoses(log); else if(mode=="savefit") SaveFit(log); else throw new Exception("Unknown mode: " + mode); }
        catch (Exception e) { log.Add("FAILED: " + e); Debug.LogException(e); }
        File.WriteAllLines(Report, log);
    }
    static string PathOf(Transform t) { return t ? (t.parent ? PathOf(t.parent) + "/" : "") + t.name : "null"; }
    static GameObject Player()
    {
        var players = UnityEngine.Object.FindObjectsOfType<LocalCharacterMotor>();
        if (players.Length != 1) throw new Exception("Expected exactly one active player, found " + players.Length);
        return players[0].gameObject;
    }
    static void Describe(GameObject go, List<string> log)
    {
        foreach (var a in go.GetComponentsInChildren<Animator>(true))
        {
            log.Add("ANIMATOR " + PathOf(a.transform) + " avatar=" + (a.avatar ? a.avatar.name : "null") + " human=" + a.isHuman + " valid=" + (a.avatar && a.avatar.isValid) + " local=" + a.transform.localPosition + "/" + a.transform.localEulerAngles + "/" + a.transform.localScale + " worldScale=" + a.transform.lossyScale);
            if (a.isHuman) foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                var t = a.GetBoneTransform(b);
                if (t) log.Add("BONE " + b + " " + PathOf(t) + " pos=" + go.transform.InverseTransformPoint(t.position).ToString("F4"));
            }
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            log.Add("RENDERER " + PathOf(r.transform) + " bounds=" + r.bounds + " materials=" + string.Join(",",r.sharedMaterials.Select(m => m ? m.name + ":" + m.shader.name : "NULL")));
        foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!c) { log.Add("MISSING_SCRIPT"); continue; }
            log.Add("COMPONENT " + PathOf(c.transform) + " " + c.GetType().Name);
            var so = new SerializedObject(c); var p = so.GetIterator();
            while (p.NextVisible(true)) if (p.propertyType == SerializedPropertyType.ObjectReference && p.name != "m_Script")
            {
                var o = p.objectReferenceValue; var t = o as Transform ?? (o as Component)?.transform ?? (o as GameObject)?.transform;
                log.Add(" REF " + p.propertyPath + "=" + (t ? PathOf(t) : o ? AssetDatabase.GetAssetPath(o) : "null"));
                if (t) log.Add("  LOCAL " + t.localPosition.ToString("F5") + " rot=" + t.localRotation.ToString("F5") + " scale=" + t.localScale.ToString("F5"));
            }
        }
    }
    static void Inspect(List<string> log)
    {
        var player = Player(); log.Add("SCENE=" + player.scene.path + " dirty=" + player.scene.isDirty);
        Describe(player, log);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!asset) throw new Exception("New model not found");
        var instance = UnityEngine.Object.Instantiate(asset);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try { log.Add("NEW_MODEL"); Describe(instance, log); }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        log.Add("INSPECTION_COMPLETE");
    }

    static T Ref<T>(Component c, string name) where T : UnityEngine.Object
    { return new SerializedObject(c).FindProperty(name).objectReferenceValue as T; }
    static void Skin(List<string> log)
    {
        var a=Ref<Animator>(Player().GetComponent<LocalShoulderAimPresenter>(),"characterAnimator");
        foreach(var r in a.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var counts=new float[r.bones.Length];
            foreach(var w in r.sharedMesh.boneWeights) {counts[w.boneIndex0]+=w.weight0; counts[w.boneIndex1]+=w.weight1; counts[w.boneIndex2]+=w.weight2; counts[w.boneIndex3]+=w.weight3;}
            for(int i=0;i<counts.Length;i++) if(counts[i]>0) log.Add(r.name+" WEIGHT "+counts[i]+" "+PathOf(r.bones[i]));
        }
    }
    static Quaternion Palm(Animator a,bool left)
    {
        var wrist=a.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
        var index=a.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal);
        var little=a.GetBoneTransform(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal);
        var forward=((index.position+little.position)*0.5f-wrist.position).normalized;
        return Quaternion.LookRotation(forward,Vector3.Cross(forward,index.position-little.position).normalized);
    }
    static Vector3 PalmCenter(Animator a,bool left)
    {
        return Vector3.Lerp(a.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand).position,
          (a.GetBoneTransform(left?HumanBodyBones.LeftIndexProximal:HumanBodyBones.RightIndexProximal).position+a.GetBoneTransform(left?HumanBodyBones.LeftLittleProximal:HumanBodyBones.RightLittleProximal).position)*0.5f,0.65f);
    }
    static void Fit(List<string> log)
    {
        var player=Player(); var p=player.GetComponent<LocalShoulderAimPresenter>(); var a=Ref<Animator>(p,"characterAnimator");
        var oldPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ModelReplacementBackups/20260904_134345/PlayerAvatar_BeforeXinghui.prefab");
        var op=oldPrefab.GetComponent<LocalShoulderAimPresenter>(); var oa=Ref<Animator>(op,"characterAnimator");
        var na=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponent<Animator>();
        // Bind-pose anatomical palm frames, not a sampled animation frame: MMD wrist axes differ.
        var oldHand=oa.GetBoneTransform(HumanBodyBones.RightHand); var newHand=na.GetBoneTransform(HumanBodyBones.RightHand);
        var oldSocket=Ref<Transform>(op,"weapon").parent; var socket=Ref<Transform>(p,"weapon").parent;
        var delta=Palm(na,false)*Quaternion.Inverse(Palm(oa,false));
        var rotation=Quaternion.Inverse(newHand.rotation)*delta*oldSocket.rotation;
        var scale=a.transform.localScale.x;
        var newCenterVector=(PalmCenter(na,false)-newHand.position)*scale;
        var offsetWorld=newCenterVector-delta*(PalmCenter(oa,false)-oldHand.position)+delta*(oldSocket.position-oldHand.position);
        var position=Quaternion.Inverse(newHand.rotation)*offsetWorld/scale;
        var path="Assets/ModelReplacementBackups/GripFit_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(player.scene,path,true)) throw new Exception("Grip backup failed");
        Undo.RecordObject(socket,"Calibrate Xinghui palm frame"); socket.localPosition=position; socket.localRotation=rotation;
        var oldLeft=oa.GetBoneTransform(HumanBodyBones.LeftHand); var newLeft=na.GetBoneTransform(HumanBodyBones.LeftHand);
        var oldLocal=Quaternion.Inverse(Palm(oa,true))*oldLeft.rotation;
        var newLocal=Quaternion.Inverse(Palm(na,true))*newLeft.rotation;
        foreach(var name in new[]{"weapon","aimWeaponPose","leftHandGrip","aimLeftHandGripPose"})
        {
            var target=Ref<Transform>(p,name); var original=Ref<Transform>(op,name);
            Undo.RecordObject(target,"Calibrate grip pose"); target.localPosition=original.localPosition; target.localRotation=original.localRotation;
            if(name.Contains("Grip")) target.localRotation=original.localRotation*Quaternion.Inverse(oldLocal)*newLocal;
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
        var ik=a.GetComponentInChildren<TwoBoneIKConstraint>(); var d=ik.data;
        // Start with the authored elbow bend; a fixed hint from the old model was not portable.
        d.hintWeight=0; d.targetRotationWeight=1; ik.data=d;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ik);
        PrefabUtility.RecordPrefabInstancePropertyModifications(socket);
        EditorSceneManager.MarkSceneDirty(player.scene); EditorSceneManager.SaveScene(player.scene);
        log.Add("PALM_FIT socket="+rotation+" position="+position+" backup="+path);
        Preview(log);
    }
    static void FitPoses(List<string> log)
    {
        var player=Player(); var p=player.GetComponent<LocalShoulderAimPresenter>(); var a=Ref<Animator>(p,"characterAnimator");
        var op=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ModelReplacementBackups/20260904_134345/PlayerAvatar_BeforeXinghui.prefab").GetComponent<LocalShoulderAimPresenter>();
        var oldModel=Ref<Animator>(op,"characterAnimator");
        foreach(bool aim in new[]{false,true})
        {
            var old=SampleCopy(oldModel.gameObject,a.runtimeAnimatorController,aim);
            var next=SampleCopy(a.gameObject,a.runtimeAnimatorController,aim);
            try
            {
                var ow=old.GetComponentsInChildren<Transform>(true).Single(t=>t.name==Ref<Transform>(op,"weapon").name);
                var nw=next.GetComponentsInChildren<Transform>(true).Single(t=>t.name==Ref<Transform>(p,"weapon").name);
                var original=Ref<Transform>(op,aim?"aimWeaponPose":"weapon"); ow.localPosition=original.localPosition; ow.localRotation=original.localRotation;
                var oldGrip=old.GetComponentsInChildren<Transform>(true).Single(t=>t.name==Ref<Transform>(op,"leftHandGrip").name);
                var originalGrip=Ref<Transform>(op,aim?"aimLeftHandGripPose":"leftHandGrip"); oldGrip.localPosition=originalGrip.localPosition; oldGrip.localRotation=originalGrip.localRotation;
                float ratio=Mathf.Clamp(Vector3.Distance(PalmCenter(next,true),PalmCenter(next,false))/Vector3.Distance(PalmCenter(old,true),PalmCenter(old,false)),0.7f,1.15f);
                // One shared gun size for both poses; keep current size and calibrate each grip separately.
                ratio=1;
                nw.SetPositionAndRotation(PalmCenter(next,false)+(ow.position-PalmCenter(old,false))*ratio,ow.rotation);
                var target=Ref<Transform>(p,aim?"aimWeaponPose":"weapon"); Undo.RecordObject(target,"Fit weapon to authored pose");
                target.localPosition=nw.localPosition; target.localRotation=nw.localRotation;
                var grip=Ref<Transform>(p,aim?"aimLeftHandGripPose":"leftHandGrip"); Undo.RecordObject(grip,"Fit support hand to authored pose");
                // Keep the authored new-avatar wrist orientation rather than old-avatar local axes.
                grip.localPosition=originalGrip.localPosition;
                grip.localRotation=Quaternion.Inverse(nw.rotation)*next.GetBoneTransform(HumanBodyBones.LeftHand).rotation;
                PrefabUtility.RecordPrefabInstancePropertyModifications(target); PrefabUtility.RecordPrefabInstancePropertyModifications(grip);
                log.Add("POSE "+aim+" weapon "+target.localPosition+" rotation "+target.localRotation);
            }
            finally {UnityEngine.Object.DestroyImmediate(old.gameObject); UnityEngine.Object.DestroyImmediate(next.gameObject);}
        }
        var hipGrip=Ref<Transform>(p,"leftHandGrip"); var aimGrip=Ref<Transform>(p,"aimLeftHandGripPose");
        aimGrip.localPosition=hipGrip.localPosition; aimGrip.localRotation=hipGrip.localRotation;
        PrefabUtility.RecordPrefabInstancePropertyModifications(aimGrip);
        var ik=a.GetComponentInChildren<TwoBoneIKConstraint>(); var d=ik.data; d.hintWeight=0; d.targetRotationWeight=1; ik.data=d;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ik);
        EditorSceneManager.MarkSceneDirty(player.scene); EditorSceneManager.SaveScene(player.scene); Preview(log);
    }
    static void SaveFit(List<string> log)
    {
        var player=Player(); var p=player.GetComponent<LocalShoulderAimPresenter>();
        const string prefab="Assets/Prefabs/Characters/PlayerAvatar_Xinghui.prefab";
        if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player)!=prefab) throw new Exception("Unexpected player prefab");
        Verify(player,log);
        var transforms=new[]{Ref<Transform>(p,"weapon").parent,Ref<Transform>(p,"weapon"),Ref<Transform>(p,"aimWeaponPose"),Ref<Transform>(p,"leftHandGrip"),Ref<Transform>(p,"aimLeftHandGripPose")};
        foreach(var t in transforms) PrefabUtility.ApplyObjectOverride(t,prefab,InteractionMode.AutomatedAction);
        var ik=Ref<Animator>(p,"characterAnimator").GetComponentInChildren<TwoBoneIKConstraint>();
        // Apply only the three calibrated weights; preserve all scene-specific object references.
        var so=new SerializedObject(ik);
        foreach(var property in new[]{"m_Data.m_HintWeight","m_Data.m_TargetRotationWeight"})
        {var prop=so.FindProperty(property); if(prop==null) throw new Exception("Missing IK property "+property); PrefabUtility.ApplyPropertyOverride(prop,prefab,InteractionMode.AutomatedAction);}
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(player.scene);
        log.Add("GRIP_CALIBRATION_SAVED_TO_SCENE_AND_PREFAB");
    }
    static void SetRef(Component c, string name, UnityEngine.Object value)
    { var so = new SerializedObject(c); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(c); }
    static Animator SampleCopy(GameObject source, RuntimeAnimatorController controller, bool aim)
    {
        var copy = UnityEngine.Object.Instantiate(source);
        copy.hideFlags = HideFlags.HideAndDontSave;
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) if (b) b.enabled = false;
        var a = copy.GetComponent<Animator>();
        a.runtimeAnimatorController = controller; a.applyRootMotion = false;
        a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        a.Rebind();
        a.SetBool("ShoulderHeld",aim); a.SetBool("Airborne",false); a.SetBool("AirShoulderHeld",false);
        for (int i=1;i<a.layerCount;i++) a.SetLayerWeight(i,0);
        a.Play("Base Layer.Idle",0,0f);
        if (aim)
        {
            int layer=a.GetLayerIndex("ShoulderLocomotion");
            a.SetLayerWeight(layer,1f); a.Play("ShoulderLocomotion.ShoulderLowerBodyLocomotion",layer,0f);
        }
        a.Update(0.1f);
        return a;
    }
    static void Replace(List<string> log)
    {
        var player=Player(); var scene=player.scene;
        if (scene.path!="Assets/Scenes/CharacterLab.unity") throw new Exception("Unexpected scene: "+scene.path);
        var presenter=player.GetComponent<LocalShoulderAimPresenter>();
        var old=Ref<Animator>(presenter,"characterAnimator");
        if (!old || !old.avatar || !old.isHuman) throw new Exception("Old Animator unavailable");
        if (old.name.Contains("Xinghui")) throw new Exception("Already migrated");
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var sourceAnimator=asset.GetComponent<Animator>();
        if (!sourceAnimator || !sourceAnimator.avatar || !sourceAnimator.avatar.isValid || !sourceAnimator.isHuman) throw new Exception("Invalid new Avatar");
        var weapon=Ref<Transform>(presenter,"weapon");
        var grip=Ref<Transform>(presenter,"leftHandGrip");
        var aimWeapon=Ref<Transform>(presenter,"aimWeaponPose");
        var aimGrip=Ref<Transform>(presenter,"aimLeftHandGripPose");
        var socket=weapon.parent;
        if (socket.parent!=old.GetBoneTransform(HumanBodyBones.RightHand) || aimWeapon.parent!=socket) throw new Exception("Unexpected weapon socket hierarchy");
        var controller=old.runtimeAnimatorController;
        var stamp=DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backup="Assets/ModelReplacementBackups/"+stamp;
        Directory.CreateDirectory(backup); AssetDatabase.Refresh();
        if (!EditorSceneManager.SaveScene(scene,backup+"/CharacterLab_BeforeXinghui.unity",true)) throw new Exception("Scene backup failed");
        PrefabUtility.SaveAsPrefabAsset(player,backup+"/PlayerAvatar_BeforeXinghui.prefab");
        log.Add("BACKUP="+backup);

        // Compare both avatars in the same authored aim pose. Raw hand-local axes differ.
        Animator oldSample=null,newSample=null;
        Quaternion socketRotation; Vector3 socketOffset; float scale;
        try
        {
            oldSample=SampleCopy(old.gameObject,controller,true);
            newSample=SampleCopy(asset,controller,true);
            var oldHead=oldSample.GetBoneTransform(HumanBodyBones.Head);
            var newHead=newSample.GetBoneTransform(HumanBodyBones.Head);
            scale=oldHead.position.y/newHead.position.y;
            if (scale<0.4f || scale>2f) throw new Exception("Unexpected avatar height ratio: "+scale);
            newSample.transform.localScale*=scale; newSample.Update(0f);
            var sampleSocket=oldSample.GetComponentsInChildren<Transform>(true).Single(t=>t.name==socket.name);
            var oldHand=oldSample.GetBoneTransform(HumanBodyBones.RightHand);
            var newHand=newSample.GetBoneTransform(HumanBodyBones.RightHand);
            socketRotation=Quaternion.Inverse(newHand.rotation)*sampleSocket.rotation;
            socketOffset=newHand.InverseTransformVector(sampleSocket.position-oldHand.position);
            log.Add("CALIBRATION scale="+scale+" socketRotation="+socketRotation+" socketOffset="+socketOffset);
        }
        finally
        {
            if(oldSample) UnityEngine.Object.DestroyImmediate(oldSample.gameObject);
            if(newSample) UnityEngine.Object.DestroyImmediate(newSample.gameObject);
        }

        // Backup exists before touching the player's hierarchy. Preserve the player root identity.
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Replace player visual with Xinghui");
        Undo.RegisterFullObjectHierarchyUndo(player,"Replace player visual");
        var oldSocketScale=socket.lossyScale;
        var oldModelParent=old.transform.parent;
        if(PrefabUtility.IsPartOfPrefabInstance(player)) PrefabUtility.UnpackPrefabInstance(player,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        var model=(GameObject)PrefabUtility.InstantiatePrefab(asset,oldModelParent);
        Undo.RegisterCreatedObjectUndo(model,"Create Xinghui visual");
        model.name="Xinghui";
        model.transform.localPosition=old.transform.localPosition;
        model.transform.localRotation=old.transform.localRotation;
        model.transform.localScale=Vector3.one*scale;
        var animator=model.GetComponent<Animator>();
        animator.runtimeAnimatorController=controller;
        animator.applyRootMotion=false; animator.updateMode=old.updateMode;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
        socket.SetParent(right,false);
        socket.localPosition=socketOffset; socket.localRotation=socketRotation;
        var hs=right.lossyScale;
        socket.localScale=new Vector3(oldSocketScale.x/hs.x,oldSocketScale.y/hs.y,oldSocketScale.z/hs.z);

        var oldConstraint=old.GetComponentInChildren<TwoBoneIKConstraint>(true);
        if (!oldConstraint) throw new Exception("Expected old left hand constraint");
        var rigObject=new GameObject("WeaponRig"); rigObject.transform.SetParent(model.transform,false);
        var rig=rigObject.AddComponent<Rig>();
        var ikObject=new GameObject("LeftHandIK"); ikObject.transform.SetParent(rigObject.transform,false);
        var ik=ikObject.AddComponent<TwoBoneIKConstraint>();
        var data=oldConstraint.data;
        data.root=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        data.mid=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        data.tip=animator.GetBoneTransform(HumanBodyBones.LeftHand);
        data.target=grip;
        var hint=new GameObject("LeftElbowHint").transform;
        hint.SetParent(animator.GetBoneTransform(HumanBodyBones.Chest),false);
        hint.position=data.mid.position-player.transform.right*0.2f-player.transform.forward*0.12f;
        data.hint=hint; ik.data=data; ik.weight=oldConstraint.weight;
        var builder=model.AddComponent<RigBuilder>(); builder.layers.Add(new RigLayer(rig));

        // Remap Animator references everywhere in the loaded scene, including fire and pitch.
        int remapped=0;
        foreach(var root in scene.GetRootGameObjects()) foreach(var c in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(!c || c.transform.IsChildOf(old.transform)) continue;
            var so=new SerializedObject(c); var p=so.GetIterator(); bool changed=false;
            while(p.NextVisible(true)) if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue==old)
            { p.objectReferenceValue=animator; changed=true; remapped++; }
            if(changed) { so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(c); }
        }
        log.Add("ANIMATOR_REFERENCES_REMAPPED="+remapped);
        SetRef(presenter,"motor",player.GetComponent<LocalCharacterMotor>());
        var legacy=new GameObject("Legacy_Suoming_Backup_"+stamp);
        Undo.RegisterCreatedObjectUndo(legacy,"Preserve old visual");
        old.transform.SetParent(legacy.transform,true); legacy.SetActive(false);
        // Leave any user preview model alone. Only replace the actual PlayerAvatar visual.
        Verify(player,log);
        string prefabPath="Assets/Prefabs/Characters/PlayerAvatar_Xinghui.prefab";
        if(File.Exists(prefabPath)) prefabPath=AssetDatabase.GenerateUniqueAssetPath(prefabPath);
        PrefabUtility.SaveAsPrefabAssetAndConnect(player,prefabPath,InteractionMode.AutomatedAction);
        Verify(player,log);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Failed to save scene");
        AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(group);
        Selection.activeGameObject=player;
        if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.Frame(new Bounds(model.transform.position+Vector3.up*0.7f,Vector3.one*1.8f),false);
        log.Add("NEW_PREFAB="+prefabPath); log.Add("REPLACEMENT_COMPLETE");
    }
    static void Verify(GameObject player,List<string> log)
    {
        var p=player.GetComponent<LocalShoulderAimPresenter>(); var a=Ref<Animator>(p,"characterAnimator");
        if(!a || !a.isHuman || !a.avatar.isValid || !a.transform.IsChildOf(player.transform)) throw new Exception("Invalid active Animator");
        foreach(var c in player.GetComponents<MonoBehaviour>())
        {
            if(!c) throw new Exception("Missing player script");
            var so=new SerializedObject(c); var prop=so.FindProperty("characterAnimator");
            if(prop!=null && prop.objectReferenceValue!=a) throw new Exception("Stale animator in "+c.GetType().Name);
        }
        var ik=a.GetComponentInChildren<TwoBoneIKConstraint>(true);
        if(!ik || ik.data.root!=a.GetBoneTransform(HumanBodyBones.LeftUpperArm) || ik.data.mid!=a.GetBoneTransform(HumanBodyBones.LeftLowerArm) || ik.data.tip!=a.GetBoneTransform(HumanBodyBones.LeftHand)) throw new Exception("IK bone mapping failed");
        if(ik.data.target!=Ref<Transform>(p,"leftHandGrip")) throw new Exception("IK target mismatch");
        if(!Ref<Transform>(p,"weapon").IsChildOf(a.GetBoneTransform(HumanBodyBones.RightHand))) throw new Exception("Weapon not on new right hand");
        var muzzle=Ref<Transform>(player.GetComponent<LocalHitscanRifle>(),"muzzle");
        if(!muzzle || !muzzle.IsChildOf(a.transform)) throw new Exception("Muzzle not migrated");
        foreach(var b in new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.LeftHand,HumanBodyBones.RightHand})
            if(!a.GetBoneTransform(b)) throw new Exception("Missing bone "+b);
        foreach(var r in a.GetComponentsInChildren<Renderer>(true)) if(r.sharedMaterials.Any(m=>!m || !m.shader || m.shader.name=="Hidden/InternalErrorShader")) throw new Exception("Invalid material on "+r.name);
        log.Add("VERIFIED: "+a.name+" Avatar, animator references, hand IK, weapon, muzzle and materials");
    }
    static void Preview(List<string> log)
    {
        foreach(bool aim in new[]{false,true})
        {
            var copy=UnityEngine.Object.Instantiate(Player()); copy.hideFlags=HideFlags.HideAndDontSave;
            copy.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);
            GameObject cameraGO=null; RenderTexture rt=null; Texture2D tex=null;
            var previous=RenderTexture.active;
            try
            {
                foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) if(b) b.enabled=false;
                foreach(var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
                var p=copy.GetComponent<LocalShoulderAimPresenter>(); var a=Ref<Animator>(p,"characterAnimator");
                var weapon=Ref<Transform>(p,"weapon"); var grip=Ref<Transform>(p,"leftHandGrip");
                if(aim)
                {
                    var wp=Ref<Transform>(p,"aimWeaponPose"); var gp=Ref<Transform>(p,"aimLeftHandGripPose");
                    weapon.localPosition=wp.localPosition; weapon.localRotation=wp.localRotation;
                    grip.localPosition=gp.localPosition; grip.localRotation=gp.localRotation;
                }
                var builder=a.GetComponent<RigBuilder>();
                foreach(var r in a.GetComponentsInChildren<Rig>(true)) r.enabled=true;
                foreach(var c in a.GetComponentsInChildren<TwoBoneIKConstraint>(true)) c.enabled=true;
                builder.enabled=false; a.Rebind();
                var sourceP=Player().GetComponent<LocalShoulderAimPresenter>();
                var sourceW=Ref<Transform>(sourceP,aim?"aimWeaponPose":"weapon");
                var sourceG=Ref<Transform>(sourceP,aim?"aimLeftHandGripPose":"leftHandGrip");
                weapon.localPosition=sourceW.localPosition; weapon.localRotation=sourceW.localRotation;
                grip.localPosition=sourceG.localPosition; grip.localRotation=sourceG.localRotation;
                a.SetBool("ShoulderHeld",aim); a.SetBool("Airborne",false); a.SetBool("AirShoulderHeld",false);
                a.Play("Base Layer.Idle",0,0f);
                for(int i=1;i<a.layerCount;i++) a.SetLayerWeight(i,0f);
                if(aim) {int li=a.GetLayerIndex("ShoulderLocomotion"); a.SetLayerWeight(li,1); a.Play("ShoulderLocomotion.ShoulderLowerBodyLocomotion",li,0f);}
                a.Update(0.1f);
                weapon.localPosition=sourceW.localPosition; weapon.localRotation=sourceW.localRotation;
                grip.localPosition=sourceG.localPosition; grip.localRotation=sourceG.localRotation;
                log.Add("WEAPON actual="+weapon.localPosition.ToString("F4")+" wanted="+sourceW.localPosition.ToString("F4")+" rot="+weapon.localRotation+" wantedRot="+sourceW.localRotation);
                for(int i=0;i<a.layerCount;i++) log.Add("STATE "+a.GetLayerName(i)+"="+a.GetCurrentAnimatorStateInfo(i).fullPathHash+" clips="+string.Join(",",a.GetCurrentAnimatorClipInfo(i).Select(c=>c.clip.name)));
                var ik=a.GetComponentInChildren<TwoBoneIKConstraint>(); var d=ik.data;
                var originalHandRotation=d.tip.rotation; SolvePreview(d.root,d.mid,d.tip,grip); d.tip.rotation=Quaternion.Slerp(originalHandRotation,grip.rotation,d.targetRotationWeight);
                float length=Vector3.Distance(d.root.position,d.mid.position)+Vector3.Distance(d.mid.position,d.tip.position);
                log.Add((aim?"AIM":"HIP")+" handTargetError="+Vector3.Distance(d.tip.position,grip.position)+" reachRatio="+Vector3.Distance(d.root.position,grip.position)/length);
                cameraGO=new GameObject("MigrationPreviewCamera"); cameraGO.hideFlags=HideFlags.HideAndDontSave;
                var cam=cameraGO.AddComponent<Camera>(); cam.cullingMask=1<<31; cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color(0.21f,0.23f,0.26f);
                var focus=(a.GetBoneTransform(HumanBodyBones.Head).position+a.GetBoneTransform(HumanBodyBones.Hips).position)*0.5f;
                cam.transform.position=focus+new Vector3(1.5f,0.35f,2.4f); cam.transform.LookAt(focus);
                cam.orthographic=true; cam.orthographicSize=0.48f; cam.nearClipPlane=0.01f; cam.farClipPlane=10;
                rt=new RenderTexture(900,900,24); cam.targetTexture=rt; cam.Render(); RenderTexture.active=rt;
                tex=new Texture2D(900,900,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,900,900),0,0); tex.Apply();
                var path="Temp/Xinghui_"+(aim?"Aim":"Hip")+".png"; File.WriteAllBytes(path,tex.EncodeToPNG()); log.Add("IMAGE="+path);
                builder.Clear();
            }
            finally
            {
                RenderTexture.active=previous;
                if(cameraGO) UnityEngine.Object.DestroyImmediate(cameraGO);
                if(tex) UnityEngine.Object.DestroyImmediate(tex);
                if(rt) UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }
        log.Add("PREVIEW_COMPLETE");
    }
    static void SolvePreview(Transform root,Transform mid,Transform tip,Transform target)
    {
        Vector3 origin=root.position, direction=(target.position-origin).normalized;
        float upper=Vector3.Distance(origin,mid.position),lower=Vector3.Distance(mid.position,tip.position);
        float distance=Mathf.Clamp(Vector3.Distance(origin,target.position),Mathf.Abs(upper-lower)+0.00001f,upper+lower-0.00001f);
        Vector3 bend=Vector3.ProjectOnPlane(mid.position-origin,direction).normalized;
        float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
        Vector3 elbow=origin+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        root.rotation=Quaternion.FromToRotation(mid.position-origin,elbow-origin)*root.rotation;
        mid.rotation=Quaternion.FromToRotation(tip.position-mid.position,target.position-mid.position)*mid.rotation;
        tip.rotation=target.rotation;
    }
}
