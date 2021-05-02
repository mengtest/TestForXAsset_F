//
// BuildScript.cs
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
using UnityEditor.Build;
using UnityEngine;

namespace libx {
    public class BuildScript : IPreprocessBuild {
        // 获取 bundle 输出目录 e.g. Bundles/Android
        internal static readonly string outputPath = Assets.BundlesDirName + "/" + GetPlatformName();

        public static void ClearAssetBundles() {
            var names = AssetDatabase.GetAllAssetBundleNames();
            for (var i = 0; i < names.Length; i++) {
                var text = names[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        string.Format("Clear Bundles {0}/{1}", i, names.Length), text,
                        i * 1f / names.Length))
                    break;

                AssetDatabase.RemoveAssetBundleName(text, true);
            }

            EditorUtility.ClearProgressBar();
        }

        // 分析 BuildRules
        internal static void AnalyzeBuildRules() {
            BuildRules buildRules = GetBuildRules();
            buildRules.Analyze();
        }

        // 获取 Rules.asset
        // {BuildRules} 对应 Rules.asset
        internal static BuildRules GetBuildRules() {
            return GetAsset<BuildRules>("Assets/Rules.asset");
        }

        internal static string GetPlatformName() {
            return GetPlatformForAssetBundles(EditorUserBuildSettings.activeBuildTarget);
        }
        
        // 获取平台名
        private static string GetPlatformForAssetBundles(BuildTarget target) {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (target) {
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
#if UNITY_2017_3_OR_NEWER
                case BuildTarget.StandaloneOSX:
                    return "OSX";
#else
                case BuildTarget.StandaloneOSXIntel:
                case BuildTarget.StandaloneOSXIntel64:
                case BuildTarget.StandaloneOSXUniversal:
                    return "OSX";
#endif
                default:
                    return null;
            }
        }

        private static string[] GetLevelsFromBuildSettings() {
            List<string> scenes = new List<string>();
            foreach (var item in GetBuildRules().scenesInBuild) {
                var path = AssetDatabase.GetAssetPath(item);
                if (!string.IsNullOrEmpty(path)) {
                    scenes.Add(path);
                }
            }

            return scenes.ToArray();
        }

        private static string GetAssetBundleManifestFilePath() {
            return Path.Combine(outputPath, GetPlatformName()) + ".manifest";
        }

        public static void BuildPlayer() {
            var path = Path.Combine(Environment.CurrentDirectory, "Build/" + GetPlatformName());
            if (path.Length == 0)
                return;

            var levels = GetLevelsFromBuildSettings();
            if (levels.Length == 0) {
                Debug.Log("Nothing to build.");
                return;
            }

            var targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
            if (targetName == null)
                return;

            var buildPlayerOptions = new BuildPlayerOptions {
                scenes = levels,
                locationPathName = path + targetName,
                assetBundleManifestPath = GetAssetBundleManifestFilePath(),
                target = EditorUserBuildSettings.activeBuildTarget,
                options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None
            };
            BuildPipeline.BuildPlayer(buildPlayerOptions);
        }

        // 创建 bundle 所在目录 e.g. Bundles/Android
        private static string CreateAssetBundleDirectory() {
            // Choose the output build according to the build target.
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            return outputPath;
        }

        // 生成 AssetBundle
        public static void BuildAssetBundles() {
            // 创建目录 e.g. Bundles/Windows
            string dir = CreateAssetBundleDirectory();
            // 获取 BuildTarget
            BuildTarget platform = EditorUserBuildSettings.activeBuildTarget;
            // 获取 buildRules
            BuildRules buildRules = GetBuildRules();

            // 获取 AssetBundleBuild[]
            AssetBundleBuild[] assetBundleBuildArray = buildRules.GetAssetBundleBuildArray();

            // 生成 bundle 官方API
            AssetBundleManifest assetBundleManifest = BuildPipeline.BuildAssetBundles(dir, assetBundleBuildArray, buildRules.options, platform);
            if (assetBundleManifest == null) {
                return;
            }

            BuildVersions(assetBundleManifest, buildRules);
        }

