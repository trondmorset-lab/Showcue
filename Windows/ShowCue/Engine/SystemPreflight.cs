using System.IO;
using ShowCue.Models;

namespace ShowCue.Engine;

/// <summary>Forhåndssjekkens oppslag mot den faktiske maskinen: filsystem, lydenheter og skjermer.</summary>
public sealed class SystemPreflightEnvironment : IPreflightEnvironment
{
    public FileStatus CheckFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return FileStatus.Missing;
            // Åpner fila bare for lesing (uten å lese innholdet) for å se at den ikke er låst eller uten tilgang.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return FileStatus.Ok;
        }
        catch (Exception)
        {
            return FileStatus.Unreadable;
        }
    }

    public bool AudioOutputExists(AudioDriverType driver, string id) => driver == AudioDriverType.Asio
        ? AudioEngine.GetAsioDrivers().Contains(id)
        : AudioEngine.GetWasapiDevices().Any(d => d.Key == id);

    public bool InputDeviceExists(string id) => AudioEngine.GetCaptureDevices().Any(d => d.Key == id);

    public bool ScreenExists(int index) => index >= 0 && index < Monitors.GetAll().Count;
}
