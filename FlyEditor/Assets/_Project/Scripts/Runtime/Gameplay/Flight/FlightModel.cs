using System;
using UnityEngine;

namespace FlyEditor.Flight
{
    public static class PaperPlaneGeometry
    {
        public static readonly Vector3 Nose = new Vector3(1.5f, 0f, 0f);
        public static readonly Vector3 Back = new Vector3(-1.2f, 0f, 0f);
        public static readonly Vector3 Left = new Vector3(-1.35f, 0.18f, -1.15f);
        public static readonly Vector3 Right = new Vector3(-1.35f, 0.18f, 1.15f);
        public static readonly Vector3 Keel = new Vector3(-0.8f, -0.38f, 0f);

        public static float ContactHeight(float pitch)
        {
            float s = Mathf.Sin(pitch * Mathf.Deg2Rad);
            float c = Mathf.Cos(pitch * Mathf.Deg2Rad);
            return -Mathf.Min(Mathf.Min(Nose.x * s, Back.x * s),
                Mathf.Min(Left.x * s + Left.y * c, Keel.x * s + Keel.y * c));
        }
    }

    [Serializable]
    public sealed class FlightTuning
    {
        [NonSerialized] public FlightEffects effects;
        [NonSerialized] public PaperFlightProfile paperProfile;
        [Min(0.1f)] public float launchHeight = 4f;
        [Min(0.1f)] public float launchSpeed = 13f;
        // +10% potential/kinetic conversion: stronger descent gain and matching climb cost.
        // Symmetric conversion prevents repeated climb/glide cycles from creating energy.
        [Min(0.1f)] public float gravity = 2.86f;
        [Min(0f)] public float drag = 0.012f;
        [Min(0f)] public float climbDrag = 0.014f;
        [Min(0f)] public float sink = 0.16f;
        [Range(1f, 40f)] public float climbPitch = 22f;
        [Range(-40f, -1f)] public float divePitch = -24f;
        [Min(0.1f)] public float releasePitchDuration = 3f;
        [Min(1f)] public float pitchResponse = 90f;
        [Min(0.1f)] public float flightPathResponse = 10f;
        [Min(0.1f)] public float stallSpeed = 4f;
        [Min(1f)] public float speedCap = 25f;
        public FlightTuning Copy() => (FlightTuning)MemberwiseClone();

        public void Sanitize()
        {
            launchHeight = Mathf.Max(0.1f, launchHeight);
            launchSpeed = Mathf.Max(0.1f, launchSpeed);
            gravity = Mathf.Max(0.1f, gravity);
            drag = Mathf.Max(0f, drag);
            climbDrag = Mathf.Max(0f, climbDrag);
            sink = Mathf.Max(0f, sink);
            climbPitch = Mathf.Clamp(climbPitch, 1f, 40f);
            divePitch = Mathf.Clamp(divePitch, -40f, -1f);
            releasePitchDuration = Mathf.Max(0.1f, releasePitchDuration);
            pitchResponse = Mathf.Max(1f, pitchResponse);
            flightPathResponse = Mathf.Max(0.1f, flightPathResponse);
            speedCap = Mathf.Max(1f, speedCap);
            stallSpeed = Mathf.Clamp(stallSpeed, 0.1f, speedCap);
        }
    }

