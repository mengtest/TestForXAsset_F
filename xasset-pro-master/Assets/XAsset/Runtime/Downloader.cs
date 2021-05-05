//
// Downloader.cs
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
using UnityEngine;

namespace libx {

    // 下载器，主要用来管理 Download
    public class Downloader {

        // 获取显示的下载速度
        public static string GetDisplaySpeed(float downloadSpeed) {

            // 大于 1mb/s 显示 mb
            if (downloadSpeed >= 1024 * 1024) {
                return string.Format("{0:f2}MB/s", downloadSpeed * BYTES_2_MB);
            }

            // 大于 1kb/s 显示 kb
            if (downloadSpeed >= 1024) {
                return string.Format("{0:f2}KB/s", downloadSpeed / 1024);
            }

            // 显示 byte/s 
            return string.Format("{0:f2}B/s", downloadSpeed);
        }

        // 获取显示的下载内容的大小
        public static string GetDisplaySize(long downloadSize) {
            // 大于 1mb 显示 mb
            if (downloadSize >= 1024 * 1024) {
                return string.Format("{0:f2}MB", downloadSize * BYTES_2_MB);
            }

            // 大于 1kb 显示 kb
            if (downloadSize >= 1024) {
                return string.Format("{0:f2}KB", downloadSize / 1024);
            }

            // 显示 byte
            return string.Format("{0:f2}B", downloadSize);
        }

        // byte 转 mb
        private const float BYTES_2_MB = 1f / (1024 * 1024);

        // 同时 存在的 Download 数量
        public int maxDownloads = 1;


        // 开始下载时, _allDownloadList 里的所有 Download 会 放进 _preparedDownloadList 中
        // _allDownloadList 中的 Download 只在 Clear 中清除
        private readonly List<Download> _allDownloadList = new List<Download>();

        // 这里的 Download 主要用来 调用 Download.Start()
        private readonly List<Download> _preparedDownloadList = new List<Download>();

        private readonly List<Download> _progressingDownloadList = new List<Download>();



        // 下载更新回调
        // totalPosition 当前总的下载进度
        // totalDownloadSize 要下载的文件的总大小
        // downloadSpeed 下载速度
        public Action<long, long, float> onUpdate;

        // 下载完成回调
        public Action onFinished;

        // 当前正在 处理的 Download 的 索引
        private int _indexOfDownload;

        // 开始下载的时间
        private float _startDownloadTime;

        // 上一次[记录]的 从开始下载到 这一帧 经过的时间
        private float _lastElapsedTimeSinceStart;

        // 上一次记录的 下载总进度
        private long _lastTotalPosition;

        // 整个下载器 是否暂停
        private bool _paused;

        // 下载是否完成
        public bool isDone { get; private set; }

        // 此次要下载的文件的总大小
        public long totalDownloadSize { get; private set; }

        // 总的下载进度
        public long totalPosition { get; private set; }

        // 下载速度
        public float downloadSpeed { get; private set; }

        public List<Download> downloads { get { return _allDownloadList; } }

        private long GetTotalPosition() {
            // 所有 要下载的文件的 总长度
            long totalLen = 0L;
            // 所有 要下载的文件的 当前进度
            long totalDownloadedPosition = 0L;

            foreach (Download download in _allDownloadList) {
                totalDownloadedPosition += download.position;
                totalLen += download.len;
            }

            return totalDownloadedPosition - (totalLen - totalDownloadSize);
        }

        private bool _started;

        // 采样时间， 每隔多少 sampleTime, 处理一次 onUpdate 回调
        public float sampleTimeForUpdate = 1f;

        // Downloader.Start()
        // 开始下载
        public void Start() {
            _indexOfDownload = 0;
            _lastTotalPosition = 0L;
            _startDownloadTime = Time.realtimeSinceStartup;
            _lastElapsedTimeSinceStart = 0;
            isDone = false;
            _preparedDownloadList.AddRange(_allDownloadList);
        }

        // Downloader.UnPause()
        public void UnPause() {
            if (!_paused) {
                return;
            }

            foreach (Download processingDownload in _progressingDownloadList) {
                processingDownload.UnPause();
            }

            _paused = false;
        }

        // Downloader.Pause()
        public void Pause() {
            if (_paused) {
                return;
            }

            // 暂停所有正在处理的 Download
            foreach (Download download in _progressingDownloadList) {
                download.Pause();
            }

            _paused = true;
        }

