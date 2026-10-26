using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using GLTFast;

namespace ARDashboard
{
    public class TargetSpawner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void SubscribeToSceneLoad()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "shooter") return;
            var host = new GameObject("[TargetSpawner]");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<TargetSpawner>();
        }

        const int   BASE_COUNT    = 3;    // drones iniciales
        const int   MAX_COUNT     = 30;   // máximo de drones simultáneos
        const float SPEED_MIN     = 1.0f;
        const float SPEED_MAX     = 2.0f;
        const float RESPAWN_DELAY = 1.2f;
        const float CUBE_SIZE     = 0.6f;
        const float DRONE_SCALE   = 0.5f;

        Transform          planeBounds;
        int                colorIdx;
        GameObject         droneTemplate;
        int                lastTargetCount = BASE_COUNT;
        ShooterController  shooter;

        // ── Geofence MAVLink ──────────────────────────────────────────────────
        const float METERS_PER_DEG   = 111320f;
        const float OFFSET_X         = 166339.2f;
        const float OFFSET_Z         = 4594877f;
        const int   FENCE_TIMEOUT_MS = 8000;

        csDronLink.Dron dron;
        bool geofenceActivo = false;

        static readonly Color[] Palette =
        {
            new Color(1.00f, 0.20f, 0.20f),
            new Color(1.00f, 0.60f, 0.10f),
            new Color(0.20f, 0.88f, 0.28f),
            new Color(0.20f, 0.65f, 1.00f),
            new Color(0.88f, 0.20f, 0.88f),
        };

        // Devuelve cuántos drones debería haber según los kills actuales.
        // Cada 5 kills → +1 dron adicional.
        int TargetCount()
        {
            int kills = shooter != null ? shooter.killCount : 0;
            return Mathf.Min(BASE_COUNT + kills / 5, MAX_COUNT);
        }

        async void Start()
        {
            shooter     = FindAnyObjectByType<ShooterController>();
            var lab = GameObject.Find("dronelab") ?? CreateDronelab();
            planeBounds = lab.transform;

            // Cargar el modelo elegido en la configuración
            string gltfPath = ShooterSettings.EnemyTypeIndex == 1
                ? Path.Combine(Application.streamingAssetsPath, "TieInterceptor", "scene.gltf")
                : Path.Combine(Application.streamingAssetsPath, "Drone", "scene.gltf");

            if (File.Exists(gltfPath))
            {
                var gltf = new GltfImport();
                bool ok  = await gltf.Load(gltfPath);
                if (ok)
                {
                    droneTemplate = new GameObject("DroneTemplate");
                    droneTemplate.SetActive(false);
                    await gltf.InstantiateMainSceneAsync(droneTemplate.transform);

                    // Colisionador en la raíz para que el SphereCast lo detecte
                    var col    = droneTemplate.AddComponent<SphereCollider>();
                    col.radius = 0.8f;
                }
            }

            for (int i = 0; i < TargetCount(); i++)
                SpawnTarget();

            if (ShooterSettings.BarriersEnabled)
                UploadGeofence();
        }

        void OnApplicationQuit() => ClearGeofence(waitMs: 2000);
        void OnDestroy()         => ClearGeofence(waitMs: 0);

        void UploadGeofence()
        {
            dron = DroneMqttBridge.Instance?.GetDron();
            if (dron == null)
            { Debug.LogWarning("[Shooter] GetDron() = null — geofence no enviado."); return; }

            var scenario = new List<List<(float lat, float lon)>> { RingFromBounds(planeBounds, FENCE_HALF) };

            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // brench (aviso de brecha) no puede ser null: EstableceEscenario lo
                    // invoca internamente sin comprobar null, y al omitirlo provoca un
                    // NullReferenceException aquí dentro.
                    dron.EstableceEscenario(
                        scenario,
                        brench: () => Debug.Log("[Shooter] Aviso de brecha del geofence."),
                        timeoutMs: FENCE_TIMEOUT_MS);
                    dron.EscribirParametros(new List<(string, float)>
                    {
                        ("FENCE_TYPE",   7f),
                        ("FENCE_ACTION", 4f),   // Brake al cruzar el límite
                        ("FENCE_ENABLE", 1f),
                    });
                    geofenceActivo = true;
                    Debug.Log("[Shooter] Geofence activo: 1 inclusión (perímetro del dronelab).");
                }
                catch (System.TimeoutException)
                { Debug.LogWarning("[Shooter] Timeout — el autopiloto no respondió."); }
                catch (System.Exception e)
                { Debug.LogWarning($"[Shooter] Error: {e}"); }
            });
        }

        void ClearGeofence(int waitMs)
        {
            if (!geofenceActivo) return;
            geofenceActivo = false;
            var d = dron; dron = null;
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try   { d.EscribirParametros(new List<(string, float)> { ("FENCE_ENABLE", 0f) }); } catch { }
                try   { d.BorraEscenario(); Debug.Log("[Shooter] Geofence borrado."); }
                catch (System.Exception e) { Debug.LogWarning($"[Shooter] Error al borrar: {e.Message}"); }
            });
            if (waitMs > 0) task.Wait(waitMs);
        }

        static List<(float lat, float lon)> RingFromBounds(Transform t, float half)
        {
            var corners = new[]
            {
                new Vector3(-half, 0f, -half),
                new Vector3( half, 0f, -half),
                new Vector3( half, 0f,  half),
                new Vector3(-half, 0f,  half),
            };
            var ring = new List<(float lat, float lon)>();
            foreach (var c in corners)
            {
                Vector3 w = t.TransformPoint(c);
                ring.Add(WorldToGPS(w.x, w.z));
            }
            return ring;
        }

        static (float lat, float lon) WorldToGPS(float wx, float wz)
        {
            float lat    = (wz + OFFSET_Z) / METERS_PER_DEG;
            float cosLat = Mathf.Cos(lat * Mathf.Deg2Rad);
            float lon    = (wx + OFFSET_X) / (cosLat * METERS_PER_DEG);
            return (lat, lon);
        }

        // Geofence invisible: el límite del campo de vuelo (FENCE_HALF) solo se
        // sube al autopiloto por MAVLink, sin representación visual.
        const float FENCE_HALF = 4.8f;  // algo más allá del límite de patrulla (4.6)

        GameObject CreateDronelab()
        {
            // GameObject vacío: solo necesitamos el Transform para TransformPoint().
            // Usar un Plane primitivo generaba una textura lila residual al ocultar su renderer.
            var go = new GameObject("dronelab");
            go.transform.position    = new Vector3(36f, 0f, 11f);
            go.transform.eulerAngles = new Vector3(0f, 73.1f, 0f);
            go.transform.localScale  = new Vector3(3.4f, 1f, 7.6f);
            return go;
        }

        void SpawnTarget()
        {
            if (planeBounds == null) return;

            GameObject go;

            // Nivel 3 = tamaño original (sizeMul 1.0), igual que antes. Por
            // encima de 3 la rampa es más pronunciada para que el nivel 10
            // llegue al doble de grande que antes (antes 10/3 ≈ 3.33x, ahora
            // 20/3 ≈ 6.67x), sin tocar los niveles 1-3.
            int   lvl     = ShooterSettings.EnemySizeLevel;
            float sizeMul = lvl <= 3 ? lvl / 3f : 1f + (lvl - 3) * (17f / 21f);
            if (droneTemplate != null)
            {
                go = Instantiate(droneTemplate);
                go.SetActive(true);
                go.name             = "AITarget";
                float sc = ShooterSettings.EnemyTypeIndex == 1 ? 0.08f
                         : DRONE_SCALE;
                go.transform.localScale = Vector3.one * sc * sizeMul;
            }
            else
            {
                // Fallback: cubo si el modelo no cargó
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name             = "AITarget";
                go.transform.localScale = Vector3.one * CUBE_SIZE * sizeMul;
                var mat = new Material(Shader.Find("Standard")) { color = Palette[colorIdx % Palette.Length] };
                go.GetComponent<Renderer>().material = mat;
                colorIdx++;
            }

            float lx = Random.Range(-4f, 4f);
            float lz = Random.Range(-4f, 4f);
            go.transform.position = planeBounds.TransformPoint(new Vector3(lx, 0.4f, lz));

            var ai         = go.AddComponent<AITarget>();
            ai.planeBounds = planeBounds;
            ai.speed       = Random.Range(SPEED_MIN, SPEED_MAX);
        }

        public void NotifyDestroyed()
        {
            StartCoroutine(RespawnAfter(RESPAWN_DELAY));

            // Dron extra por dificultad: se añade al enjambre una sola vez al
            // cruzar cada umbral de 10 kills, independiente del reemplazo de arriba.
            int desired = TargetCount();
            if (desired > lastTargetCount)
            {
                lastTargetCount = desired;
                SpawnTarget();
            }
        }

        IEnumerator RespawnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            SpawnTarget();
        }
    }
}
