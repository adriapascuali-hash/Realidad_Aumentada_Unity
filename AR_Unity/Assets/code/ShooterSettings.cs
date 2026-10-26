namespace ARDashboard
{
    /// Configuración estática compartida entre ShooterConfigMenu,
    /// TargetSpawner, AITarget y ShooterController.
    public static class ShooterSettings
    {
        // ── Señal de inicio ───────────────────────────────────────────────────
        public static bool GameStarted = false;

        // ── Opciones de partida ───────────────────────────────────────────────
        /// 0 = Drone  |  1 = TIE Interceptor
        public static int   EnemyTypeIndex    = 0;

        /// Altura de vuelo de los enemigos sobre el plano (metros locales)
        public static float EnemyHeight       = 0.6f;

        /// 0 = Pacífico  |  1 = Fácil  |  2 = Difícil
        public static int   Difficulty        = 1;

        /// Si true, al llegar a 0 de vida se pregunta si continuar
        public static bool  RespawnEnabled    = true;

        /// Si true, se generan barreras/obstáculos dentro del dronelab
        public static bool  BarriersEnabled   = true;

        /// Nivel de tamaño de los enemigos (1-10). Nivel 3 = tamaño original.
        public static int   EnemySizeLevel    = 3;

        // ── Helpers de dificultad ─────────────────────────────────────────────
        /// Offset base que se suma al DifficultyTier calculado por kills
        public static int DifficultyOffset =>
            Difficulty == 0 ? -2 :
            Difficulty == 1 ?  0 : 2;

        /// Daño que hace cada proyectil enemigo según dificultad
        public static int ProjectileDamage =>
            Difficulty == 0 ?  5 :
            Difficulty == 1 ?  5 : 10;

        /// Multiplicador de velocidad de patrulla/evasión de los enemigos:
        /// Pacífico y Fácil van más lentos; Difícil conserva la velocidad original.
        public static float EnemySpeedMultiplier =>
            Difficulty == 0 ? 0.5f  :
            Difficulty == 1 ? 0.75f : 1f;

        public static void Reset()
        {
            GameStarted      = false;
            EnemyTypeIndex   = 0;
            EnemyHeight      = 0.6f;
            Difficulty       = 1;
            RespawnEnabled   = true;
            BarriersEnabled  = true;
            EnemySizeLevel   = 3;
        }
    }
}
