//
// UpdateScreen.cs
//
// Author:
//       MoMo的奶爸 <xasset@qq.com>
//
// Copyright (c) 2020 MoMo的奶爸
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation bundles (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace libx {

    // 更新界面(Title 场景)
    public class UpdateScreen : MonoBehaviour {
        // 清除按钮
        public Button buttonClear;
        // 关于按钮
        public Button buttonAbout;
        // 开始更新按钮
        public Button buttonStart;
        public Slider progressBar;
        public Text progressText;
        public Text version;

        private void Start() {
            NetworkMonitor.Instance.onReachabilityChanged += OnReachablityChanged;
            buttonStart.gameObject.SetActive(true);
            buttonStart.onClick.AddListener(StartUpdate);
            buttonAbout.onClick.AddListener(About);
            buttonClear.onClick.AddListener(Clear);
            version.text = Assets.currentVersions.ver;
        }

        private void OnReachablityChanged(NetworkReachability reachability) {
            // 手机上关闭 wifi 和 4g
            if (reachability == NetworkReachability.NotReachable) {
                OnMessage("网络错误");
            }
        }

        private void OnProgress(float progress) {
            progressBar.value = progress;
        }

        // 显示进度信息
        private void OnMessage(string msg) {
            progressText.text = msg;
        }

        public void About() {
            var request = Assets.LoadAsset(R.GetPrefab("AboutScreen"), typeof(GameObject));
            var asset = request.asset;
            var go = Instantiate(asset) as GameObject;
            go.name = asset.name;
            request.Require(go);
            var button = go.GetComponentInChildren<Button>();
            button.onClick.AddListener(delegate {
                DestroyImmediate(go);
            });
        }

        // 清除数据
        public void Clear() {
            MessageBox.Show("提示", "清除数据后所有数据需要重新下载，请确认！,", cleanup => {
                if (cleanup) {
                    // 清除 P 目录 下的 Versions.bundle
                    File.Delete(Assets.updatePath + "/" + Assets.VersionsFileName);
                    // PlayerPrefs 删除所有
                    PlayerPrefs.DeleteAll();
                    // PlayerPrefs 保存
                    PlayerPrefs.Save();

                    OnMessage("数据清除完毕, TOUCH TO START");
                    OnProgress(0);
                    buttonStart.gameObject.SetActive(true);
                }
            }, "清除");
        }

        // 开始更新
        public void StartUpdate() {

#if UNITY_EDITOR
            if (Assets.development) {
                StartCoroutine(EnterLevel());
                return;
            }
#endif
            OnMessage("正在获取版本信息...");
            if (Application.internetReachability == NetworkReachability.NotReachable) {
                MessageBox.Show("提示", "请检查网络连接状态", retry => {
                    if (retry) {
                        StartUpdate();
                    } else {
                        Quit();
                    }
                }, "重试", "退出");
            } else {
                // 1. 先下载 Versions
                Assets.DownloadVersions(error => {
                    if (!string.IsNullOrEmpty(error)) {
                        MessageBox.Show("提示", string.Format("获取服务器版本失败：{0}", error), retry => {
                            if (retry) {
                                StartUpdate();
                            } else {
                                Quit();
                            }
                        });
                    } else {
                        Downloader downloader;

                        // 2. 下载 资源, 分包或者 全部
                        if (Assets.DownloadPatchOrAll(Assets.patches4Init, out downloader)) {
                            var totalSize = downloader.totalDownloadSize;
                            var tips = string.Format("发现内容更新，总计需要下载 {0} 内容", Downloader.GetDisplaySize(totalSize));
                            MessageBox.Show("提示", tips, download => {
                                if (download) {
                                    downloader.onUpdate += delegate (long progress, long size, float speed) {
                                        //刷新界面
                                        OnMessage(string.Format("下载中...{0}/{1}, 速度：{2}",
                                            Downloader.GetDisplaySize(progress),
                                            Downloader.GetDisplaySize(size),
                                            Downloader.GetDisplaySpeed(speed)));
                                        OnProgress(progress * 1f / size);
                                    };
                                    downloader.onFinished += OnComplete;
                                    downloader.Start();
                                } else {
                                    Quit();
                                }
                            }, "下载", "退出");
                        } else {
                            OnComplete();
                        }
                    }
                });
            }
        }

        // 加载完成 进入 Level 场景
        private void OnComplete() {
            OnProgress(1);
            version.text = Assets.currentVersions.ver;
            OnMessage("更新完成");

            StartCoroutine(EnterLevel());
        }

        // 进入 Level 场景（游戏场景）
        private IEnumerator EnterLevel() {
            yield return null;

            OnProgress(0);

            OnMessage("加载游戏场景");

            // 加载场景 Level
            SceneAssetRequest sceneAssetRequest = Assets.LoadSceneAsync(R.GetScene("Level"));

            while (!sceneAssetRequest.isDone) {
                OnProgress(sceneAssetRequest.progress);
                yield return null;
            }
        }

        private void OnDestroy() {
            MessageBox.Dispose();
        }

        // 退出
        private void Quit() {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}