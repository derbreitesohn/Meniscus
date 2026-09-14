using UnityEngine;

namespace AK.Wwise.Unity.API.WwiseTypes
{
	/// Asset written by the Wwise picker: it names a Wwise object and nothing more.
	/// The assets under Assets/Wwise/ScriptableObjects still deserialize into these,
	/// which is what keeps every event assignment in the scenes and prefabs intact.
	public class WwiseObjectReference : ScriptableObject
	{
		[SerializeField] protected string objectName = string.Empty;
		[SerializeField] protected uint id;
		[SerializeField] protected string guid = string.Empty;

		public string ObjectName => objectName;

		public string DisplayName => objectName;

		public uint Id => id;

		public System.Guid Guid
		{
			get
			{
				return System.Guid.TryParse(guid, out var parsed) ? parsed : System.Guid.Empty;
			}
		}

		public virtual WwiseObjectType WwiseObjectType => WwiseObjectType.None;

		public override string ToString() => string.IsNullOrEmpty(objectName) ? base.ToString() : objectName;
	}
}
