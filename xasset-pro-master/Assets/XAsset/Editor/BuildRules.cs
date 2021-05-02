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
        Explicit,   // 自定义 组名
        Filename,   // 文件名
        Directory,  // 文件夹
    }

    // 要打包的 Asset 信息
    [Serializable]
    public class AssetBuild {
        // 要打包的文件名
        // e.g. Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png
        public string assetName; 
        // asset 的组名
        public string groupName;
        // ab包的名字
        public string bundleName = string.Empty;
        // 没有用到
        public int id;
        // 打包规则
        public GroupBy groupBy = GroupBy.Filename;
    }

    // 要打包的 bundle 信息
    [Serializable]
    public class BundleBuild {
        public string assetBundleName;  // bundle 名
        public List<string> assetNames = new List<string>();    // 包含的 asset 名

        // 转换为 AssetBundleBuild
        public AssetBundleBuild ConvertToAssetBundleBuild() {
            return new AssetBundleBuild() {
                assetBundleName = assetBundleName,
                assetNames = assetNames.ToArray(),
            };
        }
    }

    // 分包
    [Serializable]
    public class PatchBuild {
        public string name; // 分包名字
        public List<string> assetList = new List<string>();    // 分包包含的 asset
    }

    public class BuildRules : ScriptableObject {
        // [asset名, ...]
        // 被多个 bundle 引用的 未设置过 bundle 的 asset
        private readonly List<string> _duplicatedList = new List<string>();

        // [asset名, HashSet<bundle名(可能有多个)>]
        // 主要用来计算 _unexplicitDict, _duplicatedList
        // asset 和 所属的 bundle(可能有多个)
        // 如果一个asset没有设置bundle且被多个bundle引用，那这个 asset 就会被加入到 _duplicated
        private readonly Dictionary<string, HashSet<string>> _trackerDict = new Dictionary<string, HashSet<string>>();

        // [asset名, bundle名]
        // e.g. 普通的 asset->bundle
        // [Assets/XAsset/Extend/TestImage/Btn_Tab1_n 1.png, assets_xasset_extend_testimage]
        // e.g. 单个 bundle 依赖的 的没有  bundle 的 asset
        // [Assets/XAsset/Extend/TestCommon/Btn_User_h 2.png, children_assets_xasset_extend_testprefab3]
        // e.g. 多个 bundle 依赖的 没有设置 bundle 的 asset
        // [Assets/XAsset/Extend/TestCommon/Btn_User_h 1.png, shared_assets_xasset_extend_testcommon]
        private readonly Dictionary<string, string> _asset2BundleDict = new Dictionary<string, string>();

        // [asset名, bundle名]
        // 通过分析依赖查找出来的没有显式设置 bundle 名的资源
        // bundle 名 为最后分析的那一个
        // // e.g. [Assets/XAsset/Extend/TestCommon/Btn_User_h 1.png, children_assets_extend_testprefab"]
        private readonly Dictionary<string, string> _unexplicitDict = new Dictionary<string, string>();

        [Header("版本号")]
        // 
        [Tooltip("构建的版本号")] public int build;
        public int major;
        public int minor;

        // 主要用来在 编辑器下 查找 场景文件
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
        public List<PatchBuild> patchBuildList = new List<PatchBuild>();

        [Tooltip("所有打包的资源")]
        public List<BundleBuild> bundleBuildList = new List<BundleBuild>();

        // 当前场景名字
        public string currentScene;

        public void BeginSample() {
        }

        public void EndSample() {

        }


        // BuildRules.OnLoadAsset
        public void OnLoadAsset(string assetPath) {
            // 开启了自动记录 并且是 开发模式
            if (autoRecord && Assets.development) {
                GroupAsset(assetPath, GetGroupBy(assetPath));
            } else {
                // 校验文件路径
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

        // 获取 asset 的 分组类型
        private GroupBy GetGroupBy(string assetPath) {
            // 默认 GroupBy.Filename
            GroupBy groupBy = GroupBy.Filename;
            // 获取文件夹路径
            string dir = Path.GetDirectoryName(assetPath).Replace("\\", "/");

            // 如果asset 在 自动分组 文件夹里, 就 GroupBy.Directory
            if (autoGroupByDirectories.Length > 0) {
                foreach (string groupWithDir in autoGroupByDirectories) {
                    if (groupWithDir.Contains(dir)) {
                        groupBy = GroupBy.Directory;
                        break;
                    }
                }
            }

            return groupBy;
        }

        // BuildRules.OnUnloadAsset
        // 卸载资源，暂无实现
        public void OnUnloadAsset(string assetPath) {

        }

        #region API

        // 对 Asset 进行分组
        public AssetBuild GroupAsset(string path, GroupBy groupBy = GroupBy.Filename, string group = null) {
            // 在 assetBuildList 里查找 有没有记录
            AssetBuild tempAssetBuild = this.assetBuildList.Find(assetBuild => assetBuild.assetName.Equals(path));

            if (tempAssetBuild == null) {
                tempAssetBuild = new AssetBuild();
                // 
                tempAssetBuild.assetName = path;
                this.assetBuildList.Add(tempAssetBuild);
            }

            // 自定义组名
            if (groupBy == GroupBy.Explicit) {
                tempAssetBuild.groupName = group;
            }


            // e.g. Assets/XAsset/Demo/Scenes/Title.unity
            if (IsScene(path)) {
                // e.g. Title
                currentScene = Path.GetFileNameWithoutExtension(path);
            }

            // 分组类型
            tempAssetBuild.groupBy = groupBy;

            // 开启了自动记录 并且是 开发者模式
            if (autoRecord && Assets.development) {
                // 分包
                PatchAsset(path);
            }

            return tempAssetBuild;
        }

        // 分包, 按照当前场景
        public void PatchAsset(string path) {
            // e.g. Title
            string patchName = currentScene;

            // 查找分包记录
            PatchBuild tempPatch = patchBuildList.Find(patch => patch.name.Equals(patchName));

            if (tempPatch == null) {
                tempPatch = new PatchBuild();
                tempPatch.name = patchName;
                patchBuildList.Add(tempPatch);
            }

            // 存在这个文件
            if (File.Exists(path)) {
                if (!tempPatch.assetList.Contains(path)) {
                    // 添加到 分包 的 assetList 里
                    tempPatch.assetList.Add(path);
                }
            }
        }

        // build 版本 加1
        public string AddVersion() {
            build = build + 1;
            return GetVersion();
        }

        // 获取 Version
        public string GetVersion() {
            Version version = new Version(major, minor, build);
            return version.ToString();
        }

        // 解析 Rules.asset
        public void Analyze() {
            // 清空 _unexplicits, _tracker, _duplicated, _asset2BundleDict
            Clear();
            // 收集 {BuildRule} 下的 asset
            CollectAssets();
            // 获取依赖, 分析出  _tracker, _unexplicits, _duplicated
            AnalysisAssets();
            // 优化资源
            OptimizeAssets();
            // 保存 BuildRules
            Save();
        }

        // List<BundleBuild> 转换为 AssetBundleBuild[]
        public AssetBundleBuild[] GetAssetBundleBuildArray() {
            return bundleBuildList.ConvertAll(
                delegate (BundleBuild input) {
                    return input.ConvertToAssetBundleBuild();
                }
            ).ToArray();
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

        // 记录 asset 所属的 bundles(可能有多个)
        // 添加 _tracker 记录
        // 添加 _unexplicits 记录
        // 添加 _duplicated 记录
        private void Track(string assetName, string bundleName) {
            HashSet<string> hashSet;

            if (!_trackerDict.TryGetValue(assetName, out hashSet)) {
                hashSet = new HashSet<string>();
                _trackerDict.Add(assetName, hashSet);
            }

            hashSet.Add(bundleName);

            string bundleNameTemp;
            _asset2BundleDict.TryGetValue(assetName, out bundleNameTemp);
            // _asset2BundleDict 里找不到 这个 bundle名， 因为没有设置
            // 是分析依赖时找出来的
            if (string.IsNullOrEmpty(bundleNameTemp)) {
                // e.g. [Assets/XAsset/Extend/TestCommon/Btn_User_h 1.png, children_assets_extend_testprefab"]
                // groupName = bundleName
                // isChildren = true
                // bundle 名 为最后分析的那一个
                _unexplicitDict[assetName] = GetGroupName(GroupBy.Explicit, assetName, bundleName, true);

                // 同一个资源被多个不同的 bundle 引用, 添加到 _duplicatedList
                if (hashSet.Count > 1) {
                    _duplicatedList.Add(assetName);
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
            _unexplicitDict.Clear();
            _trackerDict.Clear();
            _duplicatedList.Clear();
            _asset2BundleDict.Clear();
        }

        // 将 一对一的  [assetName, bundleName] 转化为 [bundleName, List<assetName>]
        private Dictionary<string, List<string>> GetBundle2AssetListDict() {
            Dictionary<string, List<string>> bundle2AssetListDict = new Dictionary<string, List<string>>();

            foreach (KeyValuePair<string, string> item in _asset2BundleDict) {
                string bundle = item.Value;
                List<string> list;

                if (!bundle2AssetListDict.TryGetValue(bundle, out list)) {
                    list = new List<string>();
                    bundle2AssetListDict[bundle] = list;
                }

                if (!list.Contains(item.Key))
                    list.Add(item.Key);
            }
            return bundle2AssetListDict;
        }

        // 保存到 Rules.asset 中
        private void Save() {
            bundleBuildList.Clear();

            Dictionary<string, List<string>> bundle2AssetListDict = GetBundle2AssetListDict();

            // 重新生成  Rules.asset 中的 BundleBuild
            foreach (KeyValuePair<string, List<string>> item in bundle2AssetListDict) {
                BundleBuild bundleBuild = new BundleBuild() {
                    assetBundleName = item.Key,
                    assetNames = item.Value,
                };

                bundleBuildList.Add(bundleBuild);
            }


            foreach (PatchBuild patchBuild in patchBuildList) {
                for (var i = 0; i < patchBuild.assetList.Count; ++i) {
                    var asset = patchBuild.assetList[i];
                    if (!File.Exists(asset)) {
                        patchBuild.assetList.RemoveAt(i);
                        --i;
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        // 通过 BuildRule 获得需要打包的文件路径和该文件所属的assetbundle的名称 将其转化为 AssetBuild
        private void CollectAssets() {
            List<AssetBuild> assetBuildListTemp = new List<AssetBuild>();

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
                    assetBuildListTemp.Add(assetBuild);
                }
            }

            // 处理局部的 AssetBuildList
            for (int i = 0; i < assetBuildListTemp.Count; i++) {

                AssetBuild assetBuild = assetBuildListTemp[i];
                // 跳过 GroupBy.None
                if (assetBuild.groupBy == GroupBy.None) {
                    continue;
                }

                // 获取 asset 的 GroupName, 设置为 AssetBuild.bundleName
                assetBuild.bundleName = GetGroupName(assetBuild);
                // 将 assetName 和 assetBundleName 的 对应关系 存储到 _asset2Bundles
                BundleAsset(assetBuild.assetName, assetBuild.bundleName);
            }

            // 局部 AssetBuildList 赋值给 AssetBuildList
            this.assetBuildList = assetBuildListTemp;
        }

        // 分析
        private void AnalysisAssets() {
            // 获取 [bundleName, List<assetName>]
            Dictionary<string, List<string>> bundle2AssetDict = GetBundle2AssetListDict();

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
                            Track(assetName, bundleName);
                        }
                    }
                i++;
            }
        }

        // 优化多个文件
        private void OptimizeAssets() {
            foreach (KeyValuePair<string, string> item in _unexplicitDict) {
                // 查找 _tracker 里的 asset 只属于一个 bundle, 添加到 _asset2BundleDict
                // e.g. [Assets/XAsset/Extend/TestCommon/Btn_User_h 2.png, children_assets_xasset_extend_testprefab3]
                if (_trackerDict[item.Key].Count < 2) {
                    _asset2BundleDict[item.Key] = item.Value;
                }
            }

            // 从这里开始 _trackerDict 就没有用了, 可以清掉. add by 黄鑫
            _trackerDict.Clear();
            // 从这里开始 _unexplicitDict 就没有用过了, 可以清掉. add by 黄鑫
            _unexplicitDict.Clear();

            for (int i = 0, max = _duplicatedList.Count; i < max; i++) {
                var item = _duplicatedList[i];
                if (EditorUtility.DisplayCancelableProgressBar(string.Format("优化冗余{0}/{1}", i, max), item,
                    i / (float)max)) break;
                // 优化被多个bundle 引用的 未设置过 bundle 的 asset
                OptimizeAsset(item);
            }

            // 从这里开始 _duplicatedList 就没有用过了, 可以清掉. add by 黄鑫
            _duplicatedList.Clear();
        }

        // 优化被多个bundle 引用的 未设置过 bundle 的 asset
        private void OptimizeAsset(string asset) {
            // 添加到 _asset2BundleDict
            // isShared = true
            // e.g. [Assets/XAsset/Extend/TestCommon/Btn_User_h 1.png, shared_assets_xasset_extend_testcommon]
            _asset2BundleDict[asset] = GetGroupName(GroupBy.Directory, asset, null, false, true);
        }

        #endregion
    }
}