        public void Clear() {
            totalDownloadSize = 0;
            totalPosition = 0;
            _indexOfDownload = 0;
            _lastElapsedTimeSinceStart = 0f;
            _lastTotalPosition = 0L;
            
            _startDownloadTime = 0;

            foreach (Download download in _progressingDownloadList) {
                // 取消正在下载的 Download, 并保存 已经下载的临时文件
                download.Cancel(true);
            }

            _progressingDownloadList.Clear();
            _preparedDownloadList.Clear();
            _allDownloadList.Clear();
        }

        // Downloader.AddDownload()
        public void AddDownload(string url, string filename, string hash, long len) {
            Download download = new Download {
                url = url,  // e.g. http://192.168.1.113/Bundles/Windows/_additive
                hash = hash,    // e.g. 47b621c7
                len = len,  // e.g. 107482
                filename = filename // e.g. "C/Users/void87/AppData/Local/Temp/voidgame/xasset/47b621c7
            };

            _allDownloadList.Add(download);

            // tempPath e.g. C:/Users/void87/AppData/Local/Temp/voidgame/xasset/47b621c7
            // 读取 临时的下载文件
            FileInfo fileInfo = new FileInfo(download.tempPath);
            // 如果临时文件存在, 说明以前下载过，且没有下载完,接着下载
            if (fileInfo.Exists) {
                totalDownloadSize += len - fileInfo.Length;
            } else {
                totalDownloadSize += len;
            }
        }

        // Downloader.Update()
        internal void Update() {

            if (_paused || isDone)
                return;

            if (_preparedDownloadList.Count > 0 && _progressingDownloadList.Count < maxDownloads) {
                for (int i = 0; i < Math.Min(maxDownloads - _progressingDownloadList.Count, _preparedDownloadList.Count); ++i) {
                    Download preparedDownload = _preparedDownloadList[i];
                    // 1. Download.Start()
                    preparedDownload.Start();

                    Debug.Log("Start Download:" + preparedDownload.url);

                    // 2. 将 preparedDownload 放进 _progressingDownloadList
                    _progressingDownloadList.Add(preparedDownload);

                    // 3. 从 _preparedDownloadList 中 移除 preparedDownload
                    _preparedDownloadList.RemoveAt(i);
                    --i;
                }
            }

            for (int i = 0; i < _progressingDownloadList.Count; ++i) {
                Download progressingDownload = _progressingDownloadList[i];
                // Download.isFinished 由 DownloadHandler 决定
                if (progressingDownload.isFinished) {
                    if (!string.IsNullOrEmpty(progressingDownload.error)) {
                        Debug.LogError(string.Format("Download Error:{0}, {1}", progressingDownload.url, progressingDownload.error));

                        // 出错了,自动重新下载
                        progressingDownload.Retry();

                        Debug.Log("Retry Download：" + progressingDownload.url);
                    } else {

                        progressingDownload.Finish();

                        if (progressingDownload.IsValid()) {
                            progressingDownload.Copy();
                            _progressingDownloadList.RemoveAt(i);
                            _indexOfDownload++;
                            --i;

                            Debug.Log("Finish Download：" + progressingDownload.url);
                        } else {

                            Debug.LogError(string.Format("Download Error:{0}, {1}", progressingDownload.url, progressingDownload.error));
                            // 校验没通过, 重新下载
                            progressingDownload.Retry();

                            Debug.Log("Retry Download：" + progressingDownload.url);
                        }
                    }
                }
            }

            if (!isDone && _indexOfDownload == downloads.Count) {
                // 所有的 Download 都下载完了
                Finish();
            }

            totalPosition = GetTotalPosition();

            // 从开始下载到 这一帧 经过的时间
            float elapsedTimeSinceStart = Time.realtimeSinceStartup - _startDownloadTime;

            // 经过的时间 没有 超过 采样时间, 直接返回
            if (elapsedTimeSinceStart - _lastElapsedTimeSinceStart < sampleTimeForUpdate)
                return;

            // deltaTime 主要用来 计算 下载速度
            float deltaTime = elapsedTimeSinceStart - _lastElapsedTimeSinceStart;

            // 计算下载速度
            downloadSpeed = (totalPosition - _lastTotalPosition) / deltaTime;

            // 调用 onUpdate 回调
            if (onUpdate != null) {
                // totalPosition 当前总的下载进度
                // totalDownloadSize 要下载的文件的总大小
                // downloadSpeed 下载速度
                onUpdate(totalPosition, totalDownloadSize, downloadSpeed);
            }

            // 记录经过的时间
            _lastElapsedTimeSinceStart = elapsedTimeSinceStart;

            // 记录下载总进度
            _lastTotalPosition = totalPosition;
        }

        // Downloader.Finish()
        private void Finish() {
            if (onFinished != null) {
                onFinished.Invoke();
            }

            isDone = true;
        }
    }
}