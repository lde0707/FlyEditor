using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlyEditor.Flight
{
    // Main is the shared build entry. The playable scene also works directly in Play Mode.
    public sealed class FlightEntry : MonoBehaviour
    {
        private void Start() => SceneManager.LoadSceneAsync("Assets/_Project/Scenes/InGame.unity");
    }
}
