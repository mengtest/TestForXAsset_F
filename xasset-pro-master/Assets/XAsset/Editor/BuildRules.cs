//
// BuildRules.cs
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
using UnityEditor;
using UnityEngine;

namespace libx {
    public enum GroupBy {
        None,
        Explicit,
        Filename,
        Directory,
    }

    [Serializable]
    public class AssetBuild {
        // 要打包的文件名
        // e.g. Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png
        public string assetName; 

        public string groupName;
        // ab包的名字
        public string bundleName = string.Empty;

        public int id;
        // 打包规则
        public GroupBy groupBy = GroupBy.Filename;
    }

    [Serializable]
    public class PatchBuild {
        public string name;
        public List<string> assets = new List<string>();
    }

    [Serializable]
    public class BundleBuild {
        public string assetBundleName;
        public List<string> assetNames = new List<string>();
        public AssetBundleBuild ToBuild() {
            return new AssetBundleBuild() {
                assetBundleName = assetBundleName,
                assetNames = assetNames.ToArray(),
            };
        }
    }

    public class BuildRules : ScriptableObject {
        private readonly List<string> _duplicated = new List<string>();

        // [asset名, HashSet<bundle名>]
        // asset 所属的 bundles, 主要是为 计算出_duplicated, 没有别的用处
        // 如果一个asset被多处引用且没有加入到 _asset2Bundles中， 那这个 asset 就会被加入到 _duplicated
        private readonly Dictionary<string, HashSet<string>> _tracker = new Dictionary<string, HashSet<string>>();

        // [asset名, bundle名]
        // e.g. [Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png, assets_xasset_extend_testimage]
        private readonly Dictionary<string, string> _asset2BundleDict = new Dictionary<string, string>();

        private readonly Dictionary<string, string> _unexplicits = new Dictionary<string, string>();

        [Header("版本号")]
        [Tooltip("构建的版本号")] public int build;
        public int major;
        public int minor;
        [Tooltip("场景文件夹")]
        public string[] scenesFolders = new string[] { "Assets/XAsset" };

        [Header("自动分包分组配置")]
        [Tooltip("是否自动记录资源的分包分组")]
        public bool autoRecord;
        [Tooltip("按目录自动分组")]
        public string[] autoGroupByDirectories = new string[0];

        [Header("编辑器提示选项")]
        [Tooltip("检查加载路径大小写是否存在")]
        public bool validateAssetPath;

        [Header("首包内容配置")]
        [Tooltip("是否整包")] public bool allAssetsToBuild;
        [Tooltip("首包包含的分包")] public string[] patchesInBuild = new string[0];
        [Tooltip("BuildPlayer的时候被打包的场景")] public SceneAsset[] scenesInBuild = new SceneAsset[0];

        [Header("AB打包配置")]
        [Tooltip("AB的扩展名")] public string extension = "";
        public bool nameByHash;
        [Tooltip("打包AB的选项")] public BuildAssetBundleOptions options = BuildAssetBundleOptions.ChunkBasedCompression;

        [Header("缓存数据")]

        [Tooltip("所有要打包的资源")]
        public List<AssetBuild> assetBuildList = new List<AssetBuild>();

        [Tooltip("所有分包")]
        public List<PatchBuild> patches = new List<PatchBuild>();

        [Tooltip("所有打包的资源")]
        public List<BundleBuild> bundles = new List<BundleBuild>();

        public string currentScene;

        public void BeginSample() {
        }

        public void EndSample() {

        }


        public void OnLoadAsset(string assetPath) {
            if (autoRecord && Assets.development) {
                GroupAsset(assetPath, GetGroup(assetPath));
            } else {
                if (validateAssetPath) {
                    if (assetPath.Contains("Assets")) {
                        if (File.Exists(assetPath)) {
                            if (!assetBuildList.Exists(asset => asset.assetName.Equals(assetPath))) {
                                EditorUtility.DisplayDialog("文件大消息不匹配", assetPath, "确定");
                            }
                        } else {
                            EditorUtility.DisplayDialog("资源不存在", assetPath, "确定");
                        }
                    }
                }
            }
        }

        private GroupBy GetGroup(string assetPath) {
            var groupBy = GroupBy.Filename;
            var dir = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            if (autoGroupByDirectories.Length > 0) {
                foreach (var groupWithDir in autoGroupByDirectories) {
                    if (groupWithDir.Contains(dir)) {
                        groupBy = GroupBy.Directory;
                        break;
                    }
                }
            }

            return groupBy;
        }

        public void OnUnloadAsset(string assetPath) {
        }

        #region API

        public AssetBuild GroupAsset(string path, GroupBy groupBy = GroupBy.Filename, string group = null) {
            var value = assetBuildList.Find(assetBuild => assetBuild.assetName.Equals(path));
            if (value == null) {
                value = new AssetBuild();
                value.assetName = path;
                assetBuildList.Add(value);
            }
            if (groupBy == GroupBy.Explicit) {
                value.groupName = group;
            }
            if (IsScene(path)) {
                currentScene = Path.GetFileNameWithoutExtension(path);
            }
            value.groupBy = groupBy;
            if (autoRecord && Assets.development) {
                PatchAsset(path);
            }
            return value;
        }

