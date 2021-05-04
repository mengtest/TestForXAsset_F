using UnityEngine;

namespace libx {

    // Additive 场景使用 
    public class LoadSceneAdditive : MonoBehaviour {
        public string scene;
        private void Update() {
            if (Input.GetKeyUp(KeyCode.Escape)) {
                Assets.LoadSceneAsync(R.GetScene("Level"));
            } else if (Input.GetKeyUp(KeyCode.Space)) {
                Assets.LoadSceneAsync(R.GetScene(scene), true);
            }
        }
    }
}
