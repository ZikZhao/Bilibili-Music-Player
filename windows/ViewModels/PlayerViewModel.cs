#pragma warning disable MVVMTK0045

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using bilibili_music_player_windows.Api;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace bilibili_music_player_windows.ViewModels
{
    /// <summary>
    /// 播放器 ViewModel，管理播放状态、队列、播放模式。
    ///
    /// ▸ 数据流：UI ←→ PlayerViewModel ←→ AudioPlayerService ←→ MediaPlayer
    /// ▸ 所有状态通过 [ObservableProperty] 暴露，XAML 使用 {x:Bind Mode=OneWay} 绑定。
    /// ▸ 进度条使用 StreamBuilder 模式（通过位置/时长变化事件驱动），无轮询。
    /// </summary>
    public partial class PlayerViewModel : ObservableObject
    {
        private readonly AudioPlayerService _audioPlayer;
        private readonly BilibiliClient _bilibiliClient;
        private readonly CacheService _cacheService;
        private readonly SynchronizationContext _syncContext;

        /// <summary>用于取消进行中的播放请求的 CancellationTokenSource。</summary>
        private CancellationTokenSource? _playCts;

        // ── 可观察属性 ──

        /// <summary>当前播放的曲目。</summary>
        [ObservableProperty]
        private VideoModel? _currentTrack;

        /// <summary>是否正在播放。</summary>
        [ObservableProperty]
        private bool _isPlaying;

        /// <summary>当前播放位置。</summary>
        [ObservableProperty]
        private TimeSpan _position;

        /// <summary>媒体时长。</summary>
        [ObservableProperty]
        private TimeSpan _duration;

        /// <summary>播放进度（0.0~1.0），由 Position / Duration 计算。</summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>缓冲进度（0.0~1.0）。</summary>
        [ObservableProperty]
        private double _bufferingProgress;

        /// <summary>音量（0.0~1.0）。</summary>
        [ObservableProperty]
        private double _volume = 0.8;

        /// <summary>当前曲目在队列中的索引（-1 表示不在队列中）。</summary>
        [ObservableProperty]
        private int _currentIndex = -1;

        /// <summary>队列中的曲目数量。</summary>
        [ObservableProperty]
        private int _queueCount;

        /// <summary>当前播放模式。</summary>
        [ObservableProperty]
        private PlayMode _playMode = PlayMode.Loop;

        /// <summary>是否正在执行淡入/淡出。</summary>
        [ObservableProperty]
        private bool _isFading;

        /// <summary>是否启用淡入/淡出。</summary>
        [ObservableProperty]
        private bool _enableFade = true;

        /// <summary>用户正在拖拽进度条时设为 true，阻止 PositionChanged 覆盖 Progress。</summary>
        public bool IsUserSeeking { get; set; }

        /// <summary>播放队列。</summary>
        public ObservableCollection<VideoModel> Playlist { get; } = new();

        // ── Constructor ──

        public PlayerViewModel(
            AudioPlayerService audioPlayer,
            BilibiliClient bilibiliClient,
            CacheService cacheService)
        {
            _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
            _bilibiliClient = bilibiliClient ?? throw new ArgumentNullException(nameof(bilibiliClient));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

            // 在 DI 容器的 UI 线程上捕获 SynchronizationContext，
            // 用于后续事件处理中的线程封送（避免使用 Window.Current.DispatcherQueue）。
            _syncContext = SynchronizationContext.Current
                ?? new SynchronizationContext();

            // ── 订阅 AudioPlayerService 事件 ──
            _audioPlayer.IsPlayingChanged += OnIsPlayingChanged;
            _audioPlayer.PositionChanged += OnPositionChanged;
            _audioPlayer.DurationChanged += OnDurationChanged;
            _audioPlayer.BufferingProgressChanged += OnBufferingProgressChanged;
            _audioPlayer.MediaEnded += OnMediaEnded;
            _audioPlayer.MediaFailed += OnMediaFailed;
        }

        // ── 命令 ──

        /// <summary>
        /// 播放（如果已暂停则恢复）。
        /// </summary>
        [RelayCommand]
        private void Play()
        {
            if (CurrentTrack is null)
            {
                return;
            }

            if (!IsPlaying)
            {
                if (EnableFade)
                {
                    _audioPlayer.Volume = 0;
                    _audioPlayer.Resume();
                    _ = _audioPlayer.FadeInAsync(Volume);
                }
                else
                {
                    _audioPlayer.Volume = Volume;
                    _audioPlayer.Resume();
                }
            }
        }

        /// <summary>
        /// 暂停播放（带淡出）。异步等待淡出完成，无事件闭包泄漏。
        /// </summary>
        [RelayCommand]
        private async Task PauseAsync()
        {
            if (EnableFade && IsPlaying)
            {
                IsFading = true;
                await _audioPlayer.FadeOutAsync();
                IsFading = false;
                _audioPlayer.Pause();
            }
            else
            {
                _audioPlayer.Pause();
            }
        }

        /// <summary>
        /// 切换播放/暂停。异步等待淡出完成，无事件闭包泄漏。
        /// </summary>
        [RelayCommand]
        private async Task TogglePlayAsync()
        {
            if (CurrentTrack is null)
            {
                return;
            }

            if (IsPlaying)
            {
                if (EnableFade)
                {
                    IsFading = true;
                    await _audioPlayer.FadeOutAsync();
                    IsFading = false;
                    _audioPlayer.Pause();
                }
                else
                {
                    _audioPlayer.Pause();
                }
            }
            else
            {
                if (EnableFade)
                {
                    _audioPlayer.Volume = 0;
                    _audioPlayer.Resume();
                    _ = _audioPlayer.FadeInAsync(Volume);
                }
                else
                {
                    _audioPlayer.Volume = Volume;
                    _audioPlayer.Resume();
                }
            }
        }

        /// <summary>
        /// 跳转到指定进度（0.0~1.0）。
        /// </summary>
        /// <param name="progress">进度值，范围 0.0~1.0。</param>
        [RelayCommand]
        private void Seek(double progress)
        {
            if (Duration == TimeSpan.Zero)
            {
                return;
            }

            var target = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(progress, 0.0, 1.0)));
            _audioPlayer.Seek(target);
        }

        /// <summary>
        /// 下一首。
        /// </summary>
        [RelayCommand]
        private async Task NextAsync()
        {
            if (Playlist.Count == 0)
            {
                return;
            }

            var nextIndex = GetNextIndex();
            if (nextIndex < 0 || nextIndex >= Playlist.Count)
            {
                return;
            }

            CurrentIndex = nextIndex;
            await PlayFromQueueAsync(nextIndex);
        }

        /// <summary>
        /// 上一首。
        /// </summary>
        [RelayCommand]
        private async Task PreviousAsync()
        {
            if (Playlist.Count == 0)
            {
                return;
            }

            // 如果当前进度 > 3s，回到开头；否则上一首
            if (Position.TotalSeconds > 3)
            {
                _audioPlayer.Seek(TimeSpan.Zero);
                return;
            }

            var prevIndex = CurrentIndex - 1;
            if (prevIndex < 0)
            {
                prevIndex = Playlist.Count - 1;
            }

            if (prevIndex >= 0 && prevIndex < Playlist.Count)
            {
                CurrentIndex = prevIndex;
                await PlayFromQueueAsync(prevIndex);
            }
        }

        /// <summary>
        /// 循环切换播放模式：Loop → Single → Shuffle → Loop。
        /// </summary>
        [RelayCommand]
        private void CyclePlayMode()
        {
            PlayMode = PlayMode switch
            {
                PlayMode.Loop => PlayMode.Single,
                PlayMode.Single => PlayMode.Shuffle,
                PlayMode.Shuffle => PlayMode.Loop,
                _ => PlayMode.Loop,
            };
        }

        // ── P3: PlayVideoAsync ──

        /// <summary>
        /// 播放指定视频（核心方法，流程与移动端一致）。
        ///
        /// 流程：
        /// 1. 取消前一次请求（CancellationTokenSource）
        /// 2. 检查缓存 → 如果已缓存，使用本地文件
        /// 3. 未缓存：FetchVideoInfoAsync → FetchPlayUrlAsync(audioOnly: true)
        /// 4. 触发后台下载（CacheService）
        /// 5. 每步检查 CancellationToken，被取消则静默退出
        /// 6. 设置 MediaPlayer.Source
        /// 7. 播放
        /// </summary>
        public async Task PlayVideoAsync(VideoModel video)
        {
            ArgumentNullException.ThrowIfNull(video);

            // 1. 取消上一次播放请求，创建新的 CancellationTokenSource
            _playCts?.Cancel();
            _playCts?.Dispose();
            _playCts = new CancellationTokenSource();
            var ct = _playCts.Token;

            var bvid = video.Bvid;

            // 更新 CurrentTrack 立即反映 UI
            CurrentTrack = video;

            try
            {
                // 2. 检查缓存
                var cachedPath = await _cacheService.GetAudioPathAsync(bvid);
                System.Diagnostics.Debug.WriteLine($"[PlayerVM] PlayVideoAsync bvid={bvid} cachedPath={cachedPath ?? "(null)"}");

                // 3. 如果已被取消，静默退出
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                string playUrl;

                if (cachedPath is not null)
                {
                    // 使用缓存文件
                    playUrl = cachedPath;
                }
                else
                {
                    // 4. 未缓存：获取播放地址（传递 CancellationToken）
                    VideoDetailInfo detailInfo;
                    try
                    {
                        detailInfo = await _bilibiliClient.FetchVideoInfoAsync(bvid, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (BilibiliApiException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[PlayerVM] FetchVideoInfoAsync failed for {bvid}: {ex.Message}");
                        throw new BilibiliApiException($"获取视频信息失败：{ex.Message}", ex);
                    }

                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    PlayUrlInfo playUrlInfo;
                    try
                    {
                        playUrlInfo = await _bilibiliClient.FetchPlayUrlAsync(bvid, detailInfo.Cid, audioOnly: true, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (BilibiliApiException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[PlayerVM] FetchPlayUrlAsync failed for {bvid}: {ex.Message}");
                        throw new BilibiliApiException($"获取播放地址失败：{ex.Message}", ex);
                    }

                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    // 下载音频文件并等待完成，确保后续始终从本地缓存播放。
                    // Bilibili CDN 要求特定 HTTP 头（Referer / User-Agent），
                    // MediaPlayer 直接创建 MediaSource 时不携带这些头，因此必须先下载到本地。
                    var networkUrl = playUrlInfo.Url;
                    var downloadedPath = await _cacheService.DownloadAudioAsync(networkUrl, bvid, ct);

                    if (downloadedPath is null)
                    {
                        throw new BilibiliApiException("下载音频文件失败，请检查网络连接后重试。");
                    }

                    playUrl = downloadedPath;

                    // 更新 CurrentTrack 的 Cid 和时长信息
                    if (CurrentTrack?.Bvid == bvid)
                    {
                        CurrentTrack = new VideoModel
                        {
                            Bvid = bvid,
                            Aid = detailInfo.Aid,
                            Cid = detailInfo.Cid,
                            Title = detailInfo.Title,
                            Artist = detailInfo.Owner?.Name ?? video.Artist,
                            Mid = detailInfo.Owner?.Mid ?? 0,
                            Cover = detailInfo.Cover,
                            Duration = detailInfo.FormattedDuration,
                            DurationSeconds = detailInfo.Duration,
                            Views = CurrentTrack.Views,
                        };
                    }

                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                // 6. 设置播放源
                if (!Uri.TryCreate(playUrl, UriKind.Absolute, out var uri))
                {
                    throw new BilibiliApiException($"无效的播放地址: {playUrl}");
                }

                System.Diagnostics.Debug.WriteLine($"[PlayerVM] Playing uri={uri}");
                await _audioPlayer.PlayAsync(uri);

                // 7. 播放（PlayAsync 内部已调用 Play）
                IsPlaying = true;
                System.Diagnostics.Debug.WriteLine($"[PlayerVM] PlayAsync completed, IsPlaying={IsPlaying}");
            }
            catch (OperationCanceledException)
            {
                // 被取消的请求——静默退出，不视为错误
                System.Diagnostics.Debug.WriteLine(
                    $"[PlayerVM] PlayVideoAsync cancelled for {bvid}");
            }
            catch (BilibiliApiException ex)
            {
                // 业务异常（API 错误、无效地址等）——记录日志，不吞没
                System.Diagnostics.Debug.WriteLine(
                    $"[PlayerVM] PlayVideoAsync failed for {bvid}: {ex.Message}");

                // 由于此方法从命令调用，不重新抛出；UI 层可通过其它方式展示错误
            }
        }

        // ── 队列管理 ──

        /// <summary>
        /// 设置播放队列并从指定索引开始播放。
        /// </summary>
        public async Task SetPlaylistAsync(IList<VideoModel> videos, int startIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(videos);

            Playlist.Clear();
            foreach (var video in videos)
            {
                Playlist.Add(video);
            }

            QueueCount = Playlist.Count;

            if (Playlist.Count == 0)
            {
                return;
            }

            var index = Math.Clamp(startIndex, 0, Playlist.Count - 1);
            CurrentIndex = index;

            await PlayFromQueueAsync(index);
        }

        /// <summary>
        /// 向队列末尾添加一首曲目。
        /// </summary>
        public void AddToPlaylist(VideoModel video)
        {
            ArgumentNullException.ThrowIfNull(video);

            Playlist.Add(video);
            QueueCount = Playlist.Count;
        }

        /// <summary>
        /// 从队列中移除指定曲目。
        /// </summary>
        public void RemoveFromPlaylist(int index)
        {
            if (index < 0 || index >= Playlist.Count)
            {
                return;
            }

            Playlist.RemoveAt(index);
            QueueCount = Playlist.Count;

            // 调整 CurrentIndex
            if (CurrentIndex >= Playlist.Count)
            {
                CurrentIndex = Playlist.Count - 1;
            }
        }

        /// <summary>
        /// 清空播放队列。
        /// </summary>
        [RelayCommand]
        public void ClearPlaylist()
        {
            Playlist.Clear();
            QueueCount = 0;
            CurrentIndex = -1;

            _audioPlayer.Stop();
            CurrentTrack = null;
            IsPlaying = false;
            Position = TimeSpan.Zero;
            Duration = TimeSpan.Zero;
            Progress = 0;
        }

        // ── 私有方法 ──

        private async Task PlayFromQueueAsync(int index)
        {
            if (index < 0 || index >= Playlist.Count)
            {
                return;
            }

            var video = Playlist[index];
            await PlayVideoAsync(video);
        }

        private int GetNextIndex()
        {
            if (Playlist.Count == 0)
            {
                return -1;
            }

            return PlayMode switch
            {
                PlayMode.Single => CurrentIndex, // 单曲循环：不变
                PlayMode.Shuffle => GetShuffleNextIndex(),
                _ => GetLoopNextIndex(), // Loop
            };
        }

        private int GetLoopNextIndex()
        {
            var next = CurrentIndex + 1;
            return next >= Playlist.Count ? 0 : next;
        }

        private int GetShuffleNextIndex()
        {
            if (Playlist.Count <= 1)
            {
                return 0;
            }

            // 随机选一首，确保不与当前相同
            var random = new Random();
            int next;
            do
            {
                next = random.Next(Playlist.Count);
            } while (next == CurrentIndex);

            return next;
        }

        // ── AudioPlayerService 事件处理 ──

        private void OnIsPlayingChanged(object? sender, bool isPlaying)
        {
            PostOnUIThread(() => IsPlaying = isPlaying);
        }

        private void OnPositionChanged(object? sender, TimeSpan position)
        {
            PostOnUIThread(() =>
            {
                Position = position;

                if (!IsUserSeeking && Duration > TimeSpan.Zero)
                {
                    Progress = Math.Clamp(
                        (double)position.Ticks / Duration.Ticks, 0.0, 1.0);
                }
            });
        }

        private void OnDurationChanged(object? sender, TimeSpan duration)
        {
            PostOnUIThread(() =>
            {
                Duration = duration;

                if (duration > TimeSpan.Zero && Position > TimeSpan.Zero)
                {
                    Progress = Math.Clamp(
                        (double)Position.Ticks / duration.Ticks, 0.0, 1.0);
                }
            });
        }

        private void OnBufferingProgressChanged(object? sender, double progress)
        {
            PostOnUIThread(() => BufferingProgress = progress);
        }

        private void OnMediaEnded(object? sender, EventArgs e)
        {
            PostOnUIThread(() =>
            {
                IsPlaying = false;
                Position = TimeSpan.Zero;
                Progress = 0;
            });

            // 根据播放模式决定下一首
            if (Playlist.Count > 0 && CurrentIndex >= 0)
            {
                _ = NextAsync();
            }
        }

        private void OnMediaFailed(object? sender, string errorMessage)
        {
            PostOnUIThread(() => IsPlaying = false);
            System.Diagnostics.Debug.WriteLine(
                $"[PlayerVM] Playback failed: {errorMessage}");
        }

        // ── UI 线程调度辅助（基于 SynchronizationContext，避免 Window.Current 空引用） ──

        private void PostOnUIThread(Action action)
        {
            _syncContext.Post(_ => action(), null);
        }

        // ── [ObservableProperty] partial methods ──

        /// <summary>
        /// 音量变化时同步到 AudioPlayerService。
        /// </summary>
        partial void OnVolumeChanged(double value)
        {
            if (!IsFading)
            {
                _audioPlayer.Volume = value;
            }
        }
    }
}