        public void PatchAsset(string path) {
            var patchName = currentScene;
            var value = patches.Find(patch => patch.name.Equals(patchName));
            if (value == null) {
                value = new PatchBuild();
                value.name = patchName;
                patches.Add(value);
            }
            if (File.Exists(path)) {
                if (!value.assets.Contains(path)) {
                    value.assets.Add(path);
                }
            }
        }

        public string AddVersion() {
            build = build + 1;
            return GetVersion();
        }

        public string GetVersion() {
            var ver = new Version(major, minor, build);
            return ver.ToString();
        }

        // 解析 Rules.asset
        public void Analyze() {
            Clear();
            // 收集 {BuildRule} 下的 asset
            CollectAssets();
            // 获取依赖, 分析出 _conflicted, _tracker, _duplicated
            AnalysisAssets();
            // 优化资源
            OptimizeAssets();
            // 保存 BuildRules
            Save();
        }

        public AssetBundleBuild[] GetBuilds() {
            return bundles.ConvertAll(delegate (BundleBuild input) { return input.ToBuild(); }).ToArray();
        }

        #endregion

        #region Private

        private string GetGroupName(AssetBuild assetBuild) {
            return GetGroupName(assetBuild.groupBy, assetBuild.assetName, assetBuild.groupName);
        }

        // 检查是否是符合规则的文件
        internal bool ValidateAsset(string assetName) {
            // 忽略 不在 Assets/ 下的文件
            if (!assetName.StartsWith("Assets/"))
                return false;

            // 获取后缀名
            string ext = Path.GetExtension(assetName).ToLower();
            // 不处理 .dll, .cs, .meta, .js, .boo
            return ext != ".dll" 
                && ext != ".cs" 
                && ext != ".meta" 
                && ext != ".js" 
                && ext != ".boo";
        }

        // 是否是场景
        private bool IsScene(string assetName) {
            return assetName.EndsWith(".unity");
        }

        // 获取文件的组名
        private string GetGroupName(GroupBy groupBy, string assetName, string groupName = null, bool isChildren = false, bool isShared = false) {
            // 特殊处理 shader 的 组名  固定为 shaders
            if (assetName.EndsWith(".shader")) {
                groupName = "shaders";
                groupBy = GroupBy.Explicit;
                isChildren = false;
            } else if (IsScene(assetName)) {
                groupBy = GroupBy.Filename;
            }

            switch (groupBy) {
                case GroupBy.Explicit:
                    break;
                case GroupBy.Filename: {
                        string assetNameNoExt = Path.GetFileNameWithoutExtension(assetName);
                        string directoryName = Path.GetDirectoryName(assetNameNoExt).Replace("\\", "/").Replace("/", "_");
                        groupName = directoryName + "_" + assetNameNoExt;
                    }
                    break;
                // 将 asset 以 文件夹的方式打包为 bundle
                case GroupBy.Directory: {
                        // assetName    e.g. Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png
                        // directoryName    e.g. Assets_XAsset_Extend_TestImage
                        string directoryName = Path.GetDirectoryName(assetName).Replace("\\", "/").Replace("/", "_");
                        groupName = directoryName;
                        break;
                    }
            }

            if (isChildren) {
                return "children_" + groupName;
            }

            if (isShared) {
                groupName = "shared_" + groupName;
            }
            return (nameByHash ? Utility.GetMD5Hash(groupName) : groupName.TrimEnd('_').ToLower()) + extension;
        }

        private void Track(string assetName, string bundleName) {
            HashSet<string> hashSet;

            if (!_tracker.TryGetValue(assetName, out hashSet)) {
                hashSet = new HashSet<string>();
                _tracker.Add(assetName, hashSet);
            }

            hashSet.Add(bundleName);

            string bundleNameTemp;
            _asset2BundleDict.TryGetValue(assetName, out bundleNameTemp);
            if (string.IsNullOrEmpty(bundleNameTemp)) {
                _unexplicits[assetName] = GetGroupName(GroupBy.Explicit, assetName, bundleName, true);
                if (hashSet.Count > 1) {
                    _duplicated.Add(assetName);
                }
            }
        }

        // 将 assetName 和 assetBundleName 的 对应关系 存储到 _asset2Bundles
        private void BundleAsset(string assetName, string assetBundleName) {
            // 如果 是场景文件, 要重新
            if (IsScene(assetName)) {
                assetBundleName = GetGroupName(GroupAsset(assetName));
            }

            // e.g. [Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png, assets_xasset_extend_testimage]
            _asset2BundleDict[assetName] = assetBundleName;
        }

        private void Clear() {
            _unexplicits.Clear();
            _tracker.Clear();
            _duplicated.Clear();
            _asset2BundleDict.Clear();
        }

        // 将 一对一的  [assetName, bundleName] 转化为 [bundleName, List<assetName>]
        private Dictionary<string, List<string>> GetBundles() {
            Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>();

            foreach (KeyValuePair<string, string> item in _asset2BundleDict) {
                string bundle = item.Value;
                List<string> list;

                if (!dictionary.TryGetValue(bundle, out list)) {
                    list = new List<string>();
                    dictionary[bundle] = list;
                }

                if (!list.Contains(item.Key))
                    list.Add(item.Key);
            }
            return dictionary;
        }

