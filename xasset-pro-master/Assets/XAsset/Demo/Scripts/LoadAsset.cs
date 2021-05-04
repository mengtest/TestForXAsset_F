using System.Collections.Generic;
using UnityEngine;

namespace libx {

    // Children 场景 有使用
    public class LoadAsset : MonoBehaviour {
        public string[] assetNames;

        List<AssetRequest> list = new List<AssetRequest>();

        // Use this for initialization
        void Start() {
            foreach (string assetName in assetNames) {

                AssetRequest assetRequest = Assets.LoadAssetAsync(assetName, typeof(GameObject));
                list.Add(assetRequest);

                assetRequest.completed += delegate {

                    GameObject go = Instantiate(assetRequest.asset) as GameObject;
                    if (go != null) {
                        go.name = assetRequest.asset.name;
                        var holder = go.GetComponent<ObjectHolder>();
                        if (holder.objects != null) {
                            foreach (var o in holder.objects) {
                                var go2 = Instantiate(o) as GameObject;
                                go2.name = o.name;
                            }
                        }
                    }
                };
            }
        }

        private void Update() {

            if (Input.GetKeyUp(KeyCode.Escape)) {
                for (int i = 0; i < list.Count; i++) {
                    var item = list[i];
                    item.Release();
                }
                list.Clear();
                Assets.LoadSceneAsync(R.GetScene("Level"));
            }
        }
    }
}
