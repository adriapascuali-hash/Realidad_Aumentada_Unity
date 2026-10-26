using UnityEngine;

namespace ARDashboard
{
    /// Movimiento aleatorio con IA para los cubos del escenario shooter.
    /// Siempre se mantiene dentro del plano "dronelab" usando su espacio local.
    public class AITarget : MonoBehaviour
    {
        [HideInInspector] public Transform planeBounds;
        [HideInInspector] public float speedMultiplier = 1f;

        public float speed         = 3f;
        public float reachDistance = 0.5f;
        public float pauseMin      = 0.05f;
        public float pauseMax      = 0.6f;

        static float HEIGHT => ShooterSettings.EnemyHeight;
        const float MARGIN     = 0.4f;
        const float SPEED_MIN  = 1.0f;
        const float SPEED_MAX  = 2.0f;

        Vector3     destination;
        float       pauseTimer;
        bool        pausing;
        float       shootTimer;
        float       evasionCheckTimer;
        const float SHOOT_RANGE = 15f;

        // ── Física de vuelo ───────────────────────────────────────────────────
        Vector3     _vel       = Vector3.zero;
        public float spawnTime;

        // ── Estado de adquisición per-dron ────────────────────────────────────
        // acquireTimer se acumula mientras el dron está en cámara; se resetea al salir.
        // isAcquired pasa a true al superar el umbral y vuelve a false al salir de pantalla.
        [HideInInspector] public float acquireTimer = 0f;
        [HideInInspector] public bool  isAcquired   = false;
        const float ACCEL      = 6f;    // aceleración m/s²
        const float BRAKE_DIST = 2.5f;  // distancia a la que empieza a frenar

        TargetSpawner     spawner;
        ShooterController shooter;

        // Tier de dificultad 0-6 según kills acumulados + offset de configuración —
        // sube cada 5 kills, mismo ritmo que el aumento de enemigos (ver
        // TargetSpawner.TargetCount).
        int DifficultyTier => shooter != null
            ? Mathf.Clamp(shooter.killCount / 5 + ShooterSettings.DifficultyOffset, 0, 6)
            : Mathf.Max(0, ShooterSettings.DifficultyOffset);

        Vector3 _centerOffset;
        bool    _centerCached;
        [HideInInspector] public Vector3 designatorOffset = Vector3.zero;

        public Vector3 VisualCenter
        {
            get
            {
                if (!_centerCached)
                {
                    var rends = GetComponentsInChildren<Renderer>();
                    if (rends.Length > 0)
                    {
                        var b = rends[0].bounds;
                        foreach (var r in rends) b.Encapsulate(r.bounds);
                        _centerOffset = b.center - transform.position;
                        _centerCached = true;
                    }
                }
                return transform.position + _centerOffset + designatorOffset;
            }
        }

        void Start()
        {
            spawner     = FindAnyObjectByType<TargetSpawner>();
            shooter     = FindAnyObjectByType<ShooterController>();
            shootTimer  = Random.Range(1.5f, 3.5f);
            evasionCheckTimer = Random.Range(0f, 0.5f);
            spawnTime   = Time.time;
            PickDestination();
        }