    // Ready --new click--> Flying --ground contact--> Landed --R/new click--> Ready.
    // Launch supplies flight energy. A pickup adds ground speed only.
    public sealed class FlightModel
    {
        public enum Phase { Ready, Flying, Landed }
        public Phase State { get; private set; }
        public FlightEffects Effects { get; }
        public float Durability { get; private set; }
        public int ShieldUses { get; private set; }
        public int RecoveryUses { get; private set; }
        public enum Attitude { Normal, Disturbed, Restoring }
        public Attitude Posture { get; private set; }
        public bool DiveBoostActive { get; private set; }
        private float attitudeRemaining, divingTime, carryRemaining, diveDragBlend;
        public int PickupsCollected { get; private set; }
        public int HighPickups { get; private set; }
        private bool lowGuardUsed, criticalGuardUsed, criticalRepairUsed, autoRecoveryUsed;
        public float Distance { get; private set; }
        public float Height { get; private set; }
        public float Clearance => Mathf.Max(0f, Height - PaperPlaneGeometry.ContactHeight(Pitch));
        public float Speed { get; private set; }
        public const float BoostDuration = 2f;
        private float boostRemaining, boostAmount;
        public float ForwardBoost => boostRemaining > 0f ? boostAmount : 0f;
        public float HorizontalSpeed => Speed * Mathf.Cos(FlightPathPitch * Mathf.Deg2Rad) + ForwardBoost;
        public float Pitch { get; private set; }
        public float FlightPathPitch { get; private set; }
        public Vector2 Velocity => State != Phase.Flying ? Vector2.zero :
            new Vector2(HorizontalSpeed,
                Speed * Mathf.Sin(FlightPathPitch * Mathf.Deg2Rad) - tuning.sink);
        public float Energy { get; private set; }
        public float Elapsed { get; private set; }
        public float BestDistance { get; private set; }
        public bool ControlArmed { get; private set; }
        public bool Climbing { get; private set; }
        public bool Stalling => State == Phase.Flying && Speed < tuning.stallSpeed;
        public bool Recovering => State == Phase.Flying && recovering;
        // Usable kinetic energy above stall, not a refillable source of propulsion.
        public float ClimbReserve => State == Phase.Landed ? 0f : Mathf.Clamp01(
            (Speed * Speed - tuning.stallSpeed * tuning.stallSpeed) /
            Mathf.Max(0.01f, initialSpeed * initialSpeed - tuning.stallSpeed * tuning.stallSpeed));
        private readonly FlightTuning tuning;
        private float initialSpeed;
        private bool held;
        private bool recovering;
        private bool releasePitchActive;
        private float releaseStartPitch;
        private float releasePitchElapsed;
        private float underhandLift, underhandRiseTime;

        public FlightModel(FlightTuning settings, float bestDistance = 0f)
        {
            tuning = settings;
            Effects = tuning.effects ?? FlightEffects.Base;
            tuning.Sanitize();
            State = Phase.Ready;
            BestDistance = bestDistance;
            EnterReady();
        }

        // Called from Update. Press edges are never polled from FixedUpdate.
        public void Tick(bool pressed, bool isHeld, bool restart)
        {
            switch (State)
            {
                case Phase.Ready: TickReady(pressed); break;
                case Phase.Flying: TickFlying(isHeld, restart); break;
                case Phase.Landed: TickLanded(pressed, restart); break;
            }
        }

        private void TickReady(bool pressed)
        {
            if (pressed) ChangeState(Phase.Flying);
        }

        public const float MinLaunchAngle = -12f, MaxLaunchAngle = 35f;
        internal static float UnderhandWeight(float angle)
            => Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(6f,-6f,angle));
        public void SetLaunchAngle(float angle)
            => SetLaunchPose(angle,Height);

        public void SetLaunchPose(float angle,float releaseHeight)
        {
            if (State != Phase.Ready) return;
            Pitch = FlightPathPitch = Mathf.Clamp(angle, MinLaunchAngle, MaxLaunchAngle);
            underhandLift = UnderhandWeight(Pitch) * (22f-Pitch);
            // Keep the turn within the first few metres even on upgraded aircraft.
            underhandRiseTime = Mathf.Min(Mathf.Min(.85f,tuning.releasePitchDuration*.5f),13f / Mathf.Max(1f,Speed));
            Height = Mathf.Max(PaperPlaneGeometry.ContactHeight(Pitch)+.1f,releaseHeight);
            Energy = .5f * Speed * Speed + tuning.gravity * Height;
        }
        internal FlightModel CreatePreviewModel() => new FlightModel(tuning.Copy(), BestDistance);
        internal void RestartPreview(float angle,float releaseHeight)
        {
            ChangeState(Phase.Ready);
            SetLaunchPose(angle,releaseHeight);
            Tick(true, false, false);
            Tick(false, false, false);
        }

