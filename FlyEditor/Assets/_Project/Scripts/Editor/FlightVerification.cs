using System;
using FlyEditor.Flight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FlyEditor.Editor
{
    // Batch-only verification. No scene-generation menu or runtime auto-bootstrap.
    public static class FlightVerification
    {
        private const string SessionKey = "FlyEditor.FlightVerification.Play";
        private static PaperFlightController controller;
        private static Mouse mouse;
        private static Keyboard researchKeyboard;
        private static int stage;
        private static float startHeight;
        private static Vector3 previewAnchor, aimingHand;
        private static bool sawMovingThrow;
        private static bool failed;
        private static double deadline;
        private static InputSettings originalSettings;
        private static InputSettings testSettings;
        private static double stageDeadline;
        private static int expectedPoints;
        private const string TestSaveKey = "FlyEditor.FlightVerification.ResearchPath";

        public static void Run()
        {
            Require(Application.isBatchMode, "Verification must run in a separate batch editor.");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/InGame.unity");
            BeginVerification();
        }

        [MenuItem("Tools/FlyEditor/Verify Flight Balance", priority = 21)]
        public static void RunInEditor()
        {
            Require(!EditorApplication.isPlaying, "Stop Play Mode before running verification.");
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Require(active.path == "Assets/_Project/Scenes/InGame.unity" && !active.isDirty,
                "Open the saved InGame scene first; unsaved work will not be overwritten.");
            BeginVerification();
        }

        public static void VerifyVisualMotion()
        {
            const float step=.02f;
            foreach(int fps in new[]{30,60,120,144,240})foreach(float speed in new[]{13f,100f,300f}){
                var tuning=new FlightTuning{launchSpeed=speed,speedCap=400,launchHeight=100,gravity=.1f,
                    drag=0,climbDrag=0,sink=0,releasePitchDuration=1000};
                var model=new FlightModel(tuning.Copy());var reference=new FlightModel(tuning.Copy());
                model.Tick(true,false,false);reference.Tick(true,false,false);
                var motion=new FlightVisualMotion();motion.Reset(model,0);
                double fixedAt=0;int ticks=0;float rawCamera=7,viewCamera=7,oldRaw=-7,oldView=-7,rawBack=0,viewBack=0;
                for(int frame=1;frame<=fps*3;frame++){
                    double now=(double)frame/fps;
                    while((ticks+1)*.02<=now+1e-10){
                        var before=new FlightVisualMotion.Pose(model);model.FixedTick(step);reference.FixedTick(step);
                        fixedAt=++ticks*.02;motion.RecordStep(model,before,fixedAt);
                    }
                    var pose=motion.Sample(model,now,step);
                    float blend=1f-Mathf.Exp(-12f/fps);
                    rawCamera=Mathf.Lerp(rawCamera,model.Distance+7,blend);viewCamera=Mathf.Lerp(viewCamera,pose.position.x+7,blend);
                    float raw=model.Distance-rawCamera,view=pose.position.x-viewCamera;
                    if(frame>fps){rawBack=Mathf.Max(rawBack,oldRaw-raw);viewBack=Mathf.Max(viewBack,oldView-view);}
                    oldRaw=raw;oldView=view;
                    Require(pose.position.x<=model.Distance+.0001f && model.Distance-pose.position.x<=speed*step+.01f,
                        "Render pose extrapolated or delayed more than a physics step");
                    Require(model.Distance==reference.Distance && model.Energy==reference.Energy && model.Pitch==reference.Pitch,
                        "Presentation changed flight physics");
                }
                Require(viewBack<.005f && (rawBack<.01f || viewBack<rawBack*.05f),"Render-camera backtracking at "+fps+" FPS / "+speed+" m/s");
                var held=motion.Sample(model,100,step);Require(Mathf.Abs(held.position.x-model.Distance)<.001f,"Long frame extrapolated");
                model.Tick(false,false,true);var reset=motion.Sample(model,101,step);
                Require(reset.position.x==0 && reset.pitch==model.Pitch,"Retry blended across previous run");
                model.SetLaunchPose(30,6);model.Tick(true,false,false);
                var release=motion.Sample(model,101.01,step);Require(release.position.x==0 && release.position.y==6 && release.pitch==30,"Hand release used stale ready pose");
                var equipped=new FlightModel(tuning.Copy());Require(motion.Sample(equipped,102,step).position.x==0,"New model used old flight samples");
                Debug.Log("[MotionClock] fps="+fps+" speed="+speed+" oldBack="+rawBack.ToString("F6")+" newBack="+viewBack.ToString("F6"));
            }
            Debug.Log("[MotionClock] PASS 30/60/120/144/240 FPS, 13/100/300 m/s, unchanged physics, retry, release and equipment reset.");
        }

        private static void BeginVerification()
        {
            ResearchWebVerification.VerifyAsset();
            PaperFoldingVerification.Run();
            VerifyModel();
            VerifyVisualMotion();
            VerifyResearch();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var item in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject) == 0,
                        "Missing script: " + item.name);
            var savedController = UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
            Require(savedController != null, "Controller missing from saved scene.");
            var savedTuning = new SerializedObject(savedController).FindProperty("tuning");
            var defaults = new FlightTuning();
            Require(Mathf.Approximately(savedTuning.FindPropertyRelative("climbPitch").floatValue, defaults.climbPitch)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("divePitch").floatValue, defaults.divePitch)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("releasePitchDuration").floatValue, defaults.releasePitchDuration)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("gravity").floatValue, defaults.gravity)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("launchSpeed").floatValue, defaults.launchSpeed)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("sink").floatValue, defaults.sink)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("flightPathResponse").floatValue, defaults.flightPathResponse)
                && Mathf.Approximately(savedTuning.FindPropertyRelative("pitchResponse").floatValue, defaults.pitchResponse),
                "Saved scene pitch settings differ from the verified defaults.");
            Require(Mathf.Approximately(new SerializedObject(savedController).FindProperty("cameraFollowSharpness").floatValue, 12f),
                "Saved camera follow response differs from the light-flight tuning.");
            Require(Camera.main != null, "Scene camera missing.");
            Debug.Log("[PaperFlight] SAVED SCENE CHECKS PASSED");
            string researchPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flyeditor-play-" + Guid.NewGuid().ToString("N") + ".json");
            // Isolated fixture provides enough points to exercise UI purchases after the first landing.
            new ResearchFileStore(researchPath).Save(new ResearchSave { points = 185 });
            Environment.SetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS", researchPath);
            if (Application.isBatchMode) VerifyDeveloperEditMode(researchPath);
            SessionState.SetString(TestSaveKey, researchPath);
            SessionState.SetBool(SessionKey, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void ResumePlayVerification()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            deadline = EditorApplication.timeSinceStartup + 100;
            EditorApplication.update += PlayTick;
            Application.logMessageReceived += WatchLog;
        }

        private static void WatchLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) failed = true;
        }

        private static void Press(bool down,float mouseY=0f)
        {
            var input = new MouseState { position = new Vector2(120f,mouseY) };
            InputSystem.QueueStateEvent(mouse,input.WithButton(MouseButton.Left, down));
        }

        // Contact points of the sole region, in shoe-local Unity units.
        private static readonly Vector3 SoleToe=new Vector3(.15f,-.03f,0f);
        private static readonly Vector3 SoleHeel=new Vector3(-.06f,-.03f,0f);

        private static void VerifyThrowRig(PaperFlightController game)
        {
            var rig = game.Thrower;
            Require(rig != null && rig.HoldingPlane, "Thrower missing at start.");
            var fore = rig.Grip.parent;
            var upper = fore.parent;
            var foot = rig.transform.Find("root/hip/front-thigh/front-shin/front-shoe");
            var hip = rig.transform.Find("root/hip");
            var rearFoot = hip.Find("rear-thigh/rear-shin/rear-shoe");
            Vector3 rearPlanted = rearFoot.TransformPoint(SoleToe);
            float hipStart = hip.position.x;
            var farHand = hip.Find("torso/rear-upper/rear-fore/far-arm").GetComponent<SpriteRenderer>();
            Require(farHand.sortingOrder < hip.Find("rear-thigh/rear-trousers").GetComponent<SpriteRenderer>().sortingOrder
                && farHand.sortingOrder < hip.Find("front-thigh/front-trousers").GetComponent<SpriteRenderer>().sortingOrder,
                "The far arm must draw behind both legs.");
            Vector3 planted = foot.position;
            float arm = Vector3.Distance(upper.position,fore.position);
            float forearm = Vector3.Distance(fore.position,rig.Grip.position);
            Vector3 start = rig.Grip.position;
            rig.Begin();
            for (int i=0;i<38;i++) Require(!rig.Advance(.01f),"Throw released during windup.");
            Require(rig.Grip.position.x < start.x && hip.position.x < hipStart-.1f && rig.State == LaunchThrower.Phase.Windup,
                "Windup should take the hand backward.");
            rig.Prepare(new Vector3(0,game.Model.Height,0));
            Require(Vector3.Distance(start,rig.Grip.position)<.0001f,"Canceled windup changed the next start.");
            int releases = 0;
            rig.Begin();
            for(int i=0;i<360;i++)
            {
                rig.Begin();
                if(rig.Advance(1f/240f))
                {
                    releases++;
                    Require(hip.position.x>hipStart+.2f,"Release must transfer weight toward the lead foot.");
                    Require(Vector3.Distance(rig.Grip.position,new Vector3(0,game.Model.Height,0))<.0001f,
                        "Release hand does not meet the flight origin.");
                }
                Require(Mathf.Abs(Vector3.Distance(upper.position,fore.position)-arm)<.0001f
                    && Mathf.Abs(Vector3.Distance(fore.position,rig.Grip.position)-forearm)<.0001f
                    && Vector3.Distance(foot.position,planted)<.0005f
                    && Vector3.Distance(rearFoot.TransformPoint(SoleToe),rearPlanted)<.0005f,"A limb stretched or a foot slid.");
            }
            Require(releases==1 && rig.State==LaunchThrower.Phase.Resting,"Throw release count or final pose.");
            VerifyDirectionalThrow(rig,new Vector3(0,game.Model.Height,0));
            VerifyThrowGaze(rig,new Vector3(0,game.Model.Height,0));
            rig.Prepare(new Vector3(0,game.Model.Height,0));
            Debug.Log("[Thrower] PASS: Spine-baked windup, fixed limb lengths, planted foot, one release at flight origin and reset.");
        }
        private static void VerifyThrowGaze(LaunchThrower rig,Vector3 launch)
        {
            var head=rig.transform.Find("root/hip/torso/head");
            var eye=head.Find("gaze");
            Require(eye && eye.GetComponentInChildren<SpriteRenderer>(),"Visible gaze region missing.");
            foreach(float angle in new[]{FlightModel.MinLaunchAngle,FlightModel.MaxLaunchAngle})
            {
                rig.Prepare(launch);rig.StartAiming();rig.SetAim(angle);
                Vector3 neutralEye=eye.localPosition;
                for(int i=0;i<30;i++){rig.Advance(.02f);rig.UpdateGaze(launch,.02f);}
                Require(Mathf.Abs(Mathf.DeltaAngle(eye.eulerAngles.z,angle))<.1f
                    && Mathf.DeltaAngle(0,head.eulerAngles.z)*Mathf.Sign(angle)>5f,
                    "Head and eye did not face the held aim.");
                rig.Begin();Require(rig.Advance(.76f),"Gaze test missed release.");
                // A target on the opposite side of horizontal distinguishes actual
                // tracking from simply holding the launch angle into follow-through.
                float targetAngle=angle<0f ? 25f : -20f;
                for(int i=0;i<25;i++)
                {
                    rig.Advance(.02f);
                    Vector3 target=head.TransformPoint(neutralEye)+Quaternion.Euler(0,0,targetAngle)*Vector3.right*10f;
                    rig.UpdateGaze(target,.02f);
                }
                Require(Mathf.Abs(Mathf.DeltaAngle(eye.eulerAngles.z,targetAngle))<.2f,"Gaze stayed on the launch angle.");
                for(int i=0;i<27;i++){rig.Advance(.02f);rig.UpdateGaze(launch,.02f);}
                Require(rig.State==LaunchThrower.Phase.Resting && Vector3.Distance(eye.localPosition,neutralEye)<.0001f
                    && Quaternion.Angle(eye.localRotation,Quaternion.identity)<.01f,"Gaze did not settle.");
            }
            Debug.Log("[ThrowGaze] PASS: aimed head and eye, tracked target after release, settled and reset.");
        }

        private static void VerifyDirectionalThrow(LaunchThrower rig,Vector3 launch)
        {
            var fore=rig.Grip.parent;
            var upper=fore.parent;
            var torso=upper.parent;
            var hip=torso.parent;
            var front=hip.Find("front-thigh/front-shin/front-shoe");
            var rear=hip.Find("rear-thigh/rear-shin/rear-shoe");
            rig.Prepare(launch);
            float arm=Vector3.Distance(upper.position,fore.position);
            float lower=Vector3.Distance(fore.position,rig.Grip.position);
            Vector3 frontPlant=front.position,rearPlant=rear.TransformPoint(SoleToe);
            Quaternion frontFlat=front.rotation;
            float flatHeelY=rear.TransformPoint(SoleHeel).y;
            rig.StartAiming();
            rig.SetAim(FlightModel.MinLaunchAngle);
            rig.Advance(.24f);
            Vector3 lowHand=rig.Grip.position,lowElbow=fore.position,lowBody=torso.up;
            rig.Advance(2f);
            Require(lowHand.x>hip.position.x && Vector3.Distance(lowHand,rig.Grip.position)<.0001f,
                "Holding aim must keep the hand forward in the ready stance.");
            rig.SetAim(FlightModel.MaxLaunchAngle);rig.Advance(0);
            Require(rig.Grip.position.y>lowHand.y+.35f && Vector3.Distance(fore.position,lowElbow)>.2f
                && Vector3.Angle(lowBody,torso.up)>10,"Drag on held pose should change hand, elbow and torso.");
            rig.SetAim(FlightModel.MinLaunchAngle);rig.Advance(0);
            Require(Vector3.Distance(rig.Grip.position,lowHand)<.0001f,"Aim offsets accumulated.");
            float lastExit=-180;
            var freeArm=torso.Find("rear-upper");
            var freeForearm=freeArm.Find("rear-fore");
            var handTip=new Vector3(.28f,0f,0f);
            foreach(float angle in new[]{FlightModel.MinLaunchAngle,0f,10f,22f,FlightModel.MaxLaunchAngle})
            {
                rig.Prepare(launch);rig.StartAiming();rig.SetAim(angle);rig.Advance(.24f);
                Vector3 readyHand=rig.Grip.position;
                float readyReach=freeForearm.TransformPoint(handTip).x-freeArm.position.x;
                float readyHipX=hip.position.x;
                rig.Begin();
                Require(!rig.Advance(.38f),"Backswing released the plane early.");
                Vector3 drawn=rig.Grip.position;
                float forwardReach=freeForearm.TransformPoint(handTip).x-freeArm.position.x;
                Require(forwardReach>.4f && forwardReach>readyReach+.25f,
                    "Free hand should swing ahead of the shoulder during the draw at "+angle);
                Require(drawn.x<hip.position.x && drawn.x<readyHand.x-1f && hip.position.x<readyHipX,
                    "Mouse-up must draw the hand back and shift weight onto the rear foot at "+angle);
                if (angle==FlightModel.MinLaunchAngle)
                    Require(drawn.y<hip.position.y,"Low backswing should pass below the hip.");
                if (angle==FlightModel.MinLaunchAngle || angle>=LaunchThrower.NeutralAim)
                    Require(Vector3.Distance(upper.position,drawn)>(arm+lower)*.9f,
                        "The arm should open out on the backswing at "+angle);
                Vector3 drawnHip=hip.position;
                Quaternion loadedArm=upper.rotation, loadedForearm=fore.rotation;
                var rearShin=rear.parent;
                float drawnBend=Quaternion.Angle(rearShin.localRotation,Quaternion.identity);
                Require(!rig.Advance(.12f) && hip.position.y<drawnHip.y-.05f
                    && Quaternion.Angle(rearShin.localRotation,Quaternion.identity)>drawnBend+4f
                    && Vector3.Distance(rear.TransformPoint(SoleToe),rearPlant)<.0005f && Vector3.Distance(front.position,frontPlant)<.0005f
                    && Mathf.Abs(rear.TransformPoint(SoleHeel).y-flatHeelY)<.0005f
                    && Quaternion.Angle(loadedArm,upper.rotation)<.1f && Quaternion.Angle(loadedForearm,fore.rotation)<.1f,
                    "Gather should bend the rear knee under a held throwing arm with both feet planted.");
                Vector3 loadedHip=hip.position, loadedShoulder=upper.position;
                Require(!rig.Advance(.09f) && hip.position.x>loadedHip.x+.05f && upper.position.x>loadedShoulder.x+.05f
                    && Quaternion.Angle(loadedArm,upper.rotation)<.1f && Quaternion.Angle(loadedForearm,fore.rotation)<.1f,
                    "Hips and shoulder must move forward before the throwing arm at "+angle);
                Require(!rig.Advance(.06f) && (Quaternion.Angle(loadedArm,upper.rotation)>1f ||
                    Quaternion.Angle(loadedForearm,fore.rotation)>1f),"Arm failed to follow the body drive.");
                int released=0;
                int releaseFrame=-1;
                float releaseHipX=0f;
                Quaternion exitForearm=Quaternion.identity;
                for(int i=0;i<240;i++)
                {
                    if (rig.Advance(1f/240f))
                    {
                        released++;
                        releaseFrame=i;
                        releaseHipX=hip.position.x;
                        Require(freeForearm.position.x<freeArm.position.x &&
                            freeForearm.TransformPoint(handTip).x<hip.position.x,
                            "Free elbow and hand should sweep behind the body at release at "+angle);
                        Require(rear.TransformPoint(SoleHeel).y>flatHeelY+.06f,"Back heel did not lift during the drive.");
                        exitForearm=fore.rotation;
                        float elapsed=.65f+(i+1)/240f;
                        Require(Mathf.Abs(elapsed-.75f)<=1.01f/240f,"Unexpected body-led release timing.");
                        float exit=Mathf.Atan2(fore.right.y,fore.right.x)*Mathf.Rad2Deg;
                        var releasePoint=new Vector3(launch.x,rig.ReleaseHeight,launch.z);
                        Require(exit>lastExit+1 && Vector3.Distance(rig.Grip.position,releasePoint)<.0002f
                            && Mathf.Abs(Mathf.DeltaAngle(rig.Grip.eulerAngles.z,angle))<.01f,"Directional release mismatch.");
                        if (angle == FlightModel.MinLaunchAngle)
                            Require(rig.Grip.position.x>hip.position.x && rig.Grip.position.y<hip.position.y+.2f,
                                "Underhand release must pass forward at waist height.");
                        lastExit=exit;
                    }
                    if (releaseFrame>=0 && i==releaseFrame+48)
                    {
                        Require(Mathf.Abs(front.position.x-hip.position.x)<Mathf.Abs(front.position.x-releaseHipX)-.15f,
                            "Follow-through should carry the pelvis toward the front foot.");
                        float roll=-Mathf.DeltaAngle(0f,rear.eulerAngles.z);
                        Require(freeForearm.TransformPoint(handTip).x<hip.position.x,"Free hand should trail through follow-through.");
                        Require(roll>13f && roll<=14.1f,"Rear heel should rise slightly as weight passes forward.");
                        if (angle==FlightModel.MinLaunchAngle || angle==FlightModel.MaxLaunchAngle)
                            Require(Quaternion.Angle(exitForearm,fore.rotation)>15f,"Arm stopped at release.");
                    }
                    Require(Mathf.Abs(Vector3.Distance(upper.position,fore.position)-arm)<.0001f
                        && Mathf.Abs(Vector3.Distance(rig.Grip.position,fore.position)-lower)<.0001f
                        && Vector3.Distance(front.position,frontPlant)<.0005f
                        && Quaternion.Angle(front.rotation,frontFlat)<.05f
                        && Vector3.Distance(rear.TransformPoint(SoleToe),rearPlant)<.0005f,"Directional throw stretched or slid.");
                }
                Require(released==1 && rig.State==LaunchThrower.Phase.Resting,"Directional throw did not settle.");
                Require(hip.position.x>releaseHipX+.1f,"Final stance lost the forward weight transfer.");
                Require(Mathf.Abs(Mathf.DeltaAngle(-5f,rear.eulerAngles.z))<.05f,"Heel did not ease into its final pose.");
            }
            for (float angle=FlightModel.MinLaunchAngle;angle<=FlightModel.MaxLaunchAngle;angle+=.25f)
            {
                rig.Prepare(launch);rig.StartAiming();rig.SetAim(angle);rig.Advance(1f);rig.Begin();
                if (angle>-6f && angle<6f)
                {
                    while (rig.State==LaunchThrower.Phase.Windup)
                    {
                        Quaternion oldUpper=upper.rotation, oldFore=fore.rotation;
                        rig.Advance(1f/240f);
                        Require(Quaternion.Angle(oldUpper,upper.rotation)<25f && Quaternion.Angle(oldFore,fore.rotation)<25f,
                            "Arm flipped during a blended throw at "+angle);
                    }
                }
                else rig.Advance(1f);
                Require(Vector3.Distance(rig.Grip.position,new Vector3(launch.x,rig.ReleaseHeight,launch.z))<.0002f,
                    "Preview and grip diverged at "+angle);
            }
            rig.Prepare(launch);rig.StartAiming();rig.SetAim(-12f);rig.Begin();
            Require(!rig.Advance(.38f) && rig.Grip.position.x<hip.position.x && !rig.Advance(.36f) && rig.Advance(.011f),
                "A quick click must still play the backswing before releasing.");
            Debug.Log("[ThrowLead] PASS: full draw, gather, hip and shoulder lead, arm catch-up and timed release.");
            Debug.Log("[ThrowStance] PASS: rear knee bends during gather, feet planted, forward finish and sustained arm swing.");
            Debug.Log("[HeelRoll] PASS: flat front sole, planted rear toe, lift at release and gentle settling.");
            Debug.Log("[CounterArm] PASS: forward reach on draw and rearward balance at release and follow-through.");
            Debug.Log("[ThrowAimPose] PASS: held ready stance, post-input backswing, weight transfer, fixed arms and feet, full angle sweep.");
        }

        private static void VerifyForecast(PaperFlightController game)
        {
            foreach(float speed in new[]{13f,150f})
            foreach(float angle in new[]{FlightModel.MinLaunchAngle,10f,FlightModel.MaxLaunchAngle})
            {
                var model=new FlightModel(new FlightTuning { launchSpeed=speed,speedCap=speed+10 });
                var rig=game.Thrower;
                rig.Prepare(new Vector3(0,model.Height,0));rig.StartAiming();rig.SetAim(angle);
                game.Trajectory.Show(model,angle,.02f,rig.ReleaseHeight);
                var heading = Quaternion.Euler(0,0,angle)*Vector3.right;
                foreach(var nose in new[]{new Vector3(-2.6f,4.4f,0),new Vector3(-1.5f,4.1f,0)})
                {
                    game.Trajectory.DrawFromNose(nose);
                    VerifyFirstDash(game,nose,heading);
                }
                var samples=game.Trajectory.Samples;
                Require(model.State==FlightModel.Phase.Ready && model.PickupsCollected==0,"Preview mutated live flight.");
                var course=new FlightCourse();
                model.SetLaunchPose(angle,rig.ReleaseHeight);model.Tick(true,false,false);model.Tick(false,false,false);
                var nosePath = new Vector3[samples.Count];
                nosePath[0] = AirborneNose(model);
                for(int i=1;i<samples.Count;i++)
                {
                    course.Step(model,.02f);
                    nosePath[i] = AirborneNose(model);
                    Require(Vector2.Distance(samples[i],new Vector2(model.Distance,model.Height))<.0001f,"Preview mismatch.");
                }
                VerifyRenderedArc(game,nosePath);
            }
            var lowStart=new FlightModel(new FlightTuning());
            const float lowAngle=FlightModel.MinLaunchAngle;
            float lowHeight=PaperPlaneGeometry.ContactHeight(lowAngle)+.12f;
            game.Trajectory.Show(lowStart,lowAngle,.02f,lowHeight);
            lowStart.SetLaunchPose(lowAngle,lowHeight);lowStart.Tick(true,false,false);lowStart.Tick(false,false,false);
            var shortCourse=new FlightCourse();
            for(int i=1;i<game.Trajectory.Samples.Count;i++) shortCourse.Step(lowStart,.02f);
            Require(lowStart.State==FlightModel.Phase.Landed && lowStart.Distance<10f &&
                Vector2.Distance(game.Trajectory.Samples[game.Trajectory.Samples.Count-1],
                    new Vector2(lowStart.Distance,lowStart.Height))<.0001f,"Short preview must end at first contact.");
            game.Trajectory.Hide();
            game.Thrower.Prepare(new Vector3(0,game.Model.Height,0));
            Debug.Log("[Aim10m] PASS: rendered endpoint follows the first ten metres of the physical nose path at both launch speeds.");
        }
        private static Vector3 AirborneNose(FlightModel model) => new Vector3(model.Distance,model.Height,0)
            + Quaternion.Euler(0,0,model.Pitch)*PaperPlaneGeometry.Nose;
        private static void VerifyRenderedArc(PaperFlightController game,Vector3[] path)
        {
            var holder = game.transform.Find("출발 예상 궤도");
            var mesh = holder.GetComponent<MeshFilter>().sharedMesh;
            float prefix=0f;
            for(int j=1;j<path.Length-1;j++) prefix+=Vector3.Distance(path[j-1],path[j]);
            float lastLength=Vector3.Distance(path[path.Length-2],path[path.Length-1]);
            Require(prefix<10f && prefix+lastLength>=9.9999f,"Expected a forecast reaching ten metres.");
            Vector3 tenMetres=Vector3.Lerp(path[path.Length-2],path[path.Length-1],(10f-prefix)/lastLength);
            foreach (Vector3 anchor in new[]{new Vector3(-2.6f,4.4f,0),path[0]})
            {
                game.Trajectory.DrawFromNose(anchor);
                var vertices = mesh.vertices;
                Vector3 offset = anchor-path[0];
                int last=vertices.Length-1;
                Vector3 endpoint=holder.TransformPoint((vertices[last]+vertices[last-1])*.5f)-offset;
                Require(Vector3.Distance(endpoint,tenMetres)<.0002f,"Last visible dash must end at ten metres.");
                for(int i=0;i<vertices.Length;i+=2)
                {
                    Vector3 center = holder.TransformPoint((vertices[i]+vertices[i+1])*.5f)-offset;
                    Require(Vector3.Distance(center,path[0])<=10.001f,"Aim dashes exceed the preview length.");
                    float nearest = float.PositiveInfinity;
                    for(int j=1;j<path.Length;j++)
                    {
                        Vector3 segment=path[j]-path[j-1];
                        float t=Mathf.Clamp01(Vector3.Dot(center-path[j-1],segment)/Mathf.Max(.0000001f,segment.sqrMagnitude));
                        nearest=Mathf.Min(nearest,Vector3.Distance(center,path[j-1]+segment*t));
                    }
                    Require(nearest<.0002f,"A painted dash leaves the physical nose path.");
                }
            }
        }

        private static void VerifyFirstDash(PaperFlightController game,Vector3 nose,Vector3 forward)
        {
            var holder=game.transform.Find("출발 예상 궤도");
            var vertices=holder.GetComponent<MeshFilter>().sharedMesh.vertices;
            Require(vertices.Length >= 4,"Missing first dash.");
            Vector3 start=holder.TransformPoint((vertices[0]+vertices[1])*.5f);
            Vector3 next=holder.TransformPoint((vertices[2]+vertices[3])*.5f);
            Require(Vector3.Distance(start,nose)<.0001f && Vector3.Dot((next-start).normalized,forward.normalized)>.99f,
                "First dash must touch the rendered nose and follow its direction.");
        }
        private static void VerifyHeldNose(PaperFlightController game)
        {
            var plane=game.transform.Find("Folded paper plane");
            Vector3 nose=plane.TransformPoint(PaperPlaneGeometry.Nose);
            VerifyFirstDash(game,nose,nose-plane.position);
        }
        private static void VerifyOpeningCourse(PaperFlightController game)
        {
            // Simulate both lanes with the starter plane and the production hold/release controls.
            foreach (bool lowLane in new[]{true,false})
            {
                var plane=new FlightModel(new FlightTuning());
                var course=new FlightCourse();
                var rig=game.Thrower;
                rig.Prepare(new Vector3(0,plane.Height,0));rig.StartAiming();
                float aim=lowLane ? -10f : 18f;
                rig.SetAim(aim);plane.SetLaunchPose(aim,rig.ReleaseHeight);
                plane.Tick(true,false,false);plane.Tick(false,false,false);
                bool held=false;float signClearance=0;
                for(int i=0;i<700 && plane.State==FlightModel.Phase.Flying && plane.Distance<FlightCourse.OpeningEnd;i++)
                {
                    // The original throw-style release carries through the sign before the second pickup.
                    if (lowLane) held=(plane.Distance>=23 && plane.Distance<24) || plane.Distance>=40;
                    else if (i%10==0) held=plane.Height+.4f*plane.Velocity.y<6.4f;
                    plane.Tick(false,held,false);
                    float previous=plane.Distance;
                    course.Step(plane,.02f);
                    if (previous<FlightCourse.SignCenter.x && plane.Distance>=FlightCourse.SignCenter.x)
                        signClearance=plane.Height-(FlightCourse.SignCenter.y+FlightCourse.SignHalfSize.y);
                }
                Require(plane.Distance>=FlightCourse.OpeningEnd && course.Collisions==0 &&
                    course.PickupsCollected==(lowLane ? 2 : 0) && signClearance>(lowLane ? .3f : 2f),
                    "Opening route failed: low="+lowLane+", sign clearance="+signClearance);
                Require(!course.IsPaperSign && course.Pickup.x==80 && course.Obstacle.x==67,
                    "Authored opening did not hand off to the continuing course.");
            }
            game.Thrower.Prepare(new Vector3(0,game.Model.Height,0));
            Debug.Log("[OpeningCourse] PASS: two low boosts and obstacle avoidance; clear upper bypass; both cross 50 m.");
        }
        private static void RememberAim()
        {
            previewAnchor = controller.transform.Find("출발 예상 궤도").position;
            aimingHand = controller.Thrower.Grip.position;
            sawMovingThrow = false;
        }
        private static void VerifyWindupPreview()
        {
            if (controller.Thrower.State != LaunchThrower.Phase.Windup) return;
            var holder = controller.transform.Find("출발 예상 궤도");
            Require(controller.Trajectory.Visible && Vector3.Distance(holder.position,previewAnchor)<.0001f,
                "Windup moved or hid the aim preview.");
            if (Vector3.Distance(controller.Thrower.Grip.position,aimingHand)>.25f) sawMovingThrow=true;
        }

        private static void PlayTick()
        {
            try
            {
                Require(!failed, "Runtime logged an error.");
                Require(EditorApplication.timeSinceStartup < deadline, "Play verification timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                if (controller == null)
                {
                    controller = UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
                    if (controller == null || controller.Model == null) return;
                    originalSettings = InputSystem.settings;
                    testSettings = UnityEngine.Object.Instantiate(originalSettings);
                    InputSystem.settings = testSettings;
                    testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    mouse = InputSystem.AddDevice<Mouse>();
                    researchKeyboard = InputSystem.AddDevice<Keyboard>();
                    Require(controller.FlightCamera != null, "Runtime camera missing.");
                    Require(controller.transform.Find("Folded paper plane") != null, "Runtime mesh missing.");
                    Require(controller.Model.State == FlightModel.Phase.Ready, "Ready state missing.");
                    Require(!controller.transform.Find("최고 기록 깃발").gameObject.activeSelf,"First game showed a record flag.");
                    VerifyThrowRig(controller);
                    VerifyForecast(controller);
                    VerifyOpeningCourse(controller);
                    Require(!controller.OpenResearch() && !controller.Session.Research.Unlocked,
                        "Research must stay locked before the first completed flight.");
                }
                var model = controller.Model;
                float t = Time.time;
                switch (stage)
                {
                    case 0:
                        if (t > 0.5f) { Press(true,Screen.height*.4f); stageDeadline=EditorApplication.timeSinceStartup+.6; stage++; }
                        break;
                    case 1:
                        if (EditorApplication.timeSinceStartup >= stageDeadline)
                        {
                            Require(controller.Trajectory.Visible && controller.Thrower.State==LaunchThrower.Phase.Aiming
                                && model.State==FlightModel.Phase.Ready && controller.Session.Research.ActiveRunId==0,
                                "Holding must show the preview without starting a run.");
                            VerifyHeldNose(controller);
                            Press(true,Screen.height*.1f); stage=15;
                        }
                        break;
                    case 15:
                        if (controller.LaunchAngle==FlightModel.MinLaunchAngle)
                        {
                            VerifyHeldNose(controller);
                            Press(true,Screen.height*.9f); stage=16;
                        }
                        break;
                    case 16:
                        if (controller.LaunchAngle==FlightModel.MaxLaunchAngle)
                        {
                            VerifyHeldNose(controller);
                            Press(true,Screen.height*.52f); stage=11;
                        }
                        break;
                    case 11:
                        if (Mathf.Abs(controller.LaunchAngle-22f)<.3f)
                        {
                            Require(controller.Trajectory.Samples.Count>5,"Missing predicted path.");
                            VerifyHeldNose(controller);
                            Debug.Log("[LaunchNose] PASS: visible mesh starts at held nose at neutral, low and high drag angles.");
                            RememberAim();
                            Press(false,Screen.height*.52f); stage=2;
                        }
                        break;
                    case 2:
                        VerifyWindupPreview();
                        if (model.Elapsed > .45f)
                        {
                            Require(sawMovingThrow && model.ControlArmed && !controller.Trajectory.Visible, "Release left preview active or missed moving windup.");
                            var eye=controller.Thrower.transform.Find("root/hip/torso/head/gaze");
                            Vector3 toPlane=AirborneNose(model)-eye.position;
                            Require(Vector2.Angle(eye.right,toPlane)<8f,"Thrower did not watch the launched plane.");
                            int step = Mathf.RoundToInt(model.Elapsed/Time.fixedDeltaTime);
                            var samples = controller.Trajectory.Samples;
                            Require(step < samples.Count && Vector2.Distance(samples[step],new Vector2(model.Distance,model.Height))<.001f,
                                "Actual hands-off flight diverged from the displayed trajectory.");
                            Debug.Log("[LaunchAim] PASS: hold-to-preview, drag direction, release, real gameplay path and control gate.");
                            startHeight = model.Height;
                            Press(true); stage++;
                        }
                        break;
                    case 3:
                        if (model.Elapsed > 1.95f)
                        {
                            Require(model.Climbing && model.Height > startHeight, "Held mouse did not climb.");
                            Press(false); stage++;
                            Debug.Log("[PaperFlight] PLAY INPUT: launch / release gate / climb passed");
                        }
                        break;
                    case 4:
                        if (model.State == FlightModel.Phase.Landed)
                        {
                            Require(model.Distance > 10f && model.Clearance == 0f, "Ground contact or distance failed.");
                            Require(!controller.transform.Find("최고 기록 깃발").gameObject.activeSelf,"Flag moved on the recording flight.");
                            Debug.Log("[PaperFlight] PLAY LANDING: distance=" + model.Distance.ToString("F2"));
                            expectedPoints = 185 + ResearchEconomy.BaseReward(model.Distance);
                            Require(controller.Session.Research.Points == expectedPoints
                                && controller.Session.Research.CompletedFlights == 1 && !controller.Session.SettlementPending,
                                "Landing reward was not committed exactly once.");
                            Require(!controller.Session.TrySettle(), "Landing paid twice.");
                            Require(controller.OpenResearch(), "First completed flight did not unlock research.");
                            var arguments = Environment.GetCommandLineArgs();
                            int previewIndex = Array.IndexOf(arguments, "-flightResearchPreview");
                            if (previewIndex >= 0 && previewIndex + 1 < arguments.Length)
                                ScreenCapture.CaptureScreenshot(arguments[previewIndex + 1]);
                            Press(true); stageDeadline = EditorApplication.timeSinceStartup + 0.3; stage = 8;
                        }
                        break;
                    case 5:
                        if (model.State == FlightModel.Phase.Ready)
                        {
                            Require(model.Distance == 0f && !model.ControlArmed, "Reset failed.");
                            Require(model.BestDistance > 10f, "Best distance lost.");
                            var flag=controller.transform.Find("최고 기록 깃발");
                            var onDisk=new ResearchFileStore(SessionState.GetString(TestSaveKey,"")).Load();
                            Require(flag.gameObject.activeSelf && Mathf.Abs(flag.position.x-onDisk.bestDistance)<.001f
                                && Mathf.Abs(flag.position.x-model.BestDistance)<.001f,"Next flight did not mark the saved record.");
                            Debug.Log("[RecordFlag] PASS: hidden on first flight; next flight marks saved best at "+flag.position.x+" m.");
                            Require(Mathf.Abs(model.Speed - 13f * 1.12f) < 0.001f, "Purchased launch upgrade did not apply to restart.");
                            Press(false); stage++;
                        }
                        break;
                    case 6:
                        Press(true); stage=12;
                        break;
                    case 12:
                        if (controller.Trajectory.Visible)
                        {
                            Require(controller.OpenResearch() && !controller.Trajectory.Visible
                                && controller.Session.Research.ActiveRunId==0,"Research did not cancel aim.");
                            controller.CloseResearch(); Press(false);
                            stageDeadline=EditorApplication.timeSinceStartup+.2; stage=13;
                        }
                        break;
                    case 13:
                        if(EditorApplication.timeSinceStartup >= stageDeadline)
                        {
                            Require(model.State==FlightModel.Phase.Ready,"Canceled release launched a flight.");
                            Press(true,Screen.height*.4f); stage=14;
                        }
                        break;
                    case 14:
                        if (controller.Trajectory.Visible) { Press(true,Screen.height*.1f); stage=17; }
                        break;
                    case 17:
                        if (controller.LaunchAngle==FlightModel.MinLaunchAngle)
                        {
                            VerifyHeldNose(controller);
                            RememberAim();
                            Press(false,Screen.height*.1f); stage=7;
                        }
                        break;
                    case 7:
                        VerifyWindupPreview();
                        if (model.State == FlightModel.Phase.Flying && model.Elapsed >= .15f)
                        {
                            Require(sawMovingThrow && !controller.Trajectory.Visible,"Underhand preview did not lock until takeoff.");
                            Debug.Log("[PreviewLock] PASS: normal and underhand windup keep the visible mouse-up anchor until takeoff.");
                            Require(model.ControlArmed && !model.Climbing, "Relaunch climbed without new input.");
                            var path=controller.Trajectory.Samples;
                            int index=Mathf.RoundToInt(model.Elapsed/Time.fixedDeltaTime);
                            Require(path[0].y<2.5f && index<path.Count &&
                                Vector2.Distance(path[index],new Vector2(model.Distance,model.Height))<.001f,
                                "Low release flight must follow the low-hand forecast.");
                            stage=18;
                        }
                        break;
                    case 18:
                        if (model.Elapsed>=1f)
                        {
                            Require(model.Height>controller.Trajectory.Samples[0].y && model.Velocity.y>0f,
                                "Underhand flight should keep rising beyond the drawn prefix.");
                            Debug.Log("[Underhand] PASS: mouse drag, low release and actual flight match preview.");
                            Debug.Log("[PaperFlight] PLAY CHECKS PASSED: ready, launch, hold gate, climb, dive, ground, reset, relaunch.");
                            if (Application.isBatchMode)
                            {
                                VerifyDeveloperPlayMode();
                                VerifyCompletePlayableWeb();
                            }
                            Finish(0);
                        }
                        break;
                    case 8:
                        if (EditorApplication.timeSinceStartup >= stageDeadline)
                        {
                            Require(controller.ResearchOpen && model.State == FlightModel.Phase.Landed,
                                "Research click leaked into flight reset/launch.");
                            Require(controller.VisibleResearchCount == 5, "Expected five root candidates in normal mode.");
                            Require(controller.SelectedResearchId == "", "New research must start without a selected node.");
                            foreach (var node in controller.ResearchAssets.nodes)
                                Require(!controller.Session.Research.Owns(node.id), "New game granted an upgrade without purchase.");
                            Require(controller.SelectResearchNode("Glide_1") && !controller.SelectResearchNode("Shield_1")
                                && !controller.SelectResearchNode("Launch_2"), "Future nodes leaked into runtime selection.");
                            int beforePreview = controller.Session.Research.Points;
                            Require(controller.SetResearchPreview(true) && controller.VisibleResearchCount == 98
                                && controller.SelectResearchNode("Shield_1"), "Complete 98-node preview failed.");
                            Require(controller.ResearchNavigation.Zoom < 1, "Full preview did not zoom out to fit.");
                            Require(!controller.BuyResearchNode("Launch_1") && !controller.BuySelectedResearch(ResearchNode.Throw)
                                && controller.Session.Research.Points == beforePreview, "Preview spent real points.");
                            Require(controller.SelectResearchNode("Launch_1"),"Preview selection failed.");
                            InputSystem.QueueStateEvent(researchKeyboard,new KeyboardState(Key.Enter));
                            stageDeadline=EditorApplication.timeSinceStartup+.15;stage=19;
                        }
                        break;
                    case 19:
                        if (EditorApplication.timeSinceStartup>=stageDeadline)
                        {
                            Require(controller.ResearchPreviewAll && controller.Session.Research.Points==expectedPoints
                                && !controller.Session.Research.Owns("Launch_1"),"Preview Enter spent points or left preview.");
                            Require(controller.SetResearchPreview(false) && !controller.ResearchOpen
                                && !controller.ResearchPreviewAll && controller.OpenResearch()
                                && controller.VisibleResearchCount == 5, "Preview leaked into normal research.");
                            controller.ResearchNavigation.BeginPan();
                            controller.ResearchNavigation.Drag(new Vector2(2000, 0));
                            controller.ResearchNavigation.Cancel();
                            Require(controller.SelectResearchNode("Glide_1"),"Keyboard navigation start missing.");
                            InputSystem.QueueStateEvent(researchKeyboard,new KeyboardState(Key.LeftArrow));
                            stage=20;
                        }
                        break;
                    case 20:
                        if (controller.SelectedResearchId=="Launch_1")
                        {
                            var launch=Array.Find(controller.ResearchAssets.nodes,n=>n.id=="Launch_1");
                            var size=new Vector2(1408,790);
                            Require(new Rect(Vector2.zero,size).Contains(controller.ResearchNavigation.WorldToView(launch.position,size)),
                                "Keyboard-selected node stayed offscreen.");
                            InputSystem.QueueStateEvent(researchKeyboard,new KeyboardState(Key.Enter));
                            stage=21;
                        }
                        break;
                    case 21:
                        if (controller.Session.Research.Owns("Launch_1"))
                        {
                            var graphSize = new Vector2(1408, 790);
                            foreach (var child in controller.ResearchAssets.nodes)
                                if (child.runtimeIndex >= 0 && child.parent == Array.FindIndex(controller.ResearchAssets.nodes, n => n.id == "Launch_1"))
                                    Require(new Rect(Vector2.zero, graphSize).Contains(controller.ResearchNavigation.WorldToView(child.position, graphSize)), "Revealed child is outside the viewport.");
                            Require(!controller.BuyResearchNode("Launch_1"), "A one-time node was bought twice.");
                            InputSystem.QueueStateEvent(researchKeyboard,new KeyboardState(Key.LeftArrow));
                            stage=22;
                        }
                        break;
                    case 22:
                        if (controller.SelectedResearchId=="Launch_2")
                        {
                            Require(controller.VisibleResearchCount == 8 && controller.SelectResearchNode("Launch_2")
                                && controller.SelectResearchNode("Drag_1") && !controller.SelectResearchNode("Climb_1")
                                && controller.SelectResearchNode("Boost_1") && !controller.SelectResearchNode("Launch_3"), "Expected three direct children.");
                            controller.SelectResearchNode("Drag_1");
                            InputSystem.QueueStateEvent(researchKeyboard,new KeyboardState(Key.NumpadEnter));
                            stage=23;
                        }
                        break;
                    case 23:
                        if (controller.Session.Research.Owns("Drag_1"))
                        {
                            InputSystem.QueueStateEvent(researchKeyboard,new KeyboardState());
                            Require(!controller.BuyResearchNode("Drag_1")
                                && controller.SelectResearchNode("Drag_2") && controller.SelectResearchNode("Drag_1")
                                && controller.SelectResearchNode("Launch_2"), "Independent branch selection failed.");
                            Require(controller.BuyResearchNode("Glide_1"), "First glide node purchase failed.");
                            Require(controller.VisibleResearchCount == 13 && controller.SelectResearchNode("Climb_1"), "Owned nodes and open siblings must remain visible.");
                            Require(!controller.BuySelectedResearch(ResearchNode.Control), "Deferred control research was purchasable.");
                            Require(controller.Session.Research.Points == expectedPoints - 180, "One-time purchases charged incorrectly.");
                            string path = SessionState.GetString(TestSaveKey, "");
                            var saved = new FlightResearch(new ResearchFileStore(path));
                            Require(saved.Rank(ResearchNode.Throw) == 1 && saved.Rank(ResearchNode.Glide) == 1
                                && saved.Rank(ResearchNode.Control) == 0 && saved.Owns("Drag_1") && !saved.Owns("Climb_1")
                                && saved.Points == expectedPoints - 180,
                                "One-time purchases did not survive reload.");
                            Require(!controller.BuyResearchNode("Launch_2"), "Insufficient points allowed a purchase.");
                            Debug.Log("[ResearchKeys] PASS: arrow focus and reveal, Enter and keypad purchases, preview blocked, points and save verified.");
                            controller.CloseResearch();
                            stageDeadline = EditorApplication.timeSinceStartup + 0.2; stage = 9;
                        }
                        break;
                    case 9:
                        if (EditorApplication.timeSinceStartup >= stageDeadline)
                        {
                            Require(model.State == FlightModel.Phase.Landed, "Closing research leaked a held click.");
                            Press(false); stageDeadline = EditorApplication.timeSinceStartup + 0.2; stage = 10;
                        }
                        break;
                    case 10:
                        if (EditorApplication.timeSinceStartup >= stageDeadline)
                        {
                            Press(true); stage = 5;
                            Debug.Log("[PaperFlight] RESEARCH PLAY CHECKS PASSED: unlock, reward, UI input isolation, purchase, insufficient funds, saved reload.");
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void Finish(int result)
        {
            SessionState.SetBool(SessionKey, false);
            Environment.SetEnvironmentVariable("FLYEDITOR_TEST_PROGRESS", null);
            string progressPath = SessionState.GetString(TestSaveKey, "");
            if (!string.IsNullOrEmpty(progressPath))
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                    if (System.IO.File.Exists(progressPath + suffix)) System.IO.File.Delete(progressPath + suffix);
            SessionState.EraseString(TestSaveKey);
            EditorApplication.update -= PlayTick;
            Application.logMessageReceived -= WatchLog;
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (researchKeyboard != null) InputSystem.RemoveDevice(researchKeyboard);
            if (originalSettings != null) InputSystem.settings = originalSettings;
            if (testSettings != null) UnityEngine.Object.DestroyImmediate(testSettings);
            if (Application.isBatchMode) EditorApplication.Exit(result);
            else EditorApplication.isPlaying = false;
        }

        public static void Build()
        {
            Require(Application.isBatchMode, "Batch only.");
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-flightBuild");
            Require(index >= 0 && index + 1 < args.Length, "Provide -flightBuild output.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/InGame.unity" },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = args[index + 1],
                options = BuildOptions.Development
            });
            Require(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, "Build failed.");
            Debug.Log("[PaperFlight] WINDOWS BUILD PASSED");
        }

        public static void OpenForReview()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/InGame.unity");
            var flight = UnityEngine.Object.FindAnyObjectByType<PaperFlightController>();
            if (flight != null) Selection.activeGameObject = flight.gameObject;
        }

        private sealed class MemoryResearchStore : IResearchStore
        {
            private ResearchSave data = new ResearchSave();
            public bool FailWrites;
            public ResearchSave Load() => data.Copy();
            public void Save(ResearchSave value)
            {
                if (FailWrites) throw new System.IO.IOException("Simulated write failure");
                data = value.Copy();
            }
        }

        private static void VerifyResearch()
        {
            var store = new MemoryResearchStore();
            var research = new FlightResearch(store);
            Require(!research.Unlocked && !research.TryBuy(ResearchNode.Throw), "Research unlocked before first flight.");
            Require(!research.CompleteRun(1, 100f, out _), "An unstarted run paid a reward.");
            Require(research.BeginRun(), "Run creation failed.");
            long firstRun = research.ActiveRunId;
            Require(!research.TryBuy(ResearchNode.Throw), "An active run allowed a purchase.");
            Require(research.CompleteRun(firstRun, 49.99f, out int reward) && reward == 29
                && research.Points == 29 && research.Unlocked, "Distance reward rounding/unlock failed.");
            Require(!research.TryBuy(ResearchNode.Throw) && research.Points == 29, "Insufficient funds were spent.");
            research = new FlightResearch(store);
            Require(!research.CompleteRun(firstRun, 49.99f, out _) && research.Points == 29,
                "Reload allowed a duplicate reward.");
            Require(research.BeginRun(), "Second run creation failed.");
            long secondRun = research.ActiveRunId;
            Require(research.CompleteRun(secondRun, 7f, out reward) && reward == 1 && research.Points == 30,
                "Small-distance reward failed.");
            store.FailWrites = true;
            Require(!research.TryBuy(ResearchNode.Throw) && research.Points == 30 && research.Rank(ResearchNode.Throw) == 0,
                "Failed save spent points or changed rank.");
            store.FailWrites = false;
            Require(research.TryBuy(ResearchNode.Throw) && research.Points == 0 && research.Rank(ResearchNode.Throw) == 1,
                "Exact-balance purchase failed.");
            Require(!research.TryBuy((ResearchNode)99), "Invalid node was purchasable.");
            Require(research.BeginRun(), "Abandoned run setup failed.");
            long abandoned = research.ActiveRunId;
            Require(research.AbandonRun() && !research.CompleteRun(abandoned, 500f, out _), "Abandoned run paid a reward.");
            var zero = new FlightResearch(new MemoryResearchStore());
            zero.BeginRun(); zero.CompleteRun(zero.ActiveRunId, 0f, out int zeroReward);
            Require(zeroReward == 0 && zero.Points == 0 && zero.Unlocked, "Zero-distance flight got an invented bonus or did not unlock research.");
            var retryStore = new MemoryResearchStore();
            var retry = new FlightSession(new FlightTuning(), new FlightResearch(retryStore));
            retry.Tick(true, true, false); retry.Tick(false, false, false);
            retryStore.FailWrites = true;
            for (int i = 0; i < 10000 && retry.Model.State == FlightModel.Phase.Flying; i++) retry.FixedTick(0.02f);
            Require(retry.SettlementPending && retry.Research.Points == 0, "Failed settlement lost its pending run or paid early.");
            retry.Tick(false, false, true);
            Require(retry.Model.State == FlightModel.Phase.Landed, "Restart discarded a failed landing settlement.");
            retryStore.FailWrites = false;
            Require(retry.TrySettle() && !retry.TrySettle() && retry.Research.CompletedFlights == 1,
                "Settlement retry failed or paid twice.");

            foreach (ResearchNode node in Enum.GetValues(typeof(ResearchNode)))
            {
                var funded = new FlightResearch(new MemoryResearchStore());
                funded.BeginRun(); funded.CompleteRun(funded.ActiveRunId, 0f, out _); funded.AdjustDeveloperPoints(630);
                var baseline = new FlightTuning();
                int paid = 0;
                foreach (int cost in new[] { 30, 120, 480 })
                {
                    Require(funded.Cost(node) == cost && funded.TryBuy(node), "Research price or next rank was incorrect.");
                    paid += cost;
                    int rank = funded.Rank(node);
                    Require(funded.Points == 630 - paid, "Purchase deducted the wrong amount.");
                    var tuning = funded.Apply(baseline);
                    Require(Mathf.Abs(tuning.launchSpeed - baseline.launchSpeed * (1f + (node == ResearchNode.Throw ? new[] { 0f, .12f, .32f, .62f }[rank] : 0f))) < 0.0001f,
                        "Throw effect was not additive or altered another upgrade.");
                    Require(Mathf.Abs(tuning.sink - baseline.sink * (1f - (node == ResearchNode.Glide ? new[] { 0f, .02f, .07f, .16f }[rank] : 0f))) < 0.0001f,
                        "Glide effect was not additive.");
                    float response = 1f + (node == ResearchNode.Control ? 0.05f * rank : 0f);
                    Require(Mathf.Abs(tuning.pitchResponse - 90f * response) < 0.0001f
                        && Mathf.Abs(tuning.flightPathResponse - 10f * response) < 0.0001f
                        && tuning.releasePitchDuration == 3f
                        && Mathf.Abs(Mathf.Tan(tuning.divePitch*Mathf.Deg2Rad) / Mathf.Tan(-24f*Mathf.Deg2Rad)
                            - (1f - (node == ResearchNode.Glide ? new[] { 0f, .02f, .07f, .16f }[rank] : 0f))) < .0001f,
                        "Control effect changed the release curve or response incorrectly.");
                }
                Require(!funded.TryBuy(node) && funded.Points == 0 && funded.Rank(node) == 3, "Maximum rank was exceeded.");
                Require(baseline.launchSpeed == 13f && baseline.sink == 0.16f && baseline.pitchResponse == 90f,
                    "Research mutated the baseline tuning.");
                var upgraded = new FlightModel(funded.Apply(baseline));
                var plain = new FlightModel(baseline.Copy());
                foreach (var model in new[] { upgraded, plain })
                {
                    model.Tick(true, true, false); model.Tick(false, false, false);
                    model.Tick(false, node == ResearchNode.Control, false);
                }
                for (int i = 0; i < 10; i++) { upgraded.FixedTick(0.02f); plain.FixedTick(0.02f); }
                Require(node == ResearchNode.Throw ? upgraded.Distance > plain.Distance
                    : node == ResearchNode.Glide ? upgraded.Height > plain.Height : upgraded.Pitch > plain.Pitch,
                    "Purchased research had no effect on actual flight.");
            }

            // Real on-disk save/replace/reload, including run idempotency and crash abandonment.
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "flyeditor-research-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var disk = new ResearchFileStore(file);
                var first = new FlightResearch(disk);
                first.BeginRun(); long id = first.ActiveRunId;
                first.CompleteRun(id, 1000f, out _); first.TryBuy(ResearchNode.Glide);
                var reloaded = new FlightResearch(disk);
                Require(reloaded.Points == ResearchEconomy.BaseReward(1000f) - 30 && reloaded.Rank(ResearchNode.Glide) == 1 && reloaded.CompletedFlights == 1,
                    "Disk reload lost currency/ranks/unlock.");
                Require(!reloaded.CompleteRun(id, 1000f, out _) && reloaded.Points == ResearchEconomy.BaseReward(1000f) - 30, "Disk reload duplicated reward.");
                reloaded.BeginRun(); long interrupted = reloaded.ActiveRunId;
                var session = new FlightSession(new FlightTuning(), new FlightResearch(disk));
                Require(session.Research.ActiveRunId == 0 && !session.Research.CompleteRun(interrupted, 1000f, out _)
                    && session.Research.Points == ResearchEconomy.BaseReward(1000f) - 30, "Interrupted flight paid automatically.");
                Require(session.TryBuy(ResearchNode.Throw), "Ready purchase failed.");
                Require(Mathf.Abs(session.Model.Speed - 13f * 1.12f) < 0.001f, "Ready purchase did not apply to the next launch.");
                session.Tick(true, true, false);
                Require(!session.TryBuy(ResearchNode.Control), "Purchase changed an active flight snapshot.");
                session.Tick(false, false, false);
                for (int i = 0; i < 10000 && session.Model.State == FlightModel.Phase.Flying; i++) session.FixedTick(0.02f);
                int points = session.Research.Points;
                int expected = ResearchEconomy.BaseReward(session.Model.Distance);
                Require(session.LastReward == expected && points == ResearchEconomy.BaseReward(1000f) - 60 + expected && session.Model.State == FlightModel.Phase.Landed,
                    "Session landing did not settle its own distance exactly once.");
                for (int i = 0; i < 100; i++) session.FixedTick(0.02f);
                Require(points == session.Research.Points, "Landed updates duplicated reward.");
                session.Tick(false, false, true); session.Tick(true, true, false);
                Require(!session.Model.ControlArmed && session.Model.State == FlightModel.Phase.Flying, "Research broke relaunch gate.");
                session.Tick(false, false, true);
                Require(session.Research.Points == points && session.Research.ActiveRunId == 0, "In-flight restart paid a reward.");
                System.IO.File.WriteAllText(file, "{broken");
                bool rejected = false;
                try { disk.Load(); } catch (Exception) { rejected = true; }
                Require(rejected && System.IO.File.ReadAllText(file) == "{broken", "Corrupt save was silently overwritten.");
            }
            finally
            {
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                    if (System.IO.File.Exists(file + suffix)) System.IO.File.Delete(file + suffix);
            }

            // Max-rank combinations must still dissipate energy and land, including repeated clicks.
            var maximum = new FlightResearch(new MemoryResearchStore());
            maximum.BeginRun(); maximum.CompleteRun(maximum.ActiveRunId, 3375f, out _);
            foreach (ResearchNode node in Enum.GetValues(typeof(ResearchNode))) for (int i = 0; i < 3; i++) Require(maximum.TryBuy(node), "Max setup failed.");
            foreach (float dt in new[] { 0.01f, 0.02f, 0.04f }) for (int policy = 0; policy < 4; policy++)
            {
                var model = new FlightModel(maximum.Apply(new FlightTuning()));
                model.Tick(true, true, false); model.Tick(false, false, false);
                for (int i = 0; i < 20000 && model.State == FlightModel.Phase.Flying; i++)
                {
                    bool held = policy == 1 || (policy == 2 && i % 2 == 0) || (policy == 3 && (int)(i * dt * 0.8f) % 2 == 0);
                    float energy = model.Energy;
                    model.Tick(false, held, false); model.FixedTick(dt);
                    Require(model.Energy <= energy + 0.0001f && !float.IsNaN(model.Speed), "Max upgrades generated energy.");
                }
                Require(model.State == FlightModel.Phase.Landed, "Max upgrades produced endless flight.");
            }
            Debug.Log("[PaperFlight] RESEARCH CHECKS PASSED: unlock, rewards, duplicate/aborted runs, insufficient/exact funds, 30/120/480 costs, rank cap, atomic failure, disk reload, flight effects, max-rank energy/landing.");
        }

        private static DeveloperProgressWindow OpenDeveloperTestWindow()
        {
            Require(EditorApplication.ExecuteMenuItem(DeveloperProgressWindow.MenuPath)
                && EditorWindow.HasOpenInstances<DeveloperProgressWindow>(), "Developer toggle did not open the tools.");
            return EditorWindow.GetWindow<DeveloperProgressWindow>();
        }
        private static void CloseDeveloperTestWindow()
        {
            Require(EditorApplication.ExecuteMenuItem(DeveloperProgressWindow.MenuPath)
                && !EditorWindow.HasOpenInstances<DeveloperProgressWindow>(), "Developer toggle did not close the tools.");
        }
        private static void VerifyDeveloperEditMode(string isolatedPath)
        {
            var store = ResearchFileStore.OpenCurrent();
            Require(store.Path == isolatedPath, "Developer verification must use an isolated save.");
            var original = store.Load();
            if (EditorWindow.HasOpenInstances<DeveloperProgressWindow>()) CloseDeveloperTestWindow();
            var window = OpenDeveloperTestWindow();
            try
            {
                store.Save(new ResearchSave { completedFlights = 2, points = 25, a1 = 1, bestDistance = 50 });
                Require(window.Apply(DeveloperProgressWindow.Action.Add, 75) && store.Load().points == 100, "Adding points failed.");
                Require(window.Apply(DeveloperProgressWindow.Action.Subtract, 40) && store.Load().points == 60, "Subtracting points failed.");
                Require(window.Apply(DeveloperProgressWindow.Action.Subtract, 100) && store.Load().points == 0, "Balance underflow.");
                Require(window.Apply(DeveloperProgressWindow.Action.Add, int.MaxValue)
                    && window.Apply(DeveloperProgressWindow.Action.Add, 1) && store.Load().points == int.MaxValue, "Balance overflow.");
                Require(window.Apply(DeveloperProgressWindow.Action.ZeroPoints) && store.Load().points == 0
                    && store.Load().a1 == 1 && store.Load().bestDistance == 50, "Zeroing points changed purchases or records.");
                Require(window.Apply(DeveloperProgressWindow.Action.ResetProgress), "Edit mode reset failed.");
                var reset = new FlightResearch(store);
                Require(reset.Points == 0 && !reset.Unlocked && !reset.Owns("Launch_1") && reset.BestDistance == 0, "Incomplete reset.");
            }
            finally { CloseDeveloperTestWindow(); store.Save(original); }
            Debug.Log("[DeveloperMode] EDIT CHECKS PASSED: menu toggle, add/subtract, bounds, points-only reset, full reset, reload.");
        }
        private static void VerifyCompletePlayableWeb()
        {
            var research = controller.Session.Research;
            Require(research.BeginRun() && research.CompleteRun(research.ActiveRunId, 0, out _), "Fixture first landing failed.");
            Require(controller.AdjustDeveloperPoints(1000000) && controller.OpenResearch() && !controller.ResearchPreviewAll,
                "Expected normal purchase screen.");
            foreach (string root in new[] { "Launch_1", "Glide_1", "Recover_1", "Durability_1", "Magnet_1" })
                Require(controller.SelectResearchNode(root) && !research.Owns(root), "Missing initial branch " + root);
            var nodes = controller.ResearchAssets.nodes;
            foreach (var n in nodes)
                if (n.runtimeIndex == 0 || n.runtimeIndex == 1 || n.runtimeIndex == 3 || n.runtimeIndex == 4)
                    Require(controller.BuyResearchNode(n.id), "Original twelve purchase failed: " + n.id);
            Require(controller.VisibleResearchCount == 31 && controller.SelectResearchNode("Boost_1")
                && controller.SelectResearchNode("Durability_1") && controller.SelectResearchNode("Magnet_1")
                && controller.SelectResearchNode("Recover_1"), "Regression: normal web stopped at the two completed arms.");
            int total = 0;
            foreach (var n in nodes)
            {
                total += n.costs[0];
                if (!research.Owns(n.id)) Require(controller.SelectResearchNode(n.id) && controller.BuyResearchNode(n.id), "Unreachable node: " + n.id);
                Require(!controller.BuyResearchNode(n.id), "Duplicate purchase: " + n.id);
            }
            controller.FitResearchWeb();
            var saved = new FlightResearch(ResearchFileStore.OpenCurrent());
            Require(controller.VisibleResearchCount == 98 && !controller.ResearchPreviewAll && saved.Points == 1000000 - total
                && Array.TrueForAll(nodes, n => saved.Owns(n.id)), "Full normal ownership/save mismatch.");
            Debug.Log("[ResearchWeb] NORMAL PLAY PASSED: five roots, original ownership and expanded candidates, all 98 purchased and reloaded.");
        }
        private static void VerifyDeveloperPlayMode()
        {
            var window = OpenDeveloperTestWindow();
            try
            {
                int balance = controller.Session.Research.Points;
                Require(window.Apply(DeveloperProgressWindow.Action.Add, 55) && controller.Session.Research.Points == balance + 55,
                    "Play adjustment did not use live progress.");
                Require(window.Apply(DeveloperProgressWindow.Action.Subtract, 5) && controller.Session.Research.Points == balance + 50,
                    "Play deduction failed.");
                Require(window.Apply(DeveloperProgressWindow.Action.ResetProgress), "Live reset failed.");
                var disk = new FlightResearch(ResearchFileStore.OpenCurrent());
                Require(controller.Model.State == FlightModel.Phase.Ready && !controller.OpenResearch() && controller.SelectedResearchId == ""
                    && controller.Session.Research.ActiveRunId == 0 && controller.Session.LastReward == 0
                    && !controller.Session.Research.Owns("Drag_1") && disk.Points == 0 && disk.BestDistance == 0
                    && !disk.Unlocked && !disk.Owns("Drag_1"), "Play reset left stale progress.");
            }
            finally { CloseDeveloperTestWindow(); }
            Debug.Log("[DeveloperMode] PLAY CHECKS PASSED: live balance, active-flight reset, ownership, save reload.");
        }
        private static void VerifyUnderhandRise()
        {
            foreach (float speed in new[]{13f,150f})
            foreach (float dt in new[]{.01f,.02f,1f/30f})
            {
                var model=new FlightModel(new FlightTuning { launchSpeed=speed, speedCap=speed });
                model.SetLaunchPose(-12f,2f);
                model.Tick(true,false,false);model.Tick(false,false,false);
                bool rose=false, descended=false;
                Require(model.Velocity.y<0f,"Low throw should leave the hand along its aim.");
                while (model.State==FlightModel.Phase.Flying && model.Elapsed<6f)
                {
                    float energy=model.Energy;
                    model.FixedTick(dt);
                    Require(model.Energy<=energy+.005f,"Launch arc added energy.");
                    if (model.Height>2f && model.Velocity.y>0f) rose=true;
                    if (rose && model.Velocity.y<0f) descended=true;
                }
                Require(rose && descended,"Low throw must clear its starting height then descend.");
            }
            var controlled=new FlightModel(new FlightTuning());
            controlled.SetLaunchPose(-12f,2f);
            controlled.Tick(true,false,false);controlled.Tick(false,false,false);
            for (int i=0;i<10;i++) controlled.FixedTick(.02f);
            controlled.Tick(true,true,false);controlled.FixedTick(.02f);
            Require(controlled.Climbing,"A fresh click must take over the launch arc immediately.");
            controlled.Tick(false,false,false);
            float before=controlled.Pitch;
            for (int i=0;i<10;i++) controlled.FixedTick(.02f);
            Require(controlled.Pitch<before+3.5f,"Manual release carry exceeded the short inertia window.");
            for (int i=0;i<10;i++) controlled.FixedTick(.02f);
            Require(controlled.Pitch<before,"Releasing manual climb should not restart launch lift.");
            Debug.Log("[UnderhandRise] PASS: low exit, rising arc, descent, energy and manual takeover.");
        }

        private static void VerifyModel()
        {
            VerifyUnderhandRise();
            VerifyLightResponse();
            VerifyGlideContinuity();
            VerifyForwardSpeed();
            VerifyGradualDive();
            VerifyStarterBalance();
            var model = new FlightModel(new FlightTuning());
            model.FixedTick(1f);
            Require(model.State == FlightModel.Phase.Ready && model.Distance == 0f, "Ready advanced.");
            model.Tick(true, true, false);
            for (int i = 0; i < 30; i++) { model.Tick(false, true, false); model.FixedTick(0.02f); }
            Require(!model.ControlArmed && !model.Climbing, "Launch click was treated as climb.");
            model.Tick(false, false, false);
            model.Tick(true, true, false);
            model.FixedTick(0.02f);
            Require(model.Climbing, "Subsequent hold should climb.");
            model.SuspendInput();
            Require(!model.ControlArmed, "Focus loss did not clear input gate.");
            model.Tick(false, true, true);
            Require(model.State == FlightModel.Phase.Ready && model.Distance == 0f, "In-flight restart failed.");

            for (int policy = 0; policy < 4; policy++)
            {
                float referenceDistance = 0f;
                foreach (float dt in new[] { 0.01f, 0.02f, 0.04f })
                {
                    model = new FlightModel(new FlightTuning());
                    model.Tick(true, true, false);
                    model.Tick(false, false, false);
                    for (int i = 0; i < 20000 && model.State == FlightModel.Phase.Flying; i++)
                    {
                        bool hold = policy == 1 || (policy == 2 && i % 2 == 0)
                            || (policy == 3 && (int)(i * dt * 0.8f) % 2 == 0);
                        float energy = model.Energy;
                        float distance = model.Distance;
                        model.Tick(false, hold, false);
                        model.FixedTick(dt);
                        Require(model.Energy <= energy + 0.0001f, "Steering created energy.");
                        Require(model.Distance >= distance && model.Height >= 0f, "Invalid position.");
                        Require(!float.IsNaN(model.Speed) && !float.IsInfinity(model.Speed), "Invalid speed.");
                    }
                    Require(model.State == FlightModel.Phase.Landed && model.Speed == 0f, "Flight never landed.");
                    float landedDistance = model.Distance;
                    model.FixedTick(1f);
                    Require(model.Distance == landedDistance, "Landed flight moved.");
                    if (dt == 0.01f) referenceDistance = landedDistance;
                    if (policy < 2) Require(Mathf.Abs(landedDistance - referenceDistance) < 2f, "Fixed-step result drifted.");
                    model.Tick(true, true, false);
                    Require(model.State == FlightModel.Phase.Ready && model.Distance == 0f, "Landing reset auto-launched.");
                    Require(model.BestDistance == landedDistance, "Best flight reset.");
                    Debug.Log("[PaperFlight] MODEL policy=" + policy + " dt=" + dt + " distance=" + landedDistance.ToString("F2"));
                }
            }
            Debug.Log("[PaperFlight] MODEL CHECKS PASSED: FSM, energy, stall recovery, ground, timestep, reset.");
        }

        private static void VerifyStarterBalance()
        {
            var released = new FlightModel(new FlightTuning());
            released.Tick(true, true, false);
            released.Tick(false, false, false);
            for (int i = 0; i < 50; i++) released.FixedTick(0.02f);
            Require(released.State == FlightModel.Phase.Flying && released.Clearance > 2f,
                "A released launch must retain altitude for an initial correction.");
            Require(released.Pitch < 0f && released.Pitch > new FlightTuning().divePitch,
                "Release should still be progressing toward the final dive angle at one second.");
            Debug.Log("[PaperFlight] STARTER release@1s distance=" + released.Distance.ToString("F2")
                + " clearance=" + released.Clearance.ToString("F2"));
            var held = new FlightModel(new FlightTuning());
            held.Tick(true, true, false);
            held.Tick(false, false, false);
            float launchEnergy = held.Energy;
            for (int i = 0; i < 100; i++) { held.Tick(false, true, false); held.FixedTick(0.02f); }
            Require(held.ClimbReserve < 0.5f && held.ClimbReserve > 0.05f, "Two-second climb reserve must be limited but usable.");
            Require(held.Height > 4f && held.Energy < launchEnergy, "Climb must trade speed and energy for altitude.");
            Require(Mathf.Approximately(held.Pitch, new FlightTuning().climbPitch), "Held flight did not reach the climb angle.");
            Debug.Log("[PaperFlight] STARTER hold@2s reserve=" + held.ClimbReserve.ToString("F3")
                + " speed=" + held.Speed.ToString("F2") + " height=" + held.Height.ToString("F2"));
            float pitch = held.Pitch;
            held.Tick(false, false, false);
            held.FixedTick(0.02f);
            Require(Mathf.Abs(held.Pitch - pitch) < 0.01f, "Release should ease in without an immediate nose drop.");
            for (int i = 0; i < 50; i++) held.FixedTick(0.02f);
            Require(held.State == FlightModel.Phase.Flying, "Short release after climb crashed immediately.");
            Require(held.Pitch > 0f && held.Pitch < pitch, "Release from full climb should lower the nose gradually.");
            Debug.Log("[PaperFlight] RELEASE FROM CLIMB @1.02s pitch=" + held.Pitch.ToString("F3"));
            for (int i = 0; i < 100; i++) held.FixedTick(0.02f);
            Require(held.State == FlightModel.Phase.Flying && Mathf.Abs(held.Pitch - new FlightTuning().divePitch) < 0.001f,
                "A released climb should reach its maximum dive after three seconds if still airborne.");
            held.Tick(false, false, true);
            Require(Mathf.Abs(held.ClimbReserve - 1f) < 0.001f, "Restart must restore launch reserve.");
        }

        private static void VerifyGradualDive()
        {
            foreach (float dt in new[] { 0.01f, 0.02f, 0.04f })
            {
                // Extra test altitude keeps ground contact from truncating the full angle curve.
                var settings = new FlightTuning { launchHeight = 30f };
                var model = new FlightModel(settings);
                model.Tick(true, true, false);
                model.Tick(false, false, false);
                float firstPitch = 0f;
                int finalStep = Mathf.RoundToInt(settings.releasePitchDuration / dt);
                for (int i = 1; i <= finalStep; i++)
                {
                    float previousPitch = model.Pitch;
                    float previousEnergy = model.Energy;
                    model.FixedTick(dt);
                    Require(model.State == FlightModel.Phase.Flying, "Curve test landed before completion.");
                    Require(model.Pitch <= previousPitch + 0.00001f && model.Pitch >= settings.divePitch - 0.00001f,
                        "Released pitch reversed direction or overshot its limit.");
                    Require(model.Energy <= previousEnergy + 0.0001f, "Gradual steering created energy.");
                    if (i == 1)
                    {
                        firstPitch = model.Pitch;
                        Require(Mathf.Abs(firstPitch) < 0.02f, "Released flight immediately pitched downward.");
                    }
                    if (i == Mathf.RoundToInt(0.4f / dt))
                        Require(model.Pitch > -2f && model.Pitch < -0.2f, "Early descent is not gradual.");
                    if (i == Mathf.RoundToInt(1f / dt) || i == Mathf.RoundToInt(2f / dt) || i == finalStep)
                        Debug.Log("[PaperFlight] DIVE CURVE dt=" + dt + " t=" + (i * dt).ToString("F2")
                            + " pitch=" + model.Pitch.ToString("F3"));
                    if (i == Mathf.RoundToInt(1f / dt)) Require(model.Pitch > -7f && model.Pitch < -5f, "One-second descent is outside its intended window.");
                    if (i == Mathf.RoundToInt(2f / dt)) Require(model.Pitch > -19f && model.Pitch < -16f, "Two-second descent is outside its intended window.");
                }
                Require(Mathf.Abs(model.Pitch - settings.divePitch) < 0.001f, "Maximum descent angle was not reached on time.");
                for (int i = 0; i < 10; i++) model.FixedTick(dt);
                Require(Mathf.Abs(model.Pitch - settings.divePitch) < 0.001f, "Pitch continued past its maximum.");

                model.Tick(false, true, false);
                for (int i = 0; i < Mathf.RoundToInt(0.8f / dt); i++) model.FixedTick(dt);
                float beforeRelease = model.Pitch;
                Require(beforeRelease > -1f, "Pressing again did not cancel descent and resume ascent.");
                model.Tick(false, false, false);
                model.FixedTick(dt);
                Require(Mathf.Abs(model.Pitch - beforeRelease) < 0.03f, "Re-release reused the old descent timer.");
                model.Tick(false, false, true);
                Require(model.State == FlightModel.Phase.Ready && model.Pitch == 0f, "Restart did not reset pitch.");
                model.Tick(true, true, false);
                model.Tick(false, false, false);
                model.FixedTick(dt);
                Require(Mathf.Abs(model.Pitch - firstPitch) < 0.00001f, "Restart did not reset the descent curve.");
            }
            Debug.Log("[PaperFlight] GRADUAL DIVE CHECKS PASSED: ease-in, monotonic progression, timed limit, no overshoot, re-press, reset, energy.");
        }

        private static void VerifyGlideContinuity()
        {
            foreach (float dt in new[] { 0.01f, 0.02f, 0.04f })
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    var model = new FlightModel(new FlightTuning { launchHeight = 30f });
                    var releasedTwin = new FlightModel(new FlightTuning { launchHeight = 30f });
                    foreach (var item in new[] { model, releasedTwin })
                    {
                        item.Tick(true, true, false);
                        item.Tick(false, false, false);
                    }
                    Vector2 lastDisplacementVelocity = Vector2.zero;
                    bool reached = false;
                    float maxAcceleration = 0f;
                    for (int i = 0; i < 2000; i++)
                    {
                        Vector2 previousVelocity = model.Velocity;
                        float previousReserve = model.ClimbReserve;
                        float x = model.Distance, y = model.Height;
                        model.Tick(false, true, false);
                        releasedTwin.Tick(false, true, false);
                        model.FixedTick(dt);
                        releasedTwin.FixedTick(dt);
                        lastDisplacementVelocity = new Vector2(model.Distance - x, model.Height - y) / dt;
                        if (previousReserve < 0.03f)
                        {
                            Require((model.Velocity - previousVelocity).magnitude / dt < 8f,
                                "Velocity jumped immediately before climb reserve reached zero.");
                            Require(Vector2.Angle(model.Velocity, previousVelocity) / dt < 45f,
                                "Travel direction jumped at the reserve-zero boundary.");
                        }
                        if ((mode == 0 && (i + 1) * dt >= 1f - 0.0001f)
                            || (mode == 1 && model.ClimbReserve == 0f)) { reached = true; break; }
                    }
                    Require(reached && model.State == FlightModel.Phase.Flying, "Could not reach the release/exhaustion boundary.");
                    float releasePitch = model.Pitch, releaseHeight = model.Height;
                    float releaseSpeed = model.Speed;
                    Require(model.Velocity.y > 0f, "Transition must begin while moving upward.");
                    float minimumSpeed = model.Speed;
                    for (int i = 0; i < Mathf.RoundToInt(3f / dt); i++)
                    {
                        Vector2 previousVelocity = model.Velocity;
                        float energy = model.Energy;
                        float x = model.Distance, y = model.Height;
                        model.Tick(false, mode == 1, false);
                        // Once exhausted, releasing the button must not restart the glide or change its path.
                        releasedTwin.Tick(false, mode == 1 && i * dt < 0.4f, false);
                        model.FixedTick(dt);
                        releasedTwin.FixedTick(dt);
                        Require(model.State == FlightModel.Phase.Flying, "Continuity test landed unexpectedly.");
                        Vector2 displacementVelocity = new Vector2(model.Distance - x, model.Height - y) / dt;
                        float acceleration = (model.Velocity - previousVelocity).magnitude / dt;
                        maxAcceleration = Mathf.Max(maxAcceleration, acceleration);
                        Require(acceleration < 8f && (displacementVelocity - lastDisplacementVelocity).magnitude / dt < 8f,
                            "Release or exhaustion caused a velocity discontinuity.");
                        Require(Vector2.Angle(model.Velocity, previousVelocity) / dt < 45f
                            && Vector2.Angle(displacementVelocity, lastDisplacementVelocity) / dt < 45f,
                            "Release or exhaustion caused an abrupt travel-direction turn.");
                        Require(model.Energy <= energy + 0.0001f, "Inertial glide generated energy.");
                        Require(Mathf.Abs(model.Pitch - releasedTwin.Pitch) < 0.001f
                            && Mathf.Abs(model.Distance - releasedTwin.Distance) < 0.001f
                            && Mathf.Abs(model.Height - releasedTwin.Height) < 0.001f,
                            "Releasing during exhausted glide restarted or changed the transition.");
                        if (i == 0)
                            Require(Mathf.Abs(model.Pitch - releasePitch) < 0.03f && model.Velocity.y > 0f,
                                "First glide frame must preserve upward momentum and ease the nose down.");
                        if (i == Mathf.RoundToInt(0.4f / dt) - 1)
                            Require(model.Height > releaseHeight && model.Velocity.y > 0f,
                                "Glide lost upward momentum too early.");
                        minimumSpeed = Mathf.Min(minimumSpeed, model.Speed);
                        lastDisplacementVelocity = displacementVelocity;
                    }
                    Require(model.Velocity.y < 0f && Mathf.Abs(model.Pitch + 24f) < 0.001f,
                        "Glide must naturally become a descent and reach its final nose angle.");
                    Require(minimumSpeed > 1f, "Glide stalled to zero speed instead of carrying momentum.");
                    Debug.Log("[PaperFlight] CONTINUITY " + (mode == 0 ? "release" : "reserve-zero") + " dt=" + dt
                        + " startSpeed=" + releaseSpeed.ToString("F3") + " minSpeed=" + minimumSpeed.ToString("F3")
                        + " maxAcceleration=" + maxAcceleration.ToString("F3") + " endVy=" + model.Velocity.y.ToString("F3"));
                    model.Tick(false, false, true);
                    Require(model.FlightPathPitch == 0f && model.Velocity == Vector2.zero, "Restart retained travel inertia.");
                }
                var empty = new FlightModel(new FlightTuning { launchSpeed = 0.1f, launchHeight = 0.5f });
                empty.Tick(true, true, false);
                empty.Tick(false, false, false);
                for (int i = 0; i < 20000 && empty.State == FlightModel.Phase.Flying; i++)
                {
                    float energy = empty.Energy;
                    empty.Tick(false, i % 2 == 0, false);
                    empty.FixedTick(dt);
                    Require(empty.Energy <= energy + 0.0001f && !float.IsNaN(empty.Speed),
                        "Near-empty flight generated energy or an invalid speed.");
                }
                Require(empty.State == FlightModel.Phase.Landed && empty.Energy == 0f && empty.Velocity == Vector2.zero,
                    "Near-empty flight must land and stop rather than hover indefinitely.");
            }
            Debug.Log("[PaperFlight] CONTINUITY CHECKS PASSED: reserve-zero boundary, release, actual displacement, momentum, descent, reset, near-empty landing.");
        }

        private static void VerifyForwardSpeed()
        {
            foreach (float dt in new[] { 0.01f, 0.02f, 0.04f })
            {
                var previous = new FlightModel(new FlightTuning { launchSpeed = 10f });
                var faster = new FlightModel(new FlightTuning());
                Require(Mathf.Approximately(faster.Speed, 13f), "Launch speed should be 13 m/s.");
                previous.Tick(true, true, false);
                faster.Tick(true, true, false);
                previous.Tick(false, false, false);
                faster.Tick(false, false, false);
                for (int i = 0; i < Mathf.RoundToInt(1f / dt); i++)
                {
                    float energy = faster.Energy;
                    previous.FixedTick(dt);
                    faster.FixedTick(dt);
                    Require(faster.Energy <= energy + 0.0001f, "Faster movement created energy during flight.");
                    Require(Mathf.Abs(faster.Pitch - previous.Pitch) < 0.001f, "Forward tuning changed the release curve.");
                }
                Require(faster.Distance > previous.Distance * 1.2f && faster.Speed > previous.Speed * 1.2f,
                    "First-second forward movement should be visibly faster than the previous tuning.");
                Debug.Log("[PaperFlight] FORWARD SPEED dt=" + dt + " oldDistance=" + previous.Distance.ToString("F2")
                    + " newDistance=" + faster.Distance.ToString("F2") + " oldSpeed=" + previous.Speed.ToString("F2")
                    + " newSpeed=" + faster.Speed.ToString("F2"));
            }
            Debug.Log("[PaperFlight] FORWARD SPEED CHECKS PASSED: faster launch and movement, unchanged release, dissipating energy.");
        }

        private static void VerifyLightResponse()
        {
            foreach (float dt in new[] { 0.01f, 0.02f, 0.04f })
            {
                var model = new FlightModel(new FlightTuning());
                model.Tick(true, true, false);
                model.Tick(false, false, false);
                float initialEnergy = model.Energy;
                model.Tick(false, true, false);
                for (int i = 0; i < Mathf.RoundToInt(0.2f / dt); i++) model.FixedTick(dt);
                Require(model.Pitch > 10f && model.Pitch < 14f && model.Height > 4.05f,
                    "Short climb should ease in while responding within 0.2 seconds.");
                Debug.Log("[PaperFlight] LIGHT RESPONSE dt=" + dt + " t=0.20 pitch=" + model.Pitch.ToString("F3")
                    + " height=" + model.Height.ToString("F3") + " speed=" + model.Speed.ToString("F3"));
                for (int i = 0; i < Mathf.RoundToInt(0.24f / dt); i++) model.FixedTick(dt);
                Require(model.Pitch > 21.5f && model.Pitch <= 22f, "Full climb should engage within 0.44 seconds without overshoot.");
                Require(model.Energy < initialEnergy, "Faster response must not create energy.");
                var recovery = new FlightModel(new FlightTuning { launchHeight = 30f, launchSpeed = 3f });
                recovery.Tick(true, true, false);
                recovery.Tick(false, false, false);
                recovery.Tick(false, true, false);
                recovery.FixedTick(dt);
                Require(recovery.Recovering && Mathf.Abs(recovery.Pitch) < 0.02f,
                    "Exhausted launch should ease into the same glide as a released launch.");
            }
            Debug.Log("[PaperFlight] LIGHT RESPONSE CHECKS PASSED: short-input response, climb engagement, energy.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[PaperFlight] " + message);
        }
    }
}
