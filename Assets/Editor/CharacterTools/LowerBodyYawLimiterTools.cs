using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LowerBodyYawLimiterTools
{
    const string Request = "Temp/LowerBodyYawLimiterTests.request";
    const string Report = "Temp/LowerBodyYawLimiterTests.report.txt";
    static double nextPoll;
    static LowerBodyYawLimiterTools() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Move(Request, Request + ".started." + DateTime.UtcNow.Ticks);
        RunTests();
    }

    [MenuItem("Tools/Characters/Add Lower Body Yaw Limiter to Selected Player")]
    public static void AddToSelected()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play mode before adding the limiter."); return; }
        var root = Selection.activeGameObject;
        if (!root || EditorUtility.IsPersistent(root)) { Debug.LogWarning("Select the scene PlayerAvatar root first."); return; }
        Animator animator = null;
        var aim = root.GetComponent<LocalShoulderAimPresenter>();
        if (aim) animator = new SerializedObject(aim).FindProperty("characterAnimator").objectReferenceValue as Animator;
        if (!animator) animator = root.GetComponentInChildren<Animator>();
        if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.isHuman) { Debug.LogWarning("Selected player needs a valid Humanoid Animator."); return; }
        var existing = root.GetComponentInChildren<HumanoidLowerBodyYawLimiter>(true);
        if (existing) { Selection.activeObject = existing; Debug.Log("Limiter already exists; no duplicate added."); return; }
        var limiter = Undo.AddComponent<HumanoidLowerBodyYawLimiter>(root);
        var so = new SerializedObject(limiter);
        so.FindProperty("characterAnimator").objectReferenceValue = animator;
        so.FindProperty("facingReference").objectReferenceValue = root.transform;
        so.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(limiter);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeObject = limiter;
        Debug.Log("Added yaw limiter (75% retention, 20 degree cap). Save the scene; no prefab overrides were applied automatically.");
    }

    static object Invoke(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Near(float a, float b, string message) { Check(Mathf.Abs(a-b)<0.01f,message+": "+a+" vs "+b); }

    [MenuItem("Tools/Characters/Test Lower Body Yaw Limiter")]
    public static void RunTests()
    {
        GameObject root = null;
        try
        {
            Near(HumanoidLowerBodyYawLimiter.ComputeCorrection(60,0.75f,20),-15,"positive yaw");
            Near(HumanoidLowerBodyYawLimiter.ComputeCorrection(-60,0.75f,20),15,"negative yaw");
            Near(HumanoidLowerBodyYawLimiter.ComputeCorrection(120,0,20),-20,"safety cap");
            Near(HumanoidLowerBodyYawLimiter.ComputeCorrection(60,1,20),0,"bypass");
            Near(HumanoidLowerBodyYawLimiter.ComputeCorrection(350,0.5f,20),5,"wraparound");
            root = new GameObject("YawLimiterTestRoot") { hideFlags = HideFlags.HideAndDontSave };
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Recourses/PlayerModels/星绘/星绘泳装_Unity.fbx");
            Check(asset,"Test model not found");
            var model=UnityEngine.Object.Instantiate(asset,root.transform); model.hideFlags=HideFlags.HideAndDontSave;
            var a=model.GetComponent<Animator>();
            var limiter=root.AddComponent<HumanoidLowerBodyYawLimiter>();
            var so=new SerializedObject(limiter);
            so.FindProperty("characterAnimator").objectReferenceValue=a;
            so.FindProperty("facingReference").objectReferenceValue=root.transform;
            so.FindProperty("useAnimatorActivation").boolValue=false;
            so.FindProperty("blendDuration").floatValue=0;
            so.ApplyModifiedPropertiesWithoutUndo();
            Check((bool)Invoke(limiter,"TryInitialize"),"Initialization failed");
            var hips=a.GetBoneTransform(HumanBodyBones.Hips); var spine=a.GetBoneTransform(HumanBodyBones.Spine);
            var foot=a.GetBoneTransform(HumanBodyBones.LeftFoot); var knee=a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var lower=(Transform)limiter.GetType().GetField("lowerBodyRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(limiter);
            lower.rotation=Quaternion.AngleAxis(45,Vector3.up)*lower.rotation;
            var original=lower.localRotation; var upperPosition=spine.position; var upperRotation=spine.rotation;
            var originalFootPosition=foot.position;
            var renderer=model.GetComponentInChildren<SkinnedMeshRenderer>();
            Vector3[] beforeVertices=BakeVertices(renderer);
            float legLength=Vector3.Distance(foot.position,knee.position);
            Invoke(limiter,"ApplyCorrection",0.016f,true);
            var corrected=lower.localRotation;
            Check(Quaternion.Angle(original,corrected)>5,"Lower-body root was not corrected");
            Check(Vector3.Distance(spine.position,upperPosition)<0.00001f,"Upper body position changed");
            Check(Quaternion.Angle(spine.rotation,upperRotation)<0.01f,"Upper body rotation changed");
            Near(Vector3.Distance(foot.position,knee.position),legLength,"Leg length changed");
            float footDisplacement=Vector3.Distance(originalFootPosition,foot.position);
            Check(footDisplacement>0.001f,"Hips rotated but the actual foot did not move");
            var afterVertices=BakeVertices(renderer);
            float meshDisplacement=0;
            for(int i=0;i<beforeVertices.Length;i++) meshDisplacement=Mathf.Max(meshDisplacement,Vector3.Distance(beforeVertices[i],afterVertices[i]));
            Check(meshDisplacement>0.001f,"Hips rotated but the skinned mesh did not move");
            for(int i=0;i<120;i++) Invoke(limiter,"ApplyCorrection",0.016f,true);
            Check(Quaternion.Angle(lower.localRotation,corrected)<0.01f,"Correction accumulated");
            Invoke(limiter,"ApplyCorrection",0.016f,false);
            Check(Quaternion.Angle(lower.localRotation,original)<0.01f,"Inactive pose not restored");
            Invoke(limiter,"ApplyCorrection",0.016f,true); Invoke(limiter,"OnDisable");
            Check(Quaternion.Angle(lower.localRotation,original)<0.01f,"Disable did not restore pose");
            string samples="";
            a.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Player/PlayerLocomotion.controller");
            a.applyRootMotion=false; a.cullingMode=AnimatorCullingMode.AlwaysAnimate; a.Rebind();
            a.SetBool("ShoulderHeld",true);
            for(int i=1;i<a.layerCount;i++) a.SetLayerWeight(i,0);
            int layer=a.GetLayerIndex("ShoulderLocomotion");
            a.SetLayerWeight(layer,1);
            foreach(var direction in new[]{new Vector2(-0.707f,0.707f),new Vector2(0.707f,0.707f),new Vector2(-0.707f,-0.707f),new Vector2(0.707f,-0.707f)})
            {
                Invoke(limiter,"RestorePose");
                a.SetFloat("ShoulderMoveX",direction.x); a.SetFloat("ShoulderMoveY",direction.y);
                a.Play("Base Layer.Idle",0,0);
                a.Play("ShoulderLocomotion.ShoulderLowerBodyLocomotion",layer,0.3f); a.Update(0);
                var left=a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var right=a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                float actualYaw=Vector3.SignedAngle(Vector3.right,Vector3.ProjectOnPlane(right.position-left.position,Vector3.up),Vector3.up);
                Invoke(limiter,"ApplyCorrection",0.016f,true);
                so.Update();
                samples+="direction="+direction+" actualThighYaw="+actualYaw+" measured="+so.FindProperty("measuredYaw").floatValue+" correction="+so.FindProperty("appliedCorrection").floatValue+"\n";
            }
            Invoke(limiter,"RestorePose");
            File.WriteAllText(Report,"PASS: yaw signs, cap, bypass, wraparound, actual Humanoid initialization, pelvis correction, upper-body preservation, leg length, 120-frame no accumulation, inactive/disable restore.\nActual foot displacement="+footDisplacement+", maximum baked mesh vertex displacement="+meshDisplacement+"\nThis isolated model test does not verify scene execution or actual diagonal animation.\n");
            File.AppendAllText(Report,samples);
            Debug.Log("Lower body yaw limiter tests passed.");
        }
        catch(Exception e) {File.WriteAllText(Report,"FAIL: "+e); Debug.LogException(e);}
        finally {if(root) UnityEngine.Object.DestroyImmediate(root);}
    }

    static Vector3[] BakeVertices(SkinnedMeshRenderer renderer)
    {
        Check(renderer,"Missing skinned renderer");
        var mesh=new Mesh();
        try { renderer.BakeMesh(mesh); return mesh.vertices; }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
