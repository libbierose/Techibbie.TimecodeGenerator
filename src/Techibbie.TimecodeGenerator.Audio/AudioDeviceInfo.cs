namespace Techibbie.TimecodeGenerator.Audio;

/// <summary>Describes an audio output device available for LTC or click playback.</summary>
public sealed record AudioDeviceInfo(int Id, string Name, int Channels, double SampleRate);
