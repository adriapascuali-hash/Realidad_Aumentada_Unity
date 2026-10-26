public static class GameConfig
{
    // ── Vídeo ──────────────────────────────────────────────
    public static VideoSource SelectedSource    = VideoSource.LocalWebcam;
    public static int         WebcamDeviceIndex = 0;

    // ── Dron ───────────────────────────────────────────────
    public enum DroneConnectionType { SITL, Antenna }
    public static DroneConnectionType DroneConnection = DroneConnectionType.SITL;

    // TCP (SITL)
    public static string SitlHost = "127.0.0.1";
    // csDronLink.Dron.Conectar() ignora este valor en modo simulación: calcula
    // el puerto internamente como 5763 + (id-1)*10 (5763 para id=1, el único
    // que usa esta app). Se deja aquí en 5763 solo para que la UI no mienta
    // sobre el puerto real al que se va a conectar.
    public static int    SitlPort = 5763;

    // Serie (Antena)
    public static string SerialPort = "";
    public static int    Baudrate   = 57600;

    // Devuelve el connectionString listo para pasarle al DLL
    public static string GetConnectionString()
    {
        if (DroneConnection == DroneConnectionType.SITL)
            return $"tcp:{SitlHost}:{SitlPort}";
        else
            return SerialPort;
    }
}
