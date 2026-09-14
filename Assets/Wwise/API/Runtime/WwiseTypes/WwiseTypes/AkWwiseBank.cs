using AK.Wwise.Unity.API.WwiseTypes;

namespace AK.Wwise
{
	/// Soundbanks were the Wwise delivery format. Clips are loaded straight from
	/// Resources now, so loading a bank is a no-op that still parses from the scene.
	[System.Serializable]
	public class Bank : BaseType
	{
		public WwiseBankReference WwiseObjectReference;

		public override Unity.API.WwiseTypes.WwiseObjectReference ObjectReference
		{
			get { return WwiseObjectReference; }
			set { WwiseObjectReference = value as WwiseBankReference; }
		}

		public override WwiseObjectType WwiseObjectType => WwiseObjectType.Soundbank;

		public void Load() { }

		public void Unload() { }
	}
}