        private void Save() {
            bundles.Clear();
            var map = GetBundles();
            foreach (var item in map) {
                var bundle = new BundleBuild() {
                    assetBundleName = item.Key,
                    assetNames = item.Value,
                };
                bundles.Add(bundle);
            }

            foreach (var patch in patches) {
                for (var i = 0; i < patch.assets.Count; ++i) {
                    var asset = patch.assets[i];
                    if (!File.Exists(asset)) {
                        patch.assets.RemoveAt(i);
                        --i;
                    }
                }
            }
            EditorUtility.ClearProgressBar();
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        private void OptimizeAssets() {
            foreach (var item in _unexplicits) {
                if (_tracker[item.Key].Count < 2) {
                    _asset2BundleDict[item.Key] = item.Value;
                }
            }

            for (int i = 0, max = _duplicated.Count; i < max; i++) {
                var item = _duplicated[i];
                if (EditorUtility.DisplayCancelableProgressBar(string.Format("优化冗余{0}/{1}", i, max), item,
                    i / (float)max)) break;
                OptimizeAsset(item);
            }
        }

        // 分析
        private void AnalysisAssets() {
            // 获取 [bundleName, List<assetName>]
            Dictionary<string, List<string>> bundle2AssetDict = GetBundles();

            int i = 0;
            foreach (KeyValuePair<string, List<string>> item in bundle2AssetDict) {
                string bundleName = item.Key;

                var tips = string.Format("分析依赖{0}/{1}", i, bundle2AssetDict.Count);

                if (EditorUtility.DisplayCancelableProgressBar(tips, bundleName, i / (float)bundle2AssetDict.Count))
                    break;

                // [assetName, ...]
                string[] assetPaths = item.Value.ToArray();

                // 获取 [assetName, ...] 的依赖文件名（包括自身）
                string[] dependencies = AssetDatabase.GetDependencies(assetPaths, true);

                if (dependencies.Length > 0)
                    foreach (string assetName in dependencies) {
                        // 不Track .spriteatlas, .giparams, LightingData.asset
                        if (Directory.Exists(assetName) 
                            || assetName.EndsWith(".spriteatlas") 
                            || assetName.EndsWith(".giparams") 
                            || assetName.EndsWith("LightingData.asset")) {
                            continue;
                        }

                        // 验证文件
                        if (ValidateAsset(assetName)) {
                            // 添加到
                            Track(assetName, bundleName);
                        }
                    }
                i++;
            }
        }

        // 通过 BuildRule 获得需要打包的文件路径和该文件所属的assetbundle的名称 将其转化为 AssetBuild
        private void CollectAssets() {
            List<AssetBuild> assetBuildList = new List<AssetBuild>();

            // D:\\Projects\\UnityProjects\\TestForXAsset5.1\\xasset-pro-master
            int len = Environment.CurrentDirectory.Length + 1;

            // 读取 Rules.asset 里的 AssetBuildList
            for (int index = 0; index < this.assetBuildList.Count; index++) {
                AssetBuild assetBuild = this.assetBuildList[index];
                // 通过 文件名  读取文件信息
                FileInfo fileInfo = new FileInfo(assetBuild.assetName);

                // 存在文件且符合规则
                if (fileInfo.Exists && ValidateAsset(assetBuild.assetName)) {
                    // FullName e.g. D:\\Projects\\UnityProjects\\TestForXAsset5.1\\xasset-pro-master\\Assets\\XAsset\\Extend\\TestImage\\Btn_Tab1_n 1.png
                    // relativePath e.g. Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png
                    string relativePath = fileInfo.FullName.Substring(len).Replace("\\", "/");

                    if (!relativePath.Equals(assetBuild.assetName)) {
                        Debug.LogWarningFormat("检查到路径大小写不匹配！输入：{0}实际：{1}，已经自动修复。", assetBuild.assetName, relativePath);
                        assetBuild.assetName = relativePath;
                    }
                    // 添加到 局部的 AssetBuildList
                    assetBuildList.Add(assetBuild);
                }
            }

            // 处理局部的 AssetBuildList
            for (int i = 0; i < assetBuildList.Count; i++) {

                AssetBuild assetBuild = assetBuildList[i];
                // 跳过 GroupBy.None
                if (assetBuild.groupBy == GroupBy.None) {
                    continue;
                }

                // 获取 asset 的 GroupName, 设置为 AssetBuild.bundleName
                assetBuild.bundleName = GetGroupName(assetBuild);
                BundleAsset(assetBuild.assetName, assetBuild.bundleName);
            }

            // 局部 AssetBuildList 赋值给 AssetBuildList
            this.assetBuildList = assetBuildList;
        }

        private void OptimizeAsset(string asset) {
            _asset2BundleDict[asset] = GetGroupName(GroupBy.Directory, asset, null, false, true);
        }

        #endregion
    }
}