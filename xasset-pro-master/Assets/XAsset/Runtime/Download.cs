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

        private FileStream _fileStream;

        public string filename { get; set; }

        public int id { get; set; }

        public string error { get; private set; }

        public long len { get; set; }

        public string hash { get; set; }

        public string url { get; set; }

        public long position { get; set; }

        public string tempPath {
            get {
                var path = string.Format("{0}/{1}", Application.temporaryCachePath, hash);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                return path;
            }
        }

        public bool isFinished { get; internal set; }

        public bool canceled { get; private set; }

        public override string ToString() {
            return string.Format("{0}, size:{1}, hash:{2}", url, len, hash);
        }

        public void Start() {
            error = null;
            isFinished = false;
            canceled = false;
            _fileStream = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write);
            position = _fileStream.Length;
            if (position < len) {
                _fileStream.Seek(position, SeekOrigin.Begin);
                _unityWebRequest = UnityWebRequest.Get(url);
                _unityWebRequest.SetRequestHeader("Range", "bytes=" + position + "-");
                _unityWebRequest.downloadHandler = new DownloadHandler(this);
                _unityWebRequest.SendWebRequest();
            } else {
                isFinished = true;
            }
        }

        public void Pause() {
            Cancel(true);
        }

        public void UnPause() {
            Start();
        }

        public void Cancel(bool save = false) {
            CloseStream();

            if (!save) {
                if (File.Exists(tempPath)) {
                    File.Delete(tempPath);
                }
            }

            canceled = true;

            if (_unityWebRequest != null) {
                _unityWebRequest.Abort();
            }

            DisposeRequest();
        }

        public void Finish() {
            if (_unityWebRequest != null && _unityWebRequest.isHttpError) {
                error = string.Format("Error downloading [{0}]: [{1}] [{2}]", url, _unityWebRequest.responseCode,
                    _unityWebRequest.error);
            }

            if (_unityWebRequest != null && _unityWebRequest.isNetworkError) {
                error = string.Format("Error downloading [{0}]: [{1}]", url, _unityWebRequest.error);
            }

            CloseStream();

            DisposeRequest();
        }

        public bool IsValid() {
            var isOk = true;
            if (File.Exists(tempPath)) {
                using (var stream = File.OpenRead(tempPath)) {
                    if (stream.Length != len) {
                        error = string.Format("文件长度校验不通过，期望值:{0}，实际值:{1}", len, stream.Length);
                        isOk = false;
                    } else {
                        if (Assets.verifyBy == VerifyBy.CRC) {
                            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
                            var crc = Utility.GetCRC32Hash(stream);
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

        private void DisposeRequest() {
            if (_unityWebRequest == null) return;
            _unityWebRequest.Dispose();
            _unityWebRequest = null;
        }

        private void CloseStream() {
            if (_fileStream == null) return;
            _fileStream.Flush();
            _fileStream.Close();
            _fileStream = null;
        }

        public void Retry() {
            Cancel();
            Start();
        }

        public void Write(byte[] buffer, int index, int dataLength) {
            _fileStream.Write(buffer, index, dataLength);
            position += dataLength;
        }

        public void Copy() {
            if (File.Exists(tempPath)) {
                var dir = Path.GetDirectoryName(filename);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(tempPath, filename, true);
                PlayerPrefs.SetString(filename, hash);
            }
            File.Delete(tempPath);
        }
    }

    public class DownloadHandler : DownloadHandlerScript {
        private Download _download;

        public DownloadHandler(Download download) {
            _download = download;
        }

        protected override bool ReceiveData(byte[] buffer, int dataLength) {
            if (buffer == null || dataLength == 0) {
                return false;
            }

            if (!_download.canceled) {
                _download.Write(buffer, 0, dataLength);
            }

            return true;
        }

        protected override void CompleteContent() {
            _download.isFinished = true;
        }
    }
}