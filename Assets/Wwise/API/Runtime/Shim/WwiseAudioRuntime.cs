// Playback engine standing in for the native Wwise sound engine.
//
// Wwise ships no WebGL binaries in this project (Assets/Wwise/.../Plugins holds
// Mac and Windows only) and no WebGL soundbanks were ever generated, so the
// authored mix cannot run in a browser. This plays the original .wav sources
// through Unity's own audio instead, keyed by the same event names the game
// already posts, so every existing inspector assignment keeps working.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Meniscus.WwiseShim
{
	public sealed class WwiseAudioRuntime : MonoBehaviour
	{
		// Music and room tone sit under the effects, roughly where the Wwise busses had them.
		static readonly HashSet<string> Beds = new HashSet<string>
		{
			"Play_Piano_Chords_State1_82BPM",
			"Play_Piano_Auftakt",
			"Play_24Bit_Piano_Chords_Melodie_State2_3_102BPM",
			"Play_music",
			"Play_bar_ambience01",
		};

		const float BedVolume = 0.45f;
		const float VoiceVolume = 1f;
		const int WarmVoices = 8;

		sealed class Voice
		{
			public AudioSource Source;
			public string EventName;
			public GameObject Owner;
			public uint PlayingId;
			public bool Loop;
		}

		static WwiseAudioRuntime s_instance;
		static bool s_quitting;
		static float s_masterVolume = 1f;

		/// Master level for everything this shim plays: the music and room-tone beds and
		/// every effect voice alike, the cat included. The authored balance between them is
		/// kept, the whole mix just rides up and down together.
		/// Applies to voices already playing, not just the next one posted.
		public static float MasterVolume
		{
			get { return s_masterVolume; }
			set
			{
				s_masterVolume = Mathf.Clamp01(value);
				if (s_instance)
					s_instance.ApplyMasterVolume();
			}
		}

		readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
		readonly List<Voice> _active = new List<Voice>();
		readonly Stack<AudioSource> _idle = new Stack<AudioSource>();
		readonly HashSet<string> _warned = new HashSet<string>();
		uint _nextPlayingId = 1;

		public static WwiseAudioRuntime Instance
		{
			get
			{
				if (s_instance || s_quitting)
					return s_instance;

				var go = new GameObject("~WwiseAudioRuntime") { hideFlags = HideFlags.HideAndDontSave };
				s_instance = go.AddComponent<WwiseAudioRuntime>();
				DontDestroyOnLoad(go);
				return s_instance;
			}
		}

		void Awake()
		{
			for (var i = 0; i < WarmVoices; i++)
				_idle.Push(CreateSource());

			EnsureListener();
		}

		/// Nothing is audible without an AudioListener somewhere in the scene. The scenes
		/// were built around Wwise's own listener and carry no Unity one, so AkAudioListener
		/// now adds it; this covers any scene that lacks even that.
		void EnsureListener()
		{
			if (FindAnyObjectByType<AudioListener>() == null)
				gameObject.AddComponent<AudioListener>();
		}

		void OnApplicationQuit() => s_quitting = true;

		void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

		void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

		void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
			=> EnsureListener();

		AudioSource CreateSource()
		{
			var source = gameObject.AddComponent<AudioSource>();
			source.playOnAwake = false;
			// The Wwise attenuations are gone with the engine, so everything plays flat
			// rather than risk positioned sounds falling outside an unmapped listener.
			source.spatialBlend = 0f;
			return source;
		}

		public uint Post(string eventName, GameObject owner)
		{
			if (string.IsNullOrEmpty(eventName))
				return 0;

			if (!WwiseAudioMap.Events.TryGetValue(eventName, out var entry))
			{
				if (_warned.Add(eventName))
					Debug.LogWarning($"[WwiseShim] no audio mapped for event '{eventName}'.");
				return 0;
			}

			// Stop_* events carry no media of their own; they silence their Play_ counterpart.
			if (!string.IsNullOrEmpty(entry.Stops))
			{
				Stop(entry.Stops, owner);
				return 0;
			}

			if (entry.Clips == null || entry.Clips.Length == 0)
				return 0;

			var clip = Resolve(entry.Clips[Random.Range(0, entry.Clips.Length)]);
			if (!clip)
				return 0;

			// A looping event restarted on the same owner should not stack voices.
			if (entry.Loop)
				Stop(eventName, owner);

			var source = _idle.Count > 0 ? _idle.Pop() : CreateSource();
			source.clip = clip;
			source.loop = entry.Loop;
			source.volume = VolumeFor(eventName);
			source.time = 0f;

			// On WebGL a clip whose audio data is not resident yet plays nothing at all and
			// fails quietly, so wait for the decode rather than dropping the sound.
			if (clip.loadState == AudioDataLoadState.Unloaded)
				clip.LoadAudioData();

			if (clip.loadState == AudioDataLoadState.Loaded)
				source.Play();
			else
				StartCoroutine(PlayWhenLoaded(source, clip));

			var id = _nextPlayingId++;
			_active.Add(new Voice
			{
				Source = source,
				EventName = eventName,
				Owner = owner,
				PlayingId = id,
				Loop = entry.Loop,
			});
			return id;
		}

		/// Waits for a clip's audio data, retrying if the decode stalls.
		///
		/// A browser will not decode audio until the page has had a user gesture, and a
		/// decode requested before that never completes on its own - it sits in Loading
		/// forever, even long after the context has been unlocked. Anything posted at scene
		/// start (the menu music, the room tone) lands in exactly that window, so the load
		/// is re-requested until it takes.
		static IEnumerator PlayWhenLoaded(AudioSource source, AudioClip clip)
		{
			const float AttemptSeconds = 4f;
			const float TotalSeconds = 40f;

			var giveUp = Time.realtimeSinceStartup + TotalSeconds;

			while (clip && Time.realtimeSinceStartup < giveUp)
			{
				// The voice may have been stopped and recycled onto another clip meanwhile.
				if (!source || source.clip != clip)
					yield break;

				if (clip.loadState == AudioDataLoadState.Loaded)
				{
					source.Play();
					yield break;
				}

				var attemptEnd = Time.realtimeSinceStartup + AttemptSeconds;
				while (clip && clip.loadState == AudioDataLoadState.Loading &&
				       Time.realtimeSinceStartup < attemptEnd)
					yield return null;

				if (!clip || clip.loadState == AudioDataLoadState.Loaded)
					continue;

				// Still not decoded: throw the request away and ask again.
				clip.UnloadAudioData();
				clip.LoadAudioData();
				yield return null;
			}
		}

		static float VolumeFor(string eventName)
			=> (Beds.Contains(eventName) ? BedVolume : VoiceVolume) * s_masterVolume;

		void ApplyMasterVolume()
		{
			for (var i = 0; i < _active.Count; i++)
			{
				var voice = _active[i];
				if (voice.Source)
					voice.Source.volume = VolumeFor(voice.EventName);
			}
		}

		public void Stop(string eventName, GameObject owner)
		{
			for (var i = _active.Count - 1; i >= 0; i--)
			{
				var voice = _active[i];
				if (voice.EventName != eventName)
					continue;
				// A null owner stops the event wherever it is playing.
				if (owner && voice.Owner && voice.Owner != owner)
					continue;
				Release(i);
			}
		}

		public void StopAll(GameObject owner)
		{
			for (var i = _active.Count - 1; i >= 0; i--)
				if (_active[i].Owner == owner)
					Release(i);
		}

		void Update()
		{
			// Reclaim one-shots that have finished and anything whose emitter is gone.
			for (var i = _active.Count - 1; i >= 0; i--)
			{
				var voice = _active[i];
				var ownerGone = voice.Owner == null && !ReferenceEquals(voice.Owner, null);
				if (ownerGone || (!voice.Loop && !voice.Source.isPlaying))
					Release(i);
			}
		}

		void Release(int index)
		{
			var voice = _active[index];
			_active.RemoveAt(index);
			voice.Source.Stop();
			voice.Source.clip = null;
			_idle.Push(voice.Source);
		}

		AudioClip Resolve(string clipName)
		{
			if (_clips.TryGetValue(clipName, out var cached))
				return cached;

			var clip = Resources.Load<AudioClip>(WwiseAudioMap.ResourcePath + clipName);
			if (!clip && _warned.Add(clipName))
				Debug.LogWarning($"[WwiseShim] missing AudioClip resource '{clipName}'.");

			_clips[clipName] = clip;
			return clip;
		}
	}
}
