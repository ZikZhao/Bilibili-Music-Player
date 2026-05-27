using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace bilibili_music_player_windows.Services
{
    /// <summary>
    /// 音频播放引擎，封装 WinUI <see cref="Windows.Media.Playback.MediaPlayer"/>。
    ///
    /// ▸ 淡入/淡出通过 TaskCompletionSource 暴露为可等待操作，消除事件闭包泄漏。
    /// ▸ 音量曲线使用纯函数计算（基于 elapsed/total），不依赖可变步长状态。
    /// ▸ IsFading 使用 Volatile 语义确保多核可见性。
    /// ▸ SetVolume 已移除——直接使用 Volume 属性。
    /// </summary>
    public sealed class AudioPlayerService : INotifyPropertyChanged, IDisposable
    {
        private readonly MediaPlayer _mediaPlayer;

        // ── PeriodicTimer 异步循环 ──
        private readonly CancellationTokenSource _serviceCts = new();
        private PeriodicTimer? _positionTimer;
        private PeriodicTimer? _fadeTimer;
        private Task? _positionLoopTask;
        private Task? _fadeLoopTask;

        // ── 淡入/淡出 TCS（取代事件订阅，消除闭包泄漏） ──
        private TaskCompletionSource? _fadeTcs;
        private readonly object _fadeLock = new();

        // ── 常量 ──
        private const int FadeDurationMs = 800;
        private static readonly TimeSpan FadeInterval = TimeSpan.FromMilliseconds(50);

        private bool _isDisposed;

        /// <summary>淡入/淡出进行中（Volatile 语义确保多核可见性）。</summary>
        public bool IsFading => Volatile.Read(ref _isFadingField);
        private bool _isFadingField;

        /// <summary>当前播放位置。</summary>
        public TimeSpan Position => _mediaPlayer.PlaybackSession.Position;

        /// <summary>媒体时长（0 表示未加载或直播）。</summary>
        public TimeSpan Duration => _mediaPlayer.PlaybackSession.NaturalDuration;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => _mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        /// <summary>缓冲进度（0.0~1.0）。</summary>
        public double BufferingProgress => _mediaPlayer.PlaybackSession.BufferingProgress;

        /// <summary>音量（0.0~1.0）。直接设置即可，无需 SetVolume 包装。</summary>
        public double Volume
        {
            get => _mediaPlayer.Volume;
            set
            {
                var clamped = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_mediaPlayer.Volume - clamped) > 0.001)
                {
                    _mediaPlayer.Volume = clamped;
                    OnPropertyChanged();
                }
            }
        }

        // ── Events ──

        public event EventHandler<bool>? IsPlayingChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimeSpan>? DurationChanged;
        public event EventHandler<double>? BufferingProgressChanged;
        public event EventHandler? MediaEnded;
        public event EventHandler<string>? MediaFailed;

        // ── Constructor ──

        public AudioPlayerService()
        {
            _mediaPlayer = new MediaPlayer();

            _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
            _mediaPlayer.PlaybackSession.BufferingProgressChanged += OnBufferingProgressChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        // ── Public API ──

        public Task PlayAsync(Uri uri, IReadOnlyDictionary<string, string>? headers = null)
        {
            ThrowIfDisposed();
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] PlayAsync uri={uri}");
            _mediaPlayer.Source = MediaSource.CreateFromUri(uri);
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] Source set, calling Play()");
            _mediaPlayer.Play();
            StartPositionLoop();
            return Task.CompletedTask;
        }

        public void Pause()
        {
            ThrowIfDisposed();
            _mediaPlayer.Pause();
            StopPositionLoop();
        }

        public void Resume()
        {
            ThrowIfDisposed();
            _mediaPlayer.Play();
            StartPositionLoop();
        }

        public void Seek(TimeSpan position)
        {
            ThrowIfDisposed();
            if (Duration == TimeSpan.Zero) return;
            var clamped = new TimeSpan(Math.Clamp(position.Ticks, 0, Duration.Ticks));
            _mediaPlayer.PlaybackSession.Position = clamped;
        }

        public void Stop()
        {
            ThrowIfDisposed();
            _mediaPlayer.Source = null;
            StopPositionLoop();
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Duration));
        }

        // ── 淡入 / 淡出（异步可等待，取代 FadeCompleted 事件 + 闭包） ──

        /// <summary>
        /// 线性淡入：从 0 到 <paramref name="targetVolume"/>，历时 800ms。
        /// 返回的 Task 在淡入完成后完成。
        /// </summary>
        public Task FadeInAsync(double targetVolume = 1.0)
        {
            ThrowIfDisposed();
            if (IsFading) return Task.CompletedTask;

            var tcs = CreateFadeTcs();

            Volatile.Write(ref _isFadingField, true);
            _mediaPlayer.Volume = 0;

            var endVolume = Math.Clamp(targetVolume, 0.0, 1.0);
            StartFadeLoop(startVolume: 0.0, endVolume, tcs);

            return tcs.Task;
        }

        /// <summary>
        /// 抛物线淡出：从当前音量到 0，历时 800ms（曲线：t²）。
        /// 返回的 Task 在淡出完成后完成。
        /// </summary>
        public Task FadeOutAsync()
        {
            ThrowIfDisposed();
            if (IsFading) return Task.CompletedTask;

            var tcs = CreateFadeTcs();

            Volatile.Write(ref _isFadingField, true);

            var startVolume = _mediaPlayer.Volume;
            StartFadeLoop(startVolume, endVolume: 0.0, tcs);

            return tcs.Task;
        }

        /// <summary>立即停止淡入/淡出，TCS 转入 Canceled 状态。</summary>
        public void CancelFade()
        {
            TaskCompletionSource? tcs;
            lock (_fadeLock)
            {
                tcs = _fadeTcs;
                _fadeTcs = null;
            }

            StopFadeLoop();
            Volatile.Write(ref _isFadingField, false);
            tcs?.TrySetCanceled();
        }

        private TaskCompletionSource CreateFadeTcs()
        {
            var tcs = new TaskCompletionSource();

            lock (_fadeLock)
            {
                _fadeTcs?.TrySetCanceled();
                _fadeTcs = tcs;
            }

            // service 整体取消时自动取消本次 fade
            _ = _serviceCts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

            return tcs;
        }

        // ── Position 异步循环 ──

        private void StartPositionLoop()
        {
            if (_positionLoopTask is not null && !_positionLoopTask.IsCompleted)
            {
                return;
            }

            _positionTimer?.Dispose();
            _positionTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            _positionLoopTask = RunPositionLoopAsync(_serviceCts.Token);
        }

        private void StopPositionLoop()
        {
            _positionTimer?.Dispose();
            _positionTimer = null;
        }

        private async Task RunPositionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var timer = _positionTimer;
                    if (timer is null || !await timer.WaitForNextTickAsync(ct))
                    {
                        break;
                    }

                    var pos = _mediaPlayer.PlaybackSession.Position;
                    OnPropertyChanged(nameof(Position));
                    PositionChanged?.Invoke(this, pos);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // ── Fade 异步循环（纯曲线计算，无可变步长状态） ──

        private void StartFadeLoop(double startVolume, double endVolume, TaskCompletionSource tcs)
        {
            _fadeTimer?.Dispose();
            _fadeTimer = new PeriodicTimer(FadeInterval);
            var startTime = Environment.TickCount64;
            _fadeLoopTask = RunFadeLoopAsync(startVolume, endVolume, startTime, tcs, _serviceCts.Token);
        }

        private void StopFadeLoop()
        {
            _fadeTimer?.Dispose();
            _fadeTimer = null;
        }

        /// <summary>
        /// 纯曲线淡入/淡出。音量仅由 <c>elapsed / totalDuration</c> 比例计算，
        /// 不依赖可变 <c>_fadeStep</c> 字段，避免系统调度抖动导致的音量卡顿。
        /// </summary>
        private async Task RunFadeLoopAsync(
            double startVolume, double endVolume, long startTime,
            TaskCompletionSource tcs, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var timer = _fadeTimer;
                    if (timer is null || !await timer.WaitForNextTickAsync(ct))
                    {
                        break;
                    }

                    var elapsed = Environment.TickCount64 - startTime;
                    var t = Math.Min(elapsed / (double)FadeDurationMs, 1.0);

                    var newVolume = endVolume > startVolume
                        ? startVolume + (endVolume - startVolume) * t           // 淡入：线性
                        : startVolume * (1.0 - t * t);                         // 淡出：抛物线 t²

                    _mediaPlayer.Volume = Math.Clamp(newVolume, 0.0, 1.0);

                    if (t >= 1.0)
                    {
                        _mediaPlayer.Volume = endVolume;
                        CompleteFade(tcs);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // TCS 已在 CancelFade 中处理
            }
            finally
            {
                _fadeTimer?.Dispose();
                _fadeTimer = null;
            }
        }

        private void CompleteFade(TaskCompletionSource tcs)
        {
            lock (_fadeLock)
            {
                if (_fadeTcs == tcs)
                {
                    _fadeTcs = null;
                }
            }

            Volatile.Write(ref _isFadingField, false);
            tcs.TrySetResult();
        }

        // ── Event Handlers ──

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            var isPlaying = sender.PlaybackState == MediaPlaybackState.Playing;
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] PlaybackStateChanged state={sender.PlaybackState} pos={sender.Position} dur={sender.NaturalDuration}");

            if (isPlaying) StartPositionLoop();
            else StopPositionLoop();

            OnPropertyChanged(nameof(IsPlaying));
            IsPlayingChanged?.Invoke(this, isPlaying);
        }

        private void OnNaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
            var duration = sender.NaturalDuration;
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] NaturalDurationChanged duration={duration}");
            OnPropertyChanged(nameof(Duration));
            DurationChanged?.Invoke(this, duration);
        }

        private void OnBufferingProgressChanged(MediaPlaybackSession sender, object args)
        {
            var progress = sender.BufferingProgress;
            OnPropertyChanged(nameof(BufferingProgress));
            BufferingProgressChanged?.Invoke(this, progress);
        }

        private void OnMediaEnded(MediaPlayer sender, object args)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] MediaEnded");
            StopPositionLoop();
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] MediaFailed error={args.Error} errorMessage={args.ErrorMessage} extendedError={args.ExtendedErrorCode}");
            StopPositionLoop();
            MediaFailed?.Invoke(this, args.ErrorMessage);
        }

        // ── INotifyPropertyChanged ──

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ── IDisposable ──

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            CancelFade();
            _serviceCts.Cancel();
            _serviceCts.Dispose();

            _positionTimer?.Dispose();
            _fadeTimer?.Dispose();

            _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
            _mediaPlayer.PlaybackSession.BufferingProgressChanged -= OnBufferingProgressChanged;
            _mediaPlayer.MediaEnded -= OnMediaEnded;
            _mediaPlayer.MediaFailed -= OnMediaFailed;

            _mediaPlayer.Source = null;
            _mediaPlayer.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPlayerService));
        }
    }
}
