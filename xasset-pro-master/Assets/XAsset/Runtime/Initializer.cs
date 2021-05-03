using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace libx {

    // 挂在在 打包场景中
    public class Initializer : MonoBehaviour {
        public bool splash;
        // Initializer.loggable
        public bool loggable;
        // 校验类型
        public VerifyBy verifyBy = VerifyBy.CRC;
        // 下载路径
        public string downloadURL;
        // Initializer.development，是否开发模式
        public bool development;

        public bool dontDestroyOnLoad = true;

        // 启动场景名, 根据这个名字加载场景
        // 这个场景所在的bundle 需要放进初始包内，因为这个时候还没有启动更新流程
        public string launchScene;
        // 搜索路径
        public string[] searchPaths;
        // 初始需要下载的资源
        public string[] patches4Init;
        // 是否下载所有资源
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
            // 
            Assets.searchPathArray = searchPaths;
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
                    // 这里还没有进入 下载场景, 所以 资源必须要在包里
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
