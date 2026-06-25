using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Drives the dealer / main-character figure's animation (the MC_Animator controller): his coin-toss
    /// gesture as a round opens, and his win / lose reactions when the match ends. The game logic lives in the
    /// <see cref="Meniscus.Core.GameManager"/>; this is the thin presentation seam that turns those moments
    /// into animator triggers, so the controller's COINTOSS / WIN / LOSE states — authored but never fired —
    /// finally play.
    ///
    /// "Win" / "lose" are from the dealer's point of view: he WINS when the player overflows (the player loses
    /// the match) and LOSES when the player banks three rounds (the player wins). Resolves its Animator
    /// automatically — the scene's lone MC_Animator user — so no manual wiring is required; assign one in the
    /// inspector to override. Every trigger is a no-op without a bound Animator, so EditMode tests and unwired
    /// scenes are unaffected (mirrors the other play-mode-only presentation pieces).
    /// </summary>
    [DisallowMultipleComponent]
    public class DealerAnimator : MonoBehaviour
    {
        [Tooltip("The dealer's Animator (uses the MC_Animator controller). Auto-resolved at runtime if empty.")]
        [SerializeField] Animator animator;
        [Tooltip("Name of the AnimatorController used to auto-find the dealer when no Animator is assigned. " +
                 "Matched against runtimeAnimatorController.name (the .controller asset's file name).")]
        [SerializeField] string controllerName = "MC_Animator";

        // Trigger parameter names on MC_Animator (confirmed lowercase in the controller asset).
        const string TossTrigger = "toss";
        const string WinTrigger = "win";
        const string LoseTrigger = "lose";

        static readonly int TossHash = Animator.StringToHash(TossTrigger);
        static readonly int WinHash = Animator.StringToHash(WinTrigger);
        static readonly int LoseHash = Animator.StringToHash(LoseTrigger);

        public Animator Animator => animator;

        /// <summary>The dealer (for camera framing). Falls back to this object if no Animator is bound.</summary>
        public Transform FocusTransform => animator != null ? animator.transform : transform;

        void Awake() => EnsureAnimator();

        /// <summary>The dealer tosses the coin (start-of-round coin toss). COINTOSS state.</summary>
        public void PlayToss() => Fire(TossHash, TossTrigger);

        /// <summary>The dealer wins the match — the player overflowed. His gloating WIN state.</summary>
        public void PlayWin() => Fire(WinHash, WinTrigger);

        /// <summary>The dealer loses the match — the player banked three rounds. His defeated LOSE state.</summary>
        public void PlayLose() => Fire(LoseHash, LoseTrigger);

        void Fire(int hash, string paramName)
        {
            EnsureAnimator();

            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                return;

            if (!HasParameter(paramName))
            {
                Debug.LogWarning(
                    $"[DealerAnimator] '{paramName}' trigger is missing from {animator.name}'s controller; " +
                    "the reaction won't play. Expected the MC_Animator parameters toss / win / lose.");
                return;
            }

            // Clear any stale queued trigger before re-firing so a rapid re-entry can't double up.
            animator.ResetTrigger(hash);
            animator.SetTrigger(hash);
        }

        bool HasParameter(string paramName)
        {
            var parameters = animator.parameters;

            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == paramName)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Bind an Animator if one isn't already set: prefer one on this object / its children (when the
        /// component sits on the dealer), else find the scene's MC_Animator user. Safe to call repeatedly.
        /// </summary>
        public void EnsureAnimator()
        {
            if (animator != null)
                return;

            var local = GetComponentInChildren<Animator>();

            if (IsDealerController(local))
            {
                animator = local;
                return;
            }

            animator = FindAnimatorByControllerName(controllerName);
        }

        bool IsDealerController(Animator candidate) =>
            candidate != null && candidate.runtimeAnimatorController != null &&
            candidate.runtimeAnimatorController.name == controllerName;

        static Animator FindAnimatorByControllerName(string controllerName)
        {
            var all = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].runtimeAnimatorController != null &&
                    all[i].runtimeAnimatorController.name == controllerName)
                    return all[i];
            }

            return null;
        }

        /// <summary>
        /// Find the dealer in the scene and ensure it carries a <see cref="DealerAnimator"/>, adding one to the
        /// figure that uses the MC_Animator controller if none exists yet. Returns null when no such Animator
        /// is present (an unwired scene / EditMode tests), so callers stay null-safe. Play-mode only — it adds
        /// a component, mirroring the GameManager's other runtime-fallback resolutions.
        /// </summary>
        public static DealerAnimator ResolveOrCreate(string controllerName = "MC_Animator")
        {
            var existing = Object.FindAnyObjectByType<DealerAnimator>();

            if (existing != null)
            {
                existing.EnsureAnimator();
                return existing;
            }

            var dealer = FindAnimatorByControllerName(controllerName);

            if (dealer == null)
                return null;

            var component = dealer.GetComponent<DealerAnimator>();

            if (component == null)
                component = dealer.gameObject.AddComponent<DealerAnimator>();

            component.controllerName = controllerName;
            component.animator = dealer;
            return component;
        }
    }
}
