using UnityEngine;

namespace FlyEditor.Flight
{
    // The clip is baked from the Spine rotation keys. Flight owns the released plane.
    public sealed class LaunchThrower : MonoBehaviour
    {
        public enum Phase { Holding, Aiming, Windup, FollowThrough, Resting }
        [SerializeField] private AnimationClip motion;
        [SerializeField] private Transform grip;
        [SerializeField] private float releaseTime;
        private float time;
        private const float DrawDuration = .38f, GatherDuration = .12f;
        private const float DriveDuration = .25f, ArmDelay = .09f;
        private float windupElapsed, drawStartTime;
        private Transform torso, upperArm, forearm, head;
        private Vector2 releaseShoulder, releaseUpperArm, releaseForearm;
        private Vector3 releaseOffsets, releaseDirections, authoredDraw, drawCorrection;
        private static readonly Vector3 LowDraw = new Vector3(-7f,-136f,-118f);
        private float neutralHeight, underhand;
        public float ReleaseHeight { get; private set; }
        private bool hasAim, aimChanged;
        private float aimAngle;
        public const float NeutralAim = 10f;
        // The first sixth of the clip settles into the aiming stance. The player
        // owns this hold; the rest of the backswing only plays after mouse-up.
        private float ReadyTime => releaseTime / 6f;
        // Spine keys: arm fully drawn at .22, rear knee loaded at .24, release at .36.
        private float DrawnTime => releaseTime * (22f/36f);
        private float LoadedTime => releaseTime * (2f/3f);
        public Phase State { get; private set; }
        public Transform Grip => grip;
        public bool HoldingPlane => State == Phase.Holding || Pending;
        public bool Pending => State == Phase.Aiming || State == Phase.Windup;

#if UNITY_EDITOR
        public void SetClip(AnimationClip clip, Transform hand, float release)
        { motion = clip; grip = hand; releaseTime = release; }
#endif
        public void Prepare(Vector3 launchPosition)
        {
            // One uniform body scale at setup. All moving pivots rotate at fixed offsets.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            motion.SampleAnimation(gameObject,releaseTime);
            if (forearm == null)
            {
                forearm = grip.parent;
                upperArm = forearm.parent;
                torso = upperArm.parent;
                head = torso.Find("head");
            }
            releaseShoulder = upperArm.position - torso.position;
            releaseUpperArm = forearm.position - upperArm.position;
            releaseForearm = grip.position - forearm.position;
            releaseDirections = ArmDirections();
            neutralHeight = ReleaseHeight = launchPosition.y;
            underhand = 0f;
            hasAim = aimChanged = false;
            float scale = launchPosition.y / Mathf.Max(.01f,grip.position.y);
            transform.localScale = Vector3.one * scale;
            transform.position += launchPosition - grip.position;
            motion.SampleAnimation(gameObject,DrawnTime);
            authoredDraw = ArmDirections();
            Enter(Phase.Holding);
        }
        public void StartAiming()
        {
            if (State == Phase.Holding) Enter(Phase.Aiming);
        }
        public void SetAim(float angle)
        {
            if (State != Phase.Aiming) return;
            angle = Mathf.Clamp(angle,FlightModel.MinLaunchAngle,FlightModel.MaxLaunchAngle);
            if (hasAim && angle == aimAngle) return;
            aimAngle = angle;
            hasAim = aimChanged = true;
            underhand = FlightModel.UnderhandWeight(angle);
            // Lower the release by about one and a half torso lengths at full underhand.
            float drop = 1.60f * releaseShoulder.magnitude * underhand;
            ReleaseHeight = neutralHeight - drop * transform.localScale.y;
            float forearmOffset = Mathf.Lerp(.8f * (angle-NeutralAim),angle-releaseDirections.z,underhand);
            releaseOffsets = SolveRelease(forearmOffset,drop);
            drawCorrection = DrawCorrection(authoredDraw+OverhandDrawOffset(angle-NeutralAim));
        }
        private static float Heading(Vector2 v) => Mathf.Atan2(v.y,v.x) * Mathf.Rad2Deg;
        private static float TriangleAngle(float a,float b,float opposite)
            => Mathf.Acos(Mathf.Clamp((a*a+b*b-opposite*opposite)/(2f*a*b),-1f,1f))*Mathf.Rad2Deg;

