// The surface of the native sound engine that game code still touches.
// Everything here used to marshal into the Wwise native library; on WebGL that
// library does not exist, so these are the few symbols worth keeping alive.

using UnityEngine;

public enum AKRESULT
{
	AK_Success = 1,
	AK_Fail = 2,
}

public static class AkUnitySoundEngine
{
	public const uint AK_INVALID_PLAYING_ID = 0;
	public const uint AK_INVALID_UNIQUE_ID = 0;
	public const uint AK_INVALID_GAME_OBJECT = unchecked((uint)-1);

	public static bool IsInitialized() => true;

	public static uint PostEvent(string eventName, GameObject gameObject)
	{
		var runtime = Meniscus.WwiseShim.WwiseAudioRuntime.Instance;
		return runtime ? runtime.Post(eventName, gameObject) : AK_INVALID_PLAYING_ID;
	}

	public static void StopAll(GameObject gameObject)
	{
		var runtime = Meniscus.WwiseShim.WwiseAudioRuntime.Instance;
		if (runtime)
			runtime.StopAll(gameObject);
	}
}
