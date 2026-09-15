using UnityEngine;

namespace Meniscus.Core
{
    /// <summary>
    /// Persistent cross-scene state holder. Survives scene loads via DontDestroyOnLoad so the player's
    /// stats and settings carry from the main menu into the saloon match and back again. A single
    /// instance is guaranteed: it bootstraps itself before the first scene loads, so neither the menu
    /// nor the gameplay scene needs to author one, and any duplicate that does appear destroys itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        [Header("Persistent Stats (survive scene loads)")]
        [SerializeField] int matchesPlayed;
        [SerializeField] int matchesWon;
        [SerializeField] int matchesLost;
        [SerializeField] int bestBankedCash;
        [SerializeField] int lastBankedCash;
        [SerializeField] MatchOutcome lastOutcome = MatchOutcome.None;

        [Header("Persistent Settings (survive scene loads)")]
        [SerializeField] bool soundEnabled = true;
        [SerializeField, Range(0f, 1f)] float masterVolume = 0.7f;

        const string SoundKey = "meniscus.sound";
        // Key kept from when this was a music-only level, so a player's saved setting carries over.
        const string VolumeKey = "meniscus.musicVolume";

        public int MatchesPlayed => matchesPlayed;
        public int MatchesWon => matchesWon;
        public int MatchesLost => matchesLost;
        public int BestBankedCash => bestBankedCash;
        public int LastBankedCash => lastBankedCash;
        public MatchOutcome LastOutcome => lastOutcome;
        public bool SoundEnabled => soundEnabled;
        public float MasterVolume => masterVolume;

        // Created before any scene loads so the session always exists regardless of which scene the
        // player (or a test/build) opens first.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureExists();

        /// <summary>Returns the live session, creating it if the bootstrap has not run yet.</summary>
        public static GameSession EnsureExists()
        {
            if (Instance != null)
                return Instance;

            var existing = FindAnyObjectByType<GameSession>();

            if (existing != null)
            {
                existing.AdoptAsSingleton();
                return existing;
            }

            return new GameObject("Game Session").AddComponent<GameSession>();
        }

        void Awake() => AdoptAsSingleton();

        void AdoptAsSingleton()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null); // DontDestroyOnLoad only persists root objects.
            DontDestroyOnLoad(gameObject);
            Load();
            ApplySettings();
        }

        public void SetSoundEnabled(bool enabled)
        {
            soundEnabled = enabled;
            Save();
            ApplySettings();
        }

        /// <summary>Level of everything the game plays: the music beds and the effects alike.</summary>
        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            Save();
            ApplySettings();
        }

        public void ToggleSound() => SetSoundEnabled(!soundEnabled);

        /// <summary>Pushes persisted settings onto the engine so they take effect in whatever scene is live.</summary>
        public void ApplySettings()
        {
            AudioListener.volume = soundEnabled ? 1f : 0f;
            WwiseShim.WwiseAudioRuntime.MasterVolume = masterVolume;
        }

        // Settings are expected to survive a page reload, not just a scene load.
        void Load()
        {
            soundEnabled = PlayerPrefs.GetInt(SoundKey, soundEnabled ? 1 : 0) != 0;
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, masterVolume));
        }

        void Save()
        {
            PlayerPrefs.SetInt(SoundKey, soundEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(VolumeKey, masterVolume);
            PlayerPrefs.Save();
        }

        /// <summary>Folds a finished match into the running stats. Ignores in-progress (None) outcomes.</summary>
        public void RecordMatchResult(MatchOutcome outcome, int bankedCash)
        {
            if (outcome == MatchOutcome.None)
                return;

            matchesPlayed++;
            lastOutcome = outcome;
            lastBankedCash = bankedCash;

            if (outcome == MatchOutcome.PlayerWon)
                matchesWon++;
            else if (outcome == MatchOutcome.PlayerLost)
                matchesLost++;

            if (bankedCash > bestBankedCash)
                bestBankedCash = bankedCash;
        }
    }
}
