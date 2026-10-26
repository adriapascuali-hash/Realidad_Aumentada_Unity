using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace ARDashboard
{
    /// <summary>
    /// csDronLink.dll (binario cerrado, sin fuente en este repo) no expone
    /// armar/desarmar de forma independiente ni un cambio de modo genérico
    /// (solo PonModoGuiado/PonModoLoiter) — necesarios para los botones
    /// Arm/Arm-forzado/ALTHOLD de la webapp. Para no abrir una segunda
    /// conexión al mismo SITL/COM (que reintroduciría justo el conflicto de
    /// puerto que este cambio quiere resolver), esta clase genera los
    /// paquetes MAVLink v2 con la API pública de la propia DLL
    /// (MAVLink.MavlinkParse.GenerateMAVLinkPacket20) y los envía reusando,
    /// por reflexión, el método privado Dron.EnviarMensaje(byte[]) — el que
    /// realmente escribe al SerialPort/NetworkStream que csDronLink.dll ya
    /// tiene abierto.
    ///
    /// Riesgo conocido: depende de un detalle de implementación privado de
    /// un binario de terceros. Si csDronLink.dll se recompila con otra
    /// firma para EnviarMensaje, o si un build con IL2CPP hace stripping de
    /// ese miembro, esto lanzará una excepción clara (ver try/catch en las
    /// llamadas) en vez de fallar en silencio. Probado en Editor/Development
    /// Build, que es como se ejecuta este proyecto.
    /// </summary>
    public static class RawMavlinkCommander
    {
        // custom_mode numérico de ArduCopter — csDronLink.dll no los expone.
        public static readonly Dictionary<string, uint> CopterModes =
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "STABILIZE", 0 },
            { "ALT_HOLD", 2 }, { "ALTHOLD", 2 },
            { "AUTO", 3 },
            { "GUIDED", 4 },
            { "LOITER", 5 },
            { "RTL", 6 },
            { "LAND", 9 },
        };

        // Identidad con la que nos anunciamos como emisor del mensaje
        // (convención habitual de GCS: sysid 255, sin componente concreto).
        const byte SELF_SYSTEM = 255;
        const byte SELF_COMPONENT = 0;

        // Vehículo objetivo (dron único en este proyecto).
        const byte TARGET_SYSTEM = 1;
        const byte TARGET_COMPONENT = 1; // MAV_COMP_ID_AUTOPILOT1

        static readonly MAVLink.MavlinkParse _parser = new MAVLink.MavlinkParse(false);
        static MethodInfo _enviarMensaje;
        static int _seq;

        static MethodInfo EnviarMensajeMethod()
        {
            if (_enviarMensaje == null)
            {
                _enviarMensaje = typeof(csDronLink.Dron).GetMethod(
                    "EnviarMensaje", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_enviarMensaje == null)
                    throw new MissingMethodException(
                        "csDronLink.Dron.EnviarMensaje no encontrado por reflexión (¿cambió la DLL?)");
            }
            return _enviarMensaje;
        }

        static void Send(csDronLink.Dron dron, MAVLink.MAVLINK_MSG_ID id, object payload)
        {
            byte[] packet = _parser.GenerateMAVLinkPacket20(
                id, payload, false, SELF_SYSTEM, SELF_COMPONENT, _seq++);
            EnviarMensajeMethod().Invoke(dron, new object[] { packet });
        }

        /// Arma o desarma el vehículo (MAV_CMD_COMPONENT_ARM_DISARM).
        public static void Arm(csDronLink.Dron dron, bool arm)
        {
            var cmd = new MAVLink.mavlink_command_long_t
            {
                target_system = TARGET_SYSTEM,
                target_component = TARGET_COMPONENT,
                command = (ushort)MAVLink.MAV_CMD.COMPONENT_ARM_DISARM,
                confirmation = 0,
                param1 = arm ? 1f : 0f,
            };
            Send(dron, MAVLink.MAVLINK_MSG_ID.COMMAND_LONG, cmd);
        }

        /// Arma saltándose temporalmente ARMING_CHECK, igual que hacía antes
        /// app.py: pone el parámetro a 0, arma, y lo restaura a 1. Bloquea el
        /// hilo que la llame (usar desde un hilo de fondo, no el principal).
        public static void ForceArm(csDronLink.Dron dron)
        {
            dron.EscribirParametros(new List<(string, float)> { ("ARMING_CHECK", 0f) });
            Thread.Sleep(150);
            Arm(dron, true);
            Thread.Sleep(300);
            dron.EscribirParametros(new List<(string, float)> { ("ARMING_CHECK", 1f) });
        }

        /// Cambia a un modo de vuelo arbitrario de ArduCopter (SET_MODE).
        public static void SetMode(csDronLink.Dron dron, string modeName)
        {
            if (!CopterModes.TryGetValue(modeName, out uint customMode))
                throw new ArgumentException($"Modo desconocido: {modeName}");

            var cmd = new MAVLink.mavlink_set_mode_t
            {
                target_system = TARGET_SYSTEM,
                base_mode = (byte)MAVLink.MAV_MODE_FLAG.CUSTOM_MODE_ENABLED,
                custom_mode = customMode,
            };
            Send(dron, MAVLink.MAVLINK_MSG_ID.SET_MODE, cmd);
        }
    }
}