        private static void BuildVersions(AssetBundleManifest assetBundleManifest, BuildRules buidlRules) {
            // 获取所有的 bundle名(官方API)
            string[] allAssetBundleNameArray = assetBundleManifest.GetAllAssetBundles();

            // 获取 bundle名 和 bundle 对应的 索引
            Dictionary<string, int> bundle2IdDict = GetBundle2Ids(allAssetBundleNameArray);

            // 获取所有的 BundleRef
            List<BundleRef> bundleRefList = GetBundleRefList(assetBundleManifest, allAssetBundleNameArray, bundle2IdDict);

            // build 版本 加1
            string ver = buidlRules.AddVersion();

            List<string> dirs = new List<string>();

            List<AssetRef> assetRefList = new List<AssetRef>();

            List<Patch> patchList = new List<Patch>();

            // [assetName, BundeRf]
            Dictionary<string, BundleRef> asset2BundleRefDict = new Dictionary<string, BundleRef>();

            foreach (AssetBuild assetBuild in buidlRules.assetBuildList) {
                string assetName = assetBuild.assetName;
                // 获取文件夹
                // e.g. Assets/XAsset/Extend/TestPrefab1
                string dir = Path.GetDirectoryName(assetName);

                if (!string.IsNullOrEmpty(dir)) {
                    dir = dir.Replace("\\", "/");
                }

                var index = dirs.FindIndex(o => o.Equals(dir));

                if (index == -1) {
                    index = dirs.Count;
                    // 将文件夹 添加到 dirs
                    dirs.Add(dir);
                }

                AssetRef assetRef = new AssetRef();

                if (assetBuild.groupBy == GroupBy.None) {
                    var id = AddBundle(assetName, assetRef, ref bundleRefList);
                    assetRef.bundleID = id;
                } else {
                    bundle2IdDict.TryGetValue(assetBuild.bundleName, out assetRef.bundleID);
                }

                asset2BundleRefDict[assetName] = bundleRefList[assetRef.bundleID];
                assetRef.dirID = index;
                // 文件名 e.g. Image.prefab
                assetRef.name = Path.GetFileName(assetName);
                assetRefList.Add(assetRef);
            }

            Func<List<string>, List<int>> getFiles = delegate (List<string> list) {
                List<int> ret = new List<int>();

                foreach (string file in list) {
                    BundleRef bundle;
                    asset2BundleRefDict.TryGetValue(file, out bundle);

                    if (bundle != null) {
                        if (!ret.Contains(bundle.id)) {
                            ret.Add(bundle.id);
                        }
                        foreach (var child in bundle.childrenBundleIDArray) {
                            if (!ret.Contains(child)) {
                                ret.Add(child);
                            }
                        }
                    } else {
                        Debug.LogWarning("bundle == nil, file:" + file);
                    }
                }
                return ret;
            };

            for (var i = 0; i < buidlRules.patchBuildList.Count; i++) {
                var item = buidlRules.patchBuildList[i];
                patchList.Add(new Patch {
                    name = item.name,
                    files = getFiles(item.assetList),
                });
            }

            var versions = new Versions();
            versions.activeVariants = assetBundleManifest.GetAllAssetBundlesWithVariant();
            // 设置目录
            versions.dirArray = dirs.ToArray();
            // 设置 AssetRef
            versions.assetRefList = assetRefList;
            // 设置 BundleRef
            versions.bundleRefList = bundleRefList;
            // 设置 Patch
            versions.patchList = patchList;
            // 设置 Version
            versions.ver = ver;

            // 整包
            if (buidlRules.allAssetsToBuild) {
                bundleRefList.ForEach(obj => obj.location = 1);
            // 分包
            } else {
                foreach (var patchName in buidlRules.patchesInBuild) {
                    var patch = versions.patchList.Find((Patch item) => { 
                        return item.name.Equals(patchName); 
                    });
                    if (patch != null) {
                        foreach (var file in patch.files) {
                            if (file >= 0 && file < bundleRefList.Count) {
                                bundleRefList[file].location = 1;
                            }
                        }
                    }
                }
            }

            //  e.g. Bundles/Windows/versions.bundle
            versions.Save(outputPath + "/" + Assets.VersionsFileName);
        }

        private static int AddBundle(string path, AssetRef asset, ref List<BundleRef> bundles) {
            var bundleName = path.Replace("Assets/", "");
            var destFile = Path.Combine(outputPath, bundleName);
            var destDir = Path.GetDirectoryName(destFile);
            if (!Directory.Exists(destDir) && !string.IsNullOrEmpty(destDir)) {
                Directory.CreateDirectory(destDir);
            }
            File.Copy(path, destFile, true);
            using (var stream = File.OpenRead(destFile)) {
                var bundle = new BundleRef {
                    name = bundleName,
                    id = bundles.Count,
                    len = stream.Length,
                    crc = Utility.GetCRC32Hash(stream),
                    hash = string.Empty
                };
                asset.bundleID = bundles.Count;
                bundles.Add(bundle);
            }
            return asset.bundleID;
        }

