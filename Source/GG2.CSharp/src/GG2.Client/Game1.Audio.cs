#nullable enable

using Microsoft.Xna.Framework.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using GG2.Core;

namespace GG2.Client;

public partial class Game1
{
    private SoundEffect? _menuMusic;
    private SoundEffectInstance? _menuMusicInstance;
    private SoundEffect? _faucetMusic;
    private SoundEffectInstance? _faucetMusicInstance;
    private SoundEffect? _ingameMusic;
    private SoundEffectInstance? _ingameMusicInstance;
    private bool _ingameMusicEnabled = true;
    private readonly HashSet<ulong> _processedNetworkSoundEventIds = new();
    private readonly Queue<ulong> _processedNetworkSoundEventOrder = new();

    private void LoadMenuMusic()
    {
        var candidates = new[] { "menumusic1.wav", "menumusic2.wav" };
        var chosen = candidates[_visualRandom.Next(candidates.Length)];
        var musicPath = ProjectSourceLocator.FindFile(Path.Combine("Music", chosen));
        if (musicPath is null || !File.Exists(musicPath))
        {
            return;
        }

        using var stream = File.OpenRead(musicPath);
        _menuMusic = SoundEffect.FromStream(stream);
        _menuMusicInstance = _menuMusic.CreateInstance();
        _menuMusicInstance.IsLooped = true;
    }

    private void LoadFaucetMusic()
    {
        var musicPath = ProjectSourceLocator.FindFile(Path.Combine("Music", "faucetmusic.wav"));
        if (musicPath is null || !File.Exists(musicPath))
        {
            return;
        }

        using var stream = File.OpenRead(musicPath);
        _faucetMusic = SoundEffect.FromStream(stream);
        _faucetMusicInstance = _faucetMusic.CreateInstance();
        _faucetMusicInstance.IsLooped = true;
        _faucetMusicInstance.Volume = 0.8f;
    }

    private void LoadIngameMusic()
    {
        var musicPath = ProjectSourceLocator.FindFile(Path.Combine("Music", "ingamemusic.wav"));
        if (musicPath is null || !File.Exists(musicPath))
        {
            return;
        }

        using var stream = File.OpenRead(musicPath);
        _ingameMusic = SoundEffect.FromStream(stream);
        _ingameMusicInstance = _ingameMusic.CreateInstance();
        _ingameMusicInstance.IsLooped = true;
    }

    private void EnsureMenuMusicPlaying()
    {
        if (_menuMusicInstance is null)
        {
            return;
        }

        if (_menuMusicInstance.State != SoundState.Playing)
        {
            _menuMusicInstance.Play();
        }
    }

    private void EnsureFaucetMusicPlaying()
    {
        if (_faucetMusicInstance is null)
        {
            return;
        }

        if (_faucetMusicInstance.State != SoundState.Playing)
        {
            _faucetMusicInstance.Play();
        }
    }

    private void StopMenuMusic()
    {
        if (_menuMusicInstance?.State == SoundState.Playing)
        {
            _menuMusicInstance.Stop();
        }
    }

    private void StopFaucetMusic()
    {
        if (_faucetMusicInstance?.State == SoundState.Playing)
        {
            _faucetMusicInstance.Stop();
        }
    }

    private void EnsureIngameMusicPlaying()
    {
        if (_ingameMusicInstance is null || !_ingameMusicEnabled)
        {
            return;
        }

        if (_world.MatchState.IsEnded)
        {
            return;
        }

        if (_ingameMusicInstance.State != SoundState.Playing)
        {
            _ingameMusicInstance.Play();
        }
    }

    private void StopIngameMusic()
    {
        if (_ingameMusicInstance?.State == SoundState.Playing)
        {
            _ingameMusicInstance.Stop();
        }
    }

    private void PlayDeathCamSoundIfNeeded()
    {
        var deathCamActive = _killCamEnabled && !_world.LocalPlayer.IsAlive && _world.LocalDeathCam is not null;
        if (!deathCamActive || _wasDeathCamActive)
        {
            return;
        }

        var sound = _runtimeAssets.GetSound("DeathCamSnd");
        sound?.Play(0.6f, 0f, 0f);
    }

    private void PlayRoundEndSoundIfNeeded()
    {
        if (!_world.MatchState.IsEnded || _wasMatchEnded)
        {
            return;
        }

        var soundName = _world.MatchState.WinnerTeam switch
        {
            PlayerTeam.Red when _world.LocalPlayer.Team == PlayerTeam.Red => "VictorySnd",
            PlayerTeam.Blue when _world.LocalPlayer.Team == PlayerTeam.Blue => "VictorySnd",
            null => "FailureSnd",
            _ => "FailureSnd",
        };

        if (_ingameMusicInstance?.State == SoundState.Playing)
        {
            _ingameMusicInstance.Stop();
        }

        var sound = _runtimeAssets.GetSound(soundName);
        sound?.Play(0.8f, 0f, 0f);
    }

    private void PlayPendingSoundEvents()
    {
        foreach (var soundEvent in _world.DrainPendingSoundEvents())
        {
            if (!ShouldProcessNetworkEvent(soundEvent.EventId, _processedNetworkSoundEventIds, _processedNetworkSoundEventOrder))
            {
                continue;
            }

            if (string.Equals(soundEvent.SoundName, "ExplosionSnd", StringComparison.OrdinalIgnoreCase))
            {
                _explosions.Add(new ExplosionVisual(soundEvent.X, soundEvent.Y));
            }

            var sound = _runtimeAssets.GetSound(soundEvent.SoundName);
            if (sound is null)
            {
                continue;
            }

            var dx = soundEvent.X - _world.LocalPlayer.X;
            var dy = soundEvent.Y - _world.LocalPlayer.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            var volume = Math.Clamp(1f - (distance / 1200f), 0f, 1f) * 0.6f;
            if (volume <= 0f)
            {
                continue;
            }

            var pan = Math.Clamp(dx / 600f, -1f, 1f);
            sound.Play(volume, 0f, pan);
        }
    }
}
