using System;
using System.IO;
using FlyEditor.Flight;
using UnityEditor;
using UnityEngine;

namespace FlyEditor.Editor
{
    public sealed class DeveloperProgressWindow : EditorWindow
    {
        internal const string MenuPath = "Tools/FlyEditor/개발자 모드";
        internal enum Action { Add, Subtract, ZeroPoints, ResetProgress }
        [SerializeField] private int amount = 100;
        private PaperFlightController controller;
        private FlightResearch editProgress;
        private bool transitioning;
        private string message;
        private FlightResearch Progress => EditorApplication.isPlaying
            ? (controller != null ? controller.Session?.Research : null) : editProgress;

        [MenuItem(MenuPath, priority = 22)]
        private static void Toggle()
        {
            if (HasOpenInstances<DeveloperProgressWindow>()) GetWindow<DeveloperProgressWindow>().Close();
            else GetWindow<DeveloperProgressWindow>("개발자 모드").Show();
        }
        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, HasOpenInstances<DeveloperProgressWindow>());
            return true;
        }
        private void OnEnable()
        {
            titleContent = new GUIContent("개발자 모드");
            minSize = new Vector2(270, 235);
            EditorApplication.playModeStateChanged += OnPlayState;
            Bind();
        }
        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayState;
        private void OnPlayState(PlayModeStateChange state)
        {
            transitioning = state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode;
            Bind();
        }
        private void OnFocus() => Bind();
        private void Bind()
        {
            controller = null;
            editProgress = null;
            message = null;
            if (transitioning || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) return;
            if (EditorApplication.isPlaying)
                controller = UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
            else
                ReadEditProgress();
        }
        private void ReadEditProgress()
        {
            try { editProgress = new FlightResearch(ResearchFileStore.OpenCurrent()); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { editProgress = null; message = "저장 파일을 읽을 수 없습니다. " + ex.Message; }
        }
        private void OnInspectorUpdate()
        {
            if (!transitioning && EditorApplication.isPlaying && controller == null)
                controller = UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
            Repaint();
        }
        private void OnGUI()
        {
            var progress = Progress;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("현재 연구점", progress == null ? "—" : progress.Points.ToString(), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(progress == null || transitioning))
            {
                amount = EditorGUILayout.IntField("조정할 연구점", amount);
                using (new EditorGUI.DisabledScope(amount <= 0))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("추가")) Apply(Action.Add, amount);
                    if (GUILayout.Button("차감")) Apply(Action.Subtract, amount);
                }
                if (GUILayout.Button("연구점만 0으로")) Apply(Action.ZeroPoints);
                EditorGUILayout.Space();
                if (GUILayout.Button("전체 진행 초기화…") && EditorUtility.DisplayDialog(
                    "전체 진행 초기화", "연구점, 구매한 업그레이드, 비행 횟수와 최고 기록을 초기화합니다. 진행 중인 비행도 종료됩니다.",
                    "초기화", "취소"))
                    Apply(Action.ResetProgress);
            }
            if (message != null) EditorGUILayout.HelpBox(message, MessageType.Info);
            else if (progress == null) EditorGUILayout.HelpBox("InGame 씬을 열거나 Play를 종료하세요.", MessageType.Info);
        }
        internal bool Apply(Action action, int quantity = 0)
        {
            if (transitioning || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) return false;
            if (!EditorApplication.isPlaying) ReadEditProgress();
            var progress = Progress;
            if (progress == null) return false;
            bool ok;
            if (action == Action.ResetProgress)
                ok = EditorApplication.isPlaying ? controller.ResetDeveloperProgress() : progress.ResetDeveloperProgress();
            else
            {
                if (action != Action.ZeroPoints && quantity <= 0) return false;
                int delta = action == Action.ZeroPoints ? -progress.Points : action == Action.Subtract ? -quantity : quantity;
                ok = EditorApplication.isPlaying ? controller.AdjustDeveloperPoints(delta) : progress.AdjustDeveloperPoints(delta);
            }
            message = ok ? action == Action.ResetProgress ? "전체 진행을 초기화했습니다." : "연구점을 저장했습니다." : progress.LastError;
            return ok;
        }
    }
}
