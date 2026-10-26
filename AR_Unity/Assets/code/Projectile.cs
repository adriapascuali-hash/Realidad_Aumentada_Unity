using UnityEngine;

namespace ARDashboard
{
    /// Proyectil visible que viaja a alta velocidad y destruye AITargets al impactar.
    public class Projectile : MonoBehaviour
    {
        public float speed       = 80f;
        public float maxDistance = 400f;

        static AudioClip  s_explosionClip;
        static Texture2D  s_smokeTex09;
        static Texture2D  s_smokeTex10;

        static void EnsureSmokeTextures()
        {
            if (s_smokeTex09 == null) s_smokeTex09 = Resources.Load<Texture2D>("Smoke/smoke_09");
            if (s_smokeTex10 == null) s_smokeTex10 = Resources.Load<Texture2D>("Smoke/smoke_10");
        }

        Vector3 direction;
        float   traveled;

        public void Init(Vector3 dir)
        {
            direction = dir.normalized;
            // Orientar la cápsula en la dirección de vuelo
            transform.rotation = Quaternion.LookRotation(direction);
        }

        void Update()
        {
            float step = speed * Time.deltaTime;

            // SphereCast para detectar impactos aunque el proyectil sea rápido
            if (Physics.SphereCast(transform.position, 0.04f, direction,
                out RaycastHit hit, step + 0.1f))
            {
                var target = hit.collider.GetComponent<AITarget>();
                if (target != null)
                {
                    target.Hit();
                    SpawnExplosion(hit.point);
                }
                Destroy(gameObject);
                return;
            }

            transform.position += direction * step;
            traveled           += step;

            if (traveled > maxDistance)
                Destroy(gameObject);
        }

        public static void SpawnExplosion(Vector3 pos)
        {
            if (s_explosionClip == null) s_explosionClip = GenerateExplosionSound();

            var root = new GameObject("Explosion");
            root.transform.position = pos;

            // ── Sonido ───────────────────────────────────────────────────────
            var audio          = root.AddComponent<AudioSource>();
            audio.spatialBlend = 0f;
            audio.volume       = 0.9f;
            audio.PlayOneShot(s_explosionClip);

            // ── Flash esférico inicial (destello blanco-amarillo, 60 ms) ─────
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.transform.SetParent(root.transform);
            flash.transform.localPosition = Vector3.zero;
            flash.transform.localScale    = Vector3.one * 0.12f;
            Destroy(flash.GetComponent<Collider>());
            flash.GetComponent<Renderer>().material =
                new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.96f, 0.70f) };
            Destroy(flash, 0.06f);

            // ── Fuego ─────────────────────────────────────────────────────────
            // Bolas grandes naranja-rojo que suben
            MakePS(root.transform, burst: 28, delay: 0f,
                lifeMin: 0.45f, lifeMax: 0.85f,
                speedMin: 0.5f, speedMax: 2.0f,
                sizeMin: 0.18f, sizeMax: 0.55f,
                c0: new Color(1.00f, 0.58f, 0.04f, 1.0f),
                c1: new Color(0.65f, 0.08f, 0.00f, 0.0f),
                gravity: -0.22f, radius: 0.08f, growThenShrink: true);

            // ── Núcleo interno (amarillo más brillante, más pequeño y rápido) ─
            MakePS(root.transform, burst: 15, delay: 0f,
                lifeMin: 0.20f, lifeMax: 0.45f,
                speedMin: 1.0f, speedMax: 3.5f,
                sizeMin: 0.08f, sizeMax: 0.28f,
                c0: new Color(1.00f, 0.95f, 0.40f, 1.0f),
                c1: new Color(1.00f, 0.40f, 0.00f, 0.0f),
                gravity: -0.10f, radius: 0.04f, growThenShrink: true);


            // ── Chispas ───────────────────────────────────────────────────────
            // Pequeñas, rápidas, caen con gravedad
            MakePS(root.transform, burst: 50, delay: 0f,
                lifeMin: 0.25f, lifeMax: 0.55f,
                speedMin: 4.0f, speedMax: 9.0f,
                sizeMin: 0.02f, sizeMax: 0.07f,
                c0: new Color(1.00f, 0.95f, 0.55f, 1.0f),
                c1: new Color(1.00f, 0.30f, 0.00f, 0.0f),
                gravity: 0.60f, radius: 0.03f, growThenShrink: false);

