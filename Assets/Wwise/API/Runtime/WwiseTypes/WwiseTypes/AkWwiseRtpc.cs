using AK.Wwise.Unity.API.WwiseTypes;

namespace AK.Wwise
{
	/// Game parameters drove the Wwise mix, which no longer exists here. The values
	/// are still accepted and remembered so callers behave normally and a future
	/// mix has something to read.
	[System.Serializable]
	public class RTPC : BaseType
	{
		static readonly System.Collections.Generic.Dictionary<string, float> Values =
			new System.Collections.Generic.Dictionary<string, float>();

		public WwiseRtpcReference WwiseObjectReference;

		public override Unity.API.WwiseTypes.WwiseObjectReference ObjectReference
		{
			get { return WwiseObjectReference; }
			set { WwiseObjectReference = value as WwiseRtpcReference; }
		}

		public override WwiseObjectType WwiseObjectType => WwiseObjectType.GameParameter;

		public void SetGlobalValue(float value)
		{
			if (IsValid())
				Values[Name] = value;
		}

		public float GetGlobalValue()
		{
			return IsValid() && Values.TryGetValue(Name, out var value) ? value : 0f;
		}

		public void SetValue(UnityEngine.GameObject gameObject, float value, int changeDuration = 0)
			=> SetGlobalValue(value);

		public float GetValue(UnityEngine.GameObject gameObject) => GetGlobalValue();
	}
}
