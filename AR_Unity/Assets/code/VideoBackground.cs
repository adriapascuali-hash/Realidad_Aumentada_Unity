using UnityEngine;

public enum VideoSource { LocalWebcam }

// ──────────────────────────────────────────────────────────────
// Gestión de webcam local
// ──────────────────────────────────────────────────────────────
public class LocalWebcamCapture
{
    private WebCamTexture webCamTexture;
    public bool IsPlaying => webCamTexture != null && webCamTexture.isPlaying;

    public void Start(int deviceIndex = 0)
    {
        Stop();
        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogWarning("[Webcam] No se encontró ninguna cámara.");
            return;
        }
        int idx = Mathf.Clamp(deviceIndex, 0, devices.Length - 1);
        webCamTexture = new WebCamTexture(devices[idx].name, 640, 360, 30);
        webCamTexture.Play();
        Debug.Log($"[Webcam] Iniciada: {devices[idx].name}");
    }

    public void Stop()
    {
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            UnityEngine.Object.Destroy(webCamTexture);
            webCamTexture = null;
        }
    }

    public Texture GetTexture() => IsPlaying ? webCamTexture : null;

    public static string[] GetDeviceNames()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        string[] names = new string[devices.Length];
        for (int i = 0; i < devices.Length; i++)
            names[i] = devices[i].name;
        return names;
    }
}

// ──────────────────────────────────────────────────────────────
// Controlador principal
// ──────────────────────────────────────────────────────────────
public class VideoBackground : MonoBehaviour
{
    [Header("Cámara del dron")]
    [Tooltip("Arrastra aquí el Main Camera hijo del Dron. Si está vacío usa Camera.main.")]
    public Camera droneCamera;

    [Header("Video Source")]
    public VideoSource activeSource      = VideoSource.LocalWebcam;
    public int         webcamDeviceIndex = 0;

    [Header("Video Quad")]
    [Tooltip("Distancia del quad delante de la camara (en unidades mundo).")]
    public float quadDistance = 1000;

    [Tooltip("Material Unlit/Texture (o URP Unlit). Asígnalo en el Inspector para que se incluya en el build.")]
    public Material videoMaterial;

    [Header("Corrección Ojo de Pez")]
    [Tooltip("Activa la corrección de distorsión usando los parámetros de calibración de la cámara dron.")]
    public bool applyFisheyeCorrection = true;

    [Tooltip("Ancho de las imágenes usadas en la calibración (px). Fichero: calibration_data_px.yaml")]
    public float calibImageWidth  = 640f;
    [Tooltip("Alto de las imágenes usadas en la calibración (px).")]
    public float calibImageHeight = 480f;

    [Tooltip("Distancia focal X en píxeles (camera_matrix[0][0])")]
    public float calibFx =  283.43527f;
    [Tooltip("Distancia focal Y en píxeles (camera_matrix[1][1])")]
    public float calibFy =  376.57420f;
    [Tooltip("Punto principal X en píxeles (camera_matrix[0][2])")]
    public float calibCx =  332.19374f;
    [Tooltip("Punto principal Y en píxeles (camera_matrix[1][2])")]
    public float calibCy =  258.72946f;

    [Tooltip("Coeficiente de distorsión radial k1")]
    public float calibK1 = -0.34004759f;
    [Tooltip("Coeficiente de distorsión radial k2")]
    public float calibK2 =  0.12975583f;
    [Tooltip("Coeficiente de distorsión radial k3")]
    public float calibK3 = -0.02344312f;
    [Tooltip("Coeficiente de distorsión tangencial p1")]
    public float calibP1 = -0.00089245f;
    [Tooltip("Coeficiente de distorsión tangencial p2")]
    public float calibP2 = -0.00348249f;

    private LocalWebcamCapture localWebcam = new LocalWebcamCapture();

    // ── Referencia al Renderer del Quad 3D ─────────────────────────────
    private Renderer  videoRenderer;
    private Transform videoQuadTransform;
    private float     lastAspect = -1f;
    private float     lastFov    = -1f;
    private int       lastScreenW = -1;
    private int       lastScreenH = -1;

    // ═════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    // Se ejecuta al añadir el componente desde el Inspector, para que el
    // GameObject nazca siempre con un nombre coherente con lo que hace.
    void Reset()
    {
        if (gameObject.name == "GameObject")
            gameObject.name = "VideoBackground";
    }
#endif

    void Start()
    {
        activeSource      = GameConfig.SelectedSource;
        webcamDeviceIndex = GameConfig.WebcamDeviceIndex;

        if (droneCamera == null)
            droneCamera = Camera.main;

        SetupVideoQuad();
        localWebcam.Start(webcamDeviceIndex);
    }

    void Update()
    {
        UpdateQuadScale();
        UpdateWebcamFrame();
    }

