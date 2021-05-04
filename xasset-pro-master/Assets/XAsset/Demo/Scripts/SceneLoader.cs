using UnityEngine;

namespace libx {

    // Splash 场景使用
    public class SceneLoader : MonoBehaviour {
        public string scene;
        public float delay;

        // Use this for initialization
        void Start() {
            if (delay > 0) {
                Invoke("LoadScene", 3);
                return;
            }
            LoadScene();
        }

        void LoadScene() {
            Assets.LoadSceneAsync(scene);
        }
    }
}
