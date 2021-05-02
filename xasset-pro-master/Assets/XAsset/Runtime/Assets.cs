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

        public static string[] patches4Init { get; set; }
        // Assets.searchPaths
        // 搜索路径
        // e.g.
        // [Assets/XAsset/Demo/Scenes, Asset/XAsset/Demo/UI/Prefabs]
        public static string[] searchPathArray { get; set; }

        public static string[] GetAllAssetPaths() {
            var assets = new List<string>();
            assets.AddRange(AssetToBundles.Keys);
            return assets.ToArray();
        }

        // Assets.Initializer 初始化
        public static void Initialize(Action<string> completed = null) {
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

            // 加载完 Versions 的回调
            Action<Versions> onLoadVersions = new Action<Versions>(versions => {
                // 只有这里会赋值 currentVersion
                currentVersions = versions;
                ReloadVersions(currentVersions);

                Log("Initialize");
                LogFormat("Development:{0}", development);
                LogFormat("Platform:{0}", platform);
                LogFormat("UpdatePath:{0}", updatePath);
                LogFormat("DownloadURL:{0}", downloadURL);
                LogFormat("UpdateUnusedAssetsImmediate:{0}", updateUnusedAssetsImmediate);
                LogFormat("Version:{0}", currentVersions.ver);
                if (completed != null)
                    completed(null);
            });

            // 开发模式
            if (development) {
                if (versionsLoader != null)
                    onLoadVersions(versionsLoader());
            // 非开发模式
            } else {
                // 获取 P 目录下的 versions.bundle
                // e.g. C:/Users/void87/AppData/LocalLow/mmdnb/xasset-pro/Bundles/buildInVersions.bundle
                string updatePathVersionPath = string.Format("{0}buildInVersions.bundle", updatePath);

                // 获取基于 basePath 的 versions.bundle(暂时没有用到)
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
                        if (completed != null)
                            completed(unityWebRequest.error);
                    } else {
                        // 加载 P 目录下的 Versions
                        updatePathVersions = LoadVersions(updatePathVersionPath);

                        //// 本地版本暂时设置为 0.0.0
                        //PlayerPrefs.SetString(KVersions, "0.0.0");

                        // 覆盖安装
                        if (OverlayInstallation(updatePathVersions.ver)) {
                            // 加载完 Versions 的回调
                            onLoadVersions(updatePathVersions);

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
                            // 原代码 这里永远都 是 FALSE
                            string path = GetDownloadURL(VersionsFileName);

                            onLoadVersions(File.Exists(path) ? LoadVersions(path) : updatePathVersions);
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
            // return v1 > v2;
            return true;
        }

        // 重新加载 Versions, 赋值 ActiveVariants, AssetToBundles, BundleToChildren
        private static void ReloadVersions(Versions versions) {
            ActiveVariants.Clear();
            AssetToBundles.Clear();
            BundleToChildren.Clear();

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
                BundleToChildren[bundlRef.name] = Array.ConvertAll(bundlRef.childrenBundleIDArray, id => bundleRefList[id].name);
            }

            // 将 AssetRef 转换为 AssetToBundles
            foreach (AssetRef assetRef in assetRefList) {
                // e.g.
                //  Assets/XAsset/Demo/Scenes + / + Title.unity
                string path = string.Format("{0}/{1}", dirArray[assetRef.dirID], assetRef.name);

                if (assetRef.bundleID >= 0 && assetRef.bundleID < bundleRefList.Count)
                    // 
                    AssetToBundles[path] = bundleRefList[assetRef.bundleID].name;
                else
                    AssetToBundles[path] = string.Empty;
            }

            ActiveVariants.AddRange(activeVariants);
        }

        // 下载 Versions
        public static void DownloadVersions(Action<string> completed) {
            // e.g. http://192.168.1.113/Bundles/Windows/versions.bundle
            string versionsFileNameUrl = GetDownloadURL(VersionsFileName);
            LogFormat("DownloadVersions:{0}", versionsFileNameUrl);
            // e.g. C:/Users/void87/AppData/Local/Temp/mmdnb/xasset-pro/versions.bundle
            string tempVersionsFileName = Application.temporaryCachePath + "/" + VersionsFileName;

            // 将 服务器上的 Versions 下载到 Temp 目录
            UnityWebRequest unityWebRequest = Download(versionsFileNameUrl, tempVersionsFileName);
            unityWebRequest.SendWebRequest().completed += operation => {
                if (string.IsNullOrEmpty(unityWebRequest.error)) {
                    // 重新加载 currentVersions
                    // outside = true, 表示要从外面下载
                    currentVersions = LoadVersions(Application.temporaryCachePath + "/" + VersionsFileName, true);
                    ReloadVersions(currentVersions);

                    // 设置当前的版本
                    PlayerPrefs.SetString(KVersions, currentVersions.ver);

                    RemoveUnusedAssets();
                }

                if (completed != null)
                    completed(unityWebRequest.error);

                unityWebRequest.Dispose();
            };
        }

        // 加载 P 目录 || temPath 下的 versions.bundle
        // 
        public static Versions LoadVersions(string filename, bool outside = false) {
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

        public static bool DownloadAll(string[] patches, out Downloader downLoader) {
            if (updateAll) {
                return DownloadAll(out downLoader);
            }

            List<BundleRef> bundleList = new List<BundleRef>();
            foreach (var patch in patches) {

                var saved = PlayerPrefs.GetString(patch, string.Empty);

                if (!saved.Equals(currentVersions.ver)) {
                    var newFiles = GetNewFiles(patch);
                    foreach (var file in newFiles)
                        if (!bundleList.Exists(x => x.name.Equals(file.name)))
                            bundleList.Add(file);
                }
            }

            if (bundleList.Count > 0) {
                var downloader = new Downloader();
                foreach (var item in bundleList)
                    downloader.AddDownload(GetDownloadURL(item.name), updatePath + item.name, item.crc, item.len);
                Downloaders.Add(downloader);
                downLoader = downloader;
                downLoader.onFinished += () => {
                    foreach (var item in patches) {
                        PlayerPrefs.SetString(item, currentVersions.ver);
                    }
                };
                return true;
            }

            downLoader = null;
            return false;
        }

        // Assets.DownloadAll
        public static bool DownloadAll(out Downloader downloader) {
            List<BundleRef> bundleRefList = new List<BundleRef>();


            for (int i = 0; i < currentVersions.bundleRefList.Count; i++) {
                BundleRef bundleRef = currentVersions.bundleRefList[i];
                // 原代码
                if (IsNew(bundleRef)) {
                    bundleRefList.Add(bundleRef);
                }

                //// add by 黄鑫 2021年5月1日
                //bundleRefList.Add(bundle);
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
        // e.g. Title.unity
        public static SceneAssetRequest LoadSceneAsync(string sceneName, bool additive = false) {
            Assert.IsNotNull(sceneName, "path != null");
            string assetBundleName;
            
            // 获取完整的 场景名
            sceneName = GetSearchPath(sceneName, out assetBundleName);

            SceneAssetAsyncRequest sceneAssetRequest = new SceneAssetAsyncRequest(sceneName, additive) {
                assetBundleName = assetBundleName 
            };

            LogFormat("LoadSceneAsync:{0}", sceneName);

            sceneAssetRequest.Load();
            sceneAssetRequest.Retain();
            sceneAssetRequest.name = sceneName;

            LoadingSceneAssetRequestSceneList.Add(sceneAssetRequest);

            if (!additive) {
                if (_runningScene != null) {
                    _runningScene.Release();
                    _runningScene = null;
                }

                _runningScene = sceneAssetRequest;
            } else {
                if (_runningScene != null) {
                    _runningScene.additives.Add(sceneAssetRequest);
                }
            }

            return sceneAssetRequest;
        }

        public static void UnloadScene(SceneAssetRequest scene) {
            scene.Release();
        }

        public static AssetRequest LoadAssetAsync(string path, Type type) {
            return LoadAsset(path, type, true);
        }

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
        /// </summary>
        public static Versions updatePathVersions { get; private set; }

        /// <summary>
        /// 服务器的版本
        /// </summary>
        public static Versions currentVersions { get; private set; }

        // Bundle 需要更新吗
        private static bool IsNew(BundleRef bundle) {
            if (updatePathVersions != null)
                if (updatePathVersions.Contains(bundle))
                    return false;

            string path = string.Format("{0}{1}", updatePath, bundle.name);
            FileInfo fileInfo = new FileInfo(path);
            // 不存在这个文件, 直接 true
            if (!fileInfo.Exists)
                return true;

            // 读取 PlayerPrefs 暂时注释 Edit by 黄鑫 2021年5月2日
            // 直接读取 PlayerPrefs 中保存的内容，该值在 Download.Copy 方法中写入
            var comparison = StringComparison.OrdinalIgnoreCase;
            var ver = PlayerPrefs.GetString(path);
            if (ver.Equals(bundle.crc, comparison)) {
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

        private static List<BundleRef> GetNewFiles(string patch) {
            var list = new List<BundleRef>();
            var files = currentVersions.GetFiles(patch);
            foreach (var file in files)
                if (IsNew(file))
                    list.Add(file);

            return list;
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

        private static SceneAssetRequest _runningScene;

        private static readonly Dictionary<string, AssetRequest> AssetRequests = new Dictionary<string, AssetRequest>();

        // 正在加载的 AssetRequest
        private static readonly List<AssetRequest> LoadingAssetRequestList = new List<AssetRequest>();
        // 已经加载的 AssetRequest
        private static readonly List<AssetRequest> LoadedAssetRequestList = new List<AssetRequest>();
        // 不用的 AssetRequest
        private static readonly List<AssetRequest> UnusedAssetRequestList = new List<AssetRequest>();

        // 正在使用的 SceneAssetRequest
        private static readonly List<SceneAssetRequest> SceneAssetRequestList = new List<SceneAssetRequest>();
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
                        LogFormat("RemoveDownloader:{0}", i);
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
                    AssetRequests.Remove(request.url);
                    request.Unload();
                    LogFormat("UnloadAsset:{0}", request.url);
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
                    SceneAssetRequestList.Add(sceneAssetRequest);
                    OnAssetLoaded(sceneAssetRequest.url);
                }

                --i;
            }

            // 处理正在使用的 SceneAssetRequest
            for (int i = 0; i < SceneAssetRequestList.Count; ++i) {
                SceneAssetRequest sceneAssetRequest = SceneAssetRequestList[i];
                // 跳过 被引用 大于 0
                if (!sceneAssetRequest.IsUnused())
                    continue;
                
                SceneAssetRequestList.RemoveAt(i);
                LogFormat("UnloadScene:{0}", sceneAssetRequest.url);

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
            if (ToloadBundles.Count > 0 && max > 0 && LoadingBundles.Count < max) {
                for (var i = 0; i < Math.Min(max - LoadingBundles.Count, ToloadBundles.Count); ++i) {
                    var item = ToloadBundles[i];
                    if (item.loadState == LoadState.Init) {
                        item.Load();
                        LoadingBundles.Add(item);
                        ToloadBundles.RemoveAt(i);
                        --i;
                        LogFormat("Remove {0} from to load bundles by init state.", item.url);
                    } else if (item.loadState == LoadState.Loaded) {
                        ToloadBundles.RemoveAt(i);
                        --i;
                        LogFormat("Remove {0} from to load bundles by loaded state.", item.url);
                    }
                }
            }

            for (var i = 0; i < LoadingBundles.Count; ++i) {
                var item = LoadingBundles[i];
                if (item.Update())
                    continue;
                LoadingBundles.RemoveAt(i);
                --i;
            }

            foreach (var item in BundleRequests) {
                if (item.Value.isDone && item.Value.IsUnused()) {
                    UnusedBundles.Add(item.Value);
                }
            }

            if (UnusedBundles.Count > 0) {
                for (var i = 0; i < UnusedBundles.Count; ++i) {
                    var item = UnusedBundles[i];
                    item.Unload();
                    LogFormat("UnloadBundle:{0}", item.url);
                    BundleRequests.Remove(item.name);
                }

                UnusedBundles.Clear();
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
            if (_runningScene != null) {
                sb.AppendLine("Scene:" + _runningScene.name);
                sb.AppendLine("Additive:" + _runningScene.additives.Count);
                foreach (var additive in _runningScene.additives) {
                    if (additive.IsUnused()) {
                        continue;
                    }

                    sb.AppendLine("\t" + additive.name);
                }
            }

            sb.AppendLine("Asset:" + AssetRequests.Count);
            foreach (var request in AssetRequests) {
                sb.AppendLine("\t" + request.Key);
            }

            sb.AppendLine("Bundle:" + BundleRequests.Count);
            foreach (var request in BundleRequests) {
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
            AssetRequests.Add(request.url, request);
            LoadingAssetRequestList.Add(request);
            if (load)
                request.Load();
        }

        private static AssetRequest LoadAsset(string path, Type type, bool async) {
            Assert.IsNotNull(path, "path != null");

            var isWebURL = path.StartsWith("http://", StringComparison.Ordinal) ||
                           path.StartsWith("https://", StringComparison.Ordinal) ||
                           path.StartsWith("file://", StringComparison.Ordinal) ||
                           path.StartsWith("ftp://", StringComparison.Ordinal) ||
                           path.StartsWith("jar:file://", StringComparison.Ordinal);

            string assetBundleName = null;
            if (!isWebURL) {
                path = GetSearchPath(path, out assetBundleName);
            }

            AssetRequest request;
            if (AssetRequests.TryGetValue(path, out request)) {
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

            LogFormat("LoadAsset:{0}", path);

            request.name = path;
            request.url = path;
            request.assetType = type;
            AddRequest(request);
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
            if (AssetToBundles.TryGetValue(assetName, out assetBundleName))
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
                    if (AssetToBundles.TryGetValue(existPath, out assetBundleName))
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

        // [bundle名, BundleRequest]
        private static readonly Dictionary<string, BundleRequest> BundleRequests =
            new Dictionary<string, BundleRequest>();

        private static readonly List<BundleRequest> LoadingBundles = new List<BundleRequest>();

        private static readonly List<BundleRequest> UnusedBundles = new List<BundleRequest>();

        private static readonly List<BundleRequest> ToloadBundles = new List<BundleRequest>();

        private static readonly List<string> ActiveVariants = new List<string>();

        // [asset名，bundle 文件名]
        // e.g.
        // [Assets/XAsset/Demo/Scenes/Title.unity, _title]
        private static readonly Dictionary<string, string> AssetToBundles = new Dictionary<string, string>();

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
        private static readonly Dictionary<string, string[]> BundleToChildren = new Dictionary<string, string[]>();

        // Assets.GetChildren
        // 获取 bundle 依赖的 bundle名
        internal static string[] GetChildren(string bundle) {
            string[] deps;
            if (BundleToChildren.TryGetValue(bundle, out deps))
                return deps;

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
        internal static BundleRequest LoadBundle(string assetBundleName, bool asyncMode) {
            if (string.IsNullOrEmpty(assetBundleName)) {
                Debug.LogError("bundle == null");
                return null;
            }

            assetBundleName = RemapVariantName(assetBundleName);
            var url = GetDataPath(assetBundleName) + assetBundleName;

            BundleRequest bundleRequest;

            if (BundleRequests.TryGetValue(assetBundleName, out bundleRequest)) {
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

            bundleRequest.url = url;
            bundleRequest.name = assetBundleName;

            BundleRequests.Add(assetBundleName, bundleRequest);

            if (MAX_BUNDLES_PERFRAME > 0 && (bundleRequest is BundleAsyncRequest || bundleRequest is WebBundleRequest)) {
                ToloadBundles.Add(bundleRequest);
            } else {
                bundleRequest.Load();
                LoadingBundles.Add(bundleRequest);
                LogFormat("LoadBundle: {0}", url);
            }

            bundleRequest.Retain();
            return bundleRequest;
        }

        // 获取 bundle 的读取路径
        private static string GetDataPath(string bundleName) {
            // P 目录是空的， 返回 basePath
            if (string.IsNullOrEmpty(updatePath))
                return basePath;
            // P 目录不是空的，且P目录存在这个 bundle
            if (File.Exists(updatePath + bundleName))
                return updatePath;


            
            if (currentVersions != null) {
                var server = currentVersions.GetBundle(bundleName);
                if (server != null) {
                    var local = updatePathVersions.GetBundle(bundleName);
                    if (local != null) {
                        // 服务器版本 和 P 目录版本不一样
                        if (!local.EqualsWithContent(server)) {
                            return GetDownloadURL(string.Empty);
                        }
                    }
                }
            }

            // 以上都不是, 返回 basePath
            return basePath;
        }


        private static string RemapVariantName(string bundle) {
            var bundlesWithVariant = ActiveVariants;
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