        // Set the exit direction of the forearm. With the release grip specified, the
        // hip-to-shoulder and shoulder-to-elbow segments form a two-link triangle.
        private Vector3 SolveRelease(float forearmOffset,float drop)
        {
            Vector2 elbow = releaseShoulder + releaseUpperArm;
            Vector2 gripTarget = elbow + releaseForearm;
            gripTarget.y -= drop;
            Vector2 aimedForearm = Quaternion.Euler(0,0,forearmOffset) * (Vector3)releaseForearm;
            Vector2 targetElbow = gripTarget - aimedForearm;
            float a=releaseShoulder.magnitude, b=releaseUpperArm.magnitude, d=targetElbow.magnitude;
            if ((Mathf.Abs(forearmOffset)<.0001f && drop==0f) || a<.0001f || b<.0001f || d<.0001f
                || d>a+b || d<Mathf.Abs(a-b)) return Vector3.zero;
            float side=Mathf.Sign(elbow.x*releaseShoulder.y-elbow.y*releaseShoulder.x);
            float direction=Heading(targetElbow);
            return new Vector3(Mathf.DeltaAngle(Heading(releaseShoulder),direction+side*TriangleAngle(a,d,b)),
                Mathf.DeltaAngle(Heading(releaseUpperArm),direction-side*TriangleAngle(b,d,a)),forearmOffset);
        }

        // Sum the signed local rotations so the overhand backswing can pass 180 degrees
        // without changing interpolation direction when the player drags between styles.
        private Vector3 ArmDirections()
        {
            float body = Mathf.DeltaAngle(0f,torso.eulerAngles.z);
            float arm = body + Mathf.DeltaAngle(0f,upperArm.localEulerAngles.z);
            return new Vector3(body,arm,arm + Mathf.DeltaAngle(0f,forearm.localEulerAngles.z));
        }

        private Vector3 UnderhandPose(float time,float cockTime,float finishTime)
        {
            // Absolute torso, upper-arm and forearm angles. The elbow trails behind
            // the hip, passes low and leads the wrist forward into the follow-through.
            Vector3 cock = LowDraw;
            Vector3 exit = releaseDirections + releaseOffsets;
            Vector3 finish = new Vector3(-20f,3f,48f);
            if (time <= cockTime) return cock;
            if (time >= finishTime) return finish;
            bool beforeRelease = time < releaseTime;
            float start = beforeRelease ? cockTime : releaseTime;
            float end = beforeRelease ? releaseTime : finishTime;
            float t = Mathf.InverseLerp(start,end,time);
            Vector3 from = beforeRelease ? cock : exit;
            Vector3 to = beforeRelease ? exit : finish;
            // A shared tangent on either side of release keeps the arm swinging through it.
            Vector3 tangent = (finish-cock)*((end-start)/(finishTime-cockTime));
            float basis = beforeRelease ? t*t*(t-1f) : t*(1f-t)*(1f-t);
            return Vector3.Lerp(from,to,t*t*(3f-2f*t)) + tangent*basis;
        }

        private static Vector2 Direction(float degrees)
            => new Vector2(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad));

        private static Vector3 OverhandDrawOffset(float delta)
            => new Vector3(.36f*delta,36f+.32f*delta,-37f+.27f*delta);

        // Solve just the *drawn* pose, not every passing position: the wrist
        // passes close to the shoulder during a sidearm swing. Rotating between
        // key poses keeps the elbow from flipping at that point.
        private Vector3 DrawCorrection(Vector3 over)
        {
            if (underhand<=0f || underhand>=1f) return Vector3.zero;
            float a=releaseUpperArm.magnitude, b=releaseForearm.magnitude;
            Vector2 wrist=Vector2.Lerp(Direction(over.y)*a+Direction(over.z)*b,
                Direction(LowDraw.y)*a+Direction(LowDraw.z)*b,underhand);
            float reach=Mathf.Clamp(wrist.magnitude,.94f*(a+b),a+b);
            float heading=Heading(wrist);
            float upper=heading-TriangleAngle(a,reach,b);
            float fore=heading+TriangleAngle(b,reach,a);
            float styleUpper=underhand<.5f ? over.y : LowDraw.y;
            float turns=360f*Mathf.Round((styleUpper-upper)/360f);
            Vector3 blended=Vector3.Lerp(over,LowDraw,underhand);
            return new Vector3(0f,upper+turns-blended.y,fore+turns-blended.z);
        }

