using System;

namespace Dustweave.Mansion
{
    // Heading changes run concurrently with native strafing; they are not an artificial movement delay.
    public struct Heading
    {
        public float From, Target, Elapsed, Duration;
        public float Value
        {
            get
            {
                float t = Duration <= 0 ? 1 : Math.Min(1, Elapsed / Duration);
                t = t * t * (3 - 2 * t);
                return From + Motion.Delta(From, Target) * t;
            }
        }

        public static Heading At(float yaw)
        {
            return new Heading
            {
                From = yaw,
                Target = yaw,
                Elapsed = 1
            };
        }

        public void Aim(float yaw, float duration)
        {
            if (Math.Abs(Motion.Delta(Target, yaw)) < 3)
                return;
            From = Value;
            Target = yaw;
            Elapsed = 0;
            Duration = duration;
        }

        public void Advance(float seconds)
        {
            Elapsed = Math.Min(Duration, Elapsed + seconds);
        }
    }

    public static class Motion
    {
        public const float DefaultTurnSeconds = 5f / 60f;
        public static float Angle(float x, float z)
        {
            return (float)(Math.Atan2(x, z) * 180 / Math.PI);
        }

        public static float Delta(float from, float to)
        {
            float d = (to - from) % 360;
            if (d > 180)
                d -= 360;
            if (d < -180)
                d += 360;
            return d;
        }

        public static float TurnDuration(float frameSeconds)
        {
            return Math.Max(.04f, Math.Min(.2f, frameSeconds * 5));
        }
    }
}
