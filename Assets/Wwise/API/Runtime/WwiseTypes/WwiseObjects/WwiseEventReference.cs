namespace AK.Wwise.Unity.API.WwiseTypes
{
	public class WwiseEventReference : WwiseObjectReference
	{
		// Serialized name must stay verbatim: the .asset files store it as this key.
		public bool IsInUserDefinedSoundBank;

		public override WwiseObjectType WwiseObjectType => WwiseObjectType.Event;
	}
}
