using System.Collections.Generic;
using UnityEngine;
using ARDashboard;

/// Coloca en la escena los objetos definidos por el escenario de mapa activo.
/// Adjunta este script a un GameObject vacío en cada escena de escenario custom (Escenario_*.unity).
public class MapSceneController : MonoBehaviour
{
    const float M_PER_DEG = 111320f;

    // ── Geofence MAVLink (perímetro fijo del dronelab) ───────────────────────
    // Mismos criterio, constantes y transform del "dronelab" que
    // TargetSpawner.cs (shooter) y LabyrinthGeofenceManager.cs: todo
    // escenario creado desde el creador de misión también vuela con este
    // límite de seguridad alrededor del DronLab.
    const float OFFSET_X         = 166339.2f;
    const float OFFSET_Z         = 4594877f;
    const float FENCE_HALF       = 4.8f;
    const int   FENCE_TIMEOUT_MS = 8000;

    static csDronLink.Dron _dron;
    static bool            _geofenceActivo;

    // Se registra una única vez para que el geofence se borre al cerrar la
    // aplicación aunque nadie llame a ClearGeofence() explícitamente (antes
    // dependía de OnApplicationQuit/OnDestroy de una instancia que, en la
    // práctica, nunca llegaba a existir -- ver UploadGeofence/ClearGeofence).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RegisterQuitCleanup() => Application.quitting += () => ClearGeofence(waitMs: 2000);

    void Start()
    {
        var s = SelectedScenario.Current;
        if (s == null || string.IsNullOrEmpty(s.mapObjectsJson)) return;
        var list = JsonUtility.FromJson<MapObjectList>(s.mapObjectsJson);
        if (list?.objects == null) return;
        PlaceAll(list.objects);
        if (!s.geofenceDisabled) UploadGeofence();
    }

    void OnApplicationQuit() => ClearGeofence(waitMs: 2000);
    void OnDestroy()         => ClearGeofence(waitMs: 0);

