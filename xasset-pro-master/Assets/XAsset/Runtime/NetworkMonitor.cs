using System;
using UnityEngine;

namespace libx {
    public class NetworkMonitor : MonoBehaviour {
        public Action<NetworkReachability> onReachabilityChanged;

        private static NetworkMonitor instance;

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

        public float sampleTime = 0.5f;
        private float _time;
        // 是否暂停了
        private bool _paused;

        private void Start() {
            reachability = Application.internetReachability;
            UnPause();
        }

        // 取消暂停
        public void UnPause() {
            _time = Time.timeSinceLevelLoad;
            _paused = false;
        }

        public void Pause() {
            _paused = true;
        }

        private void Update() {
            if (_paused) {
                return;
            }

            if (!(Time.timeSinceLevelLoad - _time >= sampleTime))
                return;

            NetworkReachability networkReachability = Application.internetReachability;

            if (reachability != networkReachability) {
                if (onReachabilityChanged != null) {
                    onReachabilityChanged(networkReachability);
                }
                reachability = networkReachability;
            }

            _time = Time.timeSinceLevelLoad;
        }
    }
}