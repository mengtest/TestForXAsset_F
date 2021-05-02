//
// EditorInitializer.cs
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
    // 编辑器下
    public static class EditorInitializer {

        // 编辑器运行时初始化
        [RuntimeInitializeOnLoadMethod]
        private static void OnInitialize() {
            Debug.Log("EditorInitializer.OnInitialize()");

            List<string> sceneAssetList = new List<string>();
            // 获取 BuildRules
            BuildRules buildRules = BuildScript.GetBuildRules();

            // 查找 Assets/XAsset 文件夹下的  场景文件
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", buildRules.scenesFolders)) {
                string sceneAssetPath = AssetDatabase.GUIDToAssetPath(guid);
                // 添加到 场景列表 e.g. Assets/XAsset/Demo/Scenes/Additive.unity
                sceneAssetList.Add(sceneAssetPath);
            }

            List<Patch> patchList = new List<Patch>();

            List<AssetRef> assetRefList = new List<AssetRef>();

            // 资源的搜索路径

            List<string> searchPathList = new List<string>();
            // [路径名, 路径索引]
            Dictionary<string, int> dirDict = new Dictionary<string, int>();

            // 将 AssetBuidList 转换为  AssetRefList
            foreach (var asset in buildRules.assetBuildList) {
                // 检查项目里是否由这个文件 e.g. Assets/XAsset/Extend/TestPrefab1/Image.prefab
                if (!File.Exists(asset.assetName)) {
                    continue;
                }

                // 获取文件夹 Assets/XAsset/Extend/TestPrefab1
                string dir = Path.GetDirectoryName(asset.assetName);

                if (!string.IsNullOrEmpty(dir)) {
                    // 添加到 searchPathList
                    if (!searchPathList.Contains(dir)) {
                        // e.g. [Assets/XAsset/Extend/TestPrefab1, 0]
                        dirDict[dir] = searchPathList.Count;
                        // 添加到 searchPathList
                        searchPathList.Add(dir);
                    }
                }

                // 构造 AssetRef
                AssetRef assetRef = new AssetRef {
                    // name e.g. Image.Prefab
                    name = Path.GetFileName(asset.assetName),
                    bundleID = -1,
                    dirID = dirDict[dir]
                };

                assetRefList.Add(assetRef);
            }


            EditorBuildSettingsScene[] editorBuildSettingsSceneArray = new EditorBuildSettingsScene[sceneAssetList.Count];

            for (var index = 0; index < sceneAssetList.Count; index++) {
                // e.g. Assets/XAsset/Demo/Scenes/Additive.unity
                string sceneAsset = sceneAssetList[index];
                editorBuildSettingsSceneArray[index] = new EditorBuildSettingsScene(sceneAsset, true);
                // e.g. Assets/XAsset/Demo/Scenes
                string sceneDir = Path.GetDirectoryName(sceneAsset);

                if (!searchPathList.Contains(sceneDir)) {
                    searchPathList.Add(sceneDir);
                }
            }

            for (var i = 0; i < buildRules.patchBuildList.Count; i++) {
                var item = buildRules.patchBuildList[i];
                var patch = new Patch();
                patch.name = item.name;
                patchList.Add(patch);
            }

            var developVersions = new Versions();
            developVersions.dirArray = searchPathList.ToArray();
            developVersions.assetRefList = assetRefList;
            developVersions.patchList = patchList;

            // 获取平台名
            Assets.platform = BuildScript.GetPlatformName();
            // e.g. D:/Projects/UnityProjects/TestForXAsset5.1/xasset-pro-master/Bundles/Windows/
            Assets.basePath = Environment.CurrentDirectory.Replace("\\", "/") + "/" + BuildScript.outputPath + "/";
            //Assets.basePath = "";

            // 设置加载资源的委托
            Assets.assetLoader = AssetDatabase.LoadAssetAtPath;
            // 设置 加载 Versions 回调
            Assets.versionsLoader += () => developVersions;
            // 设置加载 资源h回调
            Assets.onAssetLoaded += buildRules.OnLoadAsset;
            // 设置卸载资源回调
            Assets.onAssetUnloaded += buildRules.OnUnloadAsset;

            buildRules.BeginSample();
            // 设置 Scene In Build
            EditorBuildSettings.scenes = editorBuildSettingsSceneArray;

            EditorApplication.playModeStateChanged += EditorApplicationOnplayModeStateChanged;
        }

        private static void EditorApplicationOnplayModeStateChanged(PlayModeStateChange obj) {
            // 退出播放时
            if (obj == PlayModeStateChange.ExitingPlayMode) {
                Assets.onAssetLoaded = null;
                Assets.onAssetUnloaded = null;
                var rules = BuildScript.GetBuildRules();
                rules.EndSample();
                EditorUtility.SetDirty(rules);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // 编辑器启动时初始化
        [InitializeOnLoadMethod]
        private static void OnEditorInitialize() {
            EditorUtility.ClearProgressBar();
        }
    }
}