    public static void UploadGeofence()
    {
        _dron = DroneMqttBridge.Instance?.GetDron();
        if (_dron == null)
        { Debug.LogWarning("[MapSceneController] GetDron() = null — geofence no enviado."); return; }

        var scenario = new List<List<(float lat, float lon)>> { RingFromDronelab(FENCE_HALF) };

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // brench (aviso de brecha) no puede ser null: EstableceEscenario lo
                // invoca internamente sin comprobar null (ver TargetSpawner.cs).
                _dron.EstableceEscenario(
                    scenario,
                    brench: () => Debug.Log("[MapSceneController] Aviso de brecha del geofence."),
                    timeoutMs: FENCE_TIMEOUT_MS);
                _dron.EscribirParametros(new List<(string, float)>
                {
                    ("FENCE_TYPE",   7f),
                    ("FENCE_ACTION", 4f),   // Brake al cruzar el límite
                    ("FENCE_ENABLE", 1f),
                });
                _geofenceActivo = true;
                Debug.Log("[MapSceneController] Geofence activo: 1 inclusión (perímetro del dronelab).");
            }
            catch (System.TimeoutException)
            { Debug.LogWarning("[MapSceneController] Timeout — el autopiloto no respondió."); }
            catch (System.Exception e)
            { Debug.LogWarning($"[MapSceneController] Error: {e}"); }
        });
    }

    public static void ClearGeofence(int waitMs)
    {
        if (!_geofenceActivo) return;
        _geofenceActivo = false;
        var d = _dron; _dron = null;
        var task = System.Threading.Tasks.Task.Run(() =>
        {
            try   { d.EscribirParametros(new List<(string, float)> { ("FENCE_ENABLE", 0f) }); } catch { }
            try   { d.BorraEscenario(); Debug.Log("[MapSceneController] Geofence borrado."); }
            catch (System.Exception e) { Debug.LogWarning($"[MapSceneController] Error al borrar: {e.Message}"); }
        });
        if (waitMs > 0) task.Wait(waitMs);
    }

    // Mismo perímetro fijo (posición/rotación/escala del "dronelab") que
    // TargetSpawner.CreateDronelab/RingFromBounds — se reconstruye aquí el
    // mismo Transform vía matriz en vez de depender de que la escena tenga
    // un GameObject "dronelab" (los escenarios de mapa no lo tienen).
    static List<(float lat, float lon)> RingFromDronelab(float half)
    {
        var m = Matrix4x4.TRS(
            new Vector3(36f, 0f, 11f),
            Quaternion.Euler(0f, 73.1f, 0f),
            new Vector3(3.4f, 1f, 7.6f));

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
            Vector3 w = m.MultiplyPoint3x4(c);
            ring.Add(WorldToGPS(w.x, w.z));
        }
        return ring;
    }

    static (float lat, float lon) WorldToGPS(float wx, float wz)
    {
        float lat    = (wz + OFFSET_Z) / M_PER_DEG;
        float cosLat = Mathf.Cos(lat * Mathf.Deg2Rad);
        float lon    = (wx + OFFSET_X) / (cosLat * M_PER_DEG);
        return (lat, lon);
    }

    // Inversa de WorldToGPS — misma conversión que usa PlaceAll para cada
    // objeto, expuesta aparte para que MapCreatorUI pueda ubicar un punto
    // GPS arbitrario (p. ej. el centro visible del mapa 2D) en las mismas
    // coordenadas de mundo que usan los objetos ya colocados.
    public static Vector3 GPSToWorld(float lat, float lon)
    {
        float cosLat = Mathf.Cos(lat * Mathf.Deg2Rad);
        float x = lon * cosLat * M_PER_DEG - OFFSET_X;
        float z = lat           * M_PER_DEG - OFFSET_Z;
        return new Vector3(x, 0f, z);
    }

    // "parent" es opcional: null (por defecto, uso normal del juego) deja los
    // objetos como raíces de la escena en su posición GPS absoluta de siempre.
    // Si se pasa un Transform, se agrupan ahí dentro (conservando su posición
    // de mundo) — lo usa MapCreatorUI para el panel de previsualización del
    // escenario, así puede limpiar todo destruyendo un único contenedor.
    public static void PlaceAll(List<MapPlacedObject> objects, Transform parent = null)
    {
        if (objects == null || objects.Count == 0) return;

        for (int i = 0; i < objects.Count; i++)
        {
            Vector3 basePos = GPSToWorld(objects[i].lat, objects[i].lon);
            float x = basePos.x;
            float z = basePos.z;
            // "0" es un valor de altura válido y con significado propio (la base del
            // objeto sobre el suelo, ver GroundWrapperToBase) desde que existe el
            // ajuste de altura=base — a diferencia del diámetro/tamaño de abajo, ya
            // NO puede tratarse como "sin fijar, usar 3 m por defecto", o cualquier
            // objeto dejado a su altura por defecto (0 en casi todos los tipos, ver
            // MapCreatorUI) aparecería flotando 3 m en vez de apoyado en el suelo.
            float h = objects[i].height;
            float d = objects[i].diameter > 0 ? objects[i].diameter : 3f;
            float grosor = objects[i].type == "Aro" ? (objects[i].aroGrosor > 0f ? objects[i].aroGrosor : 1f)
                         : objects[i].type == "Flecha" ? (objects[i].flechaGrosor > 0f ? objects[i].flechaGrosor : 1f)
                         : 1f;
            float hueco = objects[i].type == "Aro" ? objects[i].aroHueco : 0f;
            float longHoriz = objects[i].type == "Flecha"
                ? (objects[i].flechaLongHoriz > 0f ? objects[i].flechaLongHoriz : 3f)
                : 3f;
            PlaceObject(objects[i].type, new Vector3(x, 0f, z), i, objects[i],
                h, d, objects[i].rotX, objects[i].rotY, objects[i].rotZ, grosor, hueco, longHoriz, parent);
        }
    }

    static void PlaceObject(string type, Vector3 pos, int idx, MapPlacedObject obj,
        float height = 3f, float diameter = 3f,
        float rotX = 0f, float rotY = 0f, float rotZ = 0f, float grosor = 1f, float hueco = 0f,
        float longHoriz = 3f, Transform parent = null)
    {
        Debug.Log($"[MapSceneController] PlaceObject type='{type}' pos={pos} idx={idx}");

        // Mismo prefab de color por variante que eligió el usuario en el
        // previsualizador del creador de misión (Assets/Resources/Prefabs/<Tipo>_<Variante>);
        // si no existe (p. ej. escenarios antiguos sin variante guardada) se cae al
        // prefab genérico de siempre.
        string variant = MapCreatorUI.GetObjectVariantName(obj);
        Vector3 targetPos = pos + Vector3.up * height;

        switch (type)
        {
            case "Aro":
            {
                var rot   = Quaternion.Euler(rotX, rotY, rotZ);
                var variantPrefab = variant != null ? Resources.Load<GameObject>("Prefabs/Aro_" + variant) : null;
                var prefab = variantPrefab != null ? variantPrefab : Resources.Load<GameObject>("Prefabs/Aro");
                if (prefab == null)
                {
                    var ring = CreateRing(pos, height, diameter);
                    ring.transform.rotation = rot;
                    if (parent != null) ring.transform.SetParent(parent, true);
                    return;
                }
                // "diameter" es un valor real en metros, y aquí (igual que la conversión
                // GPS→Unity de arriba, M_PER_DEG) 1 unidad Unity = 1 metro real. En vez de
                // asumir un tamaño de referencia fijo para el asset (que no tenía por qué
                // coincidir con lo que mide realmente el prefab), se mide su diámetro real
                // a escala 1 (MeshNativeDiameter) y se escala para que el resultado final
                // mida EXACTAMENTE "diameter" unidades/metros. Mismo criterio en
                // MapCreatorUI.MeasureMeshDiameter/ApplyAroScale (previsualizador).
                float scale = diameter / MeshNativeDiameter(prefab);
                var inst = InstantiateKeepingLocalRotation(prefab, $"Aro_{idx}", targetPos, rot, out _, parent);
                // El grosor solo escala el eje Z local (fuera del plano del aro), sin
                // afectar al diámetro (X/Y): ver comentario en MapCreatorUI.ApplyAroScale.
                inst.transform.localScale = new Vector3(scale, scale, scale * grosor);
                AroHuecoUtil.ApplyHueco(inst, hueco);
                // El material "Aro" a pelo es solo el rojo de siempre: no pisar el
                // material propio del prefab de variante (ya trae su color correcto).
                if (variantPrefab == null)
                {
                    var mat = Resources.Load<Material>("Materials/Aro");
                    if (mat != null)
                        foreach (var r in inst.GetComponentsInChildren<Renderer>())
                            r.material = mat;
                }
                MapCreatorUI.RecenterInPlace(inst);
                MapCreatorUI.GroundWrapperToBase(inst);
                return;
            }
            case "Bandera":
            {
                var prefab = (variant != null ? Resources.Load<GameObject>("Prefabs/Bandera_" + variant) : null)
                             ?? Resources.Load<GameObject>("Prefabs/bandera");
                if (prefab != null)
                {
                    var inst = InstantiateKeepingLocalRotation(prefab, $"Bandera_{idx}", targetPos,
                        Quaternion.Euler(rotX, rotY, 0f), out var wrapper, parent);
                    float s = diameter > 0 ? diameter : 1f;
                    wrapper.transform.localScale = Vector3.one * (s * MapCreatorUI.BANDERA_GAME_SCALE);
                    MapCreatorUI.GroundWrapperToBase(inst);
                }
                return;
            }
            case "Obstáculo":
            {
                var prefab = (variant != null ? Resources.Load<GameObject>("Prefabs/Obstaculo_" + variant) : null)
                             ?? Resources.Load<GameObject>("Prefabs/obstaculo");
                if (prefab != null)
                {
                    var inst = InstantiateKeepingLocalRotation(prefab, $"Obstáculo_{idx}", targetPos,
                        Quaternion.Euler(rotX, rotY, 0f), out var wrapper, parent);
                    wrapper.transform.localScale = Vector3.one * MapCreatorUI.OBSTACULO_GAME_SCALE;

                    // Mismo criterio que el previsualizador (MapCreatorUI.CreatePreviewMesh,
                    // caso "Obstáculo"): la barra (Body) nunca se escala (conserva su forma
                    // redonda), solo el soporte escala en Z con la longitud vertical, y la
                    // barra se reposiciona para seguir subiendo con él. X = longitud
                    // horizontal, afecta a toda la pieza por igual.
                    float longHorizObst = obj.obstaculoLongHoriz > 0f ? obj.obstaculoLongHoriz : 3f;
                    float longVertObst  = obj.obstaculoLongVert  > 0f ? obj.obstaculoLongVert  : 3f;
                    var supportChild = MapCreatorUI.SplitObstaculoSupport(inst, out var bodyChild, out var barRefZ);
                    inst.transform.localScale = new Vector3(longHorizObst / 3f, 1f, 1f);
                    if (supportChild != null)
                        supportChild.transform.localScale = new Vector3(1f, 1f, longVertObst / 3f);
                    if (bodyChild != null)
                        bodyChild.transform.localPosition = new Vector3(0f, 0f, barRefZ * (longVertObst / 3f - 1f));

                    // El soporte tiene un color independiente del cuerpo.
                    if (supportChild != null)
                    {
                        var supportMat = Resources.Load<Material>("Materials/Obstaculo_" + obj.obstaculoSupportVariant);
                        if (supportMat != null)
                        {
                            var sr = supportChild.GetComponent<Renderer>();
                            if (sr) sr.sharedMaterial = supportMat;
                        }
                    }
                    MapCreatorUI.RecenterInPlace(inst);
                    MapCreatorUI.GroundWrapperToBase(inst);
                }
                return;
            }
            case "Moneda":
            {
                var prefab = (variant != null ? Resources.Load<GameObject>("Prefabs/Moneda_" + variant) : null)
                             ?? Resources.Load<GameObject>("Prefabs/moneda");
                if (prefab != null)
                {
                    var inst = InstantiateKeepingLocalRotation(prefab, $"Moneda_{idx}", targetPos,
                        Quaternion.Euler(rotX, rotY, 0f), out var wrapper, parent);
                    // "diameter" es un valor real en metros; igual criterio que el caso
                    // "Aro" de arriba (MeshNativeDiameter) en vez de usarlo directamente
                    // como factor de escala, que solo coincidiría con la vida real si el
                    // modelo ya medía 1 unidad de diámetro. Mismo criterio en
                    // MapCreatorUI.ApplyMonedaScale (previsualizador).
                    float d = diameter > 0 ? diameter : 1f;
                    float scale = d / MeshNativeDiameter(prefab);
                    inst.transform.localScale = Vector3.one * scale;
                    if (wrapper.GetComponent<coin>() == null) wrapper.AddComponent<coin>();
                    MapCreatorUI.GroundWrapperToBase(inst);
                }
                return;
            }
            case "Arco":
            {
                var prefab = (variant != null ? Resources.Load<GameObject>("Prefabs/Arco_" + variant) : null)
                             ?? Resources.Load<GameObject>("Prefabs/Arco");
                if (prefab != null)
                {
                    var inst = InstantiateKeepingLocalRotation(prefab, $"Arco_{idx}", targetPos,
                        Quaternion.Euler(rotX, rotY, 0f), out _, parent);
                    // Mismo criterio que el previsualizador (MapCreatorUI.CreatePreviewMesh,
                    // caso "Arco"): Y escala con la horizontal (no la vertical) para que el
                    // grosor del tubo se mantenga proporcionado y redondo en las patas.
                    float longHorizArco = obj.arcoLongHoriz > 0f ? obj.arcoLongHoriz : 3f;
                    float longVertArco  = obj.arcoLongVert  > 0f ? obj.arcoLongVert  : 3f;
                    inst.transform.localScale = new Vector3(longHorizArco / 3f, longHorizArco / 3f, longVertArco / 3f);
                    MapCreatorUI.RecenterInPlace(inst);
                    MapCreatorUI.GroundWrapperToBase(inst);
                }
                return;
            }
            case "Segundo Arco":
            {
                var prefab = (variant != null ? Resources.Load<GameObject>("Prefabs/Arco2_" + variant) : null)
                             ?? Resources.Load<GameObject>("Prefabs/Arco2");
                if (prefab != null)
                {
                    var inst = InstantiateKeepingLocalRotation(prefab, $"Arco2_{idx}", targetPos,
                        Quaternion.Euler(rotX, rotY, 0f), out _, parent);
                    float longHorizArco2 = obj.arco2LongHoriz > 0f ? obj.arco2LongHoriz : 3f;
                    float longVertArco2  = obj.arco2LongVert  > 0f ? obj.arco2LongVert  : 3f;
                    inst.transform.localScale = new Vector3(longHorizArco2 / 3f, longHorizArco2 / 3f, longVertArco2 / 3f);
                    // El pivote de este modelo está muy lejos de su centro visual (mismo
                    // problema que en el previsualizador: MapCreatorUI.InstantiateCentered):
                    // sin recolocar por el centro real, giraría en órbita en vez de sobre su
                    // propio eje y no coincidiría con lo visto en el previsualizador.
                    MapCreatorUI.RecenterInPlace(inst);
                    MapCreatorUI.GroundWrapperToBase(inst);
                }
                return;
            }
            case "Flecha":
            {
                var prefab = (variant != null ? Resources.Load<GameObject>("Prefabs/Flecha_" + variant) : null)
                             ?? Resources.Load<GameObject>("Prefabs/flecha4");
                if (prefab != null)
                {
                    var inst = InstantiateKeepingLocalRotation(prefab, $"Flecha_{idx}", targetPos,
                        Quaternion.Euler(rotX, rotY, 0f), out _, parent);
                    // Ver comentario en MapCreatorUI.CreatePreviewMesh caso "Flecha": sin
                    // resetear esto, rotY deja de girar la punta como una brújula en cuanto
                    // es distinto de 0.
                    inst.transform.localRotation = Quaternion.identity;
                    float s = (diameter > 0 ? diameter : 1f) * MapCreatorUI.FLECHA_GAME_SCALE;
                    // X (longitud, punta a cola) escala además por longHoriz/3 encima del
                    // tamaño general "s", igual criterio que MapCreatorUI.ApplyFlechaScale.
                    inst.transform.localScale = new Vector3(s * (longHoriz / 3f), s, s * grosor);
                    MapCreatorUI.RecenterInPlace(inst);
                    MapCreatorUI.GroundWrapperToBase(inst);
                }
                return;
            }
            default:
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.position = pos + Vector3.up;
                SetColor(go, new Color(0f, 0.95f, 1f));
                go.name = $"Obj_{idx}";
                if (parent != null) go.transform.SetParent(parent, true);
                return;
            }
        }
    }

    // Crea un contenedor en la posición/rotación final e instancia el prefab
    // como hijo CONSERVANDO su propia rotación local (Instantiate(prefab, parent),
    // igual que MapCreatorUI.CreatePreviewMesh): algunos prefabs de color traen
    // ya horneada una corrección de rotación en el propio asset (p. ej. -90° en
    // X para modelos modelados tumbados). Si se instanciara directamente en la
    // posición/rotación final (pisando esa rotación local), esa corrección se
    // perdería y el objeto aparecería tumbado en vez de en la orientación vista
    // en el previsualizador.
    static GameObject InstantiateKeepingLocalRotation(GameObject prefab, string name,
        Vector3 targetPos, Quaternion rot, out GameObject wrapper, Transform parent = null)
    {
        wrapper = new GameObject(name);
        wrapper.transform.SetPositionAndRotation(targetPos, rot);
        // true = worldPositionStays: agrupa bajo "parent" sin mover el objeto de
        // la posición de mundo (GPS) ya calculada.
        if (parent != null) wrapper.transform.SetParent(parent, true);
        var inst = (GameObject)Object.Instantiate(prefab, wrapper.transform);
        inst.transform.localPosition = Vector3.zero;
        return inst;
    }

    // Diámetro real (en unidades Unity = metros, a escala 1) del propio asset del
    // prefab: mide los bounds LOCALES de la malla (Mesh.bounds, independientes de
    // cualquier rotación/escala/jerarquía del GameObject) y toma el eje más grande,
    // que para un aro (disco fino) es siempre el diámetro exterior, nunca el grosor
    // del tubo. Se puede llamar directamente sobre el asset del prefab (sin
    // instanciar), GetComponentsInChildren funciona igual sobre un asset cargado con
    // Resources.Load. Mismo criterio en MapCreatorUI.MeasureMeshDiameter.
    static float MeshNativeDiameter(GameObject prefabAsset)
    {
        float maxDim = 0f;
        foreach (var mf in prefabAsset.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var s = mf.sharedMesh.bounds.size;
            maxDim = Mathf.Max(maxDim, s.x, s.y, s.z);
        }
        return maxDim > 0.0001f ? maxDim : 3f;
    }

    // Forma un aro con 8 cilindros en círculo (puerta de dron)
    static GameObject CreateRing(Vector3 pos, float height = 3f, float diameter = 3f)
    {
        const int segs = 8;
        float     radius = diameter / 2f;

        var root = new GameObject("Aro");
        root.transform.position = pos;

        float circumference = 2f * Mathf.PI * radius;
        float segLen        = circumference / segs;

        for (int i = 0; i < segs; i++)
        {
            float angle = i * 360f / segs;
            float rad   = angle * Mathf.Deg2Rad;

            var seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seg.transform.SetParent(root.transform, false);
            seg.transform.localPosition = new Vector3(
                Mathf.Cos(rad) * radius, height, Mathf.Sin(rad) * radius);
            seg.transform.localScale    = new Vector3(0.15f, segLen / 2f, 0.15f);
            seg.transform.localRotation = Quaternion.Euler(0f, -angle, 90f);
            SetColor(seg, new Color(0f, 0.95f, 1f));
        }
        return root;
    }

    static void SetColor(GameObject go, Color color)
    {
        var r = go.GetComponent<Renderer>();
        if (r) r.material.color = color;
    }
}
