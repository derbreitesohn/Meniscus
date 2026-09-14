using UnityEngine;

/// Booted the native sound engine. The shim needs no initialization, but the
/// component stays so the scenes keep loading cleanly.
[AddComponentMenu("Wwise/AkInitializer")]
public class AkInitializer : MonoBehaviour
{
	// Typed loosely: the settings asset it used to point at is gone.
	public ScriptableObject InitializationSettings;
	public string basePath;
	public string language;
}
