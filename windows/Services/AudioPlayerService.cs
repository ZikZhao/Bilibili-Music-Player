using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Microsoft.UI.Xaml;

namespace bilibili_music_player_windows.Services
{
    /// <summary>
    /// 音频播放引擎，封装 WinUI <see cref="Windows.Media.Playback.MediaPlayer"/>。
    /// 单例注册到 DI，作为 <c>PlayerViewModel</c> 的唯一音频源。
    ///
    /// ▸ 遵循移动端 "no adapters" 原则：直接使用 MediaPlayer。
    /// ▸ 通过 <see cref="INotifyPropertyChanged"/> 暴露实时属性，供 ViewModel 和 XAML 绑定。
    /// </summary>
    public sealed class AudioPlayerService : INotifyPropertyChanged, IDisposable
    {
        private readonly MediaPlayer _mediaPlayer;
        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _fadeTimer;

        private bool _isDisposed;

        // ── 淡入/淡出状态 ──
        private double _fadeStartVolume;
        private double _fadeTargetVolume;
        private int _fadeStep;
        private const int FadeTotalSteps = 16;    // 800ms ÷ 50ms
        private static readonly TimeSpan FadeInterval = TimeSpan.FromMilliseconds(50);

        /// <summary>淡入/淡出进行中。</summary>
        public bool IsFading { get; private set; }

        /// <summary>淡入/淡出完成事件（ViewModel 可订阅）。</summary>
        public event EventHandler? FadeCompleted;

        /// <summary>当前播放位置（只读，通过 <see cref="PositionChanged"/> 订阅实时更新）。</summary>
        public TimeSpan Position => _mediaPlayer.PlaybackSession.Position;

        /// <summary>媒体时长（0 表示未加载或直播）。</summary>
        public TimeSpan Duration => _mediaPlayer.PlaybackSession.NaturalDuration;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => _mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

        /// <summary>缓冲进度（0.0~1.0）。</summary>
        public double BufferingProgress => _mediaPlayer.PlaybackSession.BufferingProgress;

        /// <summary>音量（0.0~1.0）。</summary>
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

        // ── Events (ViewModel 通过它们监听播放器状态) ──

        /// <summary>播放/暂停状态变化。</summary>
        public event EventHandler<bool>? IsPlayingChanged;

        /// <summary>位置实时更新（约 250ms 间隔）。</summary>
        public event EventHandler<TimeSpan>? PositionChanged;

        /// <summary>时长确定后触发。</summary>
        public event EventHandler<TimeSpan>? DurationChanged;

        /// <summary>缓冲进度更新。</summary>
        public event EventHandler<double>? BufferingProgressChanged;

        /// <summary>当前媒体播放完毕（自然结束）。</summary>
        public event EventHandler? MediaEnded;

        /// <summary>播放出错。</summary>
        public event EventHandler<string>? MediaFailed;

        // ── Constructor ──

        public AudioPlayerService()
        {
            _mediaPlayer = new MediaPlayer();

            // ── 订阅 PlaybackSession 事件 ──
            _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
            _mediaPlayer.PlaybackSession.BufferingProgressChanged += OnBufferingProgressChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;

            // ── 定时器轮询 Position（避免 PositionChanged 高频触发压垮 UI 线程） ──
            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _positionTimer.Tick += OnPositionTimerTick;

            // ── 淡入/淡出定时器 ──
            _fadeTimer = new DispatcherTimer
            {
                Interval = FadeInterval,
            };
            _fadeTimer.Tick += OnFadeTimerTick;
        }

        // ── Public API ──

        /// <summary>
        /// 播放指定 URI 的音频。Bilibili CDN URL 已包含签名认证，无需额外请求头。
        /// 如需自定义 HTTP 头（如防盗链），可通过 <paramref name="headers"/> 传入，
        /// 但当前实现使用 <see cref="MediaSource.CreateFromUri(Uri)"/> 直连。
        /// </summary>
        public async Task PlayAsync(Uri uri, IReadOnlyDictionary<string, string>? headers = null)
        {
            ThrowIfDisposed();

            // 未来增强：如需自定义请求头，可改用 HttpClient 下载流后通过
            // MediaSource.CreateFromStream(IRandomAccessStream) 创建源。
            // 当前 Bilibili CDN URL 无需自定义头即可播放。
            _mediaPlayer.Source = MediaSource.CreateFromUri(uri);

            _mediaPlayer.Play();
            StartPositionTimer();

            await Task.CompletedTask;
        }

