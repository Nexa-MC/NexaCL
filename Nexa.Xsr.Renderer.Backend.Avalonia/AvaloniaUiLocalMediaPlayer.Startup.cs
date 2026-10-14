using System.Diagnostics;
using Nexa.Core.Media;

namespace Nexa.Xsr.Renderer.Backend.Avalonia;

public sealed partial class AvaloniaUiLocalMediaPlayer
{
    private string _warmedPath = "";
    private long _warmedGeneration = -1;
    private PngImage? _firstFrame;

    /// <summary>Checks the configured local engine and decodes one bounded video frame while hidden.</summary>
    public Task<PngImage?> WarmUpAsync(CancellationToken token = default) => WarmUpCoreAsync(validatePlaybackDependency: true, token);

    /// <summary>Decodes an admitted immutable local snapshot; playback dependencies are checked separately.</summary>
    public static async Task<PngImage?> ValidateThemeMediaAsync(string preparedPath, bool video, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedPath);
        await using var player = new AvaloniaUiLocalMediaPlayer(video);
        await player.ConfigureAsync(preparedPath, 0, play: false, token).ConfigureAwait(false);
        PngImage? poster = await player.WarmUpCoreAsync(validatePlaybackDependency: false, token).ConfigureAwait(false);
        if (player.State.Status == AvaloniaUiMediaStatus.DependencyMissing)
            throw new PlatformNotSupportedException("The local media decoder is unavailable.");
        if (player.State.Status == AvaloniaUiMediaStatus.Failed)
            throw new InvalidDataException("The admitted theme media cannot be decoded.");
        return poster;
    }

    private async Task<PngImage?> WarmUpCoreAsync(bool validatePlaybackDependency, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_path.Length == 0) return null;
            if (_warmedPath == _path && _warmedGeneration == _generation) return _firstFrame;
            if (!File.Exists(_path)) { Publish(AvaloniaUiMediaStatus.Failed, "Local media file is missing."); return null; }
            if (new FileInfo(_path).Length > 512L * 1048576)
            { Publish(AvaloniaUiMediaStatus.Failed, "Local media exceeds 512 MiB."); return null; }
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            bounded.CancelAfter(TimeSpan.FromSeconds(4));
            var start = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            string[] arguments = _video
                ? ["-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", "mov,matroska,webm,avi,wav,mp3,ogg,flac,aac", "-threads", "1", "-i", _path,
                    "-an", "-vf", "scale=960:540:force_original_aspect_ratio=decrease", "-frames:v", "1",
                    "-f", "image2pipe", "-vcodec", "png", "-threads", "1", "pipe:1"]
                : ["-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", "mov,matroska,webm,avi,wav,mp3,ogg,flac,aac", "-threads", "1", "-i", _path, "-vn", "-t", "0.1", "-f", "null", "-"];
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new IOException("Media engine did not start.");
            using var kill = bounded.Token.Register(() => Kill(process));
            Task errors = DrainErrorsAsync(process.StandardError.BaseStream, bounded.Token);
            try
            {
                _firstFrame = _video
                    ? await ReadFrameAsync(process.StandardOutput.BaseStream, bounded.Token).ConfigureAwait(false)
                    : null;
                if (!_video) await DrainErrorsAsync(process.StandardOutput.BaseStream, bounded.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(bounded.Token).ConfigureAwait(false);
                if (process.ExitCode != 0 || _video && _firstFrame is null)
                { Publish(AvaloniaUiMediaStatus.Failed, "Local media warmup failed."); return null; }
                if (!_video && validatePlaybackDependency)
                {
                    var audioStart = new ProcessStartInfo("ffplay")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    audioStart.ArgumentList.Add("-version");
                    using Process audio = Process.Start(audioStart) ?? throw new IOException("Audio engine did not start.");
                    using var audioKill = bounded.Token.Register(() => Kill(audio));
                    try
                    {
                        Task output = DrainErrorsAsync(audio.StandardOutput.BaseStream, bounded.Token);
                        Task audioErrors = DrainErrorsAsync(audio.StandardError.BaseStream, bounded.Token);
                        await Task.WhenAll(audio.WaitForExitAsync(bounded.Token), output, audioErrors).ConfigureAwait(false);
                        if (audio.ExitCode != 0) { Publish(AvaloniaUiMediaStatus.Failed, "Audio engine warmup failed."); return null; }
                    }
                    finally { Kill(audio); }
                }
                _warmedPath = _path; _warmedGeneration = _generation;
                return _firstFrame;
            }
            finally
            {
                Kill(process);
                try { await errors.ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        { Publish(AvaloniaUiMediaStatus.DependencyMissing, "ffmpeg/ffplay is not installed."); return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        { Publish(AvaloniaUiMediaStatus.Failed, "Local media warmup timed out."); return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Publish(AvaloniaUiMediaStatus.Failed, error.Message); return null; }
        finally { _gate.Release(); }
    }
}
