using AK.Wwise.Unity.API.WwiseTypes;
using Meniscus.WwiseShim;

namespace AK.Wwise
{
	[System.Serializable]
	public class Event : BaseType
	{
		// Serialized name must stay verbatim: the scenes store the picker result here.
		public WwiseEventReference WwiseObjectReference;

		public uint PlayingId { get; private set; }

		public override Unity.API.WwiseTypes.WwiseObjectReference ObjectReference
		{
			get { return WwiseObjectReference; }
			set { WwiseObjectReference = value as WwiseEventReference; }
		}

		public override WwiseObjectType WwiseObjectType => WwiseObjectType.Event;

		public uint Post(UnityEngine.GameObject gameObject)
		{
			if (!IsValid())
				return AkUnitySoundEngine.AK_INVALID_PLAYING_ID;

			var runtime = WwiseAudioRuntime.Instance;
			if (!runtime)
				return AkUnitySoundEngine.AK_INVALID_PLAYING_ID;

			PlayingId = runtime.Post(Name, gameObject);
			return PlayingId;
		}

		public void Stop(UnityEngine.GameObject gameObject, int transitionDuration = 0)
		{
			if (!IsValid())
				return;

			var runtime = WwiseAudioRuntime.Instance;
			if (runtime)
				runtime.Stop(Name, gameObject);
		}
	}
}
