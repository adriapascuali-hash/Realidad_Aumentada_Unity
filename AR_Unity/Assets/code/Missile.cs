using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;
using ARDashboard;

public class Missile : MonoBehaviour
{
    AITarget target;
    float    speed    = 55f;
    float    lifetime = 10f;

    // ── Template estático (se carga una vez, se reutiliza) ────────────────
    static GameObject s_template;
    static bool       s_loading;

    public static async Task PreloadAsync()
    {
        if (s_template != null || s_loading) return;
        s_loading = true;

        string path = Path.Combine(Application.streamingAssetsPath, "Missile", "scene.gltf");
        if (!File.Exists(path)) { s_loading = false; return; }

        var  gltf = new GltfImport();
        bool ok   = await gltf.Load(path);
        if (!ok) { s_loading = false; return; }

        var raw = new GameObject("_MissileRaw");
        raw.SetActive(false);
        await gltf.InstantiateMainSceneAsync(raw.transform);

        Transform ty90 = FindDeep(raw.transform, "TY-90_13");

        if (ty90 != null)
        {
            var tpl = new GameObject("MissileTemplate");
            tpl.SetActive(false);
            ty90.SetParent(tpl.transform, false);
            ty90.gameObject.SetActive(true);
            Destroy(raw);
            s_template = tpl;
        }
        else
        {
            s_template = raw;
        }

        s_loading = false;
        Debug.Log($"[Missile] Template listo: {s_template.name}");
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    public static Missile Create(Vector3 pos, Vector3 dir, AITarget tgt)
    {
        var go = new GameObject("[Missile]");
        go.transform.position = pos;
        go.transform.forward  = dir;
        var m  = go.AddComponent<Missile>();
        m.target = tgt;
        m.BuildVisuals();
        return m;
    }

    void BuildVisuals()
    {
        // ── Cuerpo: modelo 3D o primitivas de fallback ────────────────────
        if (s_template != null)
        {
            var model = Instantiate(s_template);
            model.SetActive(true);
            model.transform.SetParent(transform, false);
            model.transform.localScale    = Vector3.one * 0.0028f;
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        }
        else
        {
            BuildPrimitiveFallback();
        }

        // ── Tobera ────────────────────────────────────────────────────────
        var nozzle = new GameObject("Nozzle");
        nozzle.transform.SetParent(transform, false);
        nozzle.transform.localPosition = new Vector3(0f, 0f, -0.22f);

        BuildFlame(nozzle.transform);
        BuildSmokeTrail(nozzle.transform);
        BuildSmokeCloud(nozzle.transform);
    }

    // ── Llama del motor ───────────────────────────────────────────────────
    void BuildFlame(Transform parent)
    {
        var go    = new GameObject("Flame");
        go.transform.SetParent(parent, false);
        var ps    = go.AddComponent<ParticleSystem>();
        var main  = ps.main;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(4f, 10f);
        main.startSize        = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        main.startColor       = new ParticleSystem.MinMaxGradient(
                                    new Color(1f, 0.97f, 0.60f, 1f),
                                    new Color(1f, 0.45f, 0.08f, 1f));
        main.gravityModifier  = 0f;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;

        var em   = ps.emission; em.rateOverTime = 250f;
        var sh   = ps.shape;    sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 10f; sh.radius = 0.015f;

        var col  = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.95f, 0.65f), 0f),
                    new GradientColorKey(new Color(1f, 0.18f, 0f),    1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        Shader sh1 = Shader.Find("Particles/Additive");
        if (sh1 == null) sh1 = Shader.Find("Legacy Shaders/Particles/Additive");
        if (sh1 == null) sh1 = Shader.Find("Sprites/Default");
        go.GetComponent<ParticleSystemRenderer>().material = new Material(sh1);
        ps.Play();
    }

