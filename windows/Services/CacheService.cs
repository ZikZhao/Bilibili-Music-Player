using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows.Services
{
    /// <summary>
    /// Audio file cache manager for Bilibili audio downloads.
    /// Singleton registered via DI. Mirrors the Flutter <c>CacheManager</c> from <c>cache_manager.dart</c>.
    /// </summary>
    public sealed class CacheService
    {
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private StorageFolder? _cacheFolder;
        private bool _isInitialized;

        /// <summary>
        /// Raised whenever a download progress update occurs.
        /// Subscribers receive progress for all downloads (use <see cref="DownloadProgress.Bvid"/> to filter).
        /// </summary>
        public event EventHandler<DownloadProgress>? ProgressChanged;

        // ── 常量 ──
        private const string CacheExtension = ".mp4";
        private const string LegacyExtension = ".m4s";
        private const string TempExtension = ".tmp";

        /// <summary>
        /// Tracks in-progress downloads to prevent duplicate concurrent downloads of the same BVID.
        /// </summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _activeDownloads = new();

        public CacheService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        // ────────────────────────── Initialization ──────────────────────────

        /// <summary>
        /// Ensures the cache directory exists. Safe to call multiple times.
        /// </summary>
        public async Task EnsureInitializedAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized)
                {
                    return;
                }

                _cacheFolder = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync("audio_cache", CreationCollisionOption.OpenIfExists);

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine($"[CacheService] Initialized: {_cacheFolder.Path}");
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Returns the path to the cached audio file for the given BVID, or <c>null</c> if not cached.
        /// Prefers .mp4 extension; auto-migrates legacy .m4s files by renaming them.
        /// </summary>
        public async Task<string?> GetAudioPathAsync(string bvid)
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
            {
                return null;
            }

            var sanitized = SanitizeBvid(bvid);

            // 优先查找 .mp4
            var file = await _cacheFolder.TryGetItemAsync($"{sanitized}{CacheExtension}");
            if (file is not null)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Cache hit (.mp4): {bvid}");
                return file.Path;
            }

            // 回退查找旧 .m4s 并自动迁移
            var legacyFile = await _cacheFolder.TryGetItemAsync($"{sanitized}{LegacyExtension}");
            if (legacyFile is not null)
            {
                try
                {
                    var newPath = Path.Combine(_cacheFolder.Path, $"{sanitized}{CacheExtension}");
                    if (!File.Exists(newPath))
                    {
                        File.Move(legacyFile.Path, newPath);
                        System.Diagnostics.Debug.WriteLine($"[CacheService] Migrated .m4s → .mp4: {bvid}");
                    }
                    return newPath;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CacheService] Migration failed for {bvid}: {ex.Message}");
                    return legacyFile.Path; // 回退使用旧路径
                }
            }

            return null;
        }

        // ────────────────────────── Cache Checks ──────────────────────────

        /// <summary>
        /// Returns <c>true</c> if an audio file for the given BVID is already cached locally.
        /// </summary>
        public async Task<bool> IsCachedAsync(string bvid)
        {
            var path = await GetAudioPathAsync(bvid);
            return path is not null;
        }

        /// <summary>
        /// Returns <c>true</c> if a download for the given BVID is currently in progress.
        /// </summary>
        public bool IsDownloading(string bvid)
        {
            var sanitized = SanitizeBvid(bvid);
            return _activeDownloads.ContainsKey(sanitized);
        }

        // ────────────────────────── Download ──────────────────────────

        /// <summary>
        /// Downloads an audio file from <paramref name="url"/> and caches it under <paramref name="bvid"/>.
        /// Uses atomic rename (.tmp → .mp4) to prevent reading incomplete files.
        /// Returns the local file path on success, or <c>null</c> on failure.
        /// </summary>
        /// <param name="url">The audio stream URL (from Bilibili PlayUrl API).</param>
        /// <param name="bvid">The BVID used as the cache file key.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public async Task<string?> DownloadAudioAsync(string url, string bvid, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
            {
                return null;
            }

            var sanitized = SanitizeBvid(bvid);

            // ── Check if already cached ──
            var existingPath = await GetAudioPathAsync(bvid);
            if (existingPath is not null)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Already cached, skipping: {bvid}");

                var fileInfo = new FileInfo(existingPath);
                RaiseProgress(DownloadProgress.Completed(bvid, fileInfo.Exists ? fileInfo.Length : 0));
                return existingPath;
            }

            // ── Prevent duplicate downloads ──
            var downloadLock = _activeDownloads.GetOrAdd(sanitized, _ => new SemaphoreSlim(1, 1));

            if (!await downloadLock.WaitAsync(0, cancellationToken))
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Download already in progress, skipping: {bvid}");
                return null;
            }

            try
            {
                // Double-check after acquiring the lock
                existingPath = await GetAudioPathAsync(bvid);
                if (existingPath is not null)
                {
                    var fi = new FileInfo(existingPath);
                    RaiseProgress(DownloadProgress.Completed(bvid, fi.Exists ? fi.Length : 0));
                    return existingPath;
                }

                var filePath = Path.Combine(_cacheFolder.Path, $"{sanitized}{CacheExtension}");
                var tempPath = filePath + TempExtension;

                System.Diagnostics.Debug.WriteLine($"[CacheService] Starting download: {bvid}");

                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                long receivedBytes = 0;
                var lastLoggedPercent = -1;

                // ── Stream to temp file ──
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                {
                    await using var fileStream = new FileStream(
                        tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: 81920, useAsync: true);

                    var buffer = new byte[81920];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        receivedBytes += bytesRead;

                        RaiseProgress(new DownloadProgress(bvid, receivedBytes, totalBytes));

                        // Log every 10%
                        if (totalBytes > 0)
                        {
                            var percent = (int)(receivedBytes * 100 / totalBytes);
                            var tens = percent / 10;
                            if (tens > lastLoggedPercent)
                            {
                                lastLoggedPercent = tens;
                                System.Diagnostics.Debug.WriteLine($"[CacheService] Download progress: {percent}%");
                            }
                        }
                    }

                    await fileStream.FlushAsync(cancellationToken);
                }
                // fileStream 已释放，可以安全执行 Move

                // ── Atomic rename: .tmp → .m4s ──
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                File.Move(tempPath, filePath);

                System.Diagnostics.Debug.WriteLine($"[CacheService] Download complete: {bvid}");
                RaiseProgress(DownloadProgress.Completed(bvid, receivedBytes));

                return filePath;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Download cancelled: {bvid}");
                CleanupTempFile(bvid);
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Download failed: {ex}");
                RaiseProgress(DownloadProgress.Failed(bvid, ex.Message));
                CleanupTempFile(bvid);
                return null;
            }
            finally
            {
                _activeDownloads.TryRemove(sanitized, out _);
                downloadLock.Dispose();
            }
        }

        /// <summary>
        /// Fire-and-forget background download. Use for auto-caching when a track is favorited or played.
        /// </summary>
        public void DownloadInBackground(string url, string bvid)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var path = await DownloadAudioAsync(url, bvid);
                    if (path is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CacheService] Background download complete: {bvid}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CacheService] Background download failed: {ex}");
                }
            });
        }

        // ────────────────────────── Cache Management ──────────────────────────

        /// <summary>
        /// Deletes the cached audio file for a single BVID.
        /// Returns <c>true</c> if the file existed and was deleted.
        /// </summary>
        public async Task<bool> DeleteCacheAsync(string bvid)
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
            {
                return false;
            }

            var sanitized = SanitizeBvid(bvid);
            var file = await _cacheFolder.TryGetItemAsync($"{sanitized}.m4s");

            if (file is not null)
            {
                await file.DeleteAsync();
                System.Diagnostics.Debug.WriteLine($"[CacheService] Deleted cache: {bvid}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Deletes all cached audio files and recreates the cache directory.
        /// </summary>
        public async Task ClearAllAsync()
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
            {
                return;
            }

            try
            {
                await _cacheFolder.DeleteAsync();
                _cacheFolder = await ApplicationData.Current.LocalFolder
                    .CreateFolderAsync("audio_cache", CreationCollisionOption.OpenIfExists);

                System.Diagnostics.Debug.WriteLine("[CacheService] All cache cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Clear cache failed: {ex}");
            }
        }

        /// <summary>
        /// Returns the total size of all cached files in bytes.
        /// </summary>
        public async Task<long> GetCacheSizeBytesAsync()
        {
            await EnsureInitializedAsync();
            if (_cacheFolder is null)
            {
                return 0;
            }

            long totalSize = 0;
            try
            {
                var files = await _cacheFolder.GetFilesAsync();
                foreach (var file in files)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    totalSize += (long)props.Size;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheService] Get cache size failed: {ex}");
            }

            return totalSize;
        }

        /// <summary>
        /// Returns a human-readable formatted cache size string (e.g., "42.9 MB").
        /// Mirrors the Flutter <c>CacheManager.getFormattedCacheSize()</c>.
        /// </summary>
        public async Task<string> GetFormattedCacheSizeAsync()
        {
            var size = await GetCacheSizeBytesAsync();

            if (size < 1024)
            {
                return $"{size} B";
            }

            if (size < 1024 * 1024)
            {
                return $"{(size / 1024.0):F1} KB";
            }

            if (size < 1024L * 1024 * 1024)
            {
                return $"{(size / (1024.0 * 1024.0)):F1} MB";
            }

            return $"{(size / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }

        // ────────────────────────── Helpers ──────────────────────────

        /// <summary>
        /// Sanitizes a BVID for use as a filename (replaces non-alphanumeric chars with '_').
        /// </summary>
        private static string SanitizeBvid(string bvid)
        {
            return Regex.Replace(bvid, "[^a-zA-Z0-9]", "_");
        }

        private void RaiseProgress(DownloadProgress progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }

        private void CleanupTempFile(string bvid)
        {
            try
            {
                if (_cacheFolder is null)
                {
                    return;
                }

                var sanitized = SanitizeBvid(bvid);
                var tempPath = Path.Combine(_cacheFolder.Path, $"{sanitized}.m4s.tmp");
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