            // ── Escombros ─────────────────────────────────────────────────────
            // Trozos oscuros que vuelan y caen por gravedad
            MakePS(root.transform, burst: 14, delay: 0f,
                lifeMin: 0.5f, lifeMax: 1.1f,
                speedMin: 1.5f, speedMax: 5.0f,
                sizeMin: 0.04f, sizeMax: 0.13f,
                c0: new Color(0.40f, 0.28f, 0.14f, 1.0f),
                c1: new Color(0.12f, 0.08f, 0.04f, 0.0f),
                gravity: 0.85f, radius: 0.05f, growThenShrink: false);

            Destroy(root, 8.0f);
        }

        static void MakeSmokePS(Transform parent, Texture2D tex, int burst, float delay,
            float lifeMin, float lifeMax, float sizeMin, float sizeMax,
            float upSpeed, float radius)
        {
            var go = new GameObject("SmokePS");
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;

            var ps   = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop            = false;
            main.duration        = 0.2f;
            main.startDelay      = new ParticleSystem.MinMaxCurve(delay);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize       = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                                       new Color(0.32f, 0.28f, 0.23f, 0.88f),
                                       new Color(0.18f, 0.15f, 0.12f, 0.70f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.02f);
            main.maxParticles    = burst + 10;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = radius;

            // Crece de pequeño a grande
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f,    0.08f),
                                   new Keyframe(0.15f, 0.50f),
                                   new Keyframe(0.40f, 1.00f),
                                   new Keyframe(1f,    1.00f)));

            // Gris oscuro → más oscuro → se desvanece
            var col  = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.38f, 0.33f, 0.27f), 0f),
                        new GradientColorKey(new Color(0.22f, 0.19f, 0.15f), 0.45f),
                        new GradientColorKey(new Color(0.10f, 0.08f, 0.06f), 1f) },
                new[] { new GradientAlphaKey(0f,    0.00f),
                        new GradientAlphaKey(0.82f, 0.04f),
                        new GradientAlphaKey(0.55f, 0.40f),
                        new GradientAlphaKey(0.20f, 0.80f),
                        new GradientAlphaKey(0f,    1.00f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // Sube y se dispersa
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
            vel.y = new ParticleSystem.MinMaxCurve(upSpeed * 0.5f, upSpeed);
            vel.z = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);

            var noise = ps.noise;
            noise.enabled          = true;
            noise.strength         = new ParticleSystem.MinMaxCurve(0.35f, 0.60f);
            noise.frequency        = 0.40f;
            noise.scrollSpeed      = 0.18f;
            noise.damping          = true;
            noise.quality          = ParticleSystemNoiseQuality.Medium;
            noise.octaveCount      = 2;
            noise.octaveMultiplier = 0.50f;
            noise.octaveScale      = 2.0f;

            Shader smShader = Shader.Find("Particles/Alpha Blended");
            if (smShader == null) smShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (smShader == null) smShader = Shader.Find("Sprites/Default");
            var mat = new Material(smShader);
            if (tex != null) mat.mainTexture = tex;
            go.GetComponent<ParticleSystemRenderer>().material = mat;
            ps.Play();
        }

        static void MakePS(Transform parent, int burst, float delay,
            float lifeMin, float lifeMax, float speedMin, float speedMax,
            float sizeMin, float sizeMax, Color c0, Color c1,
            float gravity, float radius, bool growThenShrink)
        {
            var go = new GameObject("PS");
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;

            var ps  = go.AddComponent<ParticleSystem>();
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.material   = new Material(Shader.Find("Sprites/Default"));
            psr.renderMode = ParticleSystemRenderMode.Billboard;

            var main = ps.main;
            main.loop            = false;
            main.duration        = 0.15f;
            main.startDelay      = new ParticleSystem.MinMaxCurve(delay);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize       = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor      = new ParticleSystem.MinMaxGradient(c0);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(gravity);
            main.maxParticles    = burst + 10;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius    = radius;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(c0.r, c0.g, c0.b), 0f),
                        new GradientColorKey(new Color(c1.r, c1.g, c1.b), 1f) },
                new[] { new GradientAlphaKey(c0.a, 0f),
                        new GradientAlphaKey(c1.a, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, growThenShrink
                ? new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f))
                : new AnimationCurve(new Keyframe(0f, 1f),   new Keyframe(1f, 0f)));

            ps.Play();
        }

        // ── Sonido de explosión procedural ────────────────────────────────────
        // Capas: crack de alta frecuencia + cuerpo mid + boom de baja + tono thud
        static AudioClip GenerateExplosionSound()
        {
            const int   RATE     = 44100;
            const float DURATION = 0.7f;
            int         samples  = (int)(RATE * DURATION);
            float[]     data     = new float[samples];

            var   rng    = new System.Random(13);
            float lp1    = 0f, lp2 = 0f, lp3 = 0f;
            const float LP_BODY = 0.06f;   // ~420 Hz
            const float LP_BOOM = 0.015f;  // ~105 Hz

            for (int i = 0; i < samples; i++)
            {
                float t        = (float)i / RATE;
                float progress = t / DURATION;
                float noise    = (float)(rng.NextDouble() * 2.0 - 1.0);

                // Filtros de paso-bajo en cascada para extraer graves
                lp1 += LP_BODY * (noise - lp1);
                lp2 += LP_BODY * (lp1   - lp2);
                lp3 += LP_BOOM * (lp2   - lp3);

                float crack = noise * Mathf.Exp(-progress * 28f) * 0.65f;
                float body  = lp2   * Mathf.Exp(-progress *  8f) * 1.4f;
                float boom  = lp3   * Mathf.Exp(-progress *  3f) * 2.2f;
                float thud  = Mathf.Sin(2f * Mathf.PI * 52f * t)
                            * Mathf.Exp(-progress * 10f) * 0.35f;

                float attack = Mathf.Min(1f, t / 0.008f);
                float s      = (crack + body + boom + thud) * attack;
                s = s / (1f + Mathf.Abs(s) * 0.55f);

                data[i] = Mathf.Clamp(s, -1f, 1f);
            }

            var clip = AudioClip.Create("Explosion", samples, 1, RATE, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ── Construcción visual del proyectil ─────────────────────────────────
        public static Projectile Create(Vector3 position, Vector3 direction)
        {
            // Núcleo: cápsula fina con blending aditivo (apariencia luminosa)
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Projectile";
            Destroy(go.GetComponent<Collider>());
            go.transform.position   = position;
            go.transform.localScale = new Vector3(0.04f, 0.22f, 0.04f);
            go.GetComponent<Renderer>().material = BoltMat(new Color(1f, 0.95f, 0.7f, 0.8f));

            // Halo exterior más ancho (reflejo/calor del bolt)
            var halo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            halo.name = "BoltHalo";
            Destroy(halo.GetComponent<Collider>());
            halo.transform.SetParent(go.transform, false);
            halo.transform.localScale = new Vector3(3.5f, 1.0f, 3.5f);
            halo.GetComponent<Renderer>().material = BoltMat(new Color(1f, 0.45f, 0.05f, 0.2f));

            // Luz puntual que ilumina superficies al pasar
            var lightGO       = new GameObject("BoltLight");
            lightGO.transform.SetParent(go.transform, false);
            var ptLight       = lightGO.AddComponent<Light>();
            ptLight.type      = LightType.Point;
            ptLight.color     = new Color(1f, 0.65f, 0.15f);
            ptLight.intensity = 2.5f;
            ptLight.range     = 2.0f;

            // Rastro con degradado amarillo → naranja → transparente
            var trail               = go.AddComponent<TrailRenderer>();
            trail.time              = 0.10f;
            trail.startWidth        = 0.12f;
            trail.endWidth          = 0f;
            trail.minVertexDistance = 0.015f;
            trail.material          = BoltMat(Color.white);
            var tGrad = new Gradient();
            tGrad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),
                         new GradientColorKey(new Color(1f, 0.30f, 0f),  1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            trail.colorGradient = tGrad;

            var proj = go.AddComponent<Projectile>();
            proj.Init(direction);
            return proj;
        }

        static Material BoltMat(Color c)
        {
            var shader = Shader.Find("Particles/Additive")
                      ?? Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
            {
                var m = new Material(shader);
                // _TintColor dobla internamente la intensidad; compensar dividiendo por 2
                if (m.HasProperty("_TintColor"))
                    m.SetColor("_TintColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a));
                else
                    m.color = c;
                return m;
            }
            return new Material(Shader.Find("Sprites/Default")) { color = c };
        }
    }
}
