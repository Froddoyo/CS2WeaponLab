using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WeaponLab.Core.Sounds;

namespace WeaponLab.App;

/// <summary>
/// Plays decoded sound files through one mixer (44.1 kHz stereo) that runs
/// while the app is open, so sounds overlap as they do in game. Windows
/// audio through NAudio; MP3 is decoded by Windows' own codec. Pitch is not
/// applied.
/// </summary>
internal sealed class SoundPlayer : IDisposable
{
    private const int Rate = 44100;

    private readonly MixingSampleProvider _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
    private WaveOutEvent? _output;

    public bool Muted { get; set; }

    /// <summary>Plays one sound file at the soundevent's volume; a file that cannot be played is skipped.</summary>
    /// <returns>An error message, or null when it plays (or sound is muted).</returns>
    public string? Play(DecodedSound sound, double volume)
    {
        if (Muted)
        {
            return null;
        }

        try
        {
            if (_output is null)
            {
                _output = new WaveOutEvent { DesiredLatency = 80 };
                _output.Init(_mixer);
                _output.Play();
            }

            _mixer.AddMixerInput(new VolumeSampleProvider(Open(sound)) { Volume = (float)Math.Clamp(volume, 0, 2) });
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FormatException or IOException
            or ArgumentException or NAudio.MmException)
        {
            return $"{Path.GetFileName(sound.Path)} could not be played: {error.Message}";
        }
    }

    /// <summary>Cuts every sound that is still playing.</summary>
    public void StopAll() => _mixer.RemoveAllMixerInputs();

    public void Dispose()
    {
        _output?.Dispose();
        _output = null;
    }

    private static ISampleProvider Open(DecodedSound sound)
    {
        var stream = new MemoryStream(sound.Data, writable: false);
        WaveStream reader = sound.IsMp3
            ? new Mp3FileReaderBase(stream, format => new AcmMp3FrameDecompressor(format))
            : new WaveFileReader(stream);
        if (reader.WaveFormat.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat))
        {
            reader = WaveFormatConversionStream.CreatePcmStream(reader); // ADPCM and the like.
        }

        var provider = reader.ToSampleProvider();
        if (provider.WaveFormat.SampleRate != Rate)
        {
            provider = new WdlResamplingSampleProvider(provider, Rate);
        }

        return provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            var channels => throw new NotSupportedException($"{channels}-channel sounds are not supported."),
        };
    }
}
