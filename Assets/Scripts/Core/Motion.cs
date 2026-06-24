using UnityEngine;

namespace Meniscus.Core
{
    /// <summary>
    /// Easing helpers for one-shot "juicy" reveals (banners, cards, confirm pops). Pure functions so
    /// they are trivially unit-testable.
    /// </summary>
    public static class Easing
    {
        /// <summary>
        /// Eases 0→1 but overshoots past 1 before settling, giving a springy "pop". <paramref name="overshoot"/>
        /// controls the bump height (the classic 1.70158 ≈ ~10% past the target). Input is clamped to 0..1,
        /// so OutBack(0) == 0 and OutBack(1) == 1 exactly.
        /// </summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t);
            var c1 = overshoot;
            var c3 = c1 + 1f;
            var p = t - 1f;
            return 1f + c3 * (p * p * p) + c1 * (p * p);
        }
    }

    /// <summary>
    /// A lightweight under-damped spring for selection lifts and button presses: it accelerates toward a
    /// target and overshoots slightly before settling, so motion feels springy instead of linear. Uses
    /// semi-implicit Euler with a clamped timestep, so a frame hitch (or the very first frame) can't make
    /// the integrator blow up. It is a struct so each animated object owns its own state with no GC alloc;
    /// store it in a field and call <see cref="Step"/> on that field so the mutation sticks.
    /// </summary>
    public struct Spring
    {
        public float Value;
        public float Velocity;

        // Higher stiffness = snappier; damping below the critical value (2·√stiffness) gives overshoot.
        readonly float stiffness;
        readonly float damping;

        public Spring(float value, float stiffness, float damping)
        {
            Value = value;
            Velocity = 0f;
            this.stiffness = stiffness;
            this.damping = damping;
        }

        /// <summary>Advances the spring one step toward <paramref name="target"/> and returns the new value.</summary>
        public float Step(float target, float deltaTime)
        {
            // Cap the step so a long frame integrates stably instead of exploding; ignore negative dt.
            var dt = Mathf.Min(Mathf.Max(deltaTime, 0f), 1f / 30f);
            Velocity += (-stiffness * (Value - target) - damping * Velocity) * dt;
            Value += Velocity * dt;
            return Value;
        }

        /// <summary>Snaps to <paramref name="value"/> with no residual velocity (no animation).</summary>
        public void Snap(float value)
        {
            Value = value;
            Velocity = 0f;
        }
    }
}