    void OnDestroy()
    {
        localWebcam.Stop();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void SetSource(VideoSource source)
    {
        if (activeSource == source) return;
        activeSource = source;
        localWebcam.Start(webcamDeviceIndex);
        Debug.Log($"[Video] Fuente cambiada a: {source}");
    }

    // ── Update frames ─────────────────────────────────────────────────────────

    void UpdateWebcamFrame()
    {
        if (videoRenderer == null) return;

        Texture tex = localWebcam.GetTexture();
        if (tex == null) return;

        videoRenderer.material.mainTexture = tex;

        var wct = tex as WebCamTexture;
        if (wct != null)
        {
            Vector2 scale  = videoRenderer.material.mainTextureScale;
            Vector2 offset = videoRenderer.material.mainTextureOffset;

            if (wct.videoVerticallyMirrored)
            {
                scale.y  = -1f;
                offset.y =  1f;
            }
            else
            {
                scale.y  =  1f;
                offset.y =  0f;
            }

            videoRenderer.material.mainTextureScale  = scale;
            videoRenderer.material.mainTextureOffset = offset;
        }
    }

    // ── Reajuste dinámico del Quad ────────────────────────────────────────────

    void UpdateQuadScale()
    {
        if (videoQuadTransform == null) return;

        float aspect = droneCamera.aspect;
        int   sw     = Screen.width;
        int   sh     = Screen.height;

        float fov = droneCamera.fieldOfView;

        if (Mathf.Approximately(aspect, lastAspect) &&
            Mathf.Approximately(fov,    lastFov)    &&
            sw == lastScreenW && sh == lastScreenH)
            return;

        lastAspect  = aspect;
        lastFov     = fov;
        lastScreenW = sw;
        lastScreenH = sh;

        float screenAspect = (float)sw / sh;

        Vector3[] corners = new Vector3[4];
        droneCamera.CalculateFrustumCorners(
            new Rect(0, 0, 1, 1),
            quadDistance,
            Camera.MonoOrStereoscopicEye.Mono,
            corners);

        Vector3 wBL = droneCamera.transform.TransformPoint(corners[0]);
        Vector3 wTL = droneCamera.transform.TransformPoint(corners[1]);
        Vector3 wTR = droneCamera.transform.TransformPoint(corners[2]);

        float worldWidth  = Vector3.Distance(wTL, wTR);
        float worldHeight = Vector3.Distance(wBL, wTL);

        worldWidth = worldHeight * screenAspect;

        Vector3 ls = droneCamera.transform.lossyScale;
        videoQuadTransform.localScale = new Vector3(
            worldWidth  / Mathf.Max(ls.x, 0.0001f),
            worldHeight / Mathf.Max(ls.y, 0.0001f),
            1f);

        Debug.Log($"[Video] Quad reescalado — screen={sw}x{sh}  aspect={screenAspect:F3}  w={worldWidth:F2}  h={worldHeight:F2}");
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    void SetupVideoQuad()
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "VideoQuad";

        quad.transform.SetParent(droneCamera.transform, false);

        quad.transform.localPosition = new Vector3(0f, 0f, quadDistance);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale    = Vector3.one;

        Vector3[] corners = new Vector3[4];
        droneCamera.CalculateFrustumCorners(
            new Rect(0, 0, 1, 1),
            quadDistance,
            Camera.MonoOrStereoscopicEye.Mono,
            corners);

        Vector3 wBL = droneCamera.transform.TransformPoint(corners[0]);
        Vector3 wTL = droneCamera.transform.TransformPoint(corners[1]);
        Vector3 wTR = droneCamera.transform.TransformPoint(corners[2]);

        float worldWidth  = Vector3.Distance(wTL, wTR);
        float worldHeight = Vector3.Distance(wBL, wTL);

        Vector3 ls = droneCamera.transform.lossyScale;
        quad.transform.localScale = new Vector3(
            worldWidth  / Mathf.Max(ls.x, 0.0001f),
            worldHeight / Mathf.Max(ls.y, 0.0001f),
            1f);

        Material mat;
        if (applyFisheyeCorrection)
        {
            Shader fisheyeShader = Resources.Load<Shader>("FisheyeUndistort")
                                ?? Shader.Find("Custom/FisheyeUndistort");
            if (fisheyeShader != null)
            {
                mat = new Material(fisheyeShader);
                ApplyFisheyeParams(mat);
                Debug.Log("[Video] Shader de corrección ojo de pez activado.");
            }
            else
            {
                Debug.LogWarning("[Video] Shader 'Custom/FisheyeUndistort' no encontrado. Usando Unlit/Texture. Asegúrate de que el shader está en Assets/Shaders/.");
                mat = videoMaterial != null
                    ? new Material(videoMaterial)
                    : new Material(Shader.Find("Unlit/Texture"));
            }
        }
        else
        {
            mat = videoMaterial != null
                ? new Material(videoMaterial)
                : new Material(Shader.Find("Unlit/Texture"));
        }

        videoRenderer      = quad.GetComponent<Renderer>();
        videoRenderer.material = mat;
        videoQuadTransform = quad.transform;

        var col = quad.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);

        videoRenderer.sortingOrder = -100;

        Debug.Log($"[Video] Quad — dist={quadDistance}  worldW={worldWidth:F2}  worldH={worldHeight:F2}");
    }

    void ApplyFisheyeParams(Material mat)
    {
        float w = calibImageWidth;
        float h = calibImageHeight;
        mat.SetFloat("_FxN", calibFx / w);
        mat.SetFloat("_FyN", calibFy / h);
        mat.SetFloat("_CxN", calibCx / w);
        mat.SetFloat("_CyN", calibCy / h);
        mat.SetFloat("_K1",  calibK1);
        mat.SetFloat("_K2",  calibK2);
        mat.SetFloat("_K3",  calibK3);
        mat.SetFloat("_P1",  calibP1);
        mat.SetFloat("_P2",  calibP2);
    }
}
