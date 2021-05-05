//
// DownloadAll.cs
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
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace libx {
    
    // 抽象的下载
    public class Download {
        // 包含的 UnityWebRequest
        private UnityWebRequest _unityWebRequest;
        // 包含的 FileStream
        private FileStream _fileStream;

        // 下载完成时的最终文件名, 要从 tempPath 拷贝
        // e.g. C:\\Users\\void87\\AppData\\LocalLow\\voidgame\\xasset\\Bundles
        public string filename { get; set; }

        // 没有使用
        public int id { get; set; }

        // 下载时出现的错误
        public string error { get; private set; }

        // 要下载的文件的长度 e.g. 107482
        public long len { get; set; }

        // 要下载的文件的hash, 也就是 crc 由 自定义计算获得 e.g. 47b621c7
        public string hash { get; set; }

        // e.g. http://192.168.1.113/Bundles/Windows/_additive
        public string url { get; set; }

        // 当前文件的 下载进度
        public long position { get; set; }

        // 下载时的 临时文件位置
        public string tempPath {
            get {
                // e.g. C:/Users/void87/AppData/Local/Temp/voidgame/xasset/47b621c7
                string path = string.Format("{0}/{1}", Application.temporaryCachePath, hash);
                // e.g. C:\\Users\\void87\\AppData\\Local\\Temp\\voidgame\\xasset
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                return path;
            }
        }

        // 是否完成了下载
        public bool isFinished { get; internal set; }

        // 是否已经取消了下载
        public bool canceled { get; private set; }

        public override string ToString() {
            return string.Format("{0}, size:{1}, hash:{2}", url, len, hash);
        }

        // Download.Start()
        public void Start() {
            error = null;
            isFinished = false;
            canceled = false;
            // 创建或打开 临时下载文件
            _fileStream = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write);
            // 获取 临时下载文件的 进度 (不一定是0，因为断点续传)
            position = _fileStream.Length;

            if (position < len) {
                _fileStream.Seek(position, SeekOrigin.Begin);
                _unityWebRequest = UnityWebRequest.Get(url);
                // 续传
                _unityWebRequest.SetRequestHeader("Range", "bytes=" + position + "-");
                _unityWebRequest.downloadHandler = new DownloadHandler(this);
                _unityWebRequest.SendWebRequest();
            } else {
                // 直接完成
                isFinished = true;
            }
        }

        // Download.Pause()
        public void Pause() {
            // 取消下载, 并保存 临时的下载文件
            Cancel(true);
        }

        // Download.UnPause()
        // 取消暂停
        public void UnPause() {
            // 重新开始下载
            Start();
        }

        // 取消这次下载
        public void Cancel(bool save = false) {
            CloseFileStream();

            // 不保存 临时的下载文件
            if (!save) {
                if (File.Exists(tempPath)) {
                    // 删除 临时下载文件
                    File.Delete(tempPath);
                }
            }

            // 取消成功
            canceled = true;

            if (_unityWebRequest != null) {
                _unityWebRequest.Abort();
            }

            DisposeRequest();
        }

        // Download.Finish()
        // 下载完成
        public void Finish() {
            // HttpError
            if (_unityWebRequest != null && _unityWebRequest.isHttpError) {
                error = string.Format("Error downloading [{0}]: [{1}] [{2}]", url, _unityWebRequest.responseCode,
                    _unityWebRequest.error);
            }

            // NetworkError
            if (_unityWebRequest != null && _unityWebRequest.isNetworkError) {
                error = string.Format("Error downloading [{0}]: [{1}]", url, _unityWebRequest.error);
            }


            CloseFileStream();

            DisposeRequest();
        }

        // 校验下载的文件是否正确
        public bool IsValid() {
            bool isOk = true;

            if (File.Exists(tempPath)) {
                using (FileStream fileStream = File.OpenRead(tempPath)) {
                    // 先校验 文件长度（通过FileStream获得）
                    if (fileStream.Length != len) {
                        error = string.Format("文件长度校验不通过，期望值:{0}，实际值:{1}", len, fileStream.Length);
                        isOk = false;
                    } else {
                        // 再校验 crc,（由自定义计算获得）
                        if (Assets.verifyBy == VerifyBy.CRC) {
                            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
                            string crc = Utility.GetCRC32Hash(fileStream);
                            if (!crc.Equals(hash, comparison)) {
                                error = string.Format("文件hash校验不通过，期望值:{0}，实际值:{1}", hash, crc);
                                isOk = false;
                            }
                        }
                    }
                }
            }

            return isOk;
        }

        // 释放 UnityWebRequest
        private void DisposeRequest() {
            if (_unityWebRequest == null) 
                return;
            _unityWebRequest.Dispose();
            _unityWebRequest = null;
        }

        // 关闭 FileStream
        private void CloseFileStream() {
            if (_fileStream == null) 
                return;
            _fileStream.Flush();
            _fileStream.Close();
            _fileStream = null;
        }

        // 重新下载
        public void Retry() {
            // 取消下载,删除 下载的临时文件
            Cancel();
            // 重新开始下载
            Start();
        }

        // 写入到 _fileStream 中
        public void Write(byte[] buffer, int index, int dataLength) {
            _fileStream.Write(buffer, index, dataLength);
            // 更新下载进度
            position += dataLength;
        }

        // Download.Copy()
        // 拷贝临时下载文件到正式下载文件, 删除临时下载文件
        public void Copy() {
            // e.g. C:/Users/void87/AppData/Local/Temp/voidgame/xasset/47b621c7
            if (File.Exists(tempPath)) {
                // dir e.g. C:\\Users\\void87\\AppData\\LocalLow\\voidgame\\xasset\\Bundles
                string dir = Path.GetDirectoryName(filename);
                // 创建目录，如果没有的话
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                // 拷贝临时下载文件 到 正式下载文件
                File.Copy(tempPath, filename, true);


                // 保存正式下载文件的 hash
                PlayerPrefs.SetString(filename, hash);
            }

            // 删除临时下载文件
            File.Delete(tempPath);
        }
    }

    
    public class DownloadHandler : DownloadHandlerScript {
        private Download _download;

        public DownloadHandler(Download download) {
            _download = download;
        }

        // DownloadHandler.ReceiveData()
        protected override bool ReceiveData(byte[] buffer, int dataLength) {

            if (buffer == null || dataLength == 0) {
                return false;
            }

            if (!_download.canceled) {
                // 写入到 _download._fileStream 中
                _download.Write(buffer, 0, dataLength);
            }

            return true;
        }

        // DownloadHandler.CompleteContent()
        protected override void CompleteContent() {
            _download.isFinished = true;
        }
    }
}