        private void TickFlying(bool isHeld, bool restart)
        {
            if (restart) { ChangeState(Phase.Ready); return; }
            // The launch click cannot also climb, even if held for several seconds.
            if (!isHeld) ControlArmed = true;
            held = ControlArmed && isHeld;
        }

        private void TickLanded(bool pressed, bool restart)
        {
            if (pressed || restart) ChangeState(Phase.Ready);
        }

        public void SuspendInput() { held = false; ControlArmed = false; }

        public void FixedTick(float dt)
        {
            if (State == Phase.Flying && dt > 0f) FixedTickFlying(dt);
        }

        private void FixedTickFlying(float dt)
        {
            float previousClearance = Clearance;
            if (Speed < tuning.stallSpeed) recovering = true;
            else if (!held && Speed > tuning.stallSpeed + 1.5f) recovering = false;
            Climbing = held && !recovering;
            if (Posture == Attitude.Normal) UpdatePitch(dt);
            else TickDisturbance(dt);
            // The travel direction carries through release/exhaustion instead of snapping to the nose.
            float pathResponse = tuning.flightPathResponse;
            if (underhandLift > 0f) pathResponse = Mathf.Max(pathResponse, 8f/underhandRiseTime);
            FlightPathPitch = Mathf.Lerp(FlightPathPitch, Pitch, 1f - Mathf.Exp(-pathResponse * dt));
            float angle = FlightPathPitch * Mathf.Deg2Rad;
            float pathSine = Mathf.Sin(angle);
            float forwardSpeed = Speed * Mathf.Cos(angle);
            float step = dt;
            float sinkDrop = tuning.sink * dt;
            float dy = Speed * pathSine * dt - sinkDrop;
            // E/m = v^2/2 + gh. Passive sink is dissipative, not a propulsion source.
            // Game-scale approximation of parasite + lift-induced drag. As the
            // glide steepens, required lift falls and its drag contribution eases.
            // The 50% split is a balance parameter, not measured paper-airfoil data.
            float descendingSine = Mathf.Min(0f, pathSine);
            float effectiveDrag = tuning.drag * (1f - .5f * descendingSine * descendingSine)
                + (Climbing ? tuning.climbDrag : 0f);
            var extra = Effects.extra;
            if (Height < 3f) effectiveDrag *= 1f - extra.lowDrag;
            if (Speed >= 12f) effectiveDrag *= 1f - extra.fastDrag;
            if (Height >= 6f && Climbing) effectiveDrag -= tuning.climbDrag * extra.highClimb;
            effectiveDrag = Mathf.Max(0f, effectiveDrag);
            effectiveDrag *= DiveDragMultiplier(dy, dt);
            // Equivalent along-path acceleration: -g*sin(angle) - drag*v^2.
            // Growing descent angle increases the gravity component; quadratic
            // drag eventually balances it. Integrating energy avoids free boosts.
            float nextEnergy = Mathf.Max(0f, Energy - effectiveDrag * Speed * Speed * Speed * dt
                - tuning.gravity * sinkDrop);
            float nextHeight = Mathf.Min(Height + dy, nextEnergy / tuning.gravity);
            float contactHeight = PaperPlaneGeometry.ContactHeight(Pitch);
            if (nextHeight <= contactHeight)
            {
                float contactFraction = previousClearance / Mathf.Max(0.000001f, previousClearance - (nextHeight - contactHeight));
                step *= Mathf.Clamp01(contactFraction);
                AdvanceDistance(forwardSpeed, step);
                Height = contactHeight;
                ChangeState(Phase.Landed);
                return;
            }
            Height = nextHeight;
            AdvanceDistance(forwardSpeed, step);
            Speed = Mathf.Min(tuning.speedCap,
                Mathf.Sqrt(Mathf.Max(0f, 2f * (nextEnergy - tuning.gravity * Height))));
            Energy = 0.5f * Speed * Speed + tuning.gravity * Height;
        }

