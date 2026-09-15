using UnityEngine;

/// Wwise listener. Wwise had its own listener, so neither scene carries Unity's
/// AudioListener — and without one in the scene every AudioSource is silent, which
/// is exactly what the shim plays through. Carry a real listener on the same object
/// (the camera, in both scenes) so the audio is actually heard.
[AddComponentMenu("Wwise/AkAudioListener")]
public class AkAudioListener : MonoBehaviour
{
	public bool isDefaultListener = true;
	public bool bOverrideScalingFactor;
	public float scalingFactor = -1f;
	public uint listenerId;

	void Awake()
	{
		// Unity warns if two listeners are ever live at once, so only add one if the
		// scene genuinely has none.
		if (!GetComponent<AudioListener>() && !FindAnyObjectByType<AudioListener>())
			gameObject.AddComponent<AudioListener>();
	}
}
