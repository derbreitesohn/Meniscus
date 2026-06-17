using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [SerializeField] CameraState currentState = CameraState.TableOverview;

        public CameraState CurrentState => currentState;

        public void SwitchCamera(CameraState newState)
        {
            if (currentState == newState)
            {
                Debug.Log($"[CameraController] Camera already in state: {newState}.");
                return;
            }

            currentState = newState;
            Debug.Log($"[CameraController] Camera switching to: {newState}.");
        }
    }
}
