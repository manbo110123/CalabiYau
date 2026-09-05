#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Creates the small, code-driven locomotion controller used by the local 3C prototype.
/// Animation owns pose only; LocalCharacterMotor remains the authority for position and jump height.
/// </summary>
public static class CreatePlayerLocomotionAnimator
{
    private const string ControllerPath = "Assets/Animations/Player/PlayerLocomotion.controller";
    private const string IdlePath = "Assets/Recourses/Animations/基础动作/@Idle.fbx";
    private const string WalkPath = "Assets/Recourses/Animations/持枪移动/Girlscout T Masuyama@Rifle Walk (1).fbx";
    private const string RunPath = "Assets/Recourses/Animations/基础动作/@Sprint forward.fbx";
    private const string HoverPath = "Assets/Recourses/Animations/基础动作/@Hover.fbx";

    private const string MoveSpeedParameter = "MoveSpeed";
    private const string IsWalkingParameter = "IsWalking";
    private const string IsGroundedParameter = "IsGrounded";

    [MenuItem("Tools/3C/Create or Rebuild Player Locomotion Animator")]
    private static void CreateOrRebuild()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Rebuild Player Locomotion Animator",
                "This replaces the existing controller and its transitions. Continue?",
                "Rebuild",
                "Cancel");

            if (!replace)
            {
                return;
            }

            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimationClip idle = FindClip(IdlePath, "Idle");
        AnimationClip walk = FindClip(WalkPath, "Rifle Walk");
        AnimationClip run = FindClip(RunPath, "Sprint forward");
        AnimationClip hover = FindClip(HoverPath, "Hover");

        if (idle == null || walk == null || run == null || hover == null)
        {
            EditorUtility.DisplayDialog(
                "Cannot Create Animator",
                "One or more expected animation clips could not be found. Check the Console for details.",
                "OK");
            return;
        }

        string directory = Path.GetDirectoryName(ControllerPath);
        if (!AssetDatabase.IsValidFolder(directory))
        {
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(MoveSpeedParameter, AnimatorControllerParameterType.Float);
        controller.AddParameter(IsWalkingParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(IsGroundedParameter, AnimatorControllerParameterType.Bool);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = stateMachine.AddState("Idle", new Vector3(250f, 40f));
        AnimatorState walkState = stateMachine.AddState("Walk_Rifle", new Vector3(500f, -45f));
        AnimatorState runState = stateMachine.AddState("Run", new Vector3(500f, 80f));
        AnimatorState hoverState = stateMachine.AddState("Hover", new Vector3(500f, 200f));

        idleState.motion = idle;
        walkState.motion = walk;
        runState.motion = run;
        hoverState.motion = hover;
        stateMachine.defaultState = idleState;

        AddMoveTransition(idleState, walkState, true);
        AddMoveTransition(idleState, runState, false);
        AddStopTransition(walkState, idleState);
        AddStopTransition(runState, idleState);
        AddWalkRunSwitch(walkState, runState, false);
        AddWalkRunSwitch(runState, walkState, true);

        AnimatorStateTransition toHover = stateMachine.AddAnyStateTransition(hoverState);
        ConfigureTransition(toHover, 0.06f);
        toHover.AddCondition(AnimatorConditionMode.IfNot, 0f, IsGroundedParameter);

        AddLandingTransition(hoverState, idleState, 0.08f, true, null);
        AddLandingTransition(hoverState, walkState, 0.08f, false, true);
        AddLandingTransition(hoverState, runState, 0.08f, false, false);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);
    }

    private static AnimationClip FindClip(string assetPath, string expectedName)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is AnimationClip clip && clip.name == expectedName)
            {
                return clip;
            }
        }

        Debug.LogError($"Expected animation clip '{expectedName}' was not found in '{assetPath}'.");
        return null;
    }

    private static void AddMoveTransition(AnimatorState from, AnimatorState to, bool walking)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureTransition(transition, 0.12f);
        transition.AddCondition(AnimatorConditionMode.Greater, 0.08f, MoveSpeedParameter);
        transition.AddCondition(walking ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, IsWalkingParameter);
    }

    private static void AddStopTransition(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureTransition(transition, 0.14f);
        transition.AddCondition(AnimatorConditionMode.Less, 0.08f, MoveSpeedParameter);
    }

    private static void AddWalkRunSwitch(AnimatorState from, AnimatorState to, bool walking)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureTransition(transition, 0.1f);
        transition.AddCondition(walking ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, IsWalkingParameter);
        transition.AddCondition(AnimatorConditionMode.Greater, 0.08f, MoveSpeedParameter);
    }

    private static void AddLandingTransition(
        AnimatorState from,
        AnimatorState to,
        float minimumSpeed,
        bool idle,
        bool? walking)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        ConfigureTransition(transition, 0.08f);
        transition.AddCondition(AnimatorConditionMode.If, 0f, IsGroundedParameter);
        transition.AddCondition(
            idle ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater,
            minimumSpeed,
            MoveSpeedParameter);

        if (walking.HasValue)
        {
            transition.AddCondition(
                walking.Value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f,
                IsWalkingParameter);
        }
    }

    private static void ConfigureTransition(AnimatorStateTransition transition, float duration)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.offset = 0f;
        transition.interruptionSource = TransitionInterruptionSource.None;
    }
}
#endif
