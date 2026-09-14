using UnityEngine;

/// Soundbank loader. Clips come from Resources now, so this only keeps the
/// component (and its scene data) valid.
[AddComponentMenu("Wwise/AkBank")]
public class AkBank : MonoBehaviour
{
	public AK.Wwise.Bank data = new AK.Wwise.Bank();
	public bool useOtherObject;
	public bool decodeBank;
	public bool loadAsynchronous;
}
