using UnityEngine;

/// Scene-placed event trigger. The two scenes use it for the music bed and the
/// saloon room tone, both on a start trigger, so it simply posts on Start.
[AddComponentMenu("Wwise/AkEvent")]
public class AkEvent : MonoBehaviour
{
	public AK.Wwise.Event data = new AK.Wwise.Event();
	public bool useOtherObject;
	public GameObject soundEmitterObject;
	public bool stopSoundOnDestroy = true;

	GameObject Emitter => useOtherObject && soundEmitterObject ? soundEmitterObject : gameObject;

	void Start()
	{
		data?.Post(Emitter);
	}

	void OnDestroy()
	{
		if (stopSoundOnDestroy)
			data?.Stop(Emitter);
	}
}