        void Update()
        {
            if (planeBounds == null) return;
            if (PauseMenuUI.IsPaused) return;

            // Mientras se muestra la pantalla de "Dron derribado" los enemigos
            // se vuelven pacíficos: dejan de disparar y de esquivar, pero
            // siguen patrullando con normalidad.
            bool peaceful = shooter != null && shooter.GameOverActive;

            // ── Disparar al jugador escalando con la dificultad ───────────
            Camera playerCam = Camera.main;
            if (!peaceful && playerCam != null)
            {
                float d = Vector3.Distance(transform.position, playerCam.transform.position);
                if (d < SHOOT_RANGE && ShooterSettings.Difficulty != 0)
                {
                    shootTimer -= Time.deltaTime;
                    if (shootTimer <= 0f)
                    {
                        int   tier      = DifficultyTier;
                        // Inaccuracy decreases con la dificultad
                        float inaccuracy = tier == 0 ? 0.10f
                                         : tier == 1 ? 0.06f
                                         : tier == 2 ? 0.03f
                                         : tier == 3 ? 0.015f
                                         : tier == 4 ? 0.005f
                                         : tier == 5 ? 0.0025f
                                         :             0.001f;
                        EnemyProjectile.Create(transform.position, inaccuracy);
                        // Cadencia de fuego muy agresiva
                        float minT = Mathf.Max(0.15f, 1.5f - tier * 0.30f);
                        float maxT = Mathf.Max(0.35f, 2.8f - tier * 0.50f);
                        shootTimer = Random.Range(minT, maxT);
                    }
                }

                // ── Evasión: esquivar si el jugador apunta ────────────────
                evasionCheckTimer -= Time.deltaTime;
                if (evasionCheckTimer <= 0f)
                {
                    int tier = DifficultyTier;
                    evasionCheckTimer = Mathf.Max(0.12f, 0.55f - tier * 0.10f);
                    TryEvade(playerCam, tier);
                }
            }

            if (pausing)
            {
                pauseTimer -= Time.deltaTime;
                if (pauseTimer <= 0f) { pausing = false; PickDestination(); }

                // Deceleración natural al posarse — frena la inercia
                _vel = Vector3.MoveTowards(_vel, Vector3.zero, ACCEL * 2f * Time.deltaTime);
                transform.position += _vel * Time.deltaTime;
                ClampToBounds();

                // Dentro del rango de ataque: cara al jugador; fuera: nivelar yaw
                bool facingPlayer = false;
                if (playerCam != null)
                {
                    Vector3 toPlayer3D = playerCam.transform.position - transform.position;
                    if (toPlayer3D.magnitude < SHOOT_RANGE)
                    {
                        Vector3 toPlayerFlat = new Vector3(toPlayer3D.x, 0f, toPlayer3D.z);
                        if (toPlayerFlat.sqrMagnitude > 0.01f)
                        {
                            transform.rotation = Quaternion.Slerp(
                                transform.rotation,
                                Quaternion.LookRotation(toPlayerFlat.normalized),
                                Time.deltaTime * 3f);
                            facingPlayer = true;
                        }
                    }
                }
                if (!facingPlayer)
                {
                    float yawOnly = transform.eulerAngles.y;
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.Euler(0f, yawOnly, 0f),
                        Time.deltaTime * 3f);
                }
                return;
            }

            // ── Movimiento con aceleración / frenada ─────────────────────────
            Vector3 toTarget  = destination - transform.position;
            float   dist      = toTarget.magnitude;
            // Velocidad deseada: máxima en crucero, se reduce al acercarse
            float   desiredSpd = dist > BRAKE_DIST
                               ? speed
                               : speed * (dist / BRAKE_DIST);
            Vector3 desiredVel = dist > 0.05f
                               ? (toTarget / dist) * desiredSpd
                               : Vector3.zero;
            _vel = Vector3.MoveTowards(_vel, desiredVel, ACCEL * Time.deltaTime);
            transform.position += _vel * Time.deltaTime;
            ClampToBounds();

            // ── Rotación con banking y pitch ─────────────────────────────────
            Vector3 flatVel = new Vector3(_vel.x, 0f, _vel.z);
            if (flatVel.sqrMagnitude > 0.15f)
            {
                // Dentro del rango de ataque: cara al jugador; fuera: dirección de vuelo
                Vector3 lookDir = flatVel.normalized;
                if (playerCam != null)
                {
                    Vector3 toPlayer3D = playerCam.transform.position - transform.position;
                    if (toPlayer3D.magnitude < SHOOT_RANGE)
                    {
                        Vector3 toPlayerFlat = new Vector3(toPlayer3D.x, 0f, toPlayer3D.z);
                        if (toPlayerFlat.sqrMagnitude > 0.01f)
                            lookDir = toPlayerFlat.normalized;
                    }
                }
                Quaternion lookRot = Quaternion.LookRotation(lookDir);

                // Banking proporcional a la tasa de giro (ladeo en curvas)
                float turnRate = Vector3.SignedAngle(
                    new Vector3(transform.forward.x, 0f, transform.forward.z).normalized,
                    lookDir,
                    Vector3.up);
                float bank  = Mathf.Clamp(-turnRate * 0.75f, -28f, 28f);

                // Pitch: inclina hacia adelante según la velocidad
                float pitch = -10f * Mathf.Clamp01(_vel.magnitude / Mathf.Max(speed, 0.1f));

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    lookRot * Quaternion.Euler(pitch, 0f, bank),
                    Time.deltaTime * 5f);
            }