        private void AdvanceDistance(float forwardSpeed, float step)
        {
            float previous = Distance;
            Distance += forwardSpeed * step + boostAmount * Mathf.Min(step, boostRemaining);
            boostRemaining = Mathf.Max(0f, boostRemaining - step);
            Elapsed += step;
            var extra = Effects.extra;
            if (extra.routeRepair == 0 && extra.intervalRepair == 0) return;
            float repair = previous < 80f && Distance >= 80f ? extra.routeRepair : 0f;
            int crossed = Mathf.Min(3, Mathf.FloorToInt(Distance / 40f))
                - Mathf.Min(3, Mathf.FloorToInt(previous / 40f));
            Durability = Mathf.Min(Effects.durability, Durability + repair + crossed * extra.intervalRepair);
        }

        private void UpdatePitch(float dt)
        {
            if (underhandLift > 0f && (held || recovering))
            {
                underhandLift = 0f;
                releasePitchActive = false;
            }
            if (Climbing)
            {
                releasePitchActive = false;
                releasePitchElapsed = 0f;
                Pitch = Mathf.MoveTowards(Pitch, tuning.climbPitch, tuning.pitchResponse * dt);
                return;
            }

            if (!releasePitchActive)
            {
                releaseStartPitch = Pitch;
                releasePitchElapsed = 0f;
                releasePitchActive = true;
            }
            releasePitchElapsed = Mathf.Min(tuning.releasePitchDuration, releasePitchElapsed + dt);
            float progress = releasePitchElapsed / tuning.releasePitchDuration;
            // Ease in from the actual release angle, then ease out at the maximum dive.
            // Manual release and exhausted climb use this same segment. Releasing while
            // recovering must not restart its timer; only an eligible climb cancels it.
            float eased = progress * progress * (3f - 2f * progress);
            Pitch = Mathf.Lerp(releaseStartPitch, tuning.divePitch, eased);
            if (underhandLift > 0f)
            {
                float rise = Mathf.SmoothStep(0f,1f,releasePitchElapsed/underhandRiseTime);
                float fade = Mathf.InverseLerp(underhandRiseTime,tuning.releasePitchDuration,releasePitchElapsed);
                Pitch += underhandLift * rise * Mathf.SmoothStep(1f,0f,fade);
                if (progress >= 1f) underhandLift = 0f;
            }
        }