        private void SamplePose(float time)
        {
            // Start from the Spine pose on every changed frame, including a new
            // angle on the held ready pose. Offsets are absolute, never accumulated.
            motion.SampleAnimation(gameObject,time);
            aimChanged = false;
            if (!hasAim) return;
            float delta=aimAngle-NeutralAim;
            float cockTime=DrawnTime;
            float swing=Mathf.SmoothStep(0,1,Mathf.InverseLerp(LoadedTime,releaseTime,time));
            float follow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(releaseTime,releaseTime+.14f,time));
            float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.10f,time))
                * (1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(releaseTime+.14f,motion.length,time)));
            // Cock the elbow behind the shoulder for a lofted throw so the hand
            // clears the face; for a low throw, fold the forearm down beside the chest.
            Vector3 cock=OverhandDrawOffset(delta);
            Vector3 finish=new Vector3(.25f,.70f,.90f)*delta;
            Vector3 offsets=Vector3.Lerp(Vector3.Lerp(cock,releaseOffsets,swing),finish,follow)*weight;
            Vector3 authored=ArmDirections();
            Vector3 ready=Vector3.zero;
            float readyWeight=0f;
            // In the ready stance the wrist is ahead of the torso. Blend out of
            // that same stance during windup, including on a quick click.
            if (time < cockTime)
            {
                readyWeight=Mathf.SmoothStep(0f,1f,time/ReadyTime)
                    * (1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(ReadyTime,cockTime,time)));
                ready=new Vector3(.26f*delta,-47f+.55f*delta,39f+1.25f*delta);
            }
            Vector3 over=Vector3.Lerp(authored+offsets,ready,readyWeight);
            if (underhand > 0f)
            {
                // Let the low throw sweep up past the release before easing into
                // the forward-weighted finishing pose.
                float lowWeight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.10f,time))
                    * (1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(releaseTime+.22f,motion.length,time)));
                Vector3 low=Vector3.Lerp(authored,UnderhandPose(time,LoadedTime,releaseTime+.22f),lowWeight);
                float drawWeight=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(ReadyTime,cockTime,time))*(1f-swing);
                over=Vector3.Lerp(over,Vector3.Lerp(low,ready,readyWeight),underhand)+drawCorrection*drawWeight;
            }
            offsets=over-authored;
            Quaternion shoulderRotation=upperArm.rotation, elbowRotation=forearm.rotation;
            Quaternion headRotation=head.rotation, handRotation=grip.rotation;
            torso.rotation=Quaternion.AngleAxis(offsets.x,Vector3.forward)*torso.rotation;
            upperArm.rotation=Quaternion.AngleAxis(offsets.y,Vector3.forward)*shoulderRotation;
            forearm.rotation=Quaternion.AngleAxis(offsets.z,Vector3.forward)*elbowRotation;
            head.rotation=Quaternion.AngleAxis(delta*.38f*weight,Vector3.forward)*headRotation;
            grip.rotation=Quaternion.Slerp(handRotation,Quaternion.Euler(0,0,aimAngle),weight);
        }
        public void Begin()
        {
            if (State == Phase.Holding || State == Phase.Aiming) Enter(Phase.Windup);
        }
        private float DriveClipTime(float progress)
        {
            float t=Mathf.Clamp01(progress);
            return Mathf.Lerp(LoadedTime,releaseTime,t*t*(2f-t));
        }
        private void SampleWindup()
        {
            float driveElapsed=windupElapsed-DrawDuration-GatherDuration;
            if (driveElapsed<=0f)
            {
                float t=Mathf.Clamp01(windupElapsed/DrawDuration);
                float next=windupElapsed<DrawDuration
                    ? Mathf.Lerp(drawStartTime,DrawnTime,t+t*t*(1f-t))
                    : Mathf.SmoothStep(DrawnTime,LoadedTime,(windupElapsed-DrawDuration)/GatherDuration);
                if (time!=next || aimChanged) SamplePose(next);
                time=next;
                return;
            }
            time=DriveClipTime(driveElapsed/DriveDuration);
            if (time>=releaseTime)
            {
                SamplePose(releaseTime);
                return;
            }
            // Both clocks meet at release. Use the delayed arm angles on the moving
            // shoulder: the feet stay on the body clip and each bone keeps its length.
            float armTime=DriveClipTime((driveElapsed-ArmDelay)/(DriveDuration-ArmDelay));
            SamplePose(armTime);
            Quaternion upper=upperArm.rotation, fore=forearm.rotation, hand=grip.rotation;
            SamplePose(time);
            upperArm.rotation=upper;
            forearm.rotation=fore;
            grip.rotation=hand;
        }
        public bool Advance(float dt)
        {
            switch (State)
            {
                case Phase.Aiming:
                    float next = Mathf.Min(time + dt, ReadyTime);
                    bool sample = next != time || aimChanged;
                    time = next;
                    if (sample) SamplePose(time);
                    break;
                case Phase.Windup:
                    windupElapsed += dt;
                    SampleWindup();
                    if (time < releaseTime) return false;
                    Enter(Phase.FollowThrough);
                    return true;
                case Phase.FollowThrough:
                    time = Mathf.Min(time + dt*.8f,motion.length);
                    SamplePose(time);
                    if (time >= motion.length) Enter(Phase.Resting);
                    break;
            }
            return false;
        }
        private void Enter(Phase next)
        {
            State = next;
            if (next == Phase.Windup)
            {
                drawStartTime = time;
                windupElapsed = 0f;
            }
            if (next == Phase.Holding)
            {
                time = 0;
                motion.SampleAnimation(gameObject,0);
            }
        }
    }
}
