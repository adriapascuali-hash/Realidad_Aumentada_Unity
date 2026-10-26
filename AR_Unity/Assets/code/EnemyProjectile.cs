using UnityEngine;

namespace ARDashboard
{
    public class EnemyProjectile : MonoBehaviour
    {
        const float SPEED        = 80f;
        const float HIT_RADIUS   = 0.35f;
        const float MAX_DISTANCE = 18f;

        Camera  cam;
        Vector3 origin;

        public static void Create(Vector3 position, float inaccuracy = 0.10f)
        {
            var go = new GameObject("EnemyBolt");
            go.transform.position = position;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.03f, 0.15f, 0.03f);
            body.GetComponent<Renderer>().material = EnemyMat(new Color(1f, 0.15f, 0.15f, 0.9f));

            var trail               = go.AddComponent<TrailRenderer>();
            trail.time              = 0.08f;
            trail.startWidth        = 0.08f;
            trail.endWidth          = 0f;
            trail.minVertexDistance = 0.01f;
            trail.material          = EnemyMat(Color.white);
            var tGrad = new Gradient();
            tGrad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.4f, 0.4f), 0f),
                         new GradientColorKey(new Color(0.8f, 0f,   0f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            trail.colorGradient = tGrad;

            var ep    = go.AddComponent<EnemyProjectile>();
            ep.cam    = Camera.main;
            ep.origin = position;

            if (ep.cam != null)
            {
                Vector3 dir = (ep.cam.transform.position - position).normalized
                            + Random.insideUnitSphere * inaccuracy;
                go.transform.rotation = Quaternion.LookRotation(dir.normalized);
            }
        }

        static Material EnemyMat(Color c)
        {
            var shader = Shader.Find("Particles/Additive")
                      ?? Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
            {
                var m = new Material(shader);
                if (m.HasProperty("_TintColor"))
                    m.SetColor("_TintColor", new Color(c.r * .5f, c.g * .5f, c.b * .5f, c.a));
                else
                    m.color = c;
                return m;
            }
            return new Material(Shader.Find("Sprites/Default")) { color = c };
        }

        void Update()
        {
            if (cam == null) { Destroy(gameObject); return; }

            Vector3 step = transform.forward * SPEED * Time.deltaTime;

            // Barrido: comprobar si la cámara queda dentro del cilindro recorrido este frame
            Vector3 toCam = cam.transform.position - transform.position;
            float   dot   = Vector3.Dot(toCam, transform.forward);
            if (dot >= 0f && dot <= step.magnitude)
            {
                Vector3 closest = transform.position + transform.forward * dot;
                if (Vector3.Distance(closest, cam.transform.position) < HIT_RADIUS)
                {
                    FindAnyObjectByType<ShooterController>()?.TakeDamage(ShooterSettings.ProjectileDamage);
                    Destroy(gameObject);
                    return;
                }
            }

            transform.position += step;

            if (Vector3.Distance(transform.position, origin) > MAX_DISTANCE)
                Destroy(gameObject);
        }
    }
}