    // ── Humo denso – columna blanca/gris que crece rápido (estilo BF/DCS) ──
    void BuildSmokeTrail(Transform parent)
    {
        var go   = new GameObject("SmokeTrail");
        go.transform.SetParent(parent, false);
        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(8.0f, 14.0f);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
        main.startSize        = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.startColor       = new ParticleSystem.MinMaxGradient(
                                    new Color(1.00f, 0.99f, 0.97f, 0.95f),
                                    new Color(0.90f, 0.88f, 0.85f, 0.85f));
        main.gravityModifier  = -0.03f;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = 2000;

        var em  = ps.emission; em.rateOverTime = 220f;
        var sh  = ps.shape;    sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 4f; sh.radius = 0.015f;

        // Crece rápido: de 0.2 a 4 unidades — columna gruesa visible
        var sol  = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
                       new AnimationCurve(new Keyframe(0f,    0.04f),
                                          new Keyframe(0.08f, 0.30f),
                                          new Keyframe(0.25f, 0.75f),
                                          new Keyframe(1f,    1.00f)));

        // Blanco puro → gris claro → gris medio, fade suave
        var col  = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1.00f, 1.00f, 1.00f), 0f),
                    new GradientColorKey(new Color(0.85f, 0.84f, 0.82f), 0.30f),
                    new GradientColorKey(new Color(0.65f, 0.63f, 0.60f), 0.70f),
                    new GradientColorKey(new Color(0.45f, 0.43f, 0.40f), 1.00f) },
            new[] { new GradientAlphaKey(0f,    0.00f),
                    new GradientAlphaKey(0.92f, 0.04f),
                    new GradientAlphaKey(0.75f, 0.40f),
                    new GradientAlphaKey(0.40f, 0.75f),
                    new GradientAlphaKey(0f,    1.00f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Deriva lateral suave + subida
        var vel  = ps.velocityOverLifetime; vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.20f, 0.20f);
        vel.y = new ParticleSystem.MinMaxCurve(0.15f,  0.50f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.20f, 0.20f);

        // Turbulencia fuerte para volumen y espiral natural
        var noise = ps.noise;
        noise.enabled          = true;
        noise.strength         = new ParticleSystem.MinMaxCurve(0.45f, 0.80f);
        noise.frequency        = 0.45f;
        noise.scrollSpeed      = 0.25f;
        noise.damping          = true;
        noise.quality          = ParticleSystemNoiseQuality.High;
        noise.octaveCount      = 3;
        noise.octaveMultiplier = 0.50f;
        noise.octaveScale      = 2.0f;

        Shader smShader = Shader.Find("Particles/Alpha Blended");
        if (smShader == null) smShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (smShader == null) smShader = Shader.Find("Sprites/Default");
        var mat07 = new Material(smShader);
        var tex07 = Resources.Load<Texture2D>("Smoke/smoke_07");
        if (tex07 != null) mat07.mainTexture = tex07;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.material     = mat07;
        rend.sortingFudge = -10f;
        ps.Play();
    }

    // ── Nube persistente – bocanadas grises que flotan tras el misil ───────
    void BuildSmokeCloud(Transform parent)
    {
        var go   = new GameObject("SmokeCloud");
        go.transform.SetParent(parent, false);
        var ps   = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime    = new ParticleSystem.MinMaxCurve(18.0f, 30.0f);
        main.startSpeed       = new ParticleSystem.MinMaxCurve(0f, 0.40f);
        main.startSize        = new ParticleSystem.MinMaxCurve(0.50f, 1.00f);
        main.startColor       = new ParticleSystem.MinMaxGradient(
                                    new Color(0.72f, 0.70f, 0.67f, 0.55f),
                                    new Color(0.52f, 0.50f, 0.47f, 0.40f));
        main.gravityModifier  = -0.02f;
        main.simulationSpace  = ParticleSystemSimulationSpace.World;
        main.maxParticles     = 600;

        var em  = ps.emission; em.rateOverTime = 65f;
        var sh  = ps.shape;    sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 6f; sh.radius = 0.02f;

        // Crece de bocanada a nube enorme — llega a 8 unidades
        var sol  = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
                       new AnimationCurve(new Keyframe(0f,    0.10f),
                                          new Keyframe(0.15f, 0.50f),
                                          new Keyframe(0.40f, 1.00f),
                                          new Keyframe(1f,    1.00f)));

        // Gris claro → gris oscuro, se disuelve lentamente
        var col  = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.78f, 0.76f, 0.73f), 0f),
                    new GradientColorKey(new Color(0.52f, 0.50f, 0.47f), 0.50f),
                    new GradientColorKey(new Color(0.30f, 0.29f, 0.27f), 1.00f) },
            new[] { new GradientAlphaKey(0f,    0.00f),
                    new GradientAlphaKey(0.55f, 0.06f),
                    new GradientAlphaKey(0.40f, 0.40f),
                    new GradientAlphaKey(0.20f, 0.75f),
                    new GradientAlphaKey(0f,    1.00f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        // Sube suavemente y se dispersa
        var vel  = ps.velocityOverLifetime; vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        vel.y = new ParticleSystem.MinMaxCurve(0.20f,  0.60f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

        var noise = ps.noise;
        noise.enabled          = true;
        noise.strength         = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        noise.frequency        = 0.28f;
        noise.scrollSpeed      = 0.12f;
        noise.damping          = true;
        noise.quality          = ParticleSystemNoiseQuality.Medium;
        noise.octaveCount      = 2;
        noise.octaveMultiplier = 0.50f;
        noise.octaveScale      = 2.0f;

        Shader smShader = Shader.Find("Particles/Alpha Blended");
        if (smShader == null) smShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (smShader == null) smShader = Shader.Find("Sprites/Default");
        var mat09 = new Material(smShader);
        var tex09 = Resources.Load<Texture2D>("Smoke/smoke_09");
        if (tex09 != null) mat09.mainTexture = tex09;
        go.GetComponent<ParticleSystemRenderer>().material = mat09;
        ps.Play();
    }

    void BuildPrimitiveFallback()
    {
        var stdMat = new Material(Shader.Find("Standard"));
        var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        nose.transform.SetParent(transform, false);
        nose.transform.localPosition = new Vector3(0f, 0f, 0.28f);
        nose.transform.localScale    = new Vector3(0.055f, 0.055f, 0.15f);
        Destroy(nose.GetComponent<Collider>());
        nose.GetComponent<Renderer>().material = new Material(stdMat) { color = new Color(0.55f, 0.55f, 0.60f) };

        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.transform.SetParent(transform, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale    = new Vector3(0.055f, 0.22f, 0.055f);
        Destroy(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().material = new Material(stdMat) { color = new Color(0.68f, 0.68f, 0.72f) };

        for (int i = 0; i < 4; i++)
        {
            float a   = i * 90f * Mathf.Deg2Rad;
            var   fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fin.transform.SetParent(transform, false);
            fin.transform.SetLocalPositionAndRotation(
                new Vector3(Mathf.Cos(a) * 0.055f, Mathf.Sin(a) * 0.055f, -0.16f),
                Quaternion.Euler(0f, 0f, i * 90f));
            fin.transform.localScale = new Vector3(0.012f, 0.075f, 0.06f);
            Destroy(fin.GetComponent<Collider>());
            fin.GetComponent<Renderer>().material = new Material(stdMat) { color = new Color(0.45f, 0.45f, 0.50f) };
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f || target == null)
        { Destroy(gameObject); return; }

        // ── Guiado 100 % de acierto: apunta directamente al objetivo ─────
        transform.forward = (target.transform.position - transform.position).normalized;
        transform.position += transform.forward * speed * Time.deltaTime;

        if (Vector3.Distance(transform.position, target.transform.position) < 1.8f)
        {
            Projectile.SpawnExplosion(target.transform.position);
            target.Hit();
            Destroy(gameObject);
        }
    }
}
