using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace libx {
    public class Initializer : MonoBehaviour {
        public bool splash;
        // Initializer.loggable
        public bool loggable;
        // 校验类型
        public VerifyBy verifyBy = VerifyBy.CRC;
        // 
        public string downloadURL;
        // Initializer.development
        public bool development;

        public bool dontDestroyOnLoad = true;

        public string launchScene;
        // Initializer.searchPaths
        public string[] searchPaths;
        // Initalizer.patches4Init
        public string[] patches4Init;
        // Initializer.updateAll
        public bool updateAll;

        private void Start() {
            if (dontDestroyOnLoad) {
                DontDestroyOnLoad(gameObject);
            }

            // 设置 Assets.development, Assets.loggable
            EditorInit();

            // 下载所有资源
            Assets.updateAll = updateAll;
            // 设置下载URL
            Assets.downloadURL = downloadURL;
            // 设置校验类型
            Assets.verifyBy = verifyBy;
            // 设置
            Assets.searchPaths = searchPaths;
            // Assets.patches4Init
            Assets.patches4Init = patches4Init;

            // 初始化
            Assets.Initialize(error => {
                if (!string.IsNullOrEmpty(error)) {
                    Debug.LogError(error);
                    return;
                }

                if (splash) {
                    Assets.LoadSceneAsync(R.GetScene("Splash"));
                } else {
                    Assets.LoadSceneAsync(R.GetScene(launchScene));
                }
            });
        }

        [Conditional("UNITY_EDITOR")]
        private void EditorInit() {
            Assets.development = development;
            Assets.loggable = loggable;
        }

        [Conditional("UNITY_EDITOR")]
        private void Update() {
            Assets.loggable = loggable;
        }
    }
}