        // 获取所有的 BundleRef
        private static List<BundleRef> GetBundleRefList(AssetBundleManifest manifest, IEnumerable<string> allBundleNames, IDictionary<string, int> bundle2Ids) {
            List<BundleRef> bundleRefList = new List<BundleRef>();
            // 遍历所有的 bundlename, 构造 BundleRef
            foreach (string bundleName in allBundleNames) {
                // 获取 bundle 的依赖 bundle （官方API）
                // e.g. [children_title, ...]
                string[] childrenBundleArray = manifest.GetAllDependencies(bundleName);
                // e.g. Bundles/Windows/_title
                string path = string.Format("{0}/{1}", outputPath, bundleName);
                // 读取 bundle
                if (File.Exists(path)) {
                    using (FileStream fileStream = File.OpenRead(path)) {
                        bundleRefList.Add(new BundleRef {
                            id = bundle2Ids[bundleName],    // e.g. 0
                            name = bundleName,  // e.g. _title
                            childrenBundleIDArray = Array.ConvertAll(childrenBundleArray, input => bundle2Ids[input]),  // e.g. [1]
                            len = fileStream.Length,    // e.g. 13638
                            // 官方 API 获取 AssetFileHash
                            hash = manifest.GetAssetBundleHash(bundleName).ToString(), 
                            // 自定义 crc
                            crc = Utility.GetCRC32Hash(fileStream)
                        });
                    }
                } else {
                    Debug.LogError(path + " file not exist.");
                }
            }

            return bundleRefList;
        }

        // 获取 bundle名 和 bundle 对应的 索引
        private static Dictionary<string, int> GetBundle2Ids(string[] bundleArray) {
            Dictionary<string, int> bundle2IdDict = new Dictionary<string, int>();
            for (var index = 0; index < bundleArray.Length; index++) {
                string bundle = bundleArray[index];
                // e.g. [assets_xasset_extend_testimage, 0]
                bundle2IdDict[bundle] = index;
            }
            return bundle2IdDict;
        }

        private static string GetBuildTargetName(BuildTarget target) {
            var time = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = PlayerSettings.productName + "-v" + PlayerSettings.bundleVersion + ".";
            switch (target) {
                case BuildTarget.Android:
                    return string.Format("/{0}{1}-{2}.apk", name, GetBuildRules().GetVersion(), time);

                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return string.Format("/{0}{1}-{2}.exe", name, GetBuildRules().GetVersion(), time);

#if UNITY_2017_3_OR_NEWER
                case BuildTarget.StandaloneOSX:
                    return "/" + name + ".app";

#else
                case BuildTarget.StandaloneOSXIntel:
                case BuildTarget.StandaloneOSXIntel64:
                case BuildTarget.StandaloneOSXUniversal:
                    return "/" + build + ".app";

#endif

                case BuildTarget.WebGL:
                case BuildTarget.iOS:
                    return "";
                // Add more build targets for your own.
                default:
                    Debug.Log("Target not implemented.");
                    return null;
            }
        }

        // 根据类型和路径获取 对象, 没有就创建
        public static T GetAsset<T>(string path) where T : ScriptableObject {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }

            return asset;
        }

        public int callbackOrder {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildTarget target, string path) {
            SetupScenesInBuild();
            CopyAssets();
        }

        private static void SetupScenesInBuild() {
            var levels = GetLevelsFromBuildSettings();
            var scenes = new EditorBuildSettingsScene[levels.Length];
            for (var index = 0; index < levels.Length; index++) {
                var asset = levels[index];
                scenes[index] = new EditorBuildSettingsScene(asset, true);
            }
            EditorBuildSettings.scenes = scenes;
        }

        public static void CopyAssets() {
            var dir = Application.streamingAssetsPath + "/" + Assets.BundlesDirName;
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, true);
            }
            Directory.CreateDirectory(dir);
            var sourceDir = outputPath;
            var versions = Assets.LoadVersions(Path.Combine(sourceDir, Assets.VersionsFileName));
            foreach (var file in versions.bundleRefList) {
                if (file.location == 1) {
                    var destFile = Path.Combine(dir, file.name);
                    var destDir = Path.GetDirectoryName(destFile);
                    if (!Directory.Exists(destDir) && !string.IsNullOrEmpty(destDir)) {
                        Directory.CreateDirectory(destDir);
                    }
                    File.Copy(Path.Combine(sourceDir, file.name), destFile);
                }
            }
            File.Copy(Path.Combine(sourceDir, Assets.VersionsFileName), Path.Combine(dir, Assets.VersionsFileName));
        }

        public static void ViewVersions(string path) {
            var versions = Assets.LoadVersions(path);
            var txt = "versions.txt";
            File.WriteAllText(txt, versions.ToString());
            EditorUtility.OpenWithDefaultApp(txt);
        }
    }
}