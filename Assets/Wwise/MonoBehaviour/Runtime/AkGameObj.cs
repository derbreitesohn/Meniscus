using UnityEngine;

/// Marked an emitter for the native engine. Positioning is handled by Unity's
/// own audio now, so nothing is required at runtime.
[AddComponentMenu("Wwise/AkGameObj")]
[DisallowMultipleComponent]
public class AkGameObj : MonoBehaviour
{
	public bool isEnvironmentAware;
	public bool isStaticObject;
	public float scalingFactor = 1f;
	public int listenerMask = 1;
}
