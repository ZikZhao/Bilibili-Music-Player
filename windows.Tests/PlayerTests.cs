using System.Threading;
using bilibili_music_player_windows.Models;
using bilibili_music_player_windows.Services;
using bilibili_music_player_windows.ViewModels;

internal static class PlayerTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All => new (string, Func<Task>)[]
    {
        ("queued tracks enable the transport and play without an existing current track", async () =>
        {
            using var rig = new Rig();
            Check(!rig.Vm.TogglePlayCommand.CanExecute(null) && !rig.Vm.CanSeek);
            rig.Vm.AddToPlaylist(Track("a"));
            Check(rig.Vm.TogglePlayCommand.CanExecute(null));
            await rig.Vm.TogglePlayCommand.ExecuteAsync(null);
            Check(rig.Vm.IsPlaying && rig.Vm.CurrentIndex == 0 && rig.Vm.CanSeek);
        }),
        ("a late source resolution cannot replace a newer playing song", async () =>
        {
            var late = new TaskCompletionSource<ResolvedAudioSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var rig = new Rig(t => t.Bvid == "a" ? late.Task : Task.FromResult(Source(t)));
            var old = rig.Vm.PlayVideoAsync(Track("a"));
            await rig.Vm.PlayVideoAsync(Track("b"));
            late.SetResult(Source(Track("a"))); await old;
            Check(rig.Vm.CurrentTrack?.Bvid == "b" && rig.Engine.OpenCount == 1 && rig.Vm.ErrorMessage is null);
        }),
        ("clearing the queue cancels a pending load and cannot start ghost audio", async () =>
        {
            var late = new TaskCompletionSource<ResolvedAudioSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var rig = new Rig(_ => late.Task);
            var load = rig.Vm.SetPlaylistAsync(new[] { Track("a") });
            rig.Vm.ClearPlaylist();
            late.SetResult(Source(Track("a"))); await load;
            Check(rig.Engine.OpenCount == 0 && rig.Vm.CurrentTrack is null && !rig.Vm.IsPlaying && !rig.Vm.IsLoading);
        }),
        ("pausing during preparation opens the song without starting playback", async () =>
        {
            var late = new TaskCompletionSource<ResolvedAudioSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var rig = new Rig(_ => late.Task);
            var load = rig.Vm.PlayVideoAsync(Track("a"));
            await rig.Vm.PauseCommand.ExecuteAsync(null);
            late.SetResult(Source(Track("a"))); await load;
            Check(!rig.Vm.IsPlaying && !rig.Vm.IsLoading && rig.Vm.CanSeek && rig.Engine.ResumeCount == 0);
        }),
        ("opening a video preview cancels pending audio and leaves it retryable", async () =>
        {
            var late = new TaskCompletionSource<ResolvedAudioSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            var block = true;
            using var rig = new Rig(t => block ? late.Task : Task.FromResult(Source(t)));
            var load = rig.Vm.PlayVideoAsync(Track("a"));
            await rig.Vm.PauseForPreviewAsync();
            late.SetResult(Source(Track("a"))); await load;
            Check(!rig.Vm.IsPlaying && rig.Engine.OpenCount == 0);
            block = false; await rig.Vm.TogglePlayCommand.ExecuteAsync(null);
            Check(rig.Vm.IsPlaying && rig.Vm.CurrentTrack?.Bvid == "a");
        }),
        ("source failures are visible and the play button retries the same song", async () =>
        {
            var fail = true;
            using var rig = new Rig(t => fail ? Task.FromException<ResolvedAudioSource>(new IOException("fixture failure")) : Task.FromResult(Source(t)));
            await rig.Vm.PlayVideoAsync(Track("a"));
            Check(rig.Vm.ErrorMessage?.Contains("fixture failure") == true && rig.Vm.PlayPauseLabel == "重试播放");
            fail = false; await rig.Vm.TogglePlayCommand.ExecuteAsync(null);
            Check(rig.Vm.IsPlaying && rig.Vm.ErrorMessage is null && rig.Vm.CurrentTrack?.Bvid == "a");
        }),
        ("decoder failures release seeking and retry reloads the media", async () =>
        {
            using var rig = new Rig(); await rig.Vm.PlayVideoAsync(Track("a"));
            rig.Engine.Emit(error: "codec failure");
            Check(!rig.Vm.CanSeek && !rig.Vm.IsPlaying && rig.Vm.ErrorMessage is not null);
            await rig.Vm.TogglePlayCommand.ExecuteAsync(null);
            Check(rig.Engine.OpenCount == 2 && rig.Vm.IsPlaying);
        }),
        ("seek preview follows the thumb and commits only once when released", async () =>
        {
            using var rig = new Rig(); await rig.Vm.PlayVideoAsync(Track("a"));
            rig.Vm.BeginSeek(); rig.Vm.PreviewSeek(.8);
            rig.Engine.Position = TimeSpan.FromSeconds(30); rig.Engine.Emit();
            Check(rig.Vm.Progress == .8 && rig.Vm.TimeText == "03:12 / 04:00" && rig.Engine.SeekCount == 0);
            rig.Vm.EndSeek(.8); rig.Vm.EndSeek(.8);
            Check(rig.Engine.SeekCount == 1 && rig.Engine.Position.TotalSeconds == 192 && !rig.Vm.IsUserSeeking);
        }),
        ("a drag from an old song cannot seek the newly selected song", async () =>
        {
            using var rig = new Rig(); await rig.Vm.PlayVideoAsync(Track("a"));
            rig.Vm.BeginSeek(); rig.Vm.PreviewSeek(.8);
            await rig.Vm.PlayVideoAsync(Track("b")); rig.Vm.EndSeek(.8);
            Check(rig.Engine.SeekCount == 0 && rig.Engine.Position == TimeSpan.Zero);
        }),
        ("cancelled seeks restore the actual position and invalid ratios never reach the engine", async () =>
        {
            using var rig = new Rig(); await rig.Vm.PlayVideoAsync(Track("a"));
            rig.Engine.Position = TimeSpan.FromSeconds(60); rig.Engine.Emit();
            rig.Vm.BeginSeek(); rig.Vm.PreviewSeek(.9); rig.Vm.EndSeek(.9, false);
            Check(rig.Vm.Progress == .25 && rig.Engine.SeekCount == 0);
            rig.Vm.SeekCommand.Execute(double.NaN);
            Check(rig.Engine.SeekCount == 0);
            rig.Vm.SeekCommand.Execute(2d);
            Check(rig.Engine.Position == TimeSpan.FromSeconds(240));
        }),
        ("manual next advances in single-repeat mode while natural ending repeats", async () =>
        {
            using var rig = new Rig(); await rig.Vm.SetPlaylistAsync(new[] { Track("a"), Track("b") });
            rig.Vm.PlayMode = PlayMode.Single;
            await rig.Vm.NextCommand.ExecuteAsync(null);
            Check(rig.Vm.CurrentIndex == 1);
            rig.Engine.Emit(ended: true);
            Check(rig.Vm.CurrentIndex == 1 && rig.Engine.OpenCount == 3);
            rig.Vm.PlayMode = PlayMode.Loop; rig.Engine.Emit(ended: true);
            Check(rig.Vm.CurrentIndex == 0);
        }),
        ("skipping a paused queue changes songs without resuming playback", async () =>
        {
            using var rig = new Rig(); await rig.Vm.SetPlaylistAsync(new[] { Track("a"), Track("b") });
            await rig.Vm.PauseCommand.ExecuteAsync(null);
            await rig.Vm.NextCommand.ExecuteAsync(null);
            Check(rig.Vm.CurrentIndex == 1 && !rig.Vm.IsPlaying);
            await rig.Vm.PreviousCommand.ExecuteAsync(null);
            Check(rig.Vm.CurrentIndex == 0 && !rig.Vm.IsPlaying);
        }),
        ("previous restarts after three seconds and otherwise wraps through the queue", async () =>
        {
            using var rig = new Rig(); await rig.Vm.SetPlaylistAsync(new[] { Track("a"), Track("b") });
            rig.Engine.Position = TimeSpan.FromSeconds(10); rig.Engine.Emit();
            await rig.Vm.PreviousCommand.ExecuteAsync(null);
            Check(rig.Engine.SeekCount == 1 && rig.Vm.CurrentIndex == 0);
            await rig.Vm.PreviousCommand.ExecuteAsync(null);
            Check(rig.Vm.CurrentIndex == 1);
        }),
        ("removing earlier queue entries preserves the current song and paused replacement stays paused", async () =>
        {
            using var rig = new Rig(); await rig.Vm.SetPlaylistAsync(new[] { Track("a"), Track("b"), Track("c") }, 1);
            rig.Vm.RemoveFromPlaylist(0);
            Check(rig.Vm.CurrentIndex == 0 && rig.Vm.CurrentTrack?.Bvid == "b" && rig.Engine.OpenCount == 1);
            await rig.Vm.PauseCommand.ExecuteAsync(null); rig.Vm.RemoveFromPlaylist(0);
            Check(rig.Vm.CurrentTrack?.Bvid == "c" && !rig.Vm.IsPlaying && rig.Vm.QueueCount == 1);
        }),
        ("an empty replacement stops playback and mute restores the prior audible volume", async () =>
        {
            using var rig = new Rig(); await rig.Vm.PlayVideoAsync(Track("a"));
            rig.Vm.Volume = .42; rig.Vm.ToggleMuteCommand.Execute(null);
            Check(rig.Vm.IsMuted && rig.Engine.Volume == 0);
            rig.Vm.ToggleMuteCommand.Execute(null);
            Check(!rig.Vm.IsMuted && rig.Vm.Volume == .42);
            rig.Vm.Volume = double.NaN; Check(rig.Vm.Volume == 0 && rig.Engine.Volume == 0);
            await rig.Vm.SetPlaylistAsync(Array.Empty<VideoModel>());
            Check(!rig.Vm.IsPlaying && rig.Vm.CurrentTrack is null && !rig.Vm.CanSeek);
        }),
        ("old native callbacks and duplicate end notifications cannot affect a new source", async () =>
        {
            using var rig = new Rig(); await rig.Vm.SetPlaylistAsync(new[] { Track("a"), Track("b") });
            var old = rig.Engine.SourceId;
            await rig.Vm.NextCommand.ExecuteAsync(null);
            rig.Engine.Emit(error: "old decoder failure", sourceId: old);
            Check(rig.Vm.IsPlaying && rig.Vm.ErrorMessage is null);
            old = rig.Engine.SourceId;
            rig.Engine.Emit(ended: true, sourceId: old); rig.Engine.Emit(ended: true, sourceId: old);
            Check(rig.Engine.OpenCount == 3 && rig.Vm.CurrentTrack?.Bvid == "a");
        }),
        ("an old fade completion cannot pause a newly selected song", async () =>
        {
            using var rig = new Rig(); await rig.Vm.PlayVideoAsync(Track("a"));
            var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            rig.Vm.EnableFade = true;
            rig.Engine.Fade = (_, _) => ++calls == 1 ? late.Task : Task.CompletedTask;
            var pause = rig.Vm.PauseCommand.ExecuteAsync(null);
            await rig.Vm.PlayVideoAsync(Track("b"));
            late.SetResult(); await pause;
            Check(rig.Vm.CurrentTrack?.Bvid == "b" && rig.Vm.IsPlaying && rig.Engine.PauseCount == 0);
        }),
    };

    private static VideoModel Track(string id) => new() { Bvid = id, Cid = 10, Title = id, Artist = "fixture", DurationSeconds = 240 };
    private static ResolvedAudioSource Source(VideoModel track) => new(track, new Uri($"https://audio.example.invalid/{track.Bvid}.mp4"));
    private static void Check(bool value) { if (!value) throw new Exception("Playback state regression failed"); }
    private sealed class Rig : IDisposable
    {
        public Engine Engine { get; } = new();
        public PlayerViewModel Vm { get; }
        public Rig(Func<VideoModel, Task<ResolvedAudioSource>>? resolve = null)
        { Vm = new(Engine, new Resolver(resolve ?? (t => Task.FromResult(Source(t)))), new InlineContext()) { EnableFade = false }; }
        public void Dispose() => Vm.Dispose();
    }
    private sealed class Resolver(Func<VideoModel, Task<ResolvedAudioSource>> resolve) : IAudioSourceResolver
    { public Task<ResolvedAudioSource> ResolveAsync(VideoModel track, CancellationToken ct) => resolve(track); }
    private sealed class InlineContext : SynchronizationContext
    { public override void Post(SendOrPostCallback callback, object? state) => callback(state); }
    private sealed class Engine : IAudioPlaybackEngine
    {
        public event EventHandler<AudioPlaybackSnapshot>? Changed;
        public long SourceId;
        public int OpenCount, ResumeCount, PauseCount, SeekCount;
        public bool Playing;
        public TimeSpan Position;
        public double Volume { get; set; }
        public Func<double, CancellationToken, Task>? Fade;
        public Task OpenAsync(Uri uri, long sourceId, CancellationToken ct)
        { SourceId = sourceId; OpenCount++; Position = TimeSpan.Zero; Playing = false; Emit(); return Task.CompletedTask; }
        public void Resume() { ResumeCount++; Playing = true; Emit(); }
        public void Pause() { PauseCount++; Playing = false; Emit(); }
        public void Stop() { Playing = false; Position = TimeSpan.Zero; }
        public void Seek(TimeSpan position) { SeekCount++; Position = position; Emit(); }
        public Task FadeToAsync(double volume, TimeSpan duration, CancellationToken ct) => Fade?.Invoke(volume, ct) ?? Task.CompletedTask;
        public void Emit(bool ended = false, string? error = null, long? sourceId = null)
        {
            if (ended) Playing = false;
            Changed?.Invoke(this, new(sourceId ?? SourceId, Position, TimeSpan.FromSeconds(240), Playing, false, true, Ended: ended, Error: error));
        }
    }
}
