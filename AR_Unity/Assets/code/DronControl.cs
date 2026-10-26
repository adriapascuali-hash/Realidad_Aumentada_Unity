using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

// Representación puramente visual del dron en la escena: carga el modelo 3D y
// mueve/orienta el objeto según la telemetría más reciente. Ya NO posee
// ninguna conexión MAVLink propia (ni SITL ni puerto COM) — quien conecta,
// desconecta y recibe la telemetría de csDronLink es siempre ARDashboard.DroneMqttBridge
// (persistente entre escenas), y este componente solo LEE esos datos cada
// frame para interpolar su transform.
public class DronControl : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Escala de la escena")]
    [Tooltip("Unidades Unity por metro real")]
    public float sceneScale = 1.0f;

    [Header("Suavizado de movimiento")]
    [Range(1f, 30f)]
    public float lerpSpeed = 10f;

    const float METERS_PER_DEG_LAT = 111320f;

    // ═════════════════════════════════════════════════════════════════════════
    void Start()
    {
        // ── Quitar la malla de cubo y cargar el modelo real ───────────────
        var mr = GetComponent<MeshRenderer>(); if (mr) Destroy(mr);
        var mf = GetComponent<MeshFilter>();   if (mf) Destroy(mf);
        _ = LoadDroneModelAsync();
    }

    async Task LoadDroneModelAsync()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Drone", "scene.gltf");
        if (!File.Exists(path)) return;

        var  gltf = new GltfImport();
        bool ok   = await gltf.Load(path);
        if (!ok) return;

        var model = new GameObject("DroneModel");
        model.transform.SetParent(transform, false);
        await gltf.InstantiateMainSceneAsync(model.transform);
        model.transform.localScale = Vector3.one * 0.5f;

        Debug.Log("[Drone] Modelo 3D cargado.");
    }

    void Update()
    {
        var bridge = ARDashboard.DroneMqttBridge.Instance;
        if (bridge == null) return;

        var snap = bridge.GetTelemetrySnapshot();
        if (!snap.hasData || (snap.lat == 0f && snap.lon == 0f)) return;

        float cosLat = Mathf.Cos(snap.lat * Mathf.Deg2Rad);
        float x = snap.lon * cosLat * METERS_PER_DEG_LAT * sceneScale - 166339.2f;
        float z = snap.lat           * METERS_PER_DEG_LAT * sceneScale - 4594877f;
        float y = snap.alt * sceneScale;

        Vector3    targetPos = new Vector3(x, y, z);
        Quaternion targetRot = Quaternion.Euler(-snap.pitch, snap.heading, -snap.roll);

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * lerpSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * lerpSpeed);
    }
}
