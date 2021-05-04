//
// Assets.cs
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace libx {
    public sealed class Assets : MonoBehaviour {
        // Bundle 所在的文件夹名
        public const string BundlesDirName = "Bundles";
        // versions 文件名
        public const string VersionsFileName = "versions.bundle";
        // PlayerPrefs 键值
        private const string KVersions = "version";
        // log tag
        private const string TAG = "[Assets]";

        // 编辑器下 加载资源的委托
        public static Func<string, Type, Object> assetLoader { get; set; }
        // 加载资源完成时的委托
        public static Action<string> onAssetLoaded { get; set; }
        // 卸载资源
        public static Action<string> onAssetUnloaded { get; set; }
        public static Func<Versions> versionsLoader { get; set; }

        private static void Log(string s) {
            if (!loggable)
                return;
            Debug.Log(string.Format("{0}{1}", TAG, s));
        }

        private static void LogFormat(string format, params object[] args) {
            if (!loggable)
                return;
            Debug.LogFormat(format, args);
        }

        #region API

        public static VerifyBy verifyBy = VerifyBy.CRC;

        // Assets.development
        public static bool development { get; set; }
        // Assets.updateAll
        public static bool updateAll { get; set; }
        // Assets.loggable
        public static bool loggable { get; set; }

        public static string downloadURL { get; set; }

        // basePath 有两个路径
        //  项目输出的bundle 文件夹(编辑器模式下会默认设置) e.g. D:/Projects/UnityProjects/TestForXAsset5.1/xasset-pro-master/Bundles/Windows/
        //  S 目录 e.g. D:
        public static string basePath { get; set; }

        // P 目录
        public static string updatePath { get; set; }

        // 在进入下载之前 需要的分包，放在 S 目录中
        // 如果这部分 资源更改了，可以下载 这部分分包资源,然后替换(在重启后)
        public static string[] patches4Init { get; set; }
        // Assets.searchPaths
        // 搜索路径
        // e.g.
        // [Assets/XAsset/Demo/Scenes, Asset/XAsset/Demo/UI/Prefabs]
        // 暂时只用来处理场景
        public static string[] searchPathArray { get; set; }

        public static string[] GetAllAssetPaths() {
            var assets = new List<string>();
            assets.AddRange(AssetToBundleDict.Keys);
            return assets.ToArray();
        }

        // Assets.Initializer 初始化
        public static void Initialize(Action<string> completedCallback = null) {
            var instance = FindObjectOfType<Assets>();
            if (instance == null) {
                // 添加 Assets
                instance = new GameObject("Assets").AddComponent<Assets>();
                DontDestroyOnLoad(instance.gameObject);
                // 网络改变监控
                NetworkMonitor.Instance.onReachabilityChanged += OnReachablityChanged;
                // 低内存调用
                Application.lowMemory += ApplicationOnLowMemory;
            }

            // 编辑器模式下 默认设置为 项目 文件夹内
            if (string.IsNullOrEmpty(basePath))
                // 如果没有设置就变成 S 目录
                basePath = Application.streamingAssetsPath + "/" + BundlesDirName + "/";

            if (string.IsNullOrEmpty(updatePath))
                // P 目录
                updatePath = Application.persistentDataPath + "/" + BundlesDirName + "/";

            // 创建 P 目录
            if (!Directory.Exists(updatePath))
                Directory.CreateDirectory(updatePath);

            // 获取平台名
            if (string.IsNullOrEmpty(platform)) {
                platform = GetPlatformForAssetBundles(Application.platform);
            }

            // 
            if (Application.platform == RuntimePlatform.OSXEditor ||
                Application.platform == RuntimePlatform.OSXPlayer ||
                Application.platform == RuntimePlatform.IPhonePlayer) {
                _localProtocol = "file://";
            } else if (Application.platform == RuntimePlatform.WindowsEditor ||
                       Application.platform == RuntimePlatform.WindowsPlayer) {
                _localProtocol = "file:///";
            }

            // 反序列化 Versions 的回调, 需要在调用之前 申明
            Action<Versions> onDeserializeVersionsCallback = new Action<Versions>(versions => {
                // 这里的 currentVersions 是 从 S 目录拷贝到 P 目录的
                currentVersions = versions;

                GetInfoFromVersions(currentVersions);

                Debug.Log("Initialize");
                Debug.LogFormat("Development:{0}", development);
                Debug.LogFormat("Platform:{0}", platform);
                Debug.LogFormat("UpdatePath:{0}", updatePath);
                Debug.LogFormat("DownloadURL:{0}", downloadURL);
                Debug.LogFormat("UpdateUnusedAssetsImmediate:{0}", updateUnusedAssetsImmediate);
                Debug.LogFormat("Version:{0}", currentVersions.ver);
                if (completedCallback != null)
                    completedCallback(null);
            });

            // 开发模式
            if (development) {
                if (versionsLoader != null)
                    onDeserializeVersionsCallback(versionsLoader());
            // 非开发模式
            } else {
                // 获取 P 目录下的 versions.bundle 路径（下载到这里）
                // e.g. C:/Users/void87/AppData/LocalLow/mmdnb/xasset-pro/Bundles/buildInVersions.bundle
                string updatePathVersionPath = string.Format("{0}buildInVersions.bundle", updatePath);

                // 获取基于 basePath 的 versions.bundle, （从这里下载）
                // e.g. file:///D:/Projects/UnityProjects/TestForXAsset5.1/xasset-pro-master/Assets/StreamingAssets/Bundles/versions.bundle
                string basePathVersionURL = GetLocalURL(VersionsFileName);

                //// add by 黄鑫
                //// 加载 P 目录下的 Versions
                //buildinVersions = LoadVersions(updatePathVersionPath);
                //onLoadVersions(buildinVersions);

                // 将 basePath 的 version 下载到 updatePath
                UnityWebRequest unityWebRequest = Download(basePathVersionURL, updatePathVersionPath);

                // 下载  本地 versions.bundle 到 buildInVersions.bundle
                unityWebRequest.SendWebRequest().completed += operation => {
                    // 下载不了 本地, 进不了游戏
                    if (!string.IsNullOrEmpty(unityWebRequest.error)) {
                        if (completedCallback != null)
                            completedCallback(unityWebRequest.error);
                    } else {
                        // 加载 P 目录下的 Versions
                        // outside = false, 表示资源不用从外面下载
                        updatePathVersions = DeserializeVersions(updatePathVersionPath);

                        //// 本地版本暂时设置为 0.0.0
                        //PlayerPrefs.SetString(KVersions, "0.0.0");

                        // 覆盖安装
                        if (OverlayInstallation(updatePathVersions.ver)) {
                            // 加载完 Versions 的回调
                            onDeserializeVersionsCallback(updatePathVersions);

                            List<BundleRef> filesInBuild = updatePathVersions.GetFilesInBuild();
                            foreach (BundleRef bundleRef in filesInBuild) {
                                var path = string.Format("{0}{1}", updatePath, bundleRef.name);
                                if (File.Exists(path)) {
                                    File.Delete(path);
                                }
                            }

                            // 设置本地的 版本号
                            PlayerPrefs.SetString(KVersions, updatePathVersions.ver);
                        } else {
                            //// 原代码 这里永远都 是 FALSE
                            //string path = GetDownloadURL(VersionsFileName);

                            //onLoadVersions(File.Exists(path) ? LoadVersions(path) : updatePathVersions);

                            onDeserializeVersionsCallback(updatePathVersions);
                        }
                    }
                    unityWebRequest.Dispose();
                };
            }
        }

        // 覆盖安装(暂时都是覆盖安装)
        private static bool OverlayInstallation(string version) {
            var innerVersion = PlayerPrefs.GetString(KVersions);
            if (string.IsNullOrEmpty(innerVersion)) {
                return true;
            }
            var v1 = new System.Version(version);
            var v2 = new System.Version(innerVersion);
            return v1 > v2;
            // return true;
        }

        // Assets.GetInfoFromVersions
        // 从Versions里读取信息, 赋值 ActiveVariants, AssetToBundles, BundleToChildren
        private static void GetInfoFromVersions(Versions versions) {
            ActiveVariantList.Clear();
            AssetToBundleDict.Clear();
            BundleToChildrenBundleDict.Clear();

            // e.g.
            // [0, name=Title.unity, bundle=2, dir=0]
            // [1, name=MessageBox.prefab, bundle=1, dir=1]
            // [2, name=Level.untiy, bundle=0, dir=0]
            List<AssetRef> assetRefList = versions.assetRefList;
            // e.g.
            // ["Assets/XAsset/Demo/Scenes", "Assets/XAsset/Demo/UI/Prefabs"]
            string[] dirArray = versions.dirArray;
            // e.g.
            // [id=0, name=_level, len=111226, location=0, hash=,crc=,children=3,7,8,0, ...]
            List<BundleRef> bundleRefList = versions.bundleRefList;

            string[] activeVariants = versions.activeVariants;

            // 将 BundleRef 转换为 BundleToChildren
            foreach (BundleRef bundlRef in bundleRefList) {
                BundleToChildrenBundleDict[bundlRef.name] = Array.ConvertAll(bundlRef.childrenBundleIDArray, id => bundleRefList[id].name);
            }

            // 将 AssetRef 转换为 AssetToBundles
            foreach (AssetRef assetRef in assetRefList) {
                // e.g.
                //  Assets/XAsset/Demo/Scenes + / + Title.unity
                string path = string.Format("{0}/{1}", dirArray[assetRef.dirID], assetRef.name);

                if (assetRef.bundleID >= 0 && assetRef.bundleID < bundleRefList.Count)
                    // 
                    AssetToBundleDict[path] = bundleRefList[assetRef.bundleID].name;
                else
                    AssetToBundleDict[path] = string.Empty;
            }

            ActiveVariantList.AddRange(activeVariants);
        }

        // 不管 包内是否包含了所有资源, 一定要先下载 服务器上的版本文件
        public static void DownloadVersions(Action<string> downloadVersionsCallback) {
            // e.g. http://192.168.1.113/Bundles/Windows/versions.bundle
            string versionsFileNameUrl = GetDownloadURL(VersionsFileName);
            Debug.LogFormat("DownloadVersions:{0}", versionsFileNameUrl);
            // e.g. C:/Users/void87/AppData/Local/Temp/mmdnb/xasset-pro/versions.bundle
            string tempVersionsFileName = Application.temporaryCachePath + "/" + VersionsFileName;

            // 将 服务器上的 Versions 下载到 Temp 目录
            // 服务器上是一定要有这个 Versions 的， 不然，流程就进行不下去了
            UnityWebRequest unityWebRequest = Download(versionsFileNameUrl, tempVersionsFileName);
            unityWebRequest.SendWebRequest().completed += operation => {
                if (string.IsNullOrEmpty(unityWebRequest.error)) {
                    // 重新加载 currentVersions
                    // 从这里这里 currentVersions 变成了 服务器版本
                    // outside = true, 表示要从外面下载
                    currentVersions = DeserializeVersions(Application.temporaryCachePath + "/" + VersionsFileName, true);

                    GetInfoFromVersions(currentVersions);

                    // 设置当前的版本
                    PlayerPrefs.SetString(KVersions, currentVersions.ver);

                    RemoveUnusedAssets();
                }

                if (downloadVersionsCallback != null)
                    downloadVersionsCallback(unityWebRequest.error);

                unityWebRequest.Dispose();
            };
        }

        // 加载versions.bundle 反序列化为 Versions
        // 
        public static Versions DeserializeVersions(string filename, bool outside = false) {
            // 不存在 直接创建一个初始的 Versions
            if (!File.Exists(filename))
                return new Versions();
            try {
                // 反序列化 Versions
                using (FileStream fileStream = File.OpenRead(filename)) {
                    BinaryReader reader = new BinaryReader(fileStream);
                    Versions version = new Versions();
                    // version.outside
                    version.outside = outside;
                    version.Deserialize(reader);
                    return version;
                }
            } catch (Exception e) {
                Debug.LogException(e);
                return new Versions();
            }
        }

        // 下载分包资源或者所有资源
        public static bool DownloadPatchOrAll(string[] patchNameArray, out Downloader downLoader) {
            // 勾选 updateAll， 直接下载所有
            if (updateAll) {
                return DownloadAll(out downLoader);
            }

            List<BundleRef> willDownloadBundleList = new List<BundleRef>();

            foreach (string patchName in patchNameArray) {
                // 获取 已经存储在本地的 分包的 版本号,必须是下载过的分包 才会有版本号
                // S 目录里的分包  的版本号是 string.empty
                string savedPatchName = PlayerPrefs.GetString(patchName, string.Empty);

                // 分包 版本号 不一致，（这里没考虑 用户自己破坏文件的 情况，但是可以通过主界面清理按钮，清除资源，重新下载）
                if (!savedPatchName.Equals(currentVersions.ver)) {
                    List<BundleRef> newBundleRefList = GetNewBundleRef(patchName);

                    foreach (BundleRef bundleRef in newBundleRefList) {
                        if (!willDownloadBundleList.Exists(x => x.name.Equals(bundleRef.name)))
                            willDownloadBundleList.Add(bundleRef);
                    }
                }
            }

            if (willDownloadBundleList.Count > 0) {
                var downloader = new Downloader();
                foreach (var item in willDownloadBundleList)
                    downloader.AddDownload(GetDownloadURL(item.name), updatePath + item.name, item.crc, item.len);
                Downloaders.Add(downloader);
                downLoader = downloader;
                downLoader.onFinished += () => {
                    foreach (var item in patchNameArray) {
                        // 保存分包的版本号
                        PlayerPrefs.SetString(item, currentVersions.ver);
                    }
                };
                return true;
            }

            downLoader = null;
            return false;
        }

        // Assets.DownloadAll
        // 下载所有的资源
        public static bool DownloadAll(out Downloader downloader) {
            List<BundleRef> bundleRefList = new List<BundleRef>();


            for (int i = 0; i < currentVersions.bundleRefList.Count; i++) {
                BundleRef bundleRef = currentVersions.bundleRefList[i];

                if (IsNewBundleRef(bundleRef)) {
                    bundleRefList.Add(bundleRef);
                }
            }

            if (bundleRefList.Count > 0) {
                Downloader tempDownloader = new Downloader();
                foreach (var item in bundleRefList)
                    tempDownloader.AddDownload(GetDownloadURL(item.name), updatePath + item.name, item.crc, item.len);
                Downloaders.Add(tempDownloader);
                downloader = tempDownloader;
                return true;
            }

            downloader = null;
            return false;
        }

        public static void Pause() {
            foreach (var downloader in Downloaders)
                downloader.Pause();
        }

        public static void UnPause() {
            foreach (var downloader in Downloaders)
                downloader.UnPause();
        }

        // 加载场景 (异步）, 传入的只是场景的名字
        // sceneName e.g. Title.unity
        public static SceneAssetRequest LoadSceneAsync(string sceneName, bool additive = false) {
            Assert.IsNotNull(sceneName, "path != null");
            string assetBundleName;
            
            // 获取完整的 场景名
            sceneName = GetSearchPath(sceneName, out assetBundleName);

            SceneAssetAsyncRequest sceneAssetAsyncRequest = new SceneAssetAsyncRequest(sceneName, additive) {
                assetBundleName = assetBundleName 
            };

            Debug.LogFormat("<color=red>[LoadSceneAsync]</color>:{0}", sceneName);

            // 
            sceneAssetAsyncRequest.Load();

            // 被引用加1
            sceneAssetAsyncRequest.Retain();

            // e.g. Assets/XAsset/Demo/Scenes/Title.unity
            sceneAssetAsyncRequest.name = sceneName;

            // 添加到 正在 加载的 场景请求 列表
            LoadingSceneAssetRequestSceneList.Add(sceneAssetAsyncRequest);

            if (!additive) {
                if (_runningSceneAssetRequest != null) {
                    _runningSceneAssetRequest.Release();
                    _runningSceneAssetRequest = null;
                }

                _runningSceneAssetRequest = sceneAssetAsyncRequest;
            } else {
                if (_runningSceneAssetRequest != null) {
                    _runningSceneAssetRequest.additives.Add(sceneAssetAsyncRequest);
                }
            }

            return sceneAssetAsyncRequest;
        }

        public static void UnloadScene(SceneAssetRequest scene) {
            scene.Release();
        }

        // Assets.LoadAssetAsync()
        public static AssetRequest LoadAssetAsync(string path, Type type) {
            return LoadAsset(path, type, true);
        }

        // Assets.LoadAsset()
        public static AssetRequest LoadAsset(string path, Type type) {
            return LoadAsset(path, type, false);
        }

        public static void UnloadAsset(AssetRequest asset) {
            asset.Release();
        }

        // Assets.RemoveUnusedAssets()
        public static void RemoveUnusedAssets() {
            updateUnusedAssetsNow = true;
        }

        #endregion

        #region Private

        // 将 url 下载 到 filename
        private static UnityWebRequest Download(string url, string filename) {
            var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(filename);
            return request;
        }

        /// <summary>
        /// StreamingAssets 内的版本
        /// S 目录 会拷贝到 P 目录
        /// </summary>
        public static Versions updatePathVersions { get; private set; }

        /// <summary>
        /// 有两个版本
        ///     1. 从 basePath 目录 拷贝到 P 目录后, 读取的是 P 目录的 版本
        ///     2. 进入下载阶段后, 先下载 服务器的 Versions 到 temp 目录, 然后读取这个 Versions
        /// </summary>
        public static Versions currentVersions { get; private set; }


        // Bundle 需要更新吗
        private static bool IsNewBundleRef(BundleRef bundleRef) {
            if (updatePathVersions != null)
                // S 目录 内 是否包含 这个 bunlde
                if (updatePathVersions.Contains(bundleRef))
                    return false;

            string path = string.Format("{0}{1}", updatePath, bundleRef.name);
            FileInfo fileInfo = new FileInfo(path);
            // 不存在这个文件, 直接 true
            if (!fileInfo.Exists)
                return true;

            // 直接读取 PlayerPrefs 中保存的内容，该值在 Download.Copy 方法中写入
            var comparison = StringComparison.OrdinalIgnoreCase;
            string ver = PlayerPrefs.GetString(path);
            if (ver.Equals(bundleRef.crc, comparison)) {
                return false;
            }

            return true;

            //var comparison = StringComparison.OrdinalIgnoreCase;
            //using (var stream = File.OpenRead(path)) {
            //    if (stream.Length != bundle.len)
            //        return true;
            //    if (verifyBy != VerifyBy.CRC)
            //        return false;
            //    var crc = Utility.GetCRC32Hash(stream);
            //    return !crc.Equals(bundle.crc, comparison);
            //}
        }

        // Assets.GetNewBundleRef()
        // 获取新Bundle
        private static List<BundleRef> GetNewBundleRef(string patchName) {
            List<BundleRef> bundleRefList = new List<BundleRef>();

            // 查找 当前 版本中 分包的 bundle
            // 
            List<BundleRef> findedBundleRefList = currentVersions.GetBundleRefByPatchName(patchName);

            // 遍历 分包中的 bundle
            foreach (BundleRef bundleRef in findedBundleRefList) {
                if (IsNewBundleRef(bundleRef)) {
                    bundleRefList.Add(bundleRef);
                }
            }

            return bundleRefList;
        }

        private static string GetPlatformForAssetBundles(RuntimePlatform target) {
            switch (target) {
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return "Windows";
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    return "OSX"; // OSX
                default:
                    return null;
            }
        }

        private static string GetDownloadURL(string filename) {
            return string.Format("{0}{1}/{2}", downloadURL, platform, filename);
        }

        // 获取基于basePath的URL
        private static string GetLocalURL(string filename) {
            return _localProtocol + string.Format("{0}{1}", basePath, filename);
        }



        private static readonly List<Downloader> Downloaders = new List<Downloader>();

        // 当前正在使用的 SceneAssetRequest
        private static SceneAssetRequest _runningSceneAssetRequest;

        // 正在使用的 AssetRequest
        // 包含 Asset
        private static readonly Dictionary<string, AssetRequest> UsingAssetRequestDict = new Dictionary<string, AssetRequest>();

        // 正在加载的 AssetRequest
        private static readonly List<AssetRequest> LoadingAssetRequestList = new List<AssetRequest>();
        // 已经加载的 AssetRequest
        private static readonly List<AssetRequest> LoadedAssetRequestList = new List<AssetRequest>();
        // 不用的 AssetRequest
        private static readonly List<AssetRequest> UnusedAssetRequestList = new List<AssetRequest>();

        // 正在使用的 SceneAssetRequest
        private static readonly List<SceneAssetRequest> UsingSceneAssetRequestList = new List<SceneAssetRequest>();
        // 正在加载的 SceneAssetRequest
        private static readonly List<SceneAssetRequest> LoadingSceneAssetRequestSceneList = new List<SceneAssetRequest>();


        private void OnApplicationFocus(bool hasFocus) {
#if UNITY_EDITOR // 编辑器去掉这个可以用来模拟手机上切换后台环境对下载器得功能进行测试
            if (hasFocus) {
                UnPause();
            } else {
                Pause();
            }
#endif
        }

        private static void OnReachablityChanged(NetworkReachability reachability) {
            if (reachability == NetworkReachability.NotReachable) {
                Pause();
            } else {
                Pause();
                UnPause();
            }
        }

        private void Update() {
            UpdateDownloaders();
            UpdateAssets();
            UpdateBundles();
        }

        // 更新
        private static void UpdateDownloaders() {
            if (Downloaders.Count > 0) {
                for (var i = 0; i < Downloaders.Count; ++i) {
                    var downloader = Downloaders[i];
                    downloader.Update();
                    if (downloader.isDone) {
                        Debug.LogFormat("RemoveDownloader:{0}", i);
                        Downloaders.RemoveAt(i);
                        --i;
                    }
                }
            }
        }

        // 更新 Asset
        private static void UpdateAssets() {
            // 处理正在加载的 AssetRequest
            for (int i = 0; i < LoadingAssetRequestList.Count; ++i) {
                AssetRequest loadingAssetRequest = LoadingAssetRequestList[i];
                if (loadingAssetRequest.Update())
                    continue;
                if (!string.IsNullOrEmpty(loadingAssetRequest.error)) {

                    loadingAssetRequest.Release();

                    Debug.LogErrorFormat("加载失败：{0}({1})", loadingAssetRequest.url, loadingAssetRequest.error);

                    if (loadingAssetRequest.IsUnused()) {
                        UnusedAssetRequestList.Add(loadingAssetRequest);
                    }

                } else {
                    OnAssetLoaded(loadingAssetRequest.url);

                    if (!LoadedAssetRequestList.Contains(loadingAssetRequest)) {
                        LoadedAssetRequestList.Add(loadingAssetRequest);
                    }
                }

                LoadingAssetRequestList.RemoveAt(i);
                --i;
            }

            if (updateUnusedAssetsNow || updateUnusedAssetsImmediate) {
                for (var i = 0; i < LoadedAssetRequestList.Count; ++i) {
                    var request = LoadedAssetRequestList[i];
                    request.UpdateRequires();
                    if (request.IsUnused()) {
                        if (!UnusedAssetRequestList.Contains(request)) {
                            UnusedAssetRequestList.Add(request);
                            LoadedAssetRequestList.RemoveAt(i);
                            --i;
                        }
                    }
                }

                updateUnusedAssetsNow = false;
            }

            if (UnusedAssetRequestList.Count > 0) {
                for (var i = 0; i < UnusedAssetRequestList.Count; ++i) {
                    var request = UnusedAssetRequestList[i];
                    OnAssetUnloaded(request.url);
                    UsingAssetRequestDict.Remove(request.url);
                    request.Unload();
                    Debug.LogFormat("<color=red>[UnloadAsset]</color>:{0}", request.url);
                }

                UnusedAssetRequestList.Clear();
            }

            // 处理正在加载的 SceneAssetRequest
            for (var i = 0; i < LoadingSceneAssetRequestSceneList.Count; ++i) {
                SceneAssetRequest sceneAssetRequest = LoadingSceneAssetRequestSceneList[i];

                // 更新 正在加载的 SceneAssetRequest
                if (sceneAssetRequest.Update()) {
                    continue;
                }

                // 从正在加载的 SceneAssetRequest 中移除
                LoadingSceneAssetRequestSceneList.RemoveAt(i);

                if (!string.IsNullOrEmpty(sceneAssetRequest.error)) {
                    Debug.LogErrorFormat("加载失败：{0}({1})", sceneAssetRequest.url, sceneAssetRequest.error);
                    sceneAssetRequest.Release();
                } else {
                    // 添加到正在使用的 SceneAssetRequest
                    UsingSceneAssetRequestList.Add(sceneAssetRequest);
                    OnAssetLoaded(sceneAssetRequest.url);
                }

                --i;
            }

            // 处理正在使用的 SceneAssetRequest
            for (int i = 0; i < UsingSceneAssetRequestList.Count; ++i) {
                SceneAssetRequest sceneAssetRequest = UsingSceneAssetRequestList[i];
                // 跳过 被引用 大于 0
                if (!sceneAssetRequest.IsUnused())
                    continue;
                
                UsingSceneAssetRequestList.RemoveAt(i);
                Debug.LogFormat("<color=red>[UnloadScene]</color>:{0}", sceneAssetRequest.url);

                OnAssetUnloaded(sceneAssetRequest.url);
                sceneAssetRequest.Unload();

                RemoveUnusedAssets();
                --i;
            }
        }

        // Assets.UpdateBundles()
        // 更新 Bundles
        private static void UpdateBundles() {
            var max = MAX_BUNDLES_PERFRAME;
            if (ToloadBundleRequestList.Count > 0 && max > 0 && LoadingBundleRequestList.Count < max) {
                for (var i = 0; i < Math.Min(max - LoadingBundleRequestList.Count, ToloadBundleRequestList.Count); ++i) {
                    var item = ToloadBundleRequestList[i];
                    if (item.loadState == LoadState.Init) {
                        item.Load();
                        LoadingBundleRequestList.Add(item);
                        ToloadBundleRequestList.RemoveAt(i);
                        --i;
                        Debug.LogFormat("Remove {0} from to load bundles by init state.", item.url);
                    } else if (item.loadState == LoadState.Loaded) {
                        ToloadBundleRequestList.RemoveAt(i);
                        --i;
                        Debug.LogFormat("Remove {0} from to load bundles by loaded state.", item.url);
                    }
                }
            }

            // 处理正在加载的 BundleReqeust
            for (var i = 0; i < LoadingBundleRequestList.Count; ++i) {
                BundleRequest bundleRequest = LoadingBundleRequestList[i];
                if (bundleRequest.Update())
                    continue;

                // 从
                LoadingBundleRequestList.RemoveAt(i);
                --i;
            }

            foreach (var item in UsingBundleRequestDict) {
                if (item.Value.isDone && item.Value.IsUnused()) {
                    UnusedBundleRequestList.Add(item.Value);
                }
            }

            if (UnusedBundleRequestList.Count > 0) {
                for (var i = 0; i < UnusedBundleRequestList.Count; ++i) {
                    var item = UnusedBundleRequestList[i];
                    item.Unload();
                    Debug.LogFormat("<color=red>[UnloadBundle]</color>:{0}", item.url);
                    UsingBundleRequestDict.Remove(item.name);
                }

                UnusedBundleRequestList.Clear();
            }
        }

        public static bool updateUnusedAssetsImmediate { get; set; }

        //
        private static bool updateUnusedAssetsNow { get; set; }

        // 平台名
        public static string platform {
            get { return _platform; }

            set { _platform = value; }
        }

        public static string DumpAssets() {
            var sb = new StringBuilder();
            if (_runningSceneAssetRequest != null) {
                sb.AppendLine("Scene:" + _runningSceneAssetRequest.name);
                sb.AppendLine("Additive:" + _runningSceneAssetRequest.additives.Count);
                foreach (var additive in _runningSceneAssetRequest.additives) {
                    if (additive.IsUnused()) {
                        continue;
                    }

                    sb.AppendLine("\t" + additive.name);
                }
            }

            sb.AppendLine("Asset:" + UsingAssetRequestDict.Count);
            foreach (var request in UsingAssetRequestDict) {
                sb.AppendLine("\t" + request.Key);
            }

            sb.AppendLine("Bundle:" + UsingBundleRequestDict.Count);
            foreach (var request in UsingBundleRequestDict) {
                sb.AppendLine("\t" + request.Key);
            }

            return sb.ToString();
        }

        private static void ApplicationOnLowMemory() {
            RemoveUnusedAssets();
        }

        // Assets.OnAssetLoaded
        // 资源加载完成
        private static void OnAssetLoaded(string path) {
            if (onAssetLoaded != null)
                onAssetLoaded(path);
        }

        // Assets.OnAssetUnloaded
        // 资源卸载完成
        private static void OnAssetUnloaded(string path) {
            if (onAssetUnloaded != null)
                onAssetUnloaded(path);
        }

        
        private static void AddRequest(AssetRequest request, bool load = true) {
            UsingAssetRequestDict.Add(request.url, request);
            LoadingAssetRequestList.Add(request);
            if (load)
                request.Load();
        }

        // AssetRequest.LoadAsset()
        private static AssetRequest LoadAsset(string path, Type type, bool async) {
            Assert.IsNotNull(path, "path != null");

            var isWebURL = path.StartsWith("http://", StringComparison.Ordinal) ||
                           path.StartsWith("https://", StringComparison.Ordinal) ||
                           path.StartsWith("file://", StringComparison.Ordinal) ||
                           path.StartsWith("ftp://", StringComparison.Ordinal) ||
                           path.StartsWith("jar:file://", StringComparison.Ordinal);
            // e.g. _messagebox
            string assetBundleName = null;
            if (!isWebURL) {
                path = GetSearchPath(path, out assetBundleName);
            }

            AssetRequest request;
            if (UsingAssetRequestDict.TryGetValue(path, out request)) {
                if (!request.isDone && !async) {
                    request.LoadImmediate();
                }

                request.Retain();
                if (!LoadingAssetRequestList.Contains(request)) {
                    LoadingAssetRequestList.Add(request);
                }

                return request;
            }

            if (!string.IsNullOrEmpty(assetBundleName)) {
                request = async
                    ? new BundleAssetAsyncRequest(assetBundleName)
                    : new BundleAssetRequest(assetBundleName);
            } else {
                request = isWebURL ? new WebAssetRequest() : new AssetRequest();
            }

            Debug.LogFormat("<color=red>[LoadAsset]</color>:{0}", path);

            // e.g. Assets/XAsset/Demo/UI/Prefabs/MessageBox.prefab
            request.name = path;
            // e.g. Assets/XAsset/Demo/UI/Prefabs/MessageBox.prefab
            request.url = path;
            request.assetType = type;

            AddRequest(request);
            // 被引用加1
            request.Retain();
            return request;
        }

        #endregion

        #region Paths

        // 平台名
        private static string _platform;
        // 文件协议, iOS file:// windows file:///
        private static string _localProtocol;

        // 获取 完整的 assetName
        private static string GetSearchPath(string assetName, out string assetBundleName) {
            // 如果 assetName 带有路径，从 AssetToBundles 里查找到后直接返回
            if (AssetToBundleDict.TryGetValue(assetName, out assetBundleName))
                return assetName;

            // e.g. [Assets/XAsset/Demo/Scenes, Asset/XAsset/Demo/UI/Prefabs]
            if (searchPathArray != null) {
                // 遍历所有搜索路径
                foreach (string searchPath in searchPathArray) {
                    // 组合 searchPath, assetName
                    // e.g. Assets/XAsset/Demo/Scenes/Title.unity
                    string existPath = string.Format("{0}/{1}", searchPath, assetName);
                    // 开发模式,  直接读取 File 信息
                    if (development && File.Exists(existPath)) {
                        return existPath;
                    }
                    // 用组合路径从 AssetToBundles 里查找
                    if (AssetToBundleDict.TryGetValue(existPath, out assetBundleName))
                        return existPath;
                }
            }

            // 以上都没有找到, 直接返回 传进来的 assetName
            return assetName;
        }

        public static string GetAssetsPath(string path) {
            var actualPath = Application.persistentDataPath;
            actualPath = Path.Combine(actualPath, BundlesDirName);
            actualPath = Path.Combine(actualPath, path);
            if (File.Exists(actualPath)) {
                return actualPath.Replace("\\", "/");
            }

            return Path.Combine(Application.dataPath, path).Replace("\\", "/");
        }

        #endregion

        #region BundleRequests

        // 暂时先改成0, 本来是10
        private static readonly int MAX_BUNDLES_PERFRAME = 0;

        // 正在使用的 BundleRequest
        // [bundle名, BundleRequest]
        private static readonly Dictionary<string, BundleRequest> UsingBundleRequestDict =
            new Dictionary<string, BundleRequest>();

        private static readonly List<BundleRequest> LoadingBundleRequestList = new List<BundleRequest>();

        private static readonly List<BundleRequest> UnusedBundleRequestList = new List<BundleRequest>();

        private static readonly List<BundleRequest> ToloadBundleRequestList = new List<BundleRequest>();

        private static readonly List<string> ActiveVariantList = new List<string>();

        // [asset名，bundle 文件名]
        // e.g.
        // [Assets/XAsset/Demo/Scenes/Title.unity, _title]
        private static readonly Dictionary<string, string> AssetToBundleDict = new Dictionary<string, string>();

        // [bundle文件名, [子bundle文件名]]
        // e.g.
        // [
        //  _level
        //  [
        //      children_level,
        //      shared_assets_xasset_demo_ui_sprites_backgrounds,
        //      shared_assets_xasset_demo_ui_sprites_messagebox,
        //      shared_assets_xasset_demo_ui_sprites_title
        //  ]
        // ]
        private static readonly Dictionary<string, string[]> BundleToChildrenBundleDict = new Dictionary<string, string[]>();

        // Assets.GetChildren
        // 获取 bundle 依赖的 bundle名
        internal static string[] GetChildrenBundleNameArray(string bundleName) {
            string[] childrenBundleNameArray;
            if (BundleToChildrenBundleDict.TryGetValue(bundleName, out childrenBundleNameArray))
                return childrenBundleNameArray;

            return new string[0];
        }

        // Assets.LoadBundle
        internal static BundleRequest LoadBundle(string assetBundleName) {
            return LoadBundle(assetBundleName, false);
        }

        // Assets.LoadBundleAsync
        internal static BundleRequest LoadBundleAsync(string assetBundleName) {
            return LoadBundle(assetBundleName, true);
        }

        internal static void UnloadBundle(BundleRequest bundle) {
            bundle.Release();
        }

        // Assets.LoadBundle
        // 加载 Bundle
        internal static BundleRequest LoadBundle(string assetBundleName, bool asyncMode) {

            if (string.IsNullOrEmpty(assetBundleName)) {
                Debug.LogError("bundle == null");
                return null;
            }

            // e.g. shared_assets_xasset_demo_ui_sprites_backgrounds
            assetBundleName = RemapVariantName(assetBundleName);
            // 
            string url = GetDataPath(assetBundleName) + assetBundleName;

            BundleRequest bundleRequest;

            // 在使用中的 BundleRequest 里查找
            if (UsingBundleRequestDict.TryGetValue(assetBundleName, out bundleRequest)) {
                // 被引用加1
                bundleRequest.Retain();

                if (!bundleRequest.isDone && !asyncMode) {
                    bundleRequest.LoadImmediate();
                }

                return bundleRequest;
            }

            if (url.StartsWith("http://", StringComparison.Ordinal) ||
                url.StartsWith("https://", StringComparison.Ordinal) ||
                url.StartsWith("file://", StringComparison.Ordinal) ||
                url.StartsWith("ftp://", StringComparison.Ordinal))
                bundleRequest = new WebBundleRequest();
            else
                bundleRequest = asyncMode ? new BundleAsyncRequest() : new BundleRequest();

            // e.g. D:/Projects/UnityProjects/TestForXAsset5.1/xasset-pro-master/Assets/StreamingAssets/Bundles/_messagebox
            bundleRequest.url = url;
            bundleRequest.name = assetBundleName;

            // 加入到 正在使用的 BundleRequest
            UsingBundleRequestDict.Add(assetBundleName, bundleRequest);


            if (MAX_BUNDLES_PERFRAME > 0 && (bundleRequest is BundleAsyncRequest || bundleRequest is WebBundleRequest)) {
                ToloadBundleRequestList.Add(bundleRequest);
            } else {
                // 加载 场景所在的 BundeReqeust
                bundleRequest.Load();

                // 将这个BundeRequest 添加到 正在使用的 BundleRequest
                LoadingBundleRequestList.Add(bundleRequest);

                Debug.LogFormat("<color=red>[LoadBundle]</color>: {0}", url);
            }

            // 被引用
            bundleRequest.Retain();
            return bundleRequest;
        }

        // 获取 bundle 的读取路径
        // 三种路径：S目录下，P目录下，URL下
        //  
        private static string GetDataPath(string bundleName) {
            // P 目录是空的， 返回 basePath
            if (string.IsNullOrEmpty(updatePath))
                return basePath;
            // P 目录不是空的，且P目录存在这个 bundle
            if (File.Exists(updatePath + bundleName))
                return updatePath;


            // 还没进入 下载阶段时, currentVersions == updatePathVersions
            // 下载完 服务器的 versions 后, currentVersions == 服务器最新的 Versions
            if (currentVersions != null) {
                BundleRef serverBundleRef = currentVersions.GetBundle(bundleName);

                if (serverBundleRef != null) {
                    BundleRef localBundleRef = updatePathVersions.GetBundle(bundleName);

                    if (localBundleRef != null) {
                        // 服务器版本 和 S 目录版本不一样
                        if (!localBundleRef.EqualsWithContent(serverBundleRef)) {
                            // 内容不相同, 返回 要下载的 地址
                            // e.g. http://192.168.1.113/Bundles/
                            return GetDownloadURL(string.Empty);
                        }
                    }
                }
            }

            // 以上都不是, 返回 basePath
            return basePath;
        }


        private static string RemapVariantName(string bundle) {
            var bundlesWithVariant = ActiveVariantList;
            // Get base bundle path
            var baseName = bundle.Split('.')[0];

            var bestFit = int.MaxValue;
            var bestFitIndex = -1;
            // Loop all the assetBundles with variant to find the best fit variant assetBundle.
            for (var i = 0; i < bundlesWithVariant.Count; i++) {
                var curSplit = bundlesWithVariant[i].Split('.');
                var curBaseName = curSplit[0];
                var curVariant = curSplit[1];

                if (curBaseName != baseName)
                    continue;

                var found = bundlesWithVariant.IndexOf(curVariant);

                // If there is no active variant found. We still want to use the first
                if (found == -1)
                    found = int.MaxValue - 1;

                if (found >= bestFit)
                    continue;
                bestFit = found;
                bestFitIndex = i;
            }

            if (bestFit == int.MaxValue - 1)
                Debug.LogWarning(
                    "Ambiguous asset bundle variant chosen because there was no matching active variant: " +
                    bundlesWithVariant[bestFitIndex]);

            return bestFitIndex != -1 ? bundlesWithVariant[bestFitIndex] : bundle;
        }

        #endregion
    }
}
