using System;
using UnityEngine;

namespace libx {
    public class NetworkMonitor : MonoBehaviour {
        // 网络状况发生改变后的回调
        public Action<NetworkReachability> onReachabilityChanged;

        private static NetworkMonitor instance;

        // 单例
        public static NetworkMonitor Instance {
            get {
                if (instance == null) {
                    instance = new GameObject("NetworkMonitor").AddComponent<NetworkMonitor>();
                    DontDestroyOnLoad(instance.gameObject);
                }
                return instance;
            }
        }

        public NetworkReachability reachability { get; private set; }

        // 采样时间
        public float sampleTime = 0.5f;

        // 距离上次场景加载完后经过的时间 秒
        private float _timeSinceLevelLoad;

        // 是否暂停了
        private bool _paused;

        private void Start() {
            reachability = Application.internetReachability;
            UnPause();
        }

        // 取消暂停
        public void UnPause() {
            _timeSinceLevelLoad = Time.timeSinceLevelLoad;
            _paused = false;
        }

        public void Pause() {
            _paused = true;
        }

        private void Update() {
            if (_paused) {
                return;
            }

            if (!(Time.timeSinceLevelLoad - _timeSinceLevelLoad >= sampleTime))
                return;

            NetworkReachability networkReachability = Application.internetReachability;

            if (reachability != networkReachability) {
                if (onReachabilityChanged != null) {
                    // 网络状况发生了改变
                    onReachabilityChanged(networkReachability);
                }
                reachability = networkReachability;
            }

            _timeSinceLevelLoad = Time.timeSinceLevelLoad;
        }
    }
}