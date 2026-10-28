#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using bilibili_music_player_windows.Models;

namespace bilibili_music_player_windows;

public sealed partial class MainWindow
{
    private bool _playbackFixtureActive;
    private double _volumeBeforePlaybackFixture;
    private PlayMode _playModeBeforePlaybackFixture;
    private async Task InspectPlaybackAsync(UiInspectionRequest request)
    {
        if (request.PlaybackFixture == "restore")
        {
            if (_playbackFixtureActive)
            {
                _playerViewModel.ClearPlaylist();
                _playerViewModel.SetInspectionSources(null);
                _playerViewModel.Volume = _volumeBeforePlaybackFixture;
                _playerViewModel.PlayMode = _playModeBeforePlaybackFixture;
                _playbackFixtureActive = false;
            }
        }
        else if (request.PlaybackFixture is { } fixture)
        {
            if (_playerViewModel.CurrentTrack is not null || _playerViewModel.Playlist.Count > 0)
                throw new InvalidOperationException("Playback inspection requires an empty queue; existing playback is preserved.");
            _volumeBeforePlaybackFixture = _playerViewModel.Volume;
            _playModeBeforePlaybackFixture = _playerViewModel.PlayMode;
            _playerViewModel.Volume = 0; // Native decoding/transport tests must not produce unexpected sound.
            _playbackFixtureActive = true;
            if (fixture is "sample" or "queue")
            {
                var path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "playback-inspection-silence.wav");
                using (var file = File.Create(path))
                using (var writer = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true))
                {
                    const int bytes = 16000 * 2 * 120;
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
                    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
                    writer.Write((short)2); writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                    file.SetLength(44 + bytes);
                }
                _playerViewModel.SetInspectionSources(new Dictionary<string, Uri>
                { ["inspection-a"] = new(path), ["inspection-b"] = new(path), ["inspection-c"] = new(path), ["inspection-d"] = new(path) });
                var covers = _libraryViewModel.Document.Media.Select(m => m.Cover).Where(c => !string.IsNullOrEmpty(c)).ToArray();
                string Cover(int i) => covers.Length > 0 ? covers[i % covers.Length] : "";
                await _playerViewModel.SetPlaylistAsync(new[]
                {
                    new VideoModel { Bvid = "inspection-a", Title = "播放检查 · 第一首较长的中文标题与 English title", Artist = "本机静音音频 · 不修改收藏", DurationSeconds = 120, Cover = Cover(0) },
                    new VideoModel { Bvid = "inspection-b", Title = "播放检查 · 第二首", Artist = "上一首、下一首和进度测试", DurationSeconds = 120, Cover = Cover(1) },
                    new VideoModel { Bvid = "inspection-c", Title = "播放检查 · 第三首", Artist = "打乱只改变接下来播放的顺序", DurationSeconds = 120, Cover = Cover(2) },
                    new VideoModel { Bvid = "inspection-d", Title = "播放检查 · 第四首", Artist = "单曲循环时显示为灰色", DurationSeconds = 120, Cover = Cover(0) },
                }.Take(fixture == "queue" ? 4 : 2).ToArray());
            }
            else if (fixture == "online")
            {
                if (request.PlaybackBvid is not { } bvid || !System.Text.RegularExpressions.Regex.IsMatch(bvid, "^BV[A-Za-z0-9]{10}$"))
                    throw new ArgumentException("Supply a valid BVID for online playback inspection.");
                await _playerViewModel.SetPlaylistAsync(new[] { new VideoModel { Bvid = bvid, Title = "在线音频播放检查" } });
            }
            else throw new ArgumentException("Unknown playback fixture.");
        }
        if (request.PlaybackAction is { } action)
        {
            switch (action)
            {
                case "play": await _playerViewModel.PlayCommand.ExecuteAsync(null); break;
                case "pause": await _playerViewModel.PauseCommand.ExecuteAsync(null); break;
                case "next": await _playerViewModel.NextCommand.ExecuteAsync(null); break;
                case "previous": await _playerViewModel.PreviousCommand.ExecuteAsync(null); break;
                case "mute": _playerViewModel.ToggleMuteCommand.Execute(null); break;
                case "shuffle": _playerViewModel.ShuffleUpcomingCommand.Execute(null); break;
                case "mode": _playerViewModel.CyclePlayModeCommand.Execute(null); break;
                default: throw new ArgumentException("Unknown playback action.");
            }
        }
        if (request.PlaybackProgress is { } progress)
        {
            if (!double.IsFinite(progress) || progress is < 0 or > 1) throw new ArgumentException("Progress must be between 0 and 1.");
            _playerViewModel.SeekCommand.Execute(progress);
        }
        if (request.PlaybackPreview is { } preview)
        {
            if (!_playbackFixtureActive) throw new InvalidOperationException("Seek preview inspection requires a playback fixture.");
            if (!double.IsFinite(preview) || preview is < 0 or > 1) throw new ArgumentException("Preview must be between 0 and 1.");
            // Inspect the exact endpoints without seeking the decoder to EOF
            // (which may clamp its position or advance the queue).
            _playerViewModel.BeginSeek();
            _playerViewModel.PreviewSeek(preview);
        }
    }
}
#endif