        /// <summary>
        /// 暂停播放。
        /// </summary>
        public void Pause()
        {
            ThrowIfDisposed();
            _mediaPlayer.Pause();
            StopPositionTimer();
        }

        /// <summary>
        /// 恢复播放（MediaPlayer.Resume 会保持当前 Source）。
        /// </summary>
        public void Resume()
        {
            ThrowIfDisposed();
            _mediaPlayer.Play();
            StartPositionTimer();
        }

        /// <summary>
        /// 跳转到指定位置。
        /// </summary>
        public void Seek(TimeSpan position)
        {
            ThrowIfDisposed();

            if (Duration == TimeSpan.Zero)
            {
                return;
            }

            var clamped = new TimeSpan(
                Math.Clamp(position.Ticks, 0, Duration.Ticks));
            _mediaPlayer.PlaybackSession.Position = clamped;
        }

        /// <summary>
        /// 停止播放并释放媒体源。
        /// </summary>
        public void Stop()
        {
            ThrowIfDisposed();
            _mediaPlayer.Source = null;
            StopPositionTimer();

            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Duration));
        }

        /// <summary>
        /// 设置音量（0.0~1.0）。
        /// </summary>
        public void SetVolume(double volume)
        {
            Volume = volume;
        }

        // ── P5: 淡入 / 淡出 ──

        /// <summary>
        /// 线性淡入：从 0 到 <paramref name="targetVolume"/>，历时 800ms。
        /// </summary>
        public void FadeIn(double targetVolume = 1.0)
        {
            ThrowIfDisposed();
            if (IsFading) return;

            IsFading = true;
            _fadeStep = 0;
            _fadeStartVolume = 0;
            _fadeTargetVolume = Math.Clamp(targetVolume, 0.0, 1.0);

            _mediaPlayer.Volume = 0;
            _fadeTimer.Start();
        }

        /// <summary>
        /// 抛物线淡出：从当前音量到 0，历时 800ms（曲线：t²）。
        /// </summary>
        public void FadeOut()
        {
            ThrowIfDisposed();
            if (IsFading) return;

            IsFading = true;
            _fadeStep = 0;
            _fadeStartVolume = _mediaPlayer.Volume;
            _fadeTargetVolume = 0;

            _fadeTimer.Start();
        }

        /// <summary>
        /// 立即停止淡入/淡出。
        /// </summary>
        public void CancelFade()
        {
            if (_fadeTimer.IsEnabled)
            {
                _fadeTimer.Stop();
            }

            IsFading = false;
        }

        private void OnFadeTimerTick(object? sender, object e)
        {
            _fadeStep++;

            double newVolume;
            if (_fadeTargetVolume > _fadeStartVolume)
            {
                // 淡入：线性 (step / total)
                newVolume = _fadeStartVolume + (_fadeTargetVolume - _fadeStartVolume) * (_fadeStep / (double)FadeTotalSteps);
            }
            else
            {
                // 淡出：抛物线 t²
                var t = _fadeStep / (double)FadeTotalSteps;
                newVolume = _fadeStartVolume * (1.0 - t * t);
            }

            _mediaPlayer.Volume = Math.Clamp(newVolume, 0.0, 1.0);

            if (_fadeStep >= FadeTotalSteps)
            {
                _fadeTimer.Stop();
                _mediaPlayer.Volume = _fadeTargetVolume;
                IsFading = false;
                FadeCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Timer ──

        private void StartPositionTimer()
        {
            if (!_positionTimer.IsEnabled)
            {
                _positionTimer.Start();
            }
        }

        private void StopPositionTimer()
        {
            if (_positionTimer.IsEnabled)
            {
                _positionTimer.Stop();
            }
        }

        private void OnPositionTimerTick(object? sender, object e)
        {
            var pos = _mediaPlayer.PlaybackSession.Position;
            OnPropertyChanged(nameof(Position));
            PositionChanged?.Invoke(this, pos);
        }

        // ── Event Handlers ──

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            var isPlaying = sender.PlaybackState == MediaPlaybackState.Playing;

            if (isPlaying)
            {
                StartPositionTimer();
            }
            else
            {
                StopPositionTimer();
            }

            OnPropertyChanged(nameof(IsPlaying));
            IsPlayingChanged?.Invoke(this, isPlaying);
        }

        private void OnNaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
            var duration = sender.NaturalDuration;
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
            StopPositionTimer();
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            StopPositionTimer();
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
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _positionTimer.Stop();
            _positionTimer.Tick -= OnPositionTimerTick;

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
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AudioPlayerService));
            }
        }
    }
}