        private float DiveDragMultiplier(float dy, float dt)
        {
            if (!Effects.diveEnabled) return 1f;
            if (!held && dy < 0 && FlightPathPitch < -5f && Posture == Attitude.Normal)
            {
                divingTime += dt;
                if (divingTime >= Effects.diveDelay) carryRemaining = Effects.diveCarry;
                DiveBoostActive = divingTime >= Effects.diveDelay;
            }
            else
            {
                divingTime = 0;
                DiveBoostActive = carryRemaining > 0;
                carryRemaining = Mathf.Max(0, carryRemaining - dt);
            }
            // Blend the purchased drag benefit instead of jumping acceleration
            // at the unlock threshold. Carry and re-press also fade continuously.
            diveDragBlend = Mathf.MoveTowards(diveDragBlend, DiveBoostActive ? 1f : 0f, dt / .8f);
            return 1f - .28f * Mathf.SmoothStep(0f, 1f, diveDragBlend);
        }
        private void ChangePosture(Attitude next, float duration)
        {
            Posture = next;
            underhandLift = 0f;
            attitudeRemaining = duration;
            releasePitchActive = false;
        }
        private void TickDisturbance(float dt)
        {
            Climbing = false;
            if (Posture == Attitude.Restoring)
                Pitch = Mathf.MoveTowards(Pitch, 0, Mathf.Abs(Pitch) * dt / Mathf.Max(dt, attitudeRemaining));
            else Pitch = Mathf.MoveTowards(Pitch, -12f, 30f * dt);
            attitudeRemaining -= dt;
            if (attitudeRemaining <= 0) ChangePosture(Attitude.Normal, 0);
        }
        public bool TryRecoverPosture()
        {
            if (State != Phase.Flying || Posture != Attitude.Disturbed || RecoveryUses <= 0) return false;
            RecoveryUses--;
            ChangePosture(Attitude.Restoring, Effects.recoveryTime);
            return true;
        }
        private void SetFlightSpeed(float speed)
        {
            Speed = Mathf.Clamp(speed, 0, tuning.speedCap);
            Energy = .5f * Speed * Speed + tuning.gravity * Height;
        }
        public void CollectBoost()
        {
            if (State != Phase.Flying) return;
            PickupsCollected++;
            if (Height >= 5f) HighPickups++;
            float express = PickupsCollected <= 4 && PickupsCollected % 2 == 0 ? Effects.extra.expressBoost : 0;
            boostAmount = Effects.boostSpeed + express;
            boostRemaining = BoostDuration;
        }
        public void StrikeObstacle(float damage)
        {
            if (State != Phase.Flying || damage <= 0) return;
            var extra = Effects.extra;
            float resistance=(tuning.paperProfile??PaperFlightProfile.Standard).impact;
            damage *= (1f-extra.damageReduction)/resistance;
            bool blocked;
            if (extra.lowGuard > 0 && Height < 3f && !lowGuardUsed)
            { lowGuardUsed = true; blocked = true; }
            else if (ShieldUses > 0)
            { ShieldUses--; blocked = true; }
            else if (extra.criticalGuard > 0 && damage >= Durability && !criticalGuardUsed)
            { criticalGuardUsed = true; blocked = true; }
            else
            { blocked = false; Durability = Mathf.Max(0, Durability - damage); }
            if (Durability > 0 && Durability <= Effects.durability * .35f && extra.criticalRepair > 0 && !criticalRepairUsed)
            { criticalRepairUsed = true; Durability = Mathf.Min(Effects.durability, Durability + extra.criticalRepair); }
            float loss = Effects.collisionSpeedLoss * (blocked && Effects.reinforcedShield ? .5f : 1f)/resistance;
            SetFlightSpeed(Speed * (1f - loss));
            if (Durability <= 0) { ChangeState(Phase.Landed); return; }
            Pitch = Mathf.Max(tuning.divePitch, Pitch - 8);
            if (extra.autoRecovery > 0 && !autoRecoveryUsed)
            { autoRecoveryUsed = true; ChangePosture(Attitude.Restoring, .2f); }
            else ChangePosture(Attitude.Disturbed, Mathf.Max(.2f, .85f - extra.disturbance));
        }

        private void ChangeState(Phase next)
        {
            if (next == State) return;
            Exit(State);
            State = next;
            switch (State)
            {
                case Phase.Ready: EnterReady(); break;
                case Phase.Flying: EnterFlying(); break;
                case Phase.Landed: EnterLanded(); break;
            }
        }

        private void Exit(Phase previous)
        {
            if (previous == Phase.Flying)
            {
                held = false;
                Climbing = false;
                releasePitchActive = false;
                releasePitchElapsed = 0f;
            }
        }

        private void EnterReady()
        {
            Durability = Effects.durability;
            ShieldUses = Effects.shieldUses;
            RecoveryUses = Effects.recoveryUses;
            PickupsCollected = HighPickups = 0;
            lowGuardUsed = criticalGuardUsed = criticalRepairUsed = autoRecoveryUsed = false;
            divingTime = carryRemaining = diveDragBlend = 0;
            boostAmount = boostRemaining = 0f;
            DiveBoostActive = false;
            ChangePosture(Attitude.Normal, 0);
            Distance = Elapsed = Pitch = FlightPathPitch = 0f;
            Height = Mathf.Max(tuning.launchHeight, PaperPlaneGeometry.ContactHeight(0f) + 0.1f);
            Speed = Mathf.Min(tuning.launchSpeed, tuning.speedCap);
            initialSpeed = Speed;
            Energy = 0.5f * Speed * Speed + tuning.gravity * Height;
            ControlArmed = held = recovering = Climbing = false;
            releasePitchActive = false;
            releaseStartPitch = releasePitchElapsed = 0f;
        }

        private void EnterFlying() { ControlArmed = held = false; }

        private void EnterLanded()
        {
            boostAmount = boostRemaining = 0f;
            Speed = Energy = 0f;
            ControlArmed = false;
            BestDistance = Mathf.Max(BestDistance, Distance);
        }
    }
}
