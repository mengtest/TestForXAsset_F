//
// Requests.cs
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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace libx {
    public enum LoadState {
        Init,   // 初始状态
        Loading,    // 加载中
        Loaded, // 加载完成
        Unload, // 卸载
    }

    // AssetRequest
    public class AssetRequest : Reference, IEnumerator {
        // 包含的 asset 的类型
        public Type assetType;
        // 请求的地址
        public string url;

        // 加载状态
        private LoadState _loadState = LoadState.Init;
        public LoadState loadState {
            get {
                return _loadState;
            }
            protected set {
                _loadState = value;
                if (value == LoadState.Loaded) {
                    Complete();
                }
            }
        }

        public AssetRequest() {
            asset = null;
            loadState = LoadState.Init;
        }

        // 判断 loadState 的状态 来确定 是否完成
        public bool isDone {
            get {
                return loadState == LoadState.Unload || loadState == LoadState.Loaded;
            }
        }

        public virtual float progress {
            get { return 1; }
        }

        public string error { get; protected set; }

        public string text { get; protected set; }

        public byte[] bytes { get; protected set; }

        public Object asset { get; internal set; }

        internal virtual void Load() {
            if (!File.Exists(url)) {
                error = "error! file not exist:" + url;
                loadState = LoadState.Loaded;
                return;
            }

            if (Assets.development && Assets.assetLoader != null)
                asset = Assets.assetLoader(url, assetType);
            if (asset == null) {
                error = "error! file not exist:" + url;
            }
            loadState = LoadState.Loaded;
        }

        internal virtual void Unload() {
            if (asset == null)
                return;

            if (!Assets.development) {
                if (!(asset is GameObject))
                    Resources.UnloadAsset(asset);
            }

            asset = null;
            loadState = LoadState.Unload;
        }

        // AssetRequest.Update()
        // 是否正在更新中
        internal virtual bool Update() {
            if (!isDone)
                return true;
            // 完成了就
            Complete();
            return false;
        }

        // 加载完成
        private void Complete() {
            if (completed != null) {
                try {
                    completed.Invoke(this);
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }

                completed = null;
            }
        }

        public Action<AssetRequest> completed;

        #region IEnumerator implementation

        public bool MoveNext() {
            return !isDone;
        }

        public void Reset() {
        }

        public object Current {
            get { return null; }
        }

        #endregion


        // AssetRequest.LoadImmediate()
        public virtual void LoadImmediate() {

        }
    }

    public class BundleAssetRequest : AssetRequest {
        protected readonly string assetBundleName;
        protected BundleRequest bundle;
        protected List<BundleRequest> children = new List<BundleRequest>();


        public BundleAssetRequest(string bundle) {
            assetBundleName = bundle;
        }

        // BundleAssetRequest.Load()
        internal override void Load() {
            bundle = Assets.LoadBundle(assetBundleName);
            var bundles = Assets.GetChildrenBundleNameArray(assetBundleName);
            foreach (var item in bundles) {
                children.Add(Assets.LoadBundle(item));
            }
            asset = bundle.assetBundle.LoadAsset(url, assetType);
            loadState = LoadState.Loaded;
        }

        internal override void Unload() {
            if (bundle != null) {
                bundle.Release();
                bundle = null;
            }

            foreach (var item in children) {
                item.Release();
            }

            children.Clear();
            asset = null;
            loadState = LoadState.Unload;
        }
    }

    public class BundleAssetAsyncRequest : BundleAssetRequest {
        private AssetBundleRequest _request;

        public BundleAssetAsyncRequest(string bundle)
            : base(bundle) {
        }

        public override float progress {
            get {
                if (isDone) {
                    return 1;
                }

                if (loadState == LoadState.Init) {
                    return 0;
                }

                if (_request != null) {
                    return _request.progress * 0.7f + 0.3f;
                }

                if (bundle == null) {
                    return 1;
                }

                var value = bundle.progress;
                var max = children.Count;
                if (max <= 0)
                    return value * 0.3f;

                for (var i = 0; i < max; i++) {
                    var item = children[i];
                    value += item.progress;
                }

                return (value / (max + 1)) * 0.3f;
            }
        }

        private bool OnError(AssetRequest request) {
            error = request.error;
            if (!string.IsNullOrEmpty(error)) {
                loadState = LoadState.Loaded;
                return true;
            }
            return false;
        }

        internal override bool Update() {
            if (!base.Update()) {
                return false;
            }

            if (loadState == LoadState.Init) {
                return true;
            }

            if (_request == null) {
                if (!bundle.isDone) {
                    return true;
                }
                if (OnError(bundle)) {
                    return false;
                }

                for (int i = 0; i < children.Count; i++) {
                    var item = children[i];
                    if (!item.isDone) {
                        return true;
                    }
                    if (OnError(item)) {
                        return false;
                    }
                }

                _request = bundle.assetBundle.LoadAssetAsync(url, assetType);
                if (_request == null) {
                    error = "request == null";
                    loadState = LoadState.Loaded;
                    return false;
                }

                return true;
            }

            if (_request.isDone) {
                asset = _request.asset;
                loadState = LoadState.Loaded;
                if (asset == null) {
                    error = "asset == null";
                }
                return false;
            }
            return true;
        }

        internal override void Load() {
            bundle = Assets.LoadBundleAsync(assetBundleName);
            var bundles = Assets.GetChildrenBundleNameArray(assetBundleName);
            foreach (var item in bundles) {
                children.Add(Assets.LoadBundleAsync(item));
            }
            loadState = LoadState.Loading;
        }

        internal override void Unload() {
            _request = null;
            loadState = LoadState.Unload;
            base.Unload();
        }

        public override void LoadImmediate() {
            bundle.LoadImmediate();
            foreach (var item in children) {
                item.LoadImmediate();
            }
            if (bundle.assetBundle != null) {
                var assetName = Path.GetFileName(url);
                asset = bundle.assetBundle.LoadAsset(assetName, assetType);
            }
            loadState = LoadState.Loaded;
        }
    }

    // 场景请求（同步）
    public class SceneAssetRequest : AssetRequest {
        // 场景加载模式
        public readonly LoadSceneMode loadSceneMode;
        protected readonly string sceneName;

        // e.g. _title
        public string assetBundleName { get; set; }

        public List<SceneAssetRequest> additives { get; set; }

        // 包含的 BundleRequest
        protected BundleRequest bundleRequest;
        // 包含的 BundleRequest 的 依赖 BundleRequest
        protected List<BundleRequest> childrenBundleRequest = new List<BundleRequest>();

        public SceneAssetRequest(string path, bool addictive) {
            url = path;
            additives = new List<SceneAssetRequest>();
            sceneName = Path.GetFileNameWithoutExtension(url);
            loadSceneMode = addictive ? LoadSceneMode.Additive : LoadSceneMode.Single;
        }

        public override float progress {
            get { return 1; }
        }

        // SceneAssetRequest.Load()
        // 场景请求（同步）
        internal override void Load() {
            if (!string.IsNullOrEmpty(assetBundleName)) {
                bundleRequest = Assets.LoadBundle(assetBundleName);
                if (bundleRequest != null) {
                    var bundles = Assets.GetChildrenBundleNameArray(assetBundleName);
                    foreach (var item in bundles) {
                        childrenBundleRequest.Add(Assets.LoadBundle(item));
                    }
                    SceneManager.LoadScene(sceneName, loadSceneMode);
                }
            } else {
                try {
                    SceneManager.LoadScene(sceneName, loadSceneMode);
                    loadState = LoadState.Loading;
                } catch (Exception e) {
                    Debug.LogException(e);
                    error = e.ToString();

                }
            }
            loadState = LoadState.Loaded;
        }

        // SceneAssetRequest.Unload()
        // 场景请求卸载
        internal override void Unload() {
            if (bundleRequest != null)
                // 包含的 BundleRequest 被引用减1
                bundleRequest.Release();

            if (childrenBundleRequest.Count > 0) {
                // 所有的 包含的 BundleRequest 的 依赖 BundleRequest 被引用减1
                foreach (var item in childrenBundleRequest) {
                    item.Release();
                }

                childrenBundleRequest.Clear();
            }

            if (additives.Count > 0) {
                for (var i = 0; i < additives.Count; i++) {
                    var additive = additives[i];
                    if (!additive.IsUnused()) {
                        additive.Release();
                    }
                }
                additives.Clear();
            }

            if (loadSceneMode == LoadSceneMode.Additive) {
                if (SceneManager.GetSceneByName(sceneName).isLoaded)
                    SceneManager.UnloadSceneAsync(sceneName);
            }

            bundleRequest = null;

            // 
            loadState = LoadState.Unload;
        }
    }

    // 场景请求（异步）
    public class SceneAssetAsyncRequest : SceneAssetRequest {
        // 包含的 SceneManager.LoadSceneAsync 产生的 AsyncOperation
        private AsyncOperation _asyncOperationOfLoadSceneAsync;

        public SceneAssetAsyncRequest(string path, bool addictive)
            : base(path, addictive) {
        }

        public override float progress {
            get {
                if (isDone) {
                    return 1;
                }

                if (loadState == LoadState.Init) {
                    return 0;
                }

                if (_asyncOperationOfLoadSceneAsync != null) {
                    return _asyncOperationOfLoadSceneAsync.progress * 0.7f + 0.3f;
                }

                if (bundleRequest == null) {
                    return 1;
                }

                var value = bundleRequest.progress;
                var max = childrenBundleRequest.Count;
                if (max <= 0)
                    return value * 0.3f;

                for (int i = 0; i < max; i++) {
                    var item = childrenBundleRequest[i];
                    value += item.progress;
                }

                return (value / (max + 1)) * 0.3f;
            }
        }

        private bool OnError(AssetRequest request) {
            error = request.error;
            if (!string.IsNullOrEmpty(error)) {
                loadState = LoadState.Loaded;
                return true;
            }
            return false;
        }

        // SceneAssetAsyncRequest.Update()
        internal override bool Update() {
            if (!base.Update()) {
                return false;
            }

            if (loadState == LoadState.Init) {
                return true;
            }

            // SceneManager.LoadSceneAsync 时会产生这个,
            // null 说明还没有开始 官方API加载场景
            if (_asyncOperationOfLoadSceneAsync == null) {
                if (bundleRequest == null) {
                    error = "request == null";

                    // SceneAssetAsyncRequest.loadState = LoadState.Loaded
                    loadState = LoadState.Loaded;
                    return false;
                }

                // 等待 场景所在的 BundleRequest 加载完成
                if (!bundleRequest.isDone) {
                    return true;
                }

                if (OnError(bundleRequest)) {
                    return false;
                }

                // 等待 依赖 bundle 加载完成
                for (int i = 0; i < childrenBundleRequest.Count; i++) {
                    BundleRequest bundleRequest = childrenBundleRequest[i];

                    if (!bundleRequest.isDone) {
                        return true;
                    }

                    if (OnError(bundleRequest)) {
                        return false;
                    }
                }

                // 以上都加载完后， 最后加载 场景
                LoadSceneAsync();

                return true;
            }


            // isDone 说明 SceneManager.LoadSceneAsync 加载完成
            if (_asyncOperationOfLoadSceneAsync.isDone) {

                // SceneAssetAsyncRequest.loadState = LoadState.Loaded
                loadState = LoadState.Loaded;
                return false;
            }


            return true;
        }

        // SceneAssetAsyncRequest.LoadSceneAsync()
        // 不使用 ab 包 加载场景
        private void LoadSceneAsync() {
            try {
                _asyncOperationOfLoadSceneAsync = SceneManager.LoadSceneAsync(sceneName, loadSceneMode);

                // SceneAssetAsyncRequest.loadState.loadState = LoadState.Loading
                loadState = LoadState.Loading;

            // 出错
            } catch (Exception e) {
                Debug.LogException(e);
                error = e.ToString();

                // SceneAssetAsyncRequest.loadState.loadState = LoadState.Loading
                loadState = LoadState.Loaded;
            }
        }

        // SceneAssetAsyncRequest.Load()
        internal override void Load() {
            // 有 ab 包名
            if (!string.IsNullOrEmpty(assetBundleName)) {
                // 加载场景所在的 Bundle
                bundleRequest = Assets.LoadBundleAsync(assetBundleName);
                // 获取 场景所在的 bundle 依赖的 bundle 名
                string[] childBundleNameArray = Assets.GetChildrenBundleNameArray(assetBundleName);

                // 加载 每个 子 bundle
                foreach (string bundleName in childBundleNameArray) {
                    // 加载 每个 子bundle
                    childrenBundleRequest.Add(
                        Assets.LoadBundleAsync(bundleName)
                    );
                }

                // SceneAssetAsyncRequest.loadState = LoadState.Loading
                loadState = LoadState.Loading;

            // 没有 ab 包 直接 加载场景
            } else {
                LoadSceneAsync();
            }
        }

        internal override void Unload() {
            base.Unload();
            _asyncOperationOfLoadSceneAsync = null;
        }
    }

    public class WebAssetRequest : AssetRequest {
        private UnityWebRequest _www;

        public override float progress {
            get {
                if (isDone) {
                    return 1;
                }
                if (loadState == LoadState.Init) {
                    return 0;
                }

                if (_www == null) {
                    return 1;
                }

                return _www.downloadProgress;
            }
        }


        internal override bool Update() {
            if (!base.Update()) {
                return false;
            }

            if (loadState == LoadState.Loading) {
                if (_www == null) {
                    error = "www == null";
                    return false;
                }

                if (!string.IsNullOrEmpty(_www.error)) {
                    error = _www.error;
                    loadState = LoadState.Loaded;
                    return false;
                }

                if (_www.isDone) {
                    GetAsset();
                    loadState = LoadState.Loaded;
                    return false;
                }

                return true;
            }

            return true;
        }

        private void GetAsset() {
            if (assetType == typeof(Texture2D)) {
                asset = DownloadHandlerTexture.GetContent(_www);
            } else if (assetType == typeof(AudioClip)) {
                asset = DownloadHandlerAudioClip.GetContent(_www);
            } else if (assetType == typeof(TextAsset)) {
                text = _www.downloadHandler.text;
            } else {
                bytes = _www.downloadHandler.data;
            }
        }

        internal override void Load() {
            if (assetType == typeof(AudioClip)) {
                _www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
            } else if (assetType == typeof(Texture2D)) {
                _www = UnityWebRequestTexture.GetTexture(url);
            } else {
                _www = UnityWebRequest.Get(url);
                _www.downloadHandler = new DownloadHandlerBuffer();
            }
            _www.SendWebRequest();
            loadState = LoadState.Loading;
        }

        internal override void Unload() {
            if (asset != null) {
                Object.Destroy(asset);
                asset = null;
            }

            if (_www != null)
                _www.Dispose();

            bytes = null;
            text = null;
            loadState = LoadState.Unload;
        }
    }

    // BundleRequest 请求（同步）
    public class BundleRequest : AssetRequest {
        // 包含的 AssetBundle
        public AssetBundle assetBundle {
            get { return asset as AssetBundle; }
            internal set { asset = value; }
        }

        // BundleRequest.Load()
        internal override void Load() {
            // 官方API
            asset = AssetBundle.LoadFromFile(url);
            if (assetBundle == null)
                error = url + " LoadFromFile failed.";
            // 同步加载， 这里就加载成功了
            loadState = LoadState.Loaded;
        }

        // BundleRequest.Unlad()
        internal override void Unload() {
            if (assetBundle == null)
                return;
            // AssetBundle 卸载
            assetBundle.Unload(true);
            assetBundle = null;

            loadState = LoadState.Unload;
        }
    }

    // Bundle请求（异步）
    public class BundleAsyncRequest : BundleRequest {
        // 包含的 AssetBundelCreateRequest
        private AssetBundleCreateRequest _assetBundleCreateRequest;

        public override float progress {
            get {
                if (isDone) {
                    return 1;
                }
                if (loadState == LoadState.Init) {
                    return 0;
                }

                if (_assetBundleCreateRequest == null) {
                    return 1;
                }
                return _assetBundleCreateRequest.progress;
            }
        }

        // BundleAsyncRequest.Update()
        internal override bool Update() {
            if (!base.Update()) {
                return false;
            }

            // 正在加载
            if (loadState == LoadState.Loading) {
                // AssetBundleCreateRequest.isDone
                if (_assetBundleCreateRequest.isDone) {
                    // 获取 AssetBundle
                    assetBundle = _assetBundleCreateRequest.assetBundle;

                    if (assetBundle == null) {
                        error = string.Format("unable to load assetBundle:{0}", url);
                    }

                    // AssetBundle 加载完成
                    loadState = LoadState.Loaded;
                    return false;
                }
            }
            return true;
        }

        // BundleAsyncRequest.Load()
        internal override void Load() {
            if (_assetBundleCreateRequest == null) {
                // 官方API
                _assetBundleCreateRequest = AssetBundle.LoadFromFileAsync(url);
                if (_assetBundleCreateRequest == null) {
                    error = url + " LoadFromFile failed.";
                    return;
                }

                loadState = LoadState.Loading;
            }
        }

        // BundleAsyncRequest.UnLoad()
        internal override void Unload() {
            _assetBundleCreateRequest = null;
            loadState = LoadState.Unload;
            base.Unload();
        }

        public override void LoadImmediate() {
            Load();
            assetBundle = _assetBundleCreateRequest.assetBundle;
            if (assetBundle != null) {
                Debug.LogWarning("LoadImmediate:" + assetBundle.name);
            }
            loadState = LoadState.Loaded;
        }
    }

    public class WebBundleRequest : BundleRequest {
        private UnityWebRequest _request;

        public override float progress {
            get {
                if (isDone) {
                    return 1;
                }
                if (loadState == LoadState.Init) {
                    return 0;
                }

                if (_request == null) {
                    return 1;
                }

                return _request.downloadProgress;
            }
        }

        internal override bool Update() {
            if (!base.Update()) {
                return false;
            }

            if (loadState == LoadState.Loading) {
                if (_request == null) {
                    error = "request = null";
                    loadState = LoadState.Loaded;
                    return false;
                }
                if (_request.isDone) {
                    assetBundle = DownloadHandlerAssetBundle.GetContent(_request);
                    if (assetBundle == null) {
                        error = "assetBundle = null";
                    }
                    loadState = LoadState.Loaded;
                    return false;
                }
            }
            return true;
        }

        internal override void Load() {
#if UNITY_2018_1_OR_NEWER
            _request = UnityWebRequestAssetBundle.GetAssetBundle(url);
#else
            _request = UnityWebRequest.GetAssetBundle(url);
#endif
            _request.SendWebRequest();
            loadState = LoadState.Loading;
        }

        internal override void Unload() {
            if (_request != null) {
                _request.Dispose();
                _request = null;
            }
            loadState = LoadState.Unload;
            base.Unload();
        }
    }
}