            if (dist < reachDistance)
            {
                pausing    = true;
                pauseTimer = Random.Range(pauseMin, pauseMax);
            }
        }

        void PickDestination()
        {
            if (planeBounds == null) return;
            float limit = 5f - MARGIN;
            destination = planeBounds.TransformPoint(
                new Vector3(Random.Range(-limit, limit), HEIGHT, Random.Range(-limit, limit)));
            // Velocidad de patrulla escala agresivamente con dificultad — tope en
            // tier 4 (máximo 6 m/s antes del multiplicador): los tiers 5/6 siguen
            // subiendo precisión y esquiva, pero no velocidad.
            int tier = Mathf.Min(DifficultyTier, 4);
            speed = Random.Range(SPEED_MIN + tier * 0.7f, SPEED_MAX + tier * 1.0f)
                  * ShooterSettings.EnemySpeedMultiplier;
        }

        void TryEvade(Camera cam, int tier)
        {
            // Evasión desde tier 0 con baja probabilidad
            // Comprobar si el jugador apunta hacia este dron
            Vector3 toSelf = transform.position - cam.transform.position;
            float   angle  = Vector3.Angle(cam.transform.forward, toSelf.normalized);
            if (angle > 32f) return;   // no apunta hacia nosotros

            // Probabilidad de esquivar según tier (empieza en tier 0) — escalonada
            // en pasos iguales desde el tier 0 hasta el tope del tier 6 (0.50).
            float chance = tier == 0 ? 0.18f
                         : tier == 1 ? 0.23f
                         : tier == 2 ? 0.29f
                         : tier == 3 ? 0.34f
                         : tier == 4 ? 0.39f
                         : tier == 5 ? 0.45f
                         :             0.50f;
            if (Random.value > chance) return;

            // Dirección perpendicular al vector jugador→dron
            Vector3 perp = Vector3.Cross(toSelf.normalized, Vector3.up).normalized;
            if (Random.value > 0.5f) perp = -perp;

            float dodgeDist = 3.0f + tier * 1.2f;
            Vector3 evadeTarget = transform.position + perp * dodgeDist;

            // Mantener dentro de los límites
            if (planeBounds != null)
            {
                Vector3 local = planeBounds.InverseTransformPoint(evadeTarget);
                float   limit = 5f - MARGIN;
                local.x = Mathf.Clamp(local.x, -limit, limit);
                local.z = Mathf.Clamp(local.z, -limit, limit);
                local.y = HEIGHT;
                evadeTarget = planeBounds.TransformPoint(local);
            }

            destination = evadeTarget;
            pausing     = false;
            // Velocidad de evasión muy rápida — mismo tope en tier 4 que la
            // patrulla (ver PickDestination): máximo 6 m/s antes del multiplicador.
            int speedTier = Mathf.Min(tier, 4);
            speed = (SPEED_MAX + speedTier * 1.0f) * ShooterSettings.EnemySpeedMultiplier;
        }

        void ClampToBounds()
        {
            Vector3 local = planeBounds.InverseTransformPoint(transform.position);
            float   limit = 5f - MARGIN;
            local.x = Mathf.Clamp(local.x, -limit, limit);
            local.z = Mathf.Clamp(local.z, -limit, limit);
            local.y = HEIGHT;
            transform.position = planeBounds.TransformPoint(local);
        }

        public void Hit()
        {
            if (shooter != null) shooter.NotifyHit();
            if (spawner != null) spawner.NotifyDestroyed();
            Destroy(gameObject);
        }
    }
}
