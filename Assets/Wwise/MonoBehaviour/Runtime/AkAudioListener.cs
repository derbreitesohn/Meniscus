using UnityEngine;

/// Wwise listener. Unity's own AudioListener on the camera does this job now.
[AddComponentMenu("Wwise/AkAudioListener")]
public class AkAudioListener : MonoBehaviour
{
	public bool isDefaultListener = true;
	public bool bOverrideScalingFactor;
	public float scalingFactor = -1f;
	public uint listenerId;
}
