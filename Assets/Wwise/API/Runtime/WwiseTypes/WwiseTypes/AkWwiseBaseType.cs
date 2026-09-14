using AK.Wwise.Unity.API.WwiseTypes;

namespace AK.Wwise
{
	/// Base for the Wwise types the game serializes. The two migration fields are
	/// kept because the scenes and prefabs still write them alongside the reference.
	[System.Serializable]
	public abstract class BaseType
	{
		public abstract WwiseObjectReference ObjectReference { get; set; }

		public abstract WwiseObjectType WwiseObjectType { get; }

		public virtual string Name => IsValid() ? ObjectReference.DisplayName : string.Empty;

		public uint Id => IsValid() ? ObjectReference.Id : AkUnitySoundEngine.AK_INVALID_UNIQUE_ID;

		public virtual bool IsValid() => ObjectReference != null;

		public bool Validate() => IsValid();

		public override string ToString() => IsValid() ? ObjectReference.ObjectName : "Empty " + GetType().Name;

		#region WwiseMigration
#pragma warning disable 0414 // assigned by the serializer, never read
		[UnityEngine.HideInInspector] [UnityEngine.SerializeField]
		private int idInternal;
		[UnityEngine.HideInInspector] [UnityEngine.SerializeField]
		private byte[] valueGuidInternal;
#pragma warning restore 0414
		#endregion
	}
}
