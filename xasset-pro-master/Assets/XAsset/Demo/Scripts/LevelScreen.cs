using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace libx {
    // Level 场景
    public class LevelScreen : MonoBehaviour {
        // 返回按钮
        public Button buttonBack;
        // 分包按钮
        public Button buttonPatch;
        public Slider progressBar;
        public Text progressText;

        private void Start() {
            buttonBack.onClick.AddListener(Back);


            if (Assets.currentVersions != null) {
                // e.g. ["name=Title,bundleIDList=2,5,7,9,1,4,6,8,0,3"]
                List<Patch> patchList = Assets.currentVersions.patchList;

                // 按照分包数量 创建多个 按钮,每个按钮都可以下载各自的分包
                for (var i = 0; i < patchList.Count; i++) {
                    Patch patch = patchList[i];

                    GameObject go = Instantiate(buttonPatch.gameObject, buttonPatch.transform.parent, false);
                    // 分包名
                    go.name = patch.name;
                    Text text = go.GetComponentInChildren<Text>();
                    // 分包名
                    text.text = go.name;
                    go.GetComponent<Button>().onClick.AddListener(delegate {

                        Downloader downloader;
                        
                        // 根据 传进来 分包名 下载所有资源
                        if (Assets.DownloadPatchOrAll(new[] { patch.name }, out downloader)) {

                            long totalSize = downloader.size;
                            string tips = string.Format("总计需要下载 {0} 内容", Downloader.GetDisplaySize(totalSize));

                            MessageBox.Show("更新提示", tips, isDownload => {
                                if (isDownload) {

                                    downloader.onUpdate += delegate (long progress, long size, float speed) {

                                        //刷新界面
                                        OnMessage(string.Format("下载中...{0}/{1}, 速度：{2}",
                                            Downloader.GetDisplaySize(progress),
                                            Downloader.GetDisplaySize(size),
                                            Downloader.GetDisplaySpeed(speed)));

                                        OnProgress(progress * 1f / size);
                                    };

                                    downloader.onFinished += delegate {
                                        OnMessage("下载完成");

                                        // Demo 中 每个 分包 按照 场景来划分,
                                        // 所以分包下载完后就直接加载相关的场景了
                                        // 可以修改这里的处理来适应自己的项目
                                        LoadScene(patch);
                                    };


                                    downloader.Start();

                                } else {
                                    MessageBox.Show("提示", "下载失败：用户取消", isOk => { }, "确定", "退出");
                                }
                            }, "下载");
                        } else {
                            LoadScene(patch);
                        }
                    });
                }
            }
            buttonPatch.gameObject.SetActive(false);
        }


        // LevelScreen.LoadScene()
        private static void LoadScene(Patch patch) {
            if (string.IsNullOrEmpty(patch.name)) {
                return;
            }

            Assets.LoadSceneAsync(R.GetScene(patch.name));
        }

        // 显示下载进度信息
        private void OnProgress(float progress) {
            progressBar.value = progress;
        }

        // 显示下载进度描述信息
        private void OnMessage(string msg) {
            progressText.text = msg;
        }

        private void Back() {
            Assets.LoadSceneAsync(R.GetScene("Title"));
        }
    }
}