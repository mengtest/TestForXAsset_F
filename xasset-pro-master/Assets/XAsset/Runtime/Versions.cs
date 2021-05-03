//
// Versions.cs
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

namespace libx {
    public enum VerifyBy {
        Size,   // 文件大小
        CRC // 文件 CRC
    }

    // 运行时的 分包信息
    [Serializable]
    public class Patch {
        public string name = string.Empty;  // 分包名字
        public List<int> bundleIDList = new List<int>();    // 包含的 bundleID

        // 序列化 Patch
        public void Serialize(BinaryWriter writer) {
            writer.Write(name);
            writer.Write(bundleIDList.Count);
            foreach (int bundleID in bundleIDList) {
                writer.Write(bundleID);
            }
        }

        // 反序列化 Patch
        public void Deserialize(BinaryReader reader) {
            name = reader.ReadString();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) {
                var file = reader.ReadInt32();
                bundleIDList.Add(file);
            }
        }

        public override string ToString() {
            return string.Format("name={0}, files={1}", name, string.Join(",", bundleIDList.ConvertAll(input => input.ToString()).ToArray()));
        }
    }

    // Asset名, 所在Bundle, 所在 目录, 序列化， 反序列化
    [Serializable]
    public class AssetRef {
        public string name; // asset名
        public int bundleID;  // 所在的 BundleRef 索引
        public int dirID; // 所在的文件夹索引

        public void Serialize(BinaryWriter writer) {
            writer.Write(name); // 写入 名字 e.g. Image.prefab
            writer.Write(bundleID);   // 写入 BudleRef 索引 e.g. 0
            writer.Write(dirID);  // 写入所在文件夹的索引 e.g. 0
        }

        // 反序列化 AssetRef
        public void Deserialize(BinaryReader reader) {
            name = reader.ReadString(); // 读取 名字 e.g. Image.prefab
            bundleID = reader.ReadInt32();  // 读取 BudleRef 索引 e.g. 0
            dirID = reader.ReadInt32(); // 读取所在文件夹的索引 e.g. 0
        }

        public override string ToString() {
            return string.Format("name={0}, bundle={1}, dir={2}", name, bundleID, dirID);
        }
    }

    [Serializable]
    public class BundleRef {
        public string name; // bundle名 e.g. assets_xasset_extend_testprefab1
        public int[] childrenBundleIDArray = new int[0]; // 依赖的 bundle 索引
        public long len;    // 文件长度  通过 File 读取
        public string hash; // 文件 hash 通过 AssetBundleManifest.GetAssetBundleHash() 计算获得（官方API)
        public string crc;  // crc Utility.GetCRC32Hash() 计算所得
        public int id { get; set; } // bundle 索引
        public byte location { get; set; }  // 0 表示 bundle 在 分包, 1 表示 bundle 在包里

        // BundleRef.Equals()
        // 判断两个 BundleRef 是否相等
        public bool Equals(BundleRef other) {
            return name == other.name &&
                   len == other.len &&
                   location == other.location &&
                   crc.Equals(other.crc, StringComparison.OrdinalIgnoreCase);
        }

        // 对比 BundleRef 的内容
        public bool EqualsWithContent(BundleRef other) {
            // 对比 bundle 的文件长度和  crc
            return len == other.len && crc.Equals(other.crc, StringComparison.OrdinalIgnoreCase);
        }

        // BundleRef 序列化
        public void Serialize(BinaryWriter writer) {
            writer.Write(location); // 写入 位置  e.g. 1
            writer.Write(len);  // 写入文件长度 e.g. 4799
            writer.Write(name); // 写入  名字 e.g. assets_xasset_extend_testimage
            writer.Write(hash); // 写入  hash e.g. 2484ef716428f14af8c0dd3a9d3efffb
            writer.Write(crc);  // 写入  crc e.g. c752f15b
            int childrenBundleCount = childrenBundleIDArray.Length;
            // 写入 依赖的  Bundle 数量
            writer.Write(childrenBundleCount);
            // 写入依赖的 bundleid
            foreach (int childrenBundleID in childrenBundleIDArray) {
                writer.Write(childrenBundleID);
            }
        }

        // 反序列化 BundleRef
        public void Deserialize(BinaryReader reader) {
            location = reader.ReadByte();   // 读取 位置 e.g. 1
            len = reader.ReadInt64();   // 读取文件长度 e.g. 4799
            name = reader.ReadString(); // 读取bundle名字 e.g. assets_xasset_extend_testimage
            hash = reader.ReadString(); // 读取 bundle  hash e.g. 2484ef716428f14af8c0dd3a9d3efffb
            crc = reader.ReadString();  // 读取  crc e.g. c752f15b
            // 读取 依赖的 bundle 数量
            int childrenBundleCount = reader.ReadInt32();
            childrenBundleIDArray = new int[childrenBundleCount];
            // 读取 依赖的 bundle 索引
            for (var i = 0; i < childrenBundleCount; i++) {
                childrenBundleIDArray[i] = reader.ReadInt32();
            }
        }

        public override string ToString() {
            return string.Format("id={0}, name={1}, len={2}, location={3}, hash={4}, crc={5}, children={6}", id, name, len,
                    location, hash, crc, string.Join(",", Array.ConvertAll(childrenBundleIDArray, input => input.ToString())));
        }
    }

    // 版本信息
    public class Versions {
        // 版本信息
        public string ver = new Version(0, 0, 0).ToString();
        public string[] activeVariants = new string[0];
        // 包含的目录
        // e.g.
        // ["Assets/XAsset/Extend/TestPrefab1", ...]
        public string[] dirArray = new string[0];

        // 包含的 AssetRef List
        public List<AssetRef> assetRefList = new List<AssetRef>();
        // 包含的 BundleRef List
        public List<BundleRef> bundleRefList = new List<BundleRef>();

        // 包含的 Patch List
        public List<Patch> patchList = new List<Patch>();

        // bundle名 和对应的 BundleRef
        // e.g. [assets_xasset_extend_testimage, BundlRef]
        private readonly Dictionary<string, BundleRef> _bundleName2BundleRefDict = new Dictionary<string, BundleRef>();
        // 分包名 和 对应的Patch
        // e.g. [Title, [name=Title, files=1,3,0,2,4]]
        private readonly Dictionary<string, Patch> _patchName2PatchDict = new Dictionary<string, Patch>();

        // 
        public bool outside { get; set; }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendLine("ver:\n" + ver);
            sb.AppendLine("activeVariants:\n" + string.Join(",", activeVariants));
            sb.AppendLine("dirs:\n" + string.Join("\n", dirArray));
            sb.AppendLine("assets:\n" + string.Join("\n", assetRefList.ConvertAll(input => input.ToString()).ToArray()));
            sb.AppendLine("bundles:\n" + string.Join("\n", bundleRefList.ConvertAll(input => input.ToString()).ToArray()));
            sb.AppendLine("patches:\n" + string.Join("\n", patchList.ConvertAll(input => input.ToString()).ToArray()));
            return sb.ToString();
        }

        // 是否包含 对应的 BundleRef
        public bool Contains(BundleRef bundleRef) {
            BundleRef tempBundleRef;
            if (_bundleName2BundleRefDict.TryGetValue(bundleRef.name, out tempBundleRef)) {
                // 两个 BundleRef 相等
                if (tempBundleRef.Equals(bundleRef)) {
                    return true;
                }
            }

            return false;
        }

        // 根据 bundle 名， 获取 BundleRef
        public BundleRef GetBundle(string bundleName) {
            BundleRef bundleRef;
            _bundleName2BundleRefDict.TryGetValue(bundleName, out bundleRef);
            return bundleRef;
        }


        // Versions.GetFiles
        // 通过分包名， 获取分包里的 BundleRef
        public List<BundleRef> GetBundleRef(string patchName) {
            List<BundleRef> bundleRefList = new List<BundleRef>();
            Patch patch;

            if (_patchName2PatchDict.TryGetValue(patchName, out patch)) {
                if (patch.bundleIDList.Count > 0) {
                    foreach (int bundleID in patch.bundleIDList) {
                        BundleRef bundleRef = this.bundleRefList[bundleID];
                        bundleRefList.Add(bundleRef);
                    }
                }
            }

            return bundleRefList;
        }

        // Versions.GetFilesInBuild()
        public List<BundleRef> GetFilesInBuild() {
            var list = new List<BundleRef>();
            foreach (var bundle in bundleRefList) {
                if (bundle.location == 1) {
                    list.Add(bundle);
                }
            }
            return list;
        }

        // Version.Serialize()
        public void Serialize(BinaryWriter writer) {
            // 写入版本 e.g. "0.0.1"
            writer.Write(ver);
            // 写入 文件夹长度 e.g. 5
            writer.Write(dirArray.Length);
            // 写入文件夹, e.g. Assets/XAsset/Extend/TestPrefab1
            foreach (var dir in dirArray)
                writer.Write(dir);

            // 写入  activeVariants 长度
            writer.Write(activeVariants.Length);
            foreach (var variant in activeVariants)
                writer.Write(variant);

            // 写入 AssetRef 数量
            writer.Write(assetRefList.Count);
            foreach (var asset in assetRefList)
                // AssetRef.Serialize()
                asset.Serialize(writer);

            // 写入 BundleRef 数量
            writer.Write(bundleRefList.Count);
            // BundleRef.Serialize()
            foreach (var file in bundleRefList)
                file.Serialize(writer);

            // 写入 Patch 数量
            writer.Write(patchList.Count);
            // Patch.Serialize()
            foreach (Patch patch in patchList)
                patch.Serialize(writer);
        }

        // 反序列化 Versions
        public void Deserialize(BinaryReader reader) {
            // 读取版本 e.g. "0.0.1"
            ver = reader.ReadString();
            // 读取 目录 数量 e.g. 4
            int count = reader.ReadInt32();
            // 读取 每个目录的名字
            // e.g.
            //  Assets/XAsset/Extend/TestPrefab1
            //  Assets/XAsset/Extend/TestPrefab2
            //  Assets/XAsset/Extend/TestPrefab3
            //  Assets/XAsset/Extend/TestImage
            dirArray = new string[count];
            for (var i = 0; i < count; i++) {
                dirArray[i] = reader.ReadString();
            }

            // 读取 activeVariants 的数量 e.g. 0
            count = reader.ReadInt32();
            // 读取 每个 activeVariants 的名字
            activeVariants = new string[count];
            for (var i = 0; i < count; i++) {
                activeVariants[i] = reader.ReadString();
            }

            // 读取 AssetRef 的数量 e.g. 
            count = reader.ReadInt32();
            for (var i = 0; i < count; i++) {
                AssetRef assetRef = new AssetRef();
                // 反序列化每个  AssetRef
                assetRef.Deserialize(reader);
                assetRefList.Add(assetRef);
            }

            // 获取 BundleRef 的数量
            count = reader.ReadInt32();
            for (var i = 0; i < count; i++) {
                BundleRef bundleRef = new BundleRef();
                // 反序列化每个 BundleRef
                bundleRef.Deserialize(reader);
                // 设置 BundleRef 的索引
                bundleRef.id = bundleRefList.Count;

                // 根据 Versions.outside 来设置 BundlRef.location
                if (outside) {
                    bundleRef.location = 1;
                }

                bundleRefList.Add(bundleRef);
                _bundleName2BundleRefDict[bundleRef.name] = bundleRef;
            }

            // 读取 Patch 的数量
            count = reader.ReadInt32();
            // 
            for (var i = 0; i < count; i++) {
                Patch patch = new Patch();
                patch.Deserialize(reader);
                patchList.Add(patch);
                _patchName2PatchDict[patch.name] = patch;
            }
        }

        // 保存 Versions.bundle
        public void Save(string path) {
            if (File.Exists(path)) {
                File.Delete(path);
            }

            using (var writer = new BinaryWriter(File.OpenWrite(path))) {
                Serialize(writer);
            }
        }
    }
}