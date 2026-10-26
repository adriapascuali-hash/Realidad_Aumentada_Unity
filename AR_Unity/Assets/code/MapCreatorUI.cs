using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;
using ARDashboard;

public class MapCreatorUI : MonoBehaviour
{
    static readonly Color C_ACCENT = new Color(0.15f, 1.00f, 0.70f, 1f);
    static readonly Color C_CYAN   = new Color(0.00f, 0.95f, 1.00f, 1f);
    static readonly Color C_BORDER = new Color(0.16f, 0.22f, 0.32f, 1f);
    static readonly Color C_TEXT   = new Color(0.90f, 0.95f, 1.00f, 1f);
    static readonly Color C_SUB    = new Color(0.42f, 0.58f, 0.72f, 1f);
    static readonly Color C_DARK   = new Color(0.01f, 0.02f, 0.06f, 1f);

    const int TILE_PIX = 256;
    const int GRID_W   = 5;
    const int GRID_H   = 4;

 // Zoom
    // Los proveedores de teselas no suelen tener imagen real más allá de este
    // nivel en muchas zonas (p. ej. la del DronLab): a partir de ahí no hay más
    // detalle que pedir. En vez de quedarse bloqueado ahí, se permite seguir
    // acercando hasta MAX_ZOOM recortando y estirando digitalmente la última
    // tesela real disponible (zoom "artificial", ver FetchTile).
    const int MAX_REAL_ZOOM = 19;
    const int MAX_ZOOM      = 22;

 // Map providers

    struct TileProvider { public string name, url, attrib; }
    TileProvider[] providers;
    int providerIdx = 1; // 0=Calles, 1=Satélite (por defecto)

 // Map state

    int    zoom      = 6;
    double centerLat = 46.0;
    double centerLon = 8.0;
    int    startTileX, startTileY;

 // UI refs

    RawImage[,]   tiles;
    Transform     markersParent;
    GameObject    locationMarker;   // punto azul de ubicación detectada
    RectTransform locationIconRt;   // icono junto al botón "Usar mi ubicación"
    double        locationLat = double.NaN;
    double        locationLon = double.NaN;
    RectTransform mapAreaRt;
    Transform     mapContainerTr;
    RectTransform leftPanelRt;
    RectTransform rightPanelRt;
    RectTransform dividerRt;
    RectTransform dragTabRt;
    TextMeshProUGUI coordsLabel;
    TextMeshProUGUI _lonLabel;
    TextMeshProUGUI zoomLabel;
    TextMeshProUGUI attribLabel;
    TextMeshProUGUI previewNameLabel;
    TextMeshProUGUI locateBtnLabel;
    TMP_InputField  inputName;
    GameObject      ownCanvas;
    Canvas          ownCanvasComp;

 // Paneo
    bool    isPanning          = false;
    bool    isDraggingDivider  = false;
    float   dividerDragStartX  = 0f;
    float   dividerSplitStart  = 0f;
    Vector2 panMouseStart;
    double  panLonStart;
    double  panLatStart;
    float   panLastReload;

    TextMeshProUGUI typeDropdownLabel;
    TextMeshProUGUI mapDropdownLabel;
    GameObject      ctxMenuGO;          // popup flotante activo (bloqueador + panel)
    RectTransform   scenPrevBtnRt;      // botón "Previsualización" (overlay encima del mapa) — excluido de overMap
    RectTransform   geofenceBtnRt;      // botón "Geofence dronelab" (overlay encima del mapa) — excluido de overMap

 // Preview 3D
    RenderTexture previewRT;
    Camera        previewCam;
    GameObject    previewRoot;
    Vector3       previewEuler = new Vector3(28f, 45f, 0f);
    Scene         previewScene;

    // Crear una Scene aparte (previewScene/scenarioPreviewScene) solo separa la
    // JERARQUÍA, no el renderizado: una Camera con cullingMask por defecto
    // ("Everything") pinta cualquier GameObject activo dentro de su frustum sin
    // importar a qué Scene pertenezca. Por eso el aro del previsualizador de
    // objeto podía "colarse" en la Previsualización de Misión (y viceversa) si
    // sus posiciones en el mundo coincidían. Se reservan dos capas sin usar
    // (30/31) para que cada cámara solo vea los objetos de su propio
    // previsualizador — no requiere tocar Tags & Layers en el editor: cualquier
    // índice de capa libre funciona en tiempo de ejecución aunque no tenga
    // nombre asignado.
    const int OBJECT_PREVIEW_LAYER   = 30;
    const int SCENARIO_PREVIEW_LAYER = 31;

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

 // Previsualización del escenario completo (todos los objetos ya colocados,
    // con la misma instanciación que la misión real — ver MapSceneController.
    // PlaceAll) — cámara orbital independiente de la del previsualizador de
    // un solo objeto de arriba, en su propia escena aislada.
    GameObject    scenarioPreviewPanel;
    RawImage      scenarioPreviewRawImg;
    RenderTexture scenarioPreviewRT;
    Camera        scenarioPreviewCam;
    Scene         scenarioPreviewScene;
    bool          scenarioPreviewSceneReady;
    GameObject    scenarioPreviewRoot;
    Vector3       scenarioPreviewLookAt;
    float         scenarioPreviewYaw   = 0f;
    float         scenarioPreviewPitch = 35f;
    float         scenarioPreviewDist  = 60f;
    RectTransform scenarioPreviewPanelRt;
    RectTransform scenarioPreviewMaxIconRt;
    bool          scenarioPreviewMaximized;
    readonly Vector2 scenarioPreviewNormalSize = new Vector2(1000f, 680f);
    static readonly Vector2 MAXICON_SIZE_WINDOWED  = new Vector2(14f, 14f); // ventana pequeña -> icono grande
    static readonly Vector2 MAXICON_SIZE_MAXIMIZED = new Vector2(8f, 8f);  // ventana maximizada -> icono pequeño

 // Aro properties
    float           aroHeight    = 3f;
    float           aroDiameter  = 3f;
    float           aroRotX      = 90f; // vista frontal por defecto (el modelo está modelado tumbado en el plano XZ)
    float           aroRotY      = 0f;
    float           aroRotZ      = 0f;
    float           aroGrosor    = 1f; // factor de escala del grosor del tubo (eje Z local, fuera del plano del aro); 1 = original
    Slider          aroGrosorSlider;
    TMP_InputField  aroGrosorLabel;
    float           aroHueco     = 0f; // agranda/encoge el hueco central del aro (positivo = banda más ancha/hueco menor); 0 = original
    Slider          aroHuecoSlider;
    TMP_InputField  aroHuecoLabel;
    GameObject      aroPreviewInstance; // instancia actual en el visualizador, para aplicar el grosor/hueco en vivo
    RectTransform   lastAroIconRt;

 // Aro object variant (qué objeto de aro, entre los disponibles en Resources/Prefabs, se coloca)
    int             aroVariantIdx = 0;
    Image           aroVariantSwatch;
    static readonly string[] AroVariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Bandera properties
    // El modelo importado de la bandera es mucho más pequeño, a igualdad de
    // escala, que el resto de objetos (Aro/Arco/Moneda) — estos factores
    // compensan esa diferencia para que a tamaño "1" en el slider se vea a
    // una escala comparable a la de los demás. Dos factores separados porque
    // el previsualizador del creador (vista 2D del mapa) y la escena final
    // de la misión (ScenarioSceneCreator) no comparten la misma referencia
    // de escala.
    public const float BANDERA_BASE_SCALE = 5000f; // previsualizador del creador de misión
    public const float BANDERA_GAME_SCALE = 1f;    // objeto real en la misión ya creada

    // Mismo motivo que BANDERA_GAME_SCALE: el objeto real de la Flecha en la
    // misión ya creada aparecía demasiado pequeño. Solo afecta a la
    // instanciación real (ScenarioSceneCreator/MapSceneController), no al
    // previsualizador del creador de misión.
    public const float FLECHA_GAME_SCALE = 50f;

    // Mismo motivo que BANDERA_GAME_SCALE/FLECHA_GAME_SCALE: el Obstáculo real
    // en la misión ya creada se veía demasiado pequeño en proporción al resto
    // de objetos (Aro, Arco...). Solo afecta a la instanciación real.
    public const float OBSTACULO_GAME_SCALE = 150f;

    // El previsualizador es solo para elegir forma/color/estilo — el tamaño
    // real (metros) no debe cambiar cómo se ve ahí, solo el dato guardado en
    // el objeto para cuando se genere la misión de verdad. Por eso el slider
    // de "Tamaño"/"Diámetro" de cada tipo NO debe tocar la escala del
    // previsualizador: se usa esta referencia fija en su lugar (grosor/
    // longitud sí siguen afectando la forma visible, esos no son "tamaño").
    const float PREVIEW_REF_DIAMETER = 3f; // Aro (su valor por defecto)
    const float PREVIEW_REF_SIZE     = 1f; // Bandera/Moneda/Flecha (su valor por defecto)

    float           banderaHeight = 0f;
    float           banderaSize   = 1f;
    float           banderaRotX   = 0f;
    float           banderaRotY   = 0f;
    GameObject      banderaPropsPanel;
    Slider          banderaHeightSlider, banderaSizeSlider, banderaRotXSlider, banderaRotYSlider;
    TMP_InputField  banderaHeightLabel,  banderaSizeLabel,  banderaRotXLabel,  banderaRotYLabel;
    RectTransform   lastBanderaIconRt;
    GameObject      banderaPreviewInstance; // instancia actual en el visualizador, para aplicar el tamaño en vivo

 // Bandera object variant (qué objeto de bandera, entre los disponibles en Resources/Prefabs, se coloca)
    int             banderaVariantIdx = 0;
    Image           banderaVariantSwatch;
    static readonly string[] BanderaVariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Obstáculo properties
    float           obstaculoHeight = 0f;
    float           obstaculoSize   = 1f;
    float           obstaculoRotX   = 0f;
    float           obstaculoRotY   = 0f;
    float           obstaculoLongHoriz = 3f; // longitud horizontal (m)
    float           obstaculoLongVert  = 3f; // longitud vertical (m)
    GameObject      obstaculoPropsPanel;
    Slider          obstaculoHeightSlider, obstaculoSizeSlider, obstaculoRotXSlider, obstaculoRotYSlider;
    TMP_InputField  obstaculoHeightLabel,  obstaculoSizeLabel,  obstaculoRotXLabel,  obstaculoRotYLabel;
    Slider          obstaculoLongHorizSlider, obstaculoLongVertSlider;
    TMP_InputField  obstaculoLongHorizLabel,  obstaculoLongVertLabel;
    RectTransform   lastObstaculoIconRt;
    GameObject      obstaculoPreviewInstance; // instancia actual en el visualizador, para aplicar el escalado en vivo
    GameObject      obstaculoSupportChild; // hijo con solo el submesh del soporte, para escalar su altura (Z) sin afectar la barra
    GameObject      obstaculoBodyChild; // hijo con solo el submesh de la barra: no se escala (conserva su forma redonda), solo se reposiciona
    float           obstaculoBarRefZ; // Z original de la barra, para calcular cuánto reposicionarla al crecer el soporte

 // Obstáculo object variant (qué objeto de obstáculo, entre los disponibles en Resources/Prefabs, se coloca)
    int             obstaculoVariantIdx = 0;
    Image           obstaculoVariantSwatch;
    static readonly string[] ObstaculoVariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Obstáculo support variant (color de los soportes del obstáculo, independiente del cuerpo)
    int             obstaculoSupportVariantIdx = 8; // Negro por defecto, igual que antes
    Image           obstaculoSupportVariantSwatch;

 // Moneda properties
    float           monedaHeight = 0f;
    float           monedaSize   = 1f;
    float           monedaRotX   = 0f;
    float           monedaRotY   = 0f;
    GameObject      monedaPropsPanel;
    Slider          monedaHeightSlider, monedaSizeSlider, monedaRotXSlider, monedaRotYSlider;
    TMP_InputField  monedaHeightLabel,  monedaSizeLabel,  monedaRotXLabel,  monedaRotYLabel;
    RectTransform   lastMonedaIconRt;
    GameObject      monedaPreviewInstance; // instancia actual en el visualizador, para aplicar el tamaño en vivo

 // Moneda object variant (qué objeto de moneda, entre los disponibles en Resources/Prefabs, se coloca)
    int             monedaVariantIdx = 0;
    Image           monedaVariantSwatch;
    static readonly string[] MonedaVariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Arco properties
    float           arcoHeight = 0f;
    float           arcoSize   = 1f;
    float           arcoRotX   = 90f; // vista frontal por defecto (el modelo está modelado tumbado en el plano XZ)
    float           arcoRotY   = 0f;
    float           arcoLongHoriz = 3f; // longitud horizontal (m)
    float           arcoLongVert  = 3f; // longitud vertical (m)
    GameObject      arcoPropsPanel;
    Slider          arcoHeightSlider, arcoSizeSlider, arcoRotXSlider, arcoRotYSlider;
    TMP_InputField  arcoHeightLabel,  arcoSizeLabel,  arcoRotXLabel,  arcoRotYLabel;
    Slider          arcoLongHorizSlider, arcoLongVertSlider;
    TMP_InputField  arcoLongHorizLabel,  arcoLongVertLabel;
    RectTransform   lastArcoIconRt;
    GameObject      arcoPreviewInstance; // instancia actual en el visualizador, para aplicar el escalado en vivo

 // Arco object variant (qué objeto de arco, entre los disponibles en Resources/Prefabs, se coloca)
    int             arcoVariantIdx = 0;
    Image           arcoVariantSwatch;
    static readonly string[] ArcoVariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Segundo Arco properties
    float           arco2Height = 0f;
    float           arco2Size   = 1f;
    float           arco2RotX   = -90f; // vista frontal por defecto (el modelo está modelado tumbado en el plano XZ)
    float           arco2RotY   = 0f;
    float           arco2LongHoriz = 3f; // longitud horizontal (m)
    float           arco2LongVert  = 3f; // longitud vertical (m)
    Slider          arco2LongHorizSlider, arco2LongVertSlider;
    TMP_InputField  arco2LongHorizLabel,  arco2LongVertLabel;
    GameObject      arco2PreviewInstance; // instancia actual en el visualizador, para aplicar el escalado en vivo
    GameObject      arco2PropsPanel;
    Slider          arco2HeightSlider, arco2SizeSlider, arco2RotXSlider, arco2RotYSlider;
    TMP_InputField  arco2HeightLabel,  arco2SizeLabel,  arco2RotXLabel,  arco2RotYLabel;
    RectTransform   lastArco2IconRt;

 // Segundo Arco object variant (qué objeto de arco 2, entre los disponibles en Resources/Prefabs, se coloca)
    int             arco2VariantIdx = 0;
    Image           arco2VariantSwatch;
    static readonly string[] Arco2VariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Flecha properties
    float           flechaHeight = 0f;
    float           flechaSize   = 1f;
    float           flechaRotX   = 0f;
    float           flechaRotY   = 90f; // vista frontal por defecto (el modelo está modelado tumbado en el plano XZ)
    float           flechaGrosor = 1f; // factor de escala del grosor (eje Z local, el fino de la malla plana); 1 = original
    float           flechaLongHoriz = 3f; // longitud horizontal (eje X local, la punta a la cola de la flecha), en metros
    GameObject      flechaPropsPanel;
    Slider          flechaHeightSlider, flechaSizeSlider, flechaRotXSlider, flechaRotYSlider;
    TMP_InputField  flechaHeightLabel,  flechaSizeLabel,  flechaRotXLabel,  flechaRotYLabel;
    Slider          flechaGrosorSlider,     flechaLongHorizSlider;
    TMP_InputField  flechaGrosorLabel,      flechaLongHorizLabel;
    GameObject      flechaPreviewInstance; // instancia actual en el visualizador, para aplicar el grosor en vivo
    RectTransform   lastFlechaIconRt;

 // Flecha object variant (qué objeto de flecha, entre los disponibles en Resources/Prefabs, se coloca)
    int             flechaVariantIdx = 0;
    Image           flechaVariantSwatch;
    static readonly string[] FlechaVariantNames =
        { "Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Purpura", "Blanco", "Negro", "Marron", "Gris" };

 // Selection
    int             selectedPlacedIdx = -1;
    GameObject      ctxMenu;
    bool            isMovingMarker;
    Button          movMarkerBtn;
    int             dragMarkerIdx     = -1;
    Vector2         dragStartPos;
    bool            dragActive;
    const float     DRAG_THRESHOLD    = 5f;

 // Snapping (guías de alineación automática al colocar/mover objetos)
    const float     POS_SNAP_PX        = 8f;    // umbral de alineación fila/columna (espacio "marcador")
    const float     ROT_SNAP_DEG       = 10f;   // umbral de "casi paralelo"
    const float     ROT_SNAP_RADIUS_PX = 220f;  // qué se considera "la misma zona" para rotación/espaciado
    const float     SPACING_SNAP_FRAC  = 0.15f; // tolerancia para enganchar el patrón de espaciado

 // Línea de grupo: guía transitoria mientras se arrastra un marcador (igual
 // que antes) y, tras soltar, tirador persistente para rotar en bloque todos
 // los objetos alineados sobre ella.
    RectTransform   groupLine;                          // creación perezosa
    List<int>       groupIndices = new List<int>();      // índices en `placed` del grupo activo
    Vector2         groupCentroid;
    Vector2         groupDir = Vector2.up;                // dirección unitaria de la línea
    Vector2         groupDirAtDragStart;                  // groupDir congelada al iniciar el arrastre
    bool            groupDragIsRotate;                    // arrastre normal = mover; con Shift = rotar
    Vector2         groupDragStartMouse;                  // posición "marcador" del ratón al iniciar el arrastre
    Vector2         groupCentroidAtDragStart;              // groupCentroid congelado al iniciar el arrastre (modo mover)

 // Copiar/pegar línea de grupo (clic derecho sobre la línea → Copiar; DOBLE
 // clic derecho seguido sobre el mapa vacío, si hay algo copiado → Pegar;
 // así un solo clic derecho en zona vacía sigue paneando como siempre)
    List<(MapPlacedObject obj, Vector2 offset)> lineClipboard; // null = nada copiado
    Vector2         lineClipboardDir = Vector2.up;         // groupDir congelada al copiar
    const float     RIGHT_DBLCLICK_TIME = 0.4f;             // segundos máx. entre los dos clics
    const float     RIGHT_DBLCLICK_DIST_PX = 25f;           // separación máx. en pantalla entre los dos clics
    float           lastRightClickTime = -10f;
    Vector2         lastRightClickScreenPos;

 // Todas las líneas de alineación existentes, dibujadas siempre (ver
 // ShowAllGroupLines, llamada cada frame) — a diferencia de groupLine (una
 // sola, la del grupo activo/rotable), esto es solo un vistazo de
 // referencia, no rotable ni agarrable.
    List<RectTransform> allGroupLines = new List<RectTransform>();

 // Grupos ya asentados (fila/columna que en algún momento llegó a tener 2+
    // objetos alineados). A diferencia de antes, NO se recalculan desde cero
    // cada frame por posición actual: se conservan aunque un objeto se mueva
    // fuera de la línea (mientras quede al menos otro sobre ella) y solo se
    // olvidan cuando ya no queda NINGÚN objeto sobre esa línea. Ver
    // UpdateSettledGroups, llamada cada frame antes de dibujar.
    List<(List<int> indices, Vector2 dir)> settledGroups = new List<(List<int>, Vector2)>();
    bool            draggingGroupLine;
    float           groupDragStartAngle;
    List<(int idx, Vector2 relPos, float rotY)> groupSnapshot;
    GameObject      aroPropsPanel;
    Slider          aroDiameterSlider;
    TMP_InputField  aroDiameterLabel;
    Slider          aroRotXSlider, aroRotYSlider;
    TMP_InputField  aroRotXLabel,  aroRotYLabel;

 // Placed objects

    readonly List<MapPlacedObject> placed    = new List<MapPlacedObject>();
    readonly List<GameObject>      markerGOs = new List<GameObject>();

    readonly string[] typeNames = { "Aro", "Bandera", "Obstáculo", "Moneda", "Arco", "Segundo Arco", "Flecha" };
    string selectedType = "Aro";

 // Split state

    float splitRatio = 0.25f; // controles en el 25% izquierdo, mapa en el 75%

 // Callbacks

    Action<List<MapPlacedObject>, string, double, double, bool, List<LineGroup>> onCreate;
    Action                                onCancel;

 // Edición de un escenario ya existente (ver Show sobrecargado más abajo): si se
 // rellenan, el creador arranca con esos objetos ya colocados en el mapa, centrado
 // en su ubicación y con el nombre precargado, en vez de arrancar vacío.
    bool   isEditing;
    string pendingInitialName;

 // Si la misión sube al autopiloto el geofence de seguridad alrededor del
 // dronelab al jugarse (ver MapSceneController.Start/UploadGeofence). Activado
 // por defecto para no cambiar el comportamiento de misiones ya existentes.
    bool   geofenceEnabled = true;
    Button geofenceBtn;

    // =========================================================================

    public static MapCreatorUI Show(int sortOrder,
        Action<List<MapPlacedObject>, string, double, double, bool, List<LineGroup>> onCreate,
        Action onCancel)
        => Show(sortOrder, onCreate, onCancel, null, null, null, null, true, null);

    // Igual que el Show de arriba, pero para EDITAR un escenario ya creado: precarga
    // los objetos, el nombre y el centro del mapa en vez de arrancar desde cero.
    public static MapCreatorUI Show(int sortOrder,
        Action<List<MapPlacedObject>, string, double, double, bool, List<LineGroup>> onCreate,
        Action onCancel,
        List<MapPlacedObject> initialObjects,
        string initialName,
        double? initialCenterLat,
        double? initialCenterLon,
        bool initialGeofenceEnabled = true,
        List<LineGroup> initialLineGroups = null)
    {
        var cvGO = new GameObject("MapCreatorCanvas");
        var cv   = cvGO.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = sortOrder;
        var sc = cvGO.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        cvGO.AddComponent<GraphicRaycaster>();

        var go = new GameObject("MapCreatorUI");
        go.transform.SetParent(cvGO.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var ui = go.AddComponent<MapCreatorUI>();
        ui.ownCanvas     = cvGO;
        ui.ownCanvasComp = cv;
        ui.onCreate         = onCreate;
        ui.onCancel         = onCancel;
        ui.geofenceEnabled  = initialGeofenceEnabled;

        if (initialObjects != null)
        {
            // "isEditing" se basa en que se haya llamado a esta sobrecarga (objects
            // no nulo), no en si ya trae objetos: un escenario editado que empezó
            // vacío sigue siendo una edición, no una creación nueva.
            ui.isEditing = true;
            ui.placed.AddRange(initialObjects);
        }
        if (!string.IsNullOrEmpty(initialName)) ui.pendingInitialName = initialName;
        if (initialCenterLat.HasValue && initialCenterLon.HasValue)
        {
            ui.centerLat = initialCenterLat.Value;
            ui.centerLon = initialCenterLon.Value;
            ui.zoom      = 17; // zoom cerrado, como al "Ir a coordenadas", en vez del zoom mundial por defecto
        }

        ui.Build();
        if (ui.placed.Count > 0) ui.RedrawMarkers();

        if (initialLineGroups != null)
        {
            foreach (var g in initialLineGroups)
            {
                if (g == null || g.indices == null || g.indices.Count < 2) continue;
                ui.settledGroups.Add((new List<int>(g.indices), g.dir));
            }
        }

        return ui;
    }

    // =========================================================================
    //  Build
    // =========================================================================

    void Build()
    {
        InitProviders();

 // Left panel (controles)
        var leftGO = new GameObject("LeftPanel");
        leftGO.transform.SetParent(transform, false);
        // Fondo negro base (igual que Menú)
        var bgBase = leftGO.AddComponent<Image>();
        bgBase.color = new Color(0.00f, 0.00f, 0.00f, 1f); bgBase.raycastTarget = true;
        leftPanelRt = leftGO.GetComponent<RectTransform>();
        leftPanelRt.anchorMin = Vector2.zero;
        leftPanelRt.anchorMax = new Vector2(splitRatio, 1f);
        leftPanelRt.offsetMin = leftPanelRt.offsetMax = Vector2.zero;

 // Capas idénticas a Menú
 // 1. 5 bandas de gradiente (C_GRAD_B → C_GRAD_A, i/4f, mismas posiciones)
        var C_GRAD_A = new Color(0.06f, 0.14f, 0.28f, 1f);
        var C_GRAD_B = new Color(0.72f, 0.80f, 0.95f, 1f); // inicio muy claro (azul pálido)
        const int NBANDS = 120;
        for (int i = 0; i < NBANDS; i++)
        {
            float yMin = i       / (float)NBANDS;
            float yMax = (i + 1) / (float)NBANDS;
            var bGO  = new GameObject("GradBand" + i);
            bGO.transform.SetParent(leftGO.transform, false);
            var bImg = bGO.AddComponent<Image>();
            bImg.color = Color.Lerp(C_GRAD_B, C_GRAD_A, i / (float)(NBANDS - 1));
            bImg.raycastTarget = false;
            var bRt  = bGO.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0f, yMin);
            bRt.anchorMax = new Vector2(1f, yMax);
            bRt.offsetMin = bRt.offsetMax = Vector2.zero;
        }

 // 2. Shader animado (BGAnim igual que Menú)
        var bgAnimGO  = new GameObject("BGAnim");
        bgAnimGO.transform.SetParent(leftGO.transform, false);
        var bgAnimImg = bgAnimGO.AddComponent<Image>();
        bgAnimImg.color = Color.white; bgAnimImg.raycastTarget = false;
        Stretch(bgAnimImg.GetComponent<RectTransform>());
        var bgMat = ShaderTimekeeper.GetMaterial();
        if (bgMat != null) bgAnimImg.material = bgMat;

 // 3. Tarjeta semitransparente (Window igual que Menú)
        var cardGO  = new GameObject("Window");
        cardGO.transform.SetParent(leftGO.transform, false);
        var cardImg = cardGO.AddComponent<Image>();
        cardImg.color = new Color(0.02f, 0.04f, 0.09f, 0.80f);
        cardImg.raycastTarget = false;
        Stretch(cardImg.GetComponent<RectTransform>());
        var cardOL = cardGO.AddComponent<Outline>();
        cardOL.effectColor    = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 1f);
        cardOL.effectDistance = new Vector2(1, -1);

        // 4. 10 bandas oscuras sobre la tarjeta (más oscuro abajo, transparente arriba)
        for (int i = 0; i < 10; i++)
        {
            float yMin  = i / 10f;
            float yMax  = (i + 1) / 10f;
            float alpha = Mathf.Lerp(0.88f, 0.00f, i / 9f);
            var dGO  = new GameObject("CardGradDark" + i);
            dGO.transform.SetParent(cardGO.transform, false);
            var dImg = dGO.AddComponent<Image>();
            dImg.color = new Color(0f, 0f, 0f, alpha);
            dImg.raycastTarget = false;
            var dRt  = dGO.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0f, yMin);
            dRt.anchorMax = new Vector2(1f, yMax);
            dRt.offsetMin = dRt.offsetMax = Vector2.zero;
        }

        // 5. Controles encima de todo (ScrollRect)
        BuildRightPanel(leftGO.transform);

 // Etiqueta de arrastre sobre todo el canvas para que sea visible
        // Color = C_GRAD_A (banda superior del gradiente del panel de configuración)
 // Etiqueta de arrastre: sale del borde del panel y tapa un poco el mapa
        var panelTabGO = new GameObject("PanelDragTab");
        panelTabGO.transform.SetParent(transform, false);
        var ptImg = panelTabGO.AddComponent<Image>();
        ptImg.sprite = MakeRoundedSprite(200, 64, 32); // pastilla horizontal
        ptImg.type   = Image.Type.Sliced;
        ptImg.color  = new Color(0.06f, 0.14f, 0.28f, 1f); // color C_GRAD_A del gradiente
        var ptOL = panelTabGO.AddComponent<Outline>();
        ptOL.effectColor    = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.5f);
        ptOL.effectDistance = new Vector2(1f, -1f);
        dragTabRt = panelTabGO.GetComponent<RectTransform>();
 // Pivot en el borde izquierdo → la etiqueta crece hacia la derecha (sobre el mapa)
        dragTabRt.anchorMin = new Vector2(splitRatio, 0.5f);
        dragTabRt.anchorMax = new Vector2(splitRatio, 0.5f);
        dragTabRt.pivot     = new Vector2(0f, 0.5f);
        dragTabRt.anchoredPosition = Vector2.zero;
        dragTabRt.sizeDelta = new Vector2(52f, 20f); // horizontal, tapa el mapa

 // Icono indicando arrastre horizontal
        var ptTxtGO = new GameObject("TabTxt");
        ptTxtGO.transform.SetParent(panelTabGO.transform, false);
        var ptTmp = ptTxtGO.AddComponent<TextMeshProUGUI>();
        ptTmp.text = "⟺"; ptTmp.fontSize = 11f;
        ptTmp.color = new Color(C_CYAN.r, C_CYAN.g, C_CYAN.b, 0.9f);
        ptTmp.alignment = TextAlignmentOptions.Center;
        ptTmp.raycastTarget = false;
        var ptTRt = ptTxtGO.GetComponent<RectTransform>();
        ptTRt.anchorMin = Vector2.zero; ptTRt.anchorMax = Vector2.one;
        ptTRt.offsetMin = ptTRt.offsetMax = Vector2.zero;

 // Divisor estilo HUD (mismo C_CYAN y estructura que Menú)
        var divGO = new GameObject("Divider");
        divGO.transform.SetParent(transform, false);
 // Base transparente el degradado lo dan las bandas hijas
        var divImg = divGO.AddComponent<Image>();
        divImg.color = new Color(0f, 0f, 0f, 0f);
        dividerRt = divGO.GetComponent<RectTransform>();
        dividerRt.anchorMin = new Vector2(splitRatio, 0f);
        dividerRt.anchorMax = new Vector2(splitRatio, 1f);
        dividerRt.offsetMin = new Vector2(-3f, 0f);
        dividerRt.offsetMax = new Vector2(3f,  0f);

        // Mismas 40 bandas de gradiente que el panel izquierdo
        var divGradA = new Color(0.06f, 0.14f, 0.28f, 1f);
        var divGradB = new Color(0.00f, 0.00f, 0.00f, 1f);
        var divBands = new Image[40];
        for (int i = 0; i < 40; i++)
        {
            float yMin = i       / 40f;
            float yMax = (i + 1) / 40f;
            var bGO  = new GameObject("DB" + i);
            bGO.transform.SetParent(divGO.transform, false);
            divBands[i] = bGO.AddComponent<Image>();
            divBands[i].color = Color.Lerp(divGradB, divGradA, i / 39f);
            divBands[i].raycastTarget = false;
            var bRt  = bGO.GetComponent<RectTransform>();
            bRt.anchorMin = new Vector2(0f, yMin);
            bRt.anchorMax = new Vector2(1f, yMax);
            bRt.offsetMin = bRt.offsetMax = Vector2.zero;
        }

        // Hover: oculta el degradado y muestra gris claro
        var divET = divGO.AddComponent<EventTrigger>();
        AddET(divET, EventTriggerType.PointerEnter, _ =>
        {
            divImg.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            foreach (var b in divBands) if (b) b.enabled = false;
        });
        AddET(divET, EventTriggerType.PointerExit, _ =>
        {
            divImg.color = new Color(0f, 0f, 0f, 0f);
            foreach (var b in divBands) if (b) b.enabled = true;
        });

        // (la pastilla de arrastre ahora está en el panel de configuración)

 // Right panel (mapa)
        var rightGO = new GameObject("RightPanel");
        rightGO.transform.SetParent(transform, false);
        rightGO.AddComponent<Image>().color = C_DARK;
        rightPanelRt = rightGO.GetComponent<RectTransform>();
        rightPanelRt.anchorMin = new Vector2(splitRatio, 0f);
        rightPanelRt.anchorMax = Vector2.one;
        rightPanelRt.offsetMin = rightPanelRt.offsetMax = Vector2.zero;

        BuildMapArea(rightGO.transform);

        RefreshTiles(true);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // Genera un sprite de rectángulo redondeado en tiempo de ejecución
    static Sprite MakeRoundedSprite(int w, int h, int radius)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        var pixels = new Color32[w * h];
        for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
        {
            // Dentro de la zona central: siempre dentro
            bool inH = px >= radius && px <= w - radius;
            bool inV = py >= radius && py <= h - radius;
            bool inside = inH || inV;
            if (!inside)
            {
                // Comprueba la esquina más cercana
                int cx = px < radius ? radius : w - radius;
                int cy = py < radius ? radius : h - radius;
                int dx = px - cx, dy = py - cy;
                inside = dx * dx + dy * dy <= radius * radius;
            }
            pixels[py * w + px] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        // Border para slicing: igual que el radio
        var border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect, border);
    }

    void InitProviders()
    {
        providers = new TileProvider[]
        {
            new TileProvider {
                name  = "Calles",
                url   = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                attrib= "© OpenStreetMap contributors"
            },
            new TileProvider {
                name  = "Satélite",
                url   = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                attrib= "© Esri, DigitalGlobe"
            },
            new TileProvider {
                name  = "Topo",
                url   = "https://tile.opentopomap.org/{z}/{x}/{y}.png",
                attrib= "© OpenTopoMap contributors"
            },
        };
    }

    // =========================================================================
 // Left panel mapa
    // =========================================================================

    void BuildMapArea(Transform parent)
    {
        // Área del mapa (con clip)
        var mapAreaGO = new GameObject("MapArea");
        mapAreaGO.transform.SetParent(parent, false);
        mapAreaGO.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f, 1f);
        mapAreaGO.AddComponent<RectMask2D>();
        mapAreaRt = mapAreaGO.GetComponent<RectTransform>();
        mapAreaRt.anchorMin = Vector2.zero;
        mapAreaRt.anchorMax = Vector2.one;
        mapAreaRt.offsetMin = mapAreaRt.offsetMax = Vector2.zero;

 // Solo scroll el paneo y colocación de objetos se gestiona en Update()
        var et = mapAreaGO.AddComponent<EventTrigger>();
        AddET(et, EventTriggerType.Scroll, d => OnMapScroll(((PointerEventData)d).scrollDelta));

        // Contenedor de tiles (tamaño en píxeles reales)
        float rawW = GRID_W * TILE_PIX;
        float rawH = GRID_H * TILE_PIX;

        var mcGO = new GameObject("MapContainer");
        mcGO.transform.SetParent(mapAreaGO.transform, false);
        var mcRt = mcGO.AddComponent<RectTransform>();
        mcRt.anchorMin = mcRt.anchorMax = new Vector2(0.5f, 0.5f);
        mcRt.pivot     = new Vector2(0.5f, 0.5f);
        mcRt.sizeDelta = new Vector2(rawW, rawH);
        mcRt.anchoredPosition = Vector2.zero;
        mapContainerTr = mcGO.transform;

        // Tiles
        tiles = new RawImage[GRID_W, GRID_H];
        for (int tx = 0; tx < GRID_W; tx++)
        for (int ty = 0; ty < GRID_H; ty++)
        {
            var tgo = new GameObject($"T{tx}_{ty}");
            tgo.transform.SetParent(mcRt, false);
            var ri = tgo.AddComponent<RawImage>();
            ri.color = new Color(0.25f, 0.28f, 0.35f, 1f);
            ri.raycastTarget = false;
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot     = new Vector2(0f, 1f);
            trt.anchoredPosition = new Vector2(tx * TILE_PIX, -ty * TILE_PIX);
            trt.sizeDelta = new Vector2(TILE_PIX, TILE_PIX);
            tiles[tx, ty] = ri;
        }

        // Capa de marcadores
        var mGO = new GameObject("Markers");
        mGO.transform.SetParent(mcRt, false);
        var mRt = mGO.AddComponent<RectTransform>();
        mRt.anchorMin = Vector2.zero; mRt.anchorMax = Vector2.one;
        mRt.offsetMin = mRt.offsetMax = Vector2.zero;
        markersParent = mGO.transform;

        // Atribución como overlay dentro del mapa (esquina inferior derecha)
        var attribGO = new GameObject("Attrib");
        attribGO.transform.SetParent(mapAreaGO.transform, false);
        attribGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        var aRt = attribGO.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 0f); aRt.anchorMax = new Vector2(1f, 0f);
        aRt.offsetMin = Vector2.zero; aRt.offsetMax = new Vector2(0f, 22f);

        var aTxtGO = new GameObject("AttribTxt");
        aTxtGO.transform.SetParent(attribGO.transform, false);
        attribLabel = aTxtGO.AddComponent<TextMeshProUGUI>();
        attribLabel.text      = providers[providerIdx].attrib;
        attribLabel.fontSize  = 8f;
        attribLabel.color     = new Color(0.65f, 0.65f, 0.65f, 0.9f);
        attribLabel.alignment = TextAlignmentOptions.Right;
        attribLabel.raycastTarget = false;
        var atRt = aTxtGO.GetComponent<RectTransform>();
        atRt.anchorMin = Vector2.zero; atRt.anchorMax = Vector2.one;
        atRt.offsetMin = new Vector2(4f, 2f); atRt.offsetMax = new Vector2(-8f, -2f);

        // Previsualización 3D de cómo se ven de verdad los objetos ya colocados
        // (misma instanciación que la misión real, ver MapSceneController.PlaceAll)
        // — overlay flotante encima del mapa, esquina superior derecha.
        var scenPrevBtn = MakeLBtn(mapAreaGO.transform, "Previsualización Misión",
            new Color(0.08f, 0.20f, 0.14f, 0.92f), new Color(0.35f, 1f, 0.65f), 11f);
        scenPrevBtnRt = scenPrevBtn.GetComponent<RectTransform>();
        scenPrevBtnRt.anchorMin = scenPrevBtnRt.anchorMax = new Vector2(1f, 1f);
        scenPrevBtnRt.pivot = new Vector2(1f, 1f);
        scenPrevBtnRt.anchoredPosition = new Vector2(-10f, -10f); scenPrevBtnRt.sizeDelta = new Vector2(175f, 30f);
        scenPrevBtn.onClick.AddListener(OpenScenarioPreview);

        // Activa/desactiva si esta misión sube el geofence de seguridad del
        // dronelab al jugarse (ver MapSceneController.Start) — overlay flotante
        // encima del mapa, esquina superior izquierda (simétrico al de arriba).
        geofenceBtn = MakeLBtn(mapAreaGO.transform, "",
            new Color(0.08f, 0.20f, 0.14f, 0.92f), new Color(0.35f, 1f, 0.65f), 11f);
        geofenceBtnRt = geofenceBtn.GetComponent<RectTransform>();
        geofenceBtnRt.anchorMin = geofenceBtnRt.anchorMax = new Vector2(0f, 1f);
        geofenceBtnRt.pivot = new Vector2(0f, 1f);
        geofenceBtnRt.anchoredPosition = new Vector2(10f, -10f); geofenceBtnRt.sizeDelta = new Vector2(175f, 30f);
        geofenceBtn.onClick.AddListener(ToggleGeofence);
        RefreshGeofenceBtnLabel();
    }

    void ToggleGeofence()
    {
        geofenceEnabled = !geofenceEnabled;
        RefreshGeofenceBtnLabel();
    }

    void RefreshGeofenceBtnLabel()
    {
        if (geofenceBtn == null) return;
        var txt  = geofenceBtn.GetComponentInChildren<TextMeshProUGUI>();
        var fill = geofenceEnabled ? new Color(0.08f, 0.20f, 0.14f, 0.92f) : new Color(0.20f, 0.08f, 0.08f, 0.92f);

        if (txt != null)
        {
            txt.text  = geofenceEnabled ? "Geofence Dronelab: Activado" : "Geofence Dronelab: Desactivado";
            txt.color = geofenceEnabled ? new Color(0.35f, 1f, 0.65f) : new Color(1f, 0.55f, 0.55f);
        }

        // Reasignar Button.colors (no solo Image.color): el sistema de
        // transición de color del propio Selectable pisa el Image.color con
        // colors.normalColor en cada cambio de estado (hover/press/etc.), así
        // que tocar solo el Image se vería bien un instante y luego "saltaría"
        // de vuelta al verde original en el primer hover.
        var cb = geofenceBtn.colors;
        cb.normalColor      = fill;
        cb.highlightedColor = Color.Lerp(fill, Color.white, 0.2f);
        cb.pressedColor     = Color.Lerp(fill, Color.black, 0.2f);
        geofenceBtn.colors  = cb;
        var img = geofenceBtn.GetComponent<Image>();
        if (img != null) img.color = fill;
    }

    // =========================================================================
 // Right panel controles
    // =========================================================================

    void BuildRightPanel(Transform parent)
    {
        // ScrollRect para que quepa todo aunque la pantalla sea pequeña
        var scrollGO = new GameObject("Scroll");
        scrollGO.transform.SetParent(parent, false);
        var scrollRT = scrollGO.AddComponent<RectTransform>();
        scrollRT.anchorMin = Vector2.zero; scrollRT.anchorMax = Vector2.one;
        scrollRT.offsetMin = scrollRT.offsetMax = Vector2.zero;
        var scroll = scrollGO.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scrollGO.AddComponent<RectMask2D>();

        var contentGO = new GameObject("Content");
        contentGO.transform.SetParent(scrollGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.offsetMin = contentRT.offsetMax = Vector2.zero;

        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 18, 18);
        vlg.spacing = 10f;
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.content = contentRT;

        Transform p = contentGO.transform;

        // Título
        var titleLbl = AddLabel(p, isEditing ? "EDITAR MISIÓN" : "CREADOR DE MISIÓN", 16, C_ACCENT, FontStyles.Bold, 50f);
        titleLbl.alignment = TextAlignmentOptions.Center;
        var orbitron = GetOrbitronFontAsset();
        if (orbitron != null) titleLbl.font = orbitron;
        AddSep(p);

        // Botón de geolocalización
        var locBtn = MakeLBtn(p, "Usar mi ubicación",
            new Color(0.06f, 0.18f, 0.28f, 1f), C_CYAN, 12f);
        locateBtnLabel = locBtn.transform.Find("Txt")?.GetComponent<TextMeshProUGUI>();
        locBtn.onClick.AddListener(LocateMe);

        // Texto centrado; el icono se coloca pegado a su izquierda (no fijo
        // al borde del botón), con poco hueco entre ambos.
        if (locateBtnLabel != null) locateBtnLabel.alignment = TextAlignmentOptions.Center;

        // Icono de ubicación (pin: cabeza redonda + punta + agujero)
 // en vez del emoji que la fuente no soporta.
        locationIconRt = BuildLocationPinIcon(locBtn.transform, new Vector2(-67f, 0f), C_CYAN,
            new Color(0.06f, 0.18f, 0.28f, 1f));
        RepositionLocationIcon();

        AddSep(p);

 // Tipo de mapa
        AddLabel(p, "TIPO DE MAPA", 11, C_SUB, FontStyles.Bold, 16f);
        BuildMapTypeRow(p);
        AddSep(p);

 // Tipo de objeto
        AddLabel(p, "OBJETO A COLOCAR", 11, C_SUB, FontStyles.Bold, 16f);
        BuildObjTypeRow(p);

        // Previsualización (incluye controles del aro)
        BuildPreviewArea(p);
        AddSep(p);

 // Coordenadas del cursor
        AddLabel(p, "COORDENADAS DEL CURSOR", 11, C_SUB, FontStyles.Bold, 16f);

        var coordRow2 = new GameObject("CoordRow");
        coordRow2.transform.SetParent(p, false);
        var crHLG = coordRow2.AddComponent<HorizontalLayoutGroup>();
        crHLG.spacing = 8f; crHLG.childAlignment = TextAnchor.MiddleLeft;
        crHLG.childControlWidth = true; crHLG.childForceExpandWidth = true;
        crHLG.childControlHeight = false; crHLG.childForceExpandHeight = false;
        SetLE(coordRow2, 20f);

        var latGO2 = new GameObject("LatLabel");
        latGO2.transform.SetParent(coordRow2.transform, false);
        coordsLabel = latGO2.AddComponent<TextMeshProUGUI>();
 coordsLabel.text = "Lat: "; coordsLabel.fontSize = 12f; coordsLabel.color = C_TEXT;
        coordsLabel.alignment = TextAlignmentOptions.Left; coordsLabel.raycastTarget = false;
        latGO2.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var lonGO2 = new GameObject("LonLabel");
        lonGO2.transform.SetParent(coordRow2.transform, false);
        _lonLabel = lonGO2.AddComponent<TextMeshProUGUI>();
 _lonLabel.text = "Lon: "; _lonLabel.fontSize = 12f; _lonLabel.color = C_TEXT;
        _lonLabel.alignment = TextAlignmentOptions.Left; _lonLabel.raycastTarget = false;
        lonGO2.AddComponent<LayoutElement>().flexibleWidth = 1f;

        AddSep(p);

 // Ir a coordenadas
        AddLabel(p, "Latitud", 8, C_SUB, FontStyles.Normal, 14f);
        var inputLat = BuildInputField(p);
        inputLat.placeholder.GetComponent<TextMeshProUGUI>().text = "ej. 41.389";

        AddLabel(p, "Longitud", 8, C_SUB, FontStyles.Normal, 14f);
        var inputLon = BuildInputField(p);
        inputLon.placeholder.GetComponent<TextMeshProUGUI>().text = "ej. 2.159";

 MakeLBtn(p, "Ir →", new Color(0.06f, 0.15f, 0.32f, 1f), C_CYAN, 10f)
            .onClick.AddListener(() =>
            {
                if (double.TryParse(inputLat.text.Trim().Replace(',', '.'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double la) &&
                    double.TryParse(inputLon.text.Trim().Replace(',', '.'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double lo))
                {
                    centerLat = Math.Max(-85.0, Math.Min(85.0, la));
                    centerLon = Math.Max(-180.0, Math.Min(180.0, lo));
                    zoom = 18; // zoom bien cerrado al ir a unas coordenadas concretas
 if (zoomLabel) zoomLabel.text = $"Zoom: {zoom} (rueda del ratón o botones +/ˆ)";
                    RefreshTiles(true);
                    RedrawMarkers();
                }
            });
        AddSep(p);

 // Nombre
        AddLabel(p, "NOMBRE DE LA MISIÓN", 11, C_SUB, FontStyles.Bold, 16f);
        inputName = BuildInputField(p);
        if (!string.IsNullOrEmpty(pendingInitialName)) inputName.text = pendingInitialName;
        AddSep(p);

 // Acciones
        BuildActionRow(p);
    }

    void BuildMapTypeRow(Transform parent)
    {
        var headerGO = new GameObject("MapDropHeader");
        headerGO.transform.SetParent(parent, false);
        headerGO.AddComponent<Image>().color = new Color(0.06f, 0.15f, 0.32f, 1f);
        SetLE(headerGO, 30f);

        var hlg = headerGO.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft; hlg.childControlWidth = true;
        hlg.childControlHeight = true; hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true; hlg.padding = new RectOffset(8, 8, 0, 0); hlg.spacing = 4f;

        var lblGO = new GameObject("Lbl");
        lblGO.transform.SetParent(headerGO.transform, false);
        mapDropdownLabel = lblGO.AddComponent<TextMeshProUGUI>();
        mapDropdownLabel.text = providers[providerIdx].name; mapDropdownLabel.fontSize = 12f;
        mapDropdownLabel.color = C_CYAN; mapDropdownLabel.fontStyle = FontStyles.Bold;
        mapDropdownLabel.alignment = TextAlignmentOptions.Center; mapDropdownLabel.raycastTarget = false;
        lblGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var arrGO = new GameObject("Arrow");
        arrGO.transform.SetParent(headerGO.transform, false);
        var arrTmp = arrGO.AddComponent<TextMeshProUGUI>();
        arrTmp.text = "<"; arrTmp.fontSize = 10f; arrTmp.color = C_SUB;
        arrTmp.alignment = TextAlignmentOptions.Right; arrTmp.raycastTarget = false;
        arrGO.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // "<" rotado para que apunte hacia abajo
        var arrLe = arrGO.AddComponent<LayoutElement>();
        arrLe.preferredWidth = 14f; arrLe.flexibleWidth = 0f;

        var headerBtn = headerGO.AddComponent<Button>();
        var hcb = headerBtn.colors;
        hcb.normalColor = new Color(0.06f, 0.15f, 0.32f, 1f);
        hcb.highlightedColor = new Color(0.10f, 0.22f, 0.42f, 1f);
        hcb.pressedColor = new Color(0.04f, 0.10f, 0.22f, 1f);
        headerBtn.colors = hcb;

        var headerRt = headerGO.GetComponent<RectTransform>();
        headerBtn.onClick.AddListener(() =>
        {
            var names = new string[providers.Length];
            for (int j = 0; j < providers.Length; j++) names[j] = providers[j].name;
            ShowContextMenu(headerRt, names, null, providerIdx, i => SwitchProvider(i));
        });
    }

    void BuildObjTypeRow(Transform parent)
    {
        var headerGO = new GameObject("DropHeader");
        headerGO.transform.SetParent(parent, false);
        headerGO.AddComponent<Image>().color = new Color(0.06f, 0.15f, 0.32f, 1f);
        SetLE(headerGO, 30f);

        var hlg = headerGO.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft; hlg.childControlWidth = true;
        hlg.childControlHeight = true; hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true; hlg.padding = new RectOffset(8, 8, 0, 0); hlg.spacing = 4f;

        var lblGO = new GameObject("Lbl");
        lblGO.transform.SetParent(headerGO.transform, false);
        typeDropdownLabel = lblGO.AddComponent<TextMeshProUGUI>();
        typeDropdownLabel.text = selectedType; typeDropdownLabel.fontSize = 12f;
        typeDropdownLabel.color = C_CYAN; typeDropdownLabel.fontStyle = FontStyles.Bold;
        typeDropdownLabel.alignment = TextAlignmentOptions.Center; typeDropdownLabel.raycastTarget = false;
        lblGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var arrGO = new GameObject("Arrow");
        arrGO.transform.SetParent(headerGO.transform, false);
        var arrTmp = arrGO.AddComponent<TextMeshProUGUI>();
        arrTmp.text = "<"; arrTmp.fontSize = 10f; arrTmp.color = C_SUB;
        arrTmp.alignment = TextAlignmentOptions.Right; arrTmp.raycastTarget = false;
        arrGO.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // "<" rotado para que apunte hacia abajo
        var arrLe = arrGO.AddComponent<LayoutElement>();
        arrLe.preferredWidth = 14f; arrLe.flexibleWidth = 0f;

        var headerBtn = headerGO.AddComponent<Button>();
        var hcb = headerBtn.colors;
        hcb.normalColor = new Color(0.06f, 0.15f, 0.32f, 1f);
        hcb.highlightedColor = new Color(0.10f, 0.22f, 0.42f, 1f);
        hcb.pressedColor = new Color(0.04f, 0.10f, 0.22f, 1f);
        headerBtn.colors = hcb;

        var headerRt = headerGO.GetComponent<RectTransform>();
        headerBtn.onClick.AddListener(() =>
        {
            var dots = new Color[typeNames.Length];
            for (int j = 0; j < typeNames.Length; j++) dots[j] = TypeColor(typeNames[j]);
            int selIdx = Array.IndexOf(typeNames, selectedType);
            ShowContextMenu(headerRt, typeNames, dots, selIdx, i => SelectType(typeNames[i]));
        });
    }

 // Color real de cada variante (mismos valores que usan los generadores de prefabs Aro/Bandera/Obstaculo/Moneda_<Color>)
    static Color VariantColor(string key) => key switch
    {
        "Rojo"     => new Color(0.9063396f, 0.1463596f, 0.21163508f),
        "Naranja"  => new Color(1f,         0.55f,      0.1f),
        "Amarillo" => new Color(1f,         0.85f,      0.1f),
        "Verde"    => new Color(0.2f,       0.85f,      0.35f),
        "Cian"     => new Color(0.1f,       0.85f,      0.9f),
        "Azul"     => new Color(0.2f,       0.45f,      1f),
        "Purpura"  => new Color(0.7f,       0.25f,      0.95f),
        "Blanco"   => new Color(0.95f,      0.95f,      0.95f),
        "Negro"    => new Color(0.08f,      0.08f,      0.08f),
        "Marron"   => new Color(0.45f,      0.29f,      0.16f),
        "Gris"     => new Color(0.5f,       0.5f,       0.5f),
        _          => Color.white,
    };

 // Menú contextual flotante (estilo menú de Windows)

    void ShowContextMenu(RectTransform anchorRt, string[] labels, Color[] dots, int selIdx, Action<int> onPick)
    {
        CloseContextMenu();

 // Raíz a nivel de canvas siempre encima de todo
        ctxMenuGO = new GameObject("CtxMenu");
        ctxMenuGO.transform.SetParent(ownCanvas.transform, false);
        ctxMenuGO.transform.SetAsLastSibling();
        var rootRt = ctxMenuGO.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

        // Bloqueador transparente: cierra el menú al clicar fuera
        var blockerGO = new GameObject("Blocker");
        blockerGO.transform.SetParent(ctxMenuGO.transform, false);
        blockerGO.AddComponent<Image>().color = Color.clear;
        var bRt = blockerGO.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero; bRt.anchorMax = Vector2.one;
        bRt.offsetMin = bRt.offsetMax = Vector2.zero;
        blockerGO.AddComponent<Button>().onClick.AddListener(() => { Debug.Log("[MapCreatorUI] Clic en BLOCKER"); CloseContextMenu(); });
        var blockerEt = blockerGO.AddComponent<EventTrigger>();
        AddET(blockerEt, EventTriggerType.PointerEnter, _ => Debug.Log("[MapCreatorUI] PointerEnter en BLOCKER"));

        // Posición: esquina inferior-izquierda del botón ancla en espacio canvas
        var canvasRt = ownCanvas.GetComponent<RectTransform>();
        Vector3[] corners = new Vector3[4];
        anchorRt.GetWorldCorners(corners);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt, corners[0], null, out Vector2 popPos);

        float itemH  = 28f;
        float padV   = 4f;
        float popW   = anchorRt.rect.width;
        float popH   = labels.Length * itemH + (labels.Length - 1) + padV * 2f;

        // Sombra
        var shadowGO = new GameObject("Shadow");
        shadowGO.transform.SetParent(ctxMenuGO.transform, false);
        shadowGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
        var sRt = shadowGO.GetComponent<RectTransform>();
        // Anchor en el centro del canvas (= origen de canvas local que devuelve ScreenPointToLocalPointInRectangle)
        sRt.anchorMin = sRt.anchorMax = new Vector2(0.5f, 0.5f); sRt.pivot = new Vector2(0f, 1f);
        sRt.anchoredPosition = popPos + new Vector2(4f, -5f);
        sRt.sizeDelta = new Vector2(popW, popH);

        // Panel principal
        var popGO = new GameObject("Popup");
        popGO.transform.SetParent(ctxMenuGO.transform, false);
        popGO.AddComponent<Image>().color = new Color(0.07f, 0.11f, 0.20f, 0.98f);
        var pRt = popGO.GetComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f); pRt.pivot = new Vector2(0f, 1f);
        pRt.anchoredPosition = popPos;
        pRt.sizeDelta = new Vector2(popW, popH);
        var popEt = popGO.AddComponent<EventTrigger>();
        AddET(popEt, EventTriggerType.PointerEnter, _ => Debug.Log("[MapCreatorUI] PointerEnter en POPUP (panel)"));

        // Franja de acento lateral izquierda (2 px)
        var stripeGO = new GameObject("Stripe");
        stripeGO.transform.SetParent(popGO.transform, false);
        stripeGO.AddComponent<Image>().color = C_CYAN;
        var stRt = stripeGO.GetComponent<RectTransform>();
        stRt.anchorMin = Vector2.zero; stRt.anchorMax = new Vector2(0f, 1f);
        stRt.pivot = new Vector2(0f, 0.5f);
        stRt.offsetMin = Vector2.zero; stRt.offsetMax = new Vector2(2f, 0f);

        // Lista de items
        var listGO = new GameObject("List");
        listGO.transform.SetParent(popGO.transform, false);
        var vlg = listGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 1f; vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
        vlg.padding = new RectOffset(2, 0, (int)padV, (int)padV);
        var lRt = listGO.GetComponent<RectTransform>();
        lRt.anchorMin = Vector2.zero; lRt.anchorMax = Vector2.one;
        lRt.offsetMin = lRt.offsetMax = Vector2.zero;

        for (int i = 0; i < labels.Length; i++)
        {
            int ci  = i;
            bool sel = i == selIdx;

            var itemGO = new GameObject("Item_" + labels[i]);
            itemGO.transform.SetParent(listGO.transform, false);
            var itemBg = itemGO.AddComponent<Image>();
            itemBg.color = sel ? new Color(0.14f, 0.26f, 0.46f, 1f) : Color.clear;
            SetLE(itemGO, itemH);

            var ihlg = itemGO.AddComponent<HorizontalLayoutGroup>();
            ihlg.childAlignment = TextAnchor.MiddleLeft; ihlg.childControlWidth = true;
            ihlg.childControlHeight = true; ihlg.childForceExpandHeight = true;
            ihlg.childForceExpandWidth = false;
            ihlg.padding = new RectOffset(10, 8, 0, 0); ihlg.spacing = 7f;

 // Checkmark (œ si es el seleccionado)
            var ckGO = new GameObject("Check");
            ckGO.transform.SetParent(itemGO.transform, false);
            var ckTmp = ckGO.AddComponent<TextMeshProUGUI>();
            ckTmp.text = ""; ckTmp.fontSize = 9f;
            ckTmp.color = C_ACCENT; ckTmp.raycastTarget = false;
            ckGO.AddComponent<LayoutElement>().preferredWidth = 12f;


            // Etiqueta
            var lbGO = new GameObject("Lbl");
            lbGO.transform.SetParent(itemGO.transform, false);
            var lbTmp = lbGO.AddComponent<TextMeshProUGUI>();
            lbTmp.text = labels[i]; lbTmp.fontSize = 10f;
            lbTmp.color = sel ? C_CYAN : C_TEXT;
            lbTmp.fontStyle = sel ? FontStyles.Bold : FontStyles.Normal;
            lbTmp.alignment = TextAlignmentOptions.Left; lbTmp.raycastTarget = false;
            lbGO.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var btn = itemGO.AddComponent<Button>();
            btn.targetGraphic = itemBg;
            var cb  = btn.colors;
            cb.normalColor      = sel ? new Color(0.14f, 0.26f, 0.46f, 1f) : Color.clear;
            // Darken the row on hover instead of tinting it blue, regardless
            // of whether it's the selected row or not. The popup background
            // is a dark navy (0.07, 0.11, 0.20), not pure black, so a low-
 // alpha black overlay barely showed on unselected (clear) rows
            // pushed the alpha up so it actually reads as "darker" there too.
            cb.highlightedColor = new Color(0f, 0f, 0f, 0.65f);
            cb.pressedColor     = new Color(0f, 0f, 0f, 0.8f);
            btn.colors = cb;
            btn.onClick.AddListener(() => { onPick(ci); CloseContextMenu(); });

            var dbgEt = itemGO.AddComponent<EventTrigger>();
            AddET(dbgEt, EventTriggerType.PointerEnter, _ => Debug.Log($"[MapCreatorUI] PointerEnter en fila: {labels[ci]}"));
            AddET(dbgEt, EventTriggerType.PointerExit,  _ => Debug.Log($"[MapCreatorUI] PointerExit en fila: {labels[ci]}"));
        }
    }

    // Grid de cuadraditos de color (para el selector de "Color" de aro/bandera/obstáculo/moneda)
    void ShowColorGridMenu(RectTransform anchorRt, string[] names, int selIdx, Action<int> onPick)
    {
        CloseContextMenu();

        ctxMenuGO = new GameObject("CtxMenu");
        ctxMenuGO.transform.SetParent(ownCanvas.transform, false);
        ctxMenuGO.transform.SetAsLastSibling();
        var rootRt = ctxMenuGO.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

        var blockerGO = new GameObject("Blocker");
        blockerGO.transform.SetParent(ctxMenuGO.transform, false);
        blockerGO.AddComponent<Image>().color = Color.clear;
        var bRt = blockerGO.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero; bRt.anchorMax = Vector2.one;
        bRt.offsetMin = bRt.offsetMax = Vector2.zero;
        blockerGO.AddComponent<Button>().onClick.AddListener(CloseContextMenu);

        var canvasRt = ownCanvas.GetComponent<RectTransform>();
        Vector3[] corners = new Vector3[4];
        anchorRt.GetWorldCorners(corners);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRt, corners[0], null, out Vector2 popPos);

        const int   cols = 4;
        const float cell = 28f, spacing = 6f, pad = 8f;
        int rows = Mathf.CeilToInt(names.Length / (float)cols);
        float popW = cols * cell + (cols - 1) * spacing + pad * 2f;
        float popH = rows * cell + (rows - 1) * spacing + pad * 2f;

        var shadowGO = new GameObject("Shadow");
        shadowGO.transform.SetParent(ctxMenuGO.transform, false);
        shadowGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
        var sRt = shadowGO.GetComponent<RectTransform>();
        sRt.anchorMin = sRt.anchorMax = new Vector2(0.5f, 0.5f); sRt.pivot = new Vector2(0f, 1f);
        sRt.anchoredPosition = popPos + new Vector2(4f, -5f);
        sRt.sizeDelta = new Vector2(popW, popH);

        var popGO = new GameObject("Popup");
        popGO.transform.SetParent(ctxMenuGO.transform, false);
        popGO.AddComponent<Image>().color = new Color(0.07f, 0.11f, 0.20f, 0.98f);
        var pRt = popGO.GetComponent<RectTransform>();
        pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f); pRt.pivot = new Vector2(0f, 1f);
        pRt.anchoredPosition = popPos;
        pRt.sizeDelta = new Vector2(popW, popH);

        var gridGO = new GameObject("Grid");
        gridGO.transform.SetParent(popGO.transform, false);
        var glg = gridGO.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(cell, cell);
        glg.spacing  = new Vector2(spacing, spacing);
        glg.padding  = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
        glg.childAlignment = TextAnchor.UpperLeft;
        glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = cols;
        var gRt = gridGO.GetComponent<RectTransform>();
        gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
        gRt.offsetMin = gRt.offsetMax = Vector2.zero;

        for (int i = 0; i < names.Length; i++)
        {
            int ci = i;
            bool sel = i == selIdx;

            var cellGO = new GameObject("Cell_" + names[i]);
            cellGO.transform.SetParent(gridGO.transform, false);
            var frameImg = cellGO.AddComponent<Image>();
            frameImg.color = sel ? C_CYAN : new Color(0.45f, 0.51f, 0.60f, 1f);

            var innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(cellGO.transform, false);
            var innerImg = innerGO.AddComponent<Image>();
            innerImg.color = VariantColor(names[i]);
            innerImg.raycastTarget = false;
            var iRt = innerGO.GetComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
            float border = sel ? 3f : 2f;
            iRt.offsetMin = new Vector2(border, border); iRt.offsetMax = new Vector2(-border, -border);

            var btn = cellGO.AddComponent<Button>();
            btn.targetGraphic = frameImg;
            var cb = btn.colors;
            cb.normalColor      = sel ? C_CYAN : new Color(0.45f, 0.51f, 0.60f, 1f);
            cb.highlightedColor = C_CYAN;
            cb.pressedColor     = new Color(0.7f, 0.8f, 0.9f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => { onPick(ci); CloseContextMenu(); });
        }
    }

    void CloseContextMenu()
    {
        if (ctxMenuGO) { Destroy(ctxMenuGO); ctxMenuGO = null; }
    }

    Image BuildVariantColorRow(Transform parent, string[] names, Func<int> getCurrentIdx, Action<int> onPick, string label = "Color")
    {
        // Fila más, al final del panel de propiedades (misma altura/estilo que las filas de sliders)
        var row = new GameObject("ColorRow");
        row.transform.SetParent(parent, false);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 5f;
        hlg.childAlignment        = TextAnchor.MiddleLeft;
        hlg.childControlWidth     = true;
        hlg.childControlHeight    = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        SetLE(row, 20f);

        var lGO = new GameObject("Lbl");
        lGO.transform.SetParent(row.transform, false);
        var lTmp = lGO.AddComponent<TextMeshProUGUI>();
        lTmp.text = label; lTmp.fontSize = 11f; lTmp.color = C_SUB;
        lTmp.alignment = TextAlignmentOptions.Left; lTmp.raycastTarget = false;
        var lLe = lGO.AddComponent<LayoutElement>();
        lLe.preferredWidth = 78f; lLe.flexibleWidth = 0f;

        // Cuadradito con el color actual (marco claro + relleno) — clic abre un grid de colores
        var swGO = new GameObject("Swatch");
        swGO.transform.SetParent(row.transform, false);
        var frameImg = swGO.AddComponent<Image>();
        frameImg.color = new Color(0.45f, 0.51f, 0.60f, 1f);
        var swLe = swGO.AddComponent<LayoutElement>();
        swLe.preferredWidth = 14f; swLe.flexibleWidth = 0f;
        swLe.preferredHeight = 14f; swLe.flexibleHeight = 0f;

        var innerGO = new GameObject("Inner");
        innerGO.transform.SetParent(swGO.transform, false);
        var innerImg = innerGO.AddComponent<Image>();
        innerImg.raycastTarget = false;
        var innerRt = innerGO.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero; innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = Vector2.zero; innerRt.offsetMax = Vector2.zero; // sin marco: el color rellena todo el cuadradito

        var swBtn = swGO.AddComponent<Button>();
        swBtn.targetGraphic = frameImg;
        var dbc = swBtn.colors;
        dbc.normalColor      = new Color(0.45f, 0.51f, 0.60f, 1f);
        dbc.highlightedColor = new Color(0.7f, 0.77f, 0.87f, 1f);
        dbc.pressedColor     = new Color(0.4f, 0.47f, 0.57f, 1f);
        swBtn.colors = dbc;

        var swRt = swGO.GetComponent<RectTransform>();
        swBtn.onClick.AddListener(() =>
            ShowColorGridMenu(swRt, names, getCurrentIdx(), onPick));

        innerImg.color = VariantColor(names[getCurrentIdx()]);
        return innerImg;
    }

    (Slider, TMP_InputField) MakeSliderRow(Transform parent, string label, float min, float max, float initial,
        Action<float> onChanged, string fmt = "F1")
    {
        var row = new GameObject("SliderRow");
        row.transform.SetParent(parent, false);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 5f;
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.childControlWidth     = true;
        hlg.childControlHeight    = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        SetLE(row, 14f);

        // Etiqueta
        var lGO = new GameObject("Lbl");
        lGO.transform.SetParent(row.transform, false);
        var lTmp = lGO.AddComponent<TextMeshProUGUI>();
        lTmp.text = label; lTmp.fontSize = 11f; lTmp.color = C_SUB;
        lTmp.alignment = TextAlignmentOptions.Left; lTmp.raycastTarget = false;
        var lLe = lGO.AddComponent<LayoutElement>();
        lLe.preferredWidth = 78f; lLe.flexibleWidth = 0f;

        // Contenedor transparente del slider (sin fondo)
        var sGO = new GameObject("Slider");
        sGO.transform.SetParent(row.transform, false);
        var sLe = sGO.AddComponent<LayoutElement>();
        sLe.flexibleWidth = 1f;
        sLe.preferredHeight = 11f;

        // Línea fina (1 px) gris oscura
        var trackGO = new GameObject("Track");
        trackGO.transform.SetParent(sGO.transform, false);
        trackGO.AddComponent<Image>().color = new Color(0.25f, 0.28f, 0.35f, 1f);
        var trackRt = trackGO.GetComponent<RectTransform>();
        trackRt.anchorMin = new Vector2(0f, 0.5f); trackRt.anchorMax = new Vector2(1f, 0.5f);
        trackRt.offsetMin = new Vector2(3f, -0.5f); trackRt.offsetMax = new Vector2(-3f, 0.5f);

        // Fill Area vacío (fill transparente, solo queremos el handle)
        var fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(sGO.transform, false);
        var faRt = fillAreaGO.AddComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.5f); faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.offsetMin = new Vector2(3f, -0.5f); faRt.offsetMax = new Vector2(-3f, 0.5f);

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        fillGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        var fillRt = fillGO.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;

 // Handle Slide Area confinado al centro vertical (±2px)
        var haGO = new GameObject("Handle Slide Area");
        haGO.transform.SetParent(sGO.transform, false);
        var haRt = haGO.AddComponent<RectTransform>();
        haRt.anchorMin = new Vector2(0f, 0.5f); haRt.anchorMax = new Vector2(1f, 0.5f);
        haRt.offsetMin = new Vector2(3f, -2f);  haRt.offsetMax = new Vector2(-3f, 2f);

        // Handle: mismo color que la barra, muy pequeño
        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(haGO.transform, false);
        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = C_ACCENT;
        var handleRt = handleGO.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(6f, 6f);

        var slider = sGO.AddComponent<Slider>();
        slider.minValue      = min;
        slider.maxValue      = max;
        slider.value         = initial;
        slider.wholeNumbers  = false;
        slider.fillRect      = fillRt;
        slider.handleRect    = handleRt;
        slider.targetGraphic = handleImg;
        slider.direction     = Slider.Direction.LeftToRight;

 // Valor numérico editable: un clic y se puede escribir a mano.
        var vGO = new GameObject("Val");
        vGO.transform.SetParent(row.transform, false);
        var vBg = vGO.AddComponent<Image>(); vBg.color = Color.clear;
        var vLe = vGO.AddComponent<LayoutElement>();
        vLe.preferredWidth = 38f; vLe.flexibleWidth = 0f;

        var vTextGO = new GameObject("Text");
        vTextGO.transform.SetParent(vGO.transform, false);
        var vTmp = vTextGO.AddComponent<TextMeshProUGUI>();
        vTmp.fontSize = 10f; vTmp.color = C_ACCENT;
        vTmp.alignment = TextAlignmentOptions.Right;
        var vTextRt = vTextGO.GetComponent<RectTransform>();
        vTextRt.anchorMin = Vector2.zero; vTextRt.anchorMax = Vector2.one;
        vTextRt.offsetMin = Vector2.zero; vTextRt.offsetMax = Vector2.zero;

        var vField = vGO.AddComponent<TMP_InputField>();
        vField.targetGraphic = vBg;
        vField.textComponent = vTmp;
        vField.contentType   = TMP_InputField.ContentType.Standard;
        vField.text = initial.ToString(fmt);

        var capturedFmt = fmt;
        var capturedMin = min;
        var capturedMax = max;
        slider.onValueChanged.AddListener(v =>
        {
            vField.SetTextWithoutNotify(v.ToString(capturedFmt));
            onChanged(v);
        });
        vField.onEndEdit.AddListener(s =>
        {
            if (float.TryParse(s.Trim().Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                slider.value = Mathf.Clamp(parsed, capturedMin, capturedMax);
            else
                vField.SetTextWithoutNotify(slider.value.ToString(capturedFmt));
        });

        // Fondo más oscuro mientras se está editando, para que quede claro
        // que el campo está seleccionado y se puede escribir un valor.
        vField.onSelect.AddListener(_ => vBg.color = new Color(0f, 0f, 0f, 0.45f));
        vField.onDeselect.AddListener(_ => vBg.color = Color.clear);

        return (slider, vField);
    }

    void BuildPreviewArea(Transform parent)
    {
 // Contenedor exterior con borde cyan (estilo card del proyecto)
        var outerGO = new GameObject("PreviewOuter");
        outerGO.transform.SetParent(parent, false);
        outerGO.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.10f, 1f);
        SetLE(outerGO, 380f);

        // titleBarGO necesario como variable para SetAsLastSibling más abajo
        var titleBarGO = new GameObject("TitleBar"); // contenedor vacío (sin barra visible)
        titleBarGO.transform.SetParent(outerGO.transform, false);
        titleBarGO.AddComponent<RectTransform>();

 // Fondo "calle desenfocada" como imagen 2D de UI (no un plano 3D): así siempre
        // cubre el recuadro entero sea cual sea el zoom de la cámara 3D, sin bordes
        // visibles. La cámara del previsualizador limpia a transparente (ver
        // SetupPreviewCamera) para que este fondo se vea a través del RawImage.
        var bgImgGO = new GameObject("Preview2DBackground");
        bgImgGO.transform.SetParent(outerGO.transform, false);
        var bgImg = bgImgGO.AddComponent<RawImage>();
        bgImg.texture = CreateStreetTexture();
        var bgImgRt = bgImgGO.GetComponent<RectTransform>();
        bgImgRt.anchorMin = Vector2.zero; bgImgRt.anchorMax = Vector2.one;
        bgImgRt.offsetMin = bgImgRt.offsetMax = Vector2.zero;

 // RenderTexture llena el recuadro entero
        previewRT = new RenderTexture(2048, 2048, 24, RenderTextureFormat.ARGB32);
        previewRT.antiAliasing = 8;
        previewRT.filterMode   = FilterMode.Trilinear;
        previewRT.Create();

        var rawGO  = new GameObject("Preview3D");
        rawGO.transform.SetParent(outerGO.transform, false);
        rawGO.transform.SetAsLastSibling(); // encima del fondo 2D, pero los overlays de propiedades se añaden después y quedan por encima de este
        var rawImg = rawGO.AddComponent<RawImage>();
        rawImg.texture = previewRT;
        var rawRt = rawGO.GetComponent<RectTransform>();
        rawRt.anchorMin = Vector2.zero; rawRt.anchorMax = Vector2.one;
        rawRt.offsetMin = rawRt.offsetMax = Vector2.zero; // llena el recuadro entero

        // Drag para rotar
        var et = rawGO.AddComponent<EventTrigger>();
        AddET(et, EventTriggerType.Drag, d =>
        {
            if (!previewRoot) return;
            var delta = ((PointerEventData)d).delta;
            previewEuler.y += delta.x * 0.6f;
            previewEuler.x -= delta.y * 0.6f;
            previewRoot.transform.eulerAngles = previewEuler;
            if (selectedType == "Aro")
            {
                aroRotX = WrapAngle(previewEuler.x);
                aroRotY = WrapAngle(previewEuler.y);
                if (aroRotXSlider) { aroRotXSlider.SetValueWithoutNotify(aroRotX); aroRotXLabel.SetTextWithoutNotify(aroRotX.ToString("F0")); }
                if (aroRotYSlider) { aroRotYSlider.SetValueWithoutNotify(aroRotY); aroRotYLabel.SetTextWithoutNotify(aroRotY.ToString("F0")); }
                if (lastAroIconRt) lastAroIconRt.localEulerAngles = new Vector3(0f, 0f, -aroRotY);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Aro")
                    placed[selectedPlacedIdx].rotY = aroRotY;
            }
            else if (selectedType == "Bandera")
            {
                banderaRotX = WrapAngle(previewEuler.x);
                banderaRotY = WrapAngle(previewEuler.y);
                if (banderaRotXSlider) { banderaRotXSlider.SetValueWithoutNotify(banderaRotX); banderaRotXLabel.SetTextWithoutNotify(banderaRotX.ToString("F0")); }
                if (banderaRotYSlider) { banderaRotYSlider.SetValueWithoutNotify(banderaRotY); banderaRotYLabel.SetTextWithoutNotify(banderaRotY.ToString("F0")); }
                if (lastBanderaIconRt) lastBanderaIconRt.localEulerAngles = new Vector3(0f, 0f, -banderaRotY);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Bandera")
                { placed[selectedPlacedIdx].rotX = banderaRotX; placed[selectedPlacedIdx].rotY = banderaRotY; }
            }
            else if (selectedType == "Obstáculo")
            {
                obstaculoRotX = WrapAngle(previewEuler.x);
                obstaculoRotY = WrapAngle(previewEuler.y);
                if (obstaculoRotXSlider) { obstaculoRotXSlider.SetValueWithoutNotify(obstaculoRotX); obstaculoRotXLabel.SetTextWithoutNotify(obstaculoRotX.ToString("F0")); }
                if (obstaculoRotYSlider) { obstaculoRotYSlider.SetValueWithoutNotify(obstaculoRotY); obstaculoRotYLabel.SetTextWithoutNotify(obstaculoRotY.ToString("F0")); }
                if (lastObstaculoIconRt) lastObstaculoIconRt.localEulerAngles = new Vector3(0f, 0f, -obstaculoRotY);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Obstáculo")
                { placed[selectedPlacedIdx].rotX = obstaculoRotX; placed[selectedPlacedIdx].rotY = obstaculoRotY; }
            }
            else if (selectedType == "Moneda")
            {
                monedaRotX = WrapAngle(previewEuler.x);
                monedaRotY = WrapAngle(previewEuler.y);
                if (monedaRotXSlider) { monedaRotXSlider.SetValueWithoutNotify(monedaRotX); monedaRotXLabel.SetTextWithoutNotify(monedaRotX.ToString("F0")); }
                if (monedaRotYSlider) { monedaRotYSlider.SetValueWithoutNotify(monedaRotY); monedaRotYLabel.SetTextWithoutNotify(monedaRotY.ToString("F0")); }
                if (lastMonedaIconRt) lastMonedaIconRt.localEulerAngles = new Vector3(0f, 0f, -monedaRotY);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Moneda")
                { placed[selectedPlacedIdx].rotX = monedaRotX; placed[selectedPlacedIdx].rotY = monedaRotY; }
            }
            else if (selectedType == "Arco")
            {
                arcoRotX = WrapAngle(previewEuler.x);
                arcoRotY = WrapAngle(previewEuler.y);
                if (arcoRotXSlider) { arcoRotXSlider.SetValueWithoutNotify(arcoRotX); arcoRotXLabel.SetTextWithoutNotify(arcoRotX.ToString("F0")); }
                if (arcoRotYSlider) { arcoRotYSlider.SetValueWithoutNotify(arcoRotY); arcoRotYLabel.SetTextWithoutNotify(arcoRotY.ToString("F0")); }
                if (lastArcoIconRt) lastArcoIconRt.localEulerAngles = new Vector3(0f, 0f, -arcoRotY);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Arco")
                { placed[selectedPlacedIdx].rotX = arcoRotX; placed[selectedPlacedIdx].rotY = arcoRotY; }
            }
            else if (selectedType == "Segundo Arco")
            {
                arco2RotX = WrapAngle(previewEuler.x);
                arco2RotY = WrapAngle(previewEuler.y);
                if (arco2RotXSlider) { arco2RotXSlider.SetValueWithoutNotify(arco2RotX); arco2RotXLabel.SetTextWithoutNotify(arco2RotX.ToString("F0")); }
                if (arco2RotYSlider) { arco2RotYSlider.SetValueWithoutNotify(arco2RotY); arco2RotYLabel.SetTextWithoutNotify(arco2RotY.ToString("F0")); }
                if (lastArco2IconRt) lastArco2IconRt.localEulerAngles = new Vector3(0f, 0f, -arco2RotY);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Segundo Arco")
                { placed[selectedPlacedIdx].rotX = arco2RotX; placed[selectedPlacedIdx].rotY = arco2RotY; }
            }
            else if (selectedType == "Flecha")
            {
                flechaRotX = WrapAngle(previewEuler.x);
                flechaRotY = WrapAngle(previewEuler.y);
                if (flechaRotXSlider) { flechaRotXSlider.SetValueWithoutNotify(flechaRotX); flechaRotXLabel.SetTextWithoutNotify(flechaRotX.ToString("F0")); }
                if (flechaRotYSlider) { flechaRotYSlider.SetValueWithoutNotify(flechaRotY); flechaRotYLabel.SetTextWithoutNotify(flechaRotY.ToString("F0")); }
                // +90 respecto al resto de tipos — ver comentario junto a SpawnMarker
                // (caso "Flecha").
                if (lastFlechaIconRt) lastFlechaIconRt.localEulerAngles = new Vector3(0f, 0f, -flechaRotY + 90f);
                if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Flecha")
                { placed[selectedPlacedIdx].rotX = flechaRotX; placed[selectedPlacedIdx].rotY = flechaRotY; }
            }
        });

        // Mueve la barra de título encima del render (SetAsLastSibling)
        titleBarGO.transform.SetAsLastSibling();

 // Nombre del objeto overlay semitransparente en la parte inferior
        var nameBarGO = new GameObject("NameBar");
        nameBarGO.transform.SetParent(outerGO.transform, false);
        nameBarGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.50f);
        var nbRt = nameBarGO.GetComponent<RectTransform>();
        nbRt.anchorMin = Vector2.zero; nbRt.anchorMax = new Vector2(1f, 0f);
        nbRt.offsetMin = Vector2.zero; nbRt.offsetMax = new Vector2(0f, 24f);

        var nameGO = new GameObject("PrevName");
        nameGO.transform.SetParent(nameBarGO.transform, false);
        previewNameLabel = nameGO.AddComponent<TextMeshProUGUI>();
        previewNameLabel.text = selectedType; previewNameLabel.fontSize = 10f;
        previewNameLabel.color = C_TEXT; previewNameLabel.fontStyle = FontStyles.Bold;
        previewNameLabel.alignment = TextAlignmentOptions.Center;
        previewNameLabel.raycastTarget = false;
        var nRt = nameGO.GetComponent<RectTransform>();
        nRt.anchorMin = Vector2.zero; nRt.anchorMax = Vector2.one;
        nRt.offsetMin = nRt.offsetMax = Vector2.zero;

 // Overlay propiedades del aro
        var overlayGO = new GameObject("AroPropsOverlay");
        overlayGO.transform.SetParent(outerGO.transform, false);
        overlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgO = overlayGO.AddComponent<VerticalLayoutGroup>();
        vlgO.spacing               = 3f;
        vlgO.childControlHeight    = false;
        vlgO.childForceExpandHeight = false;
        vlgO.childControlWidth     = true;
        vlgO.childForceExpandWidth  = true;
        vlgO.padding               = new RectOffset(6, 6, 5, 5);
        var oRt = overlayGO.GetComponent<RectTransform>();
        oRt.anchorMin = new Vector2(0f, 0f);
        oRt.anchorMax = new Vector2(1f, 0f);
        oRt.offsetMin = new Vector2(0f, 24f);
        oRt.offsetMax = new Vector2(0f, 191f);
        aroPropsPanel = overlayGO;

        AddLabel(overlayGO.transform, "PROPIEDADES DEL ARO", 13f, C_SUB, FontStyles.Bold, 15f);

        MakeSliderRow(overlayGO.transform, "Altura (m)", 0f, 100f, aroHeight,
            v => { aroHeight = v; }, "F1");
        (aroDiameterSlider, aroDiameterLabel) = MakeSliderRow(overlayGO.transform, "Diámetro (m)", 0f, 20f, aroDiameter, v =>
        {
            aroDiameter = v;
            ApplyAroScale(aroPreviewInstance, PREVIEW_REF_DIAMETER, aroGrosor);
            RecenterInPlace(aroPreviewInstance);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = aroDiameter;
        }, "F1");
        (aroGrosorSlider, aroGrosorLabel) = MakeSliderRow(overlayGO.transform, "Grosor", 0.2f, 50f, aroGrosor, v =>
        {
            aroGrosor = v;
            ApplyAroScale(aroPreviewInstance, PREVIEW_REF_DIAMETER, aroGrosor);
            RecenterInPlace(aroPreviewInstance);
            if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Aro")
                placed[selectedPlacedIdx].aroGrosor = aroGrosor;
        }, "F2");
        (aroHuecoSlider, aroHuecoLabel) = MakeSliderRow(overlayGO.transform, "Tamaño hueco", 0f, 8f, aroHueco, v =>
        {
            aroHueco = v;
            AroHuecoUtil.ApplyHueco(aroPreviewInstance, aroHueco);
            RecenterInPlace(aroPreviewInstance);
            if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Aro")
                placed[selectedPlacedIdx].aroHueco = aroHueco;
        }, "F2");
        (aroRotXSlider, aroRotXLabel) = MakeSliderRow(overlayGO.transform, "Rot. X (°)", -180f, 180f, aroRotX, v =>
        {
            aroRotX = v;
            if (previewRoot)
            { previewEuler = new Vector3(aroRotX, aroRotY, aroRotZ); previewRoot.transform.eulerAngles = previewEuler; }
        }, "F0");
        (aroRotYSlider, aroRotYLabel) = MakeSliderRow(overlayGO.transform, "Rot. Y (°)", -180f, 180f, aroRotY, v =>
        {
            aroRotY = v;
            if (previewRoot)
            { previewEuler = new Vector3(aroRotX, aroRotY, aroRotZ); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastAroIconRt) lastAroIconRt.localEulerAngles = new Vector3(0f, 0f, -aroRotY);
            if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Aro")
                placed[selectedPlacedIdx].rotY = aroRotY;
        }, "F0");
        aroVariantSwatch = BuildVariantColorRow(overlayGO.transform, AroVariantNames, () => aroVariantIdx, SelectAroVariant);

        overlayGO.SetActive(selectedType == "Aro");

        // ── Overlay propiedades de Bandera ────────────────────────────────
        var banderaOverlayGO = new GameObject("BanderaPropsOverlay");
        banderaOverlayGO.transform.SetParent(outerGO.transform, false);
        banderaOverlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgB = banderaOverlayGO.AddComponent<VerticalLayoutGroup>();
        vlgB.spacing = 3f; vlgB.childControlHeight = false; vlgB.childForceExpandHeight = false;
        vlgB.childControlWidth = true; vlgB.childForceExpandWidth = true;
        vlgB.padding = new RectOffset(6, 6, 5, 5);
        var bRt = banderaOverlayGO.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0f, 0f); bRt.anchorMax = new Vector2(1f, 0f);
        bRt.offsetMin = new Vector2(0f, 24f); bRt.offsetMax = new Vector2(0f, 145f);
        banderaPropsPanel = banderaOverlayGO;
        AddLabel(banderaOverlayGO.transform, "PROPIEDADES DE BANDERA", 13f, C_SUB, FontStyles.Bold, 15f);
        (banderaHeightSlider, banderaHeightLabel) = MakeSliderRow(banderaOverlayGO.transform, "Altura (m)", 0f, 10f, banderaHeight, v =>
        {
            banderaHeight = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].height = v;
        }, "F1");
        (banderaSizeSlider, banderaSizeLabel) = MakeSliderRow(banderaOverlayGO.transform, "Tamaño", 0.1f, 5f, banderaSize, v =>
        {
            banderaSize = v;
            if (banderaPreviewInstance)
            {
                banderaPreviewInstance.transform.localScale = Vector3.one * (PREVIEW_REF_SIZE * BANDERA_BASE_SCALE);
                RecenterInPlace(banderaPreviewInstance);
            }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = v;
        }, "F2");
        (banderaRotXSlider, banderaRotXLabel) = MakeSliderRow(banderaOverlayGO.transform, "Rot. X (°)", -180f, 180f, banderaRotX, v =>
        {
            banderaRotX = v;
            if (previewRoot) { previewEuler = new Vector3(banderaRotX, banderaRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotX = v;
        }, "F0");
        (banderaRotYSlider, banderaRotYLabel) = MakeSliderRow(banderaOverlayGO.transform, "Rot. Y (°)", -180f, 180f, banderaRotY, v =>
        {
            banderaRotY = v;
            if (previewRoot) { previewEuler = new Vector3(banderaRotX, banderaRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastBanderaIconRt) lastBanderaIconRt.localEulerAngles = new Vector3(0f, 0f, -banderaRotY);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotY = v;
        }, "F0");
        banderaVariantSwatch = BuildVariantColorRow(banderaOverlayGO.transform, BanderaVariantNames, () => banderaVariantIdx, SelectBanderaVariant);
        banderaOverlayGO.SetActive(selectedType == "Bandera");

        // ── Overlay propiedades de Obstáculo ──────────────────────────────
        var obstOverlayGO = new GameObject("ObstaculoPropsOverlay");
        obstOverlayGO.transform.SetParent(outerGO.transform, false);
        obstOverlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgOb = obstOverlayGO.AddComponent<VerticalLayoutGroup>();
        vlgOb.spacing = 3f; vlgOb.childControlHeight = false; vlgOb.childForceExpandHeight = false;
        vlgOb.childControlWidth = true; vlgOb.childForceExpandWidth = true;
        vlgOb.padding = new RectOffset(6, 6, 5, 5);
        var obRt = obstOverlayGO.GetComponent<RectTransform>();
        obRt.anchorMin = new Vector2(0f, 0f); obRt.anchorMax = new Vector2(1f, 0f);
        obRt.offsetMin = new Vector2(0f, 24f); obRt.offsetMax = new Vector2(0f, 214f);
        obstaculoPropsPanel = obstOverlayGO;
        AddLabel(obstOverlayGO.transform, "PROPIEDADES DE OBSTÁCULO", 13f, C_SUB, FontStyles.Bold, 15f);
        (obstaculoHeightSlider, obstaculoHeightLabel) = MakeSliderRow(obstOverlayGO.transform, "Altura (m)", 0f, 5f, obstaculoHeight, v =>
        {
            obstaculoHeight = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].height = v;
        }, "F1");
        (obstaculoSizeSlider, obstaculoSizeLabel) = MakeSliderRow(obstOverlayGO.transform, "Tamaño", 0.1f, 5f, obstaculoSize, v =>
        {
            obstaculoSize = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = v;
        }, "F2");
        (obstaculoLongHorizSlider, obstaculoLongHorizLabel) = MakeSliderRow(obstOverlayGO.transform, "Long. horiz.", 0.5f, 10f, obstaculoLongHoriz, v =>
        {
            obstaculoLongHoriz = v;
            if (obstaculoPreviewInstance)
            {
                obstaculoPreviewInstance.transform.localScale = new Vector3(obstaculoLongHoriz / 3f, 1f, 1f);
                if (obstaculoSupportChild) obstaculoSupportChild.transform.localScale = new Vector3(1f, 1f, obstaculoLongVert / 3f);
                if (obstaculoBodyChild) obstaculoBodyChild.transform.localPosition = new Vector3(0f, 0f, obstaculoBarRefZ * (obstaculoLongVert / 3f - 1f));
                RecenterInPlace(obstaculoPreviewInstance);
            }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].obstaculoLongHoriz = v;
        }, "F1");
        (obstaculoLongVertSlider, obstaculoLongVertLabel) = MakeSliderRow(obstOverlayGO.transform, "Long. vert.", 0.5f, 10f, obstaculoLongVert, v =>
        {
            obstaculoLongVert = v;
            if (obstaculoPreviewInstance)
            {
                obstaculoPreviewInstance.transform.localScale = new Vector3(obstaculoLongHoriz / 3f, 1f, 1f);
                if (obstaculoSupportChild) obstaculoSupportChild.transform.localScale = new Vector3(1f, 1f, obstaculoLongVert / 3f);
                if (obstaculoBodyChild) obstaculoBodyChild.transform.localPosition = new Vector3(0f, 0f, obstaculoBarRefZ * (obstaculoLongVert / 3f - 1f));
                RecenterInPlace(obstaculoPreviewInstance);
            }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].obstaculoLongVert = v;
        }, "F1");
        (obstaculoRotXSlider, obstaculoRotXLabel) = MakeSliderRow(obstOverlayGO.transform, "Rot. X (°)", -180f, 180f, obstaculoRotX, v =>
        {
            obstaculoRotX = v;
            if (previewRoot) { previewEuler = new Vector3(obstaculoRotX, obstaculoRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotX = v;
        }, "F0");
        (obstaculoRotYSlider, obstaculoRotYLabel) = MakeSliderRow(obstOverlayGO.transform, "Rot. Y (°)", -180f, 180f, obstaculoRotY, v =>
        {
            obstaculoRotY = v;
            if (previewRoot) { previewEuler = new Vector3(obstaculoRotX, obstaculoRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastObstaculoIconRt) lastObstaculoIconRt.localEulerAngles = new Vector3(0f, 0f, -obstaculoRotY);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotY = v;
        }, "F0");
        obstaculoVariantSwatch = BuildVariantColorRow(obstOverlayGO.transform, ObstaculoVariantNames, () => obstaculoVariantIdx, SelectObstaculoVariant, "Color barra");
        obstaculoSupportVariantSwatch = BuildVariantColorRow(obstOverlayGO.transform, ObstaculoVariantNames, () => obstaculoSupportVariantIdx, SelectObstaculoSupportVariant, "Color soporte");
        obstOverlayGO.SetActive(selectedType == "Obstáculo");

        // ── Overlay propiedades de Moneda ──────────────────────────────────
        var monedaOverlayGO = new GameObject("MonedaPropsOverlay");
        monedaOverlayGO.transform.SetParent(outerGO.transform, false);
        monedaOverlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgMo = monedaOverlayGO.AddComponent<VerticalLayoutGroup>();
        vlgMo.spacing = 3f; vlgMo.childControlHeight = false; vlgMo.childForceExpandHeight = false;
        vlgMo.childControlWidth = true; vlgMo.childForceExpandWidth = true;
        vlgMo.padding = new RectOffset(6, 6, 5, 5);
        var moRt = monedaOverlayGO.GetComponent<RectTransform>();
        moRt.anchorMin = new Vector2(0f, 0f); moRt.anchorMax = new Vector2(1f, 0f);
        moRt.offsetMin = new Vector2(0f, 24f); moRt.offsetMax = new Vector2(0f, 145f);
        monedaPropsPanel = monedaOverlayGO;
        AddLabel(monedaOverlayGO.transform, "PROPIEDADES DE MONEDA", 13f, C_SUB, FontStyles.Bold, 15f);
        (monedaHeightSlider, monedaHeightLabel) = MakeSliderRow(monedaOverlayGO.transform, "Altura (m)", 0f, 5f, monedaHeight, v =>
        {
            monedaHeight = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].height = v;
        }, "F1");
        (monedaSizeSlider, monedaSizeLabel) = MakeSliderRow(monedaOverlayGO.transform, "Diámetro (m)", 0f, 20f, monedaSize, v =>
        {
            monedaSize = v;
            ApplyMonedaScale(monedaPreviewInstance, PREVIEW_REF_SIZE);
            RecenterInPlace(monedaPreviewInstance);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = v;
        }, "F1");
        (monedaRotXSlider, monedaRotXLabel) = MakeSliderRow(monedaOverlayGO.transform, "Rot. X (°)", -180f, 180f, monedaRotX, v =>
        {
            monedaRotX = v;
            if (previewRoot) { previewEuler = new Vector3(monedaRotX, monedaRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotX = v;
        }, "F0");
        (monedaRotYSlider, monedaRotYLabel) = MakeSliderRow(monedaOverlayGO.transform, "Rot. Y (°)", -180f, 180f, monedaRotY, v =>
        {
            monedaRotY = v;
            if (previewRoot) { previewEuler = new Vector3(monedaRotX, monedaRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastMonedaIconRt) lastMonedaIconRt.localEulerAngles = new Vector3(0f, 0f, -monedaRotY);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotY = v;
        }, "F0");
        monedaVariantSwatch = BuildVariantColorRow(monedaOverlayGO.transform, MonedaVariantNames, () => monedaVariantIdx, SelectMonedaVariant);
        monedaOverlayGO.SetActive(selectedType == "Moneda");

        // ── Overlay propiedades de Arco ─────────────────────────────────────
        var arcoOverlayGO = new GameObject("ArcoPropsOverlay");
        arcoOverlayGO.transform.SetParent(outerGO.transform, false);
        arcoOverlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgAc = arcoOverlayGO.AddComponent<VerticalLayoutGroup>();
        vlgAc.spacing = 3f; vlgAc.childControlHeight = false; vlgAc.childForceExpandHeight = false;
        vlgAc.childControlWidth = true; vlgAc.childForceExpandWidth = true;
        vlgAc.padding = new RectOffset(6, 6, 5, 5);
        var acRt = arcoOverlayGO.GetComponent<RectTransform>();
        acRt.anchorMin = new Vector2(0f, 0f); acRt.anchorMax = new Vector2(1f, 0f);
        acRt.offsetMin = new Vector2(0f, 24f); acRt.offsetMax = new Vector2(0f, 191f);
        arcoPropsPanel = arcoOverlayGO;
        AddLabel(arcoOverlayGO.transform, "PROPIEDADES DE ARCO", 13f, C_SUB, FontStyles.Bold, 15f);
        (arcoHeightSlider, arcoHeightLabel) = MakeSliderRow(arcoOverlayGO.transform, "Altura (m)", 0f, 5f, arcoHeight, v =>
        {
            arcoHeight = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].height = v;
        }, "F1");
        (arcoSizeSlider, arcoSizeLabel) = MakeSliderRow(arcoOverlayGO.transform, "Tamaño", 0.1f, 5f, arcoSize, v =>
        {
            arcoSize = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = v;
        }, "F2");
        (arcoLongHorizSlider, arcoLongHorizLabel) = MakeSliderRow(arcoOverlayGO.transform, "Long. horiz.", 0.5f, 10f, arcoLongHoriz, v =>
        {
            arcoLongHoriz = v;
            if (arcoPreviewInstance) { arcoPreviewInstance.transform.localScale = new Vector3(arcoLongHoriz / 3f, arcoLongHoriz / 3f, arcoLongVert / 3f); RecenterInPlace(arcoPreviewInstance); }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].arcoLongHoriz = v;
        }, "F1");
        (arcoLongVertSlider, arcoLongVertLabel) = MakeSliderRow(arcoOverlayGO.transform, "Long. vert.", 0.5f, 10f, arcoLongVert, v =>
        {
            arcoLongVert = v;
            if (arcoPreviewInstance) { arcoPreviewInstance.transform.localScale = new Vector3(arcoLongHoriz / 3f, arcoLongHoriz / 3f, arcoLongVert / 3f); RecenterInPlace(arcoPreviewInstance); }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].arcoLongVert = v;
        }, "F1");
        (arcoRotXSlider, arcoRotXLabel) = MakeSliderRow(arcoOverlayGO.transform, "Rot. X (°)", -180f, 180f, arcoRotX, v =>
        {
            arcoRotX = v;
            if (previewRoot) { previewEuler = new Vector3(arcoRotX, arcoRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotX = v;
        }, "F0");
        (arcoRotYSlider, arcoRotYLabel) = MakeSliderRow(arcoOverlayGO.transform, "Rot. Y (°)", -180f, 180f, arcoRotY, v =>
        {
            arcoRotY = v;
            if (previewRoot) { previewEuler = new Vector3(arcoRotX, arcoRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastArcoIconRt) lastArcoIconRt.localEulerAngles = new Vector3(0f, 0f, -arcoRotY);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotY = v;
        }, "F0");
        arcoVariantSwatch = BuildVariantColorRow(arcoOverlayGO.transform, ArcoVariantNames, () => arcoVariantIdx, SelectArcoVariant);
        arcoOverlayGO.SetActive(selectedType == "Arco");

        // ── Overlay propiedades de Segundo Arco ───────────────────────────────────
        var arco2OverlayGO = new GameObject("Arco2PropsOverlay");
        arco2OverlayGO.transform.SetParent(outerGO.transform, false);
        arco2OverlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgAc2 = arco2OverlayGO.AddComponent<VerticalLayoutGroup>();
        vlgAc2.spacing = 3f; vlgAc2.childControlHeight = false; vlgAc2.childForceExpandHeight = false;
        vlgAc2.childControlWidth = true; vlgAc2.childForceExpandWidth = true;
        vlgAc2.padding = new RectOffset(6, 6, 5, 5);
        var ac2Rt = arco2OverlayGO.GetComponent<RectTransform>();
        ac2Rt.anchorMin = new Vector2(0f, 0f); ac2Rt.anchorMax = new Vector2(1f, 0f);
        ac2Rt.offsetMin = new Vector2(0f, 24f); ac2Rt.offsetMax = new Vector2(0f, 191f);
        arco2PropsPanel = arco2OverlayGO;
        AddLabel(arco2OverlayGO.transform, "PROPIEDADES DEL SEGUNDO ARCO", 13f, C_SUB, FontStyles.Bold, 15f);
        (arco2HeightSlider, arco2HeightLabel) = MakeSliderRow(arco2OverlayGO.transform, "Altura (m)", 0f, 5f, arco2Height, v =>
        {
            arco2Height = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].height = v;
        }, "F1");
        (arco2SizeSlider, arco2SizeLabel) = MakeSliderRow(arco2OverlayGO.transform, "Tamaño", 0.1f, 5f, arco2Size, v =>
        {
            arco2Size = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = v;
        }, "F2");
        (arco2LongHorizSlider, arco2LongHorizLabel) = MakeSliderRow(arco2OverlayGO.transform, "Long. horiz.", 0.5f, 10f, arco2LongHoriz, v =>
        {
            arco2LongHoriz = v;
            if (arco2PreviewInstance) { arco2PreviewInstance.transform.localScale = new Vector3(arco2LongHoriz / 3f, arco2LongHoriz / 3f, arco2LongVert / 3f); RecenterInPlace(arco2PreviewInstance); }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].arco2LongHoriz = v;
        }, "F1");
        (arco2LongVertSlider, arco2LongVertLabel) = MakeSliderRow(arco2OverlayGO.transform, "Long. vert.", 0.5f, 10f, arco2LongVert, v =>
        {
            arco2LongVert = v;
            if (arco2PreviewInstance) { arco2PreviewInstance.transform.localScale = new Vector3(arco2LongHoriz / 3f, arco2LongHoriz / 3f, arco2LongVert / 3f); RecenterInPlace(arco2PreviewInstance); }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].arco2LongVert = v;
        }, "F1");
        (arco2RotXSlider, arco2RotXLabel) = MakeSliderRow(arco2OverlayGO.transform, "Rot. X (°)", -180f, 180f, arco2RotX, v =>
        {
            arco2RotX = v;
            if (previewRoot) { previewEuler = new Vector3(arco2RotX, arco2RotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotX = v;
        }, "F0");
        (arco2RotYSlider, arco2RotYLabel) = MakeSliderRow(arco2OverlayGO.transform, "Rot. Y (°)", -180f, 180f, arco2RotY, v =>
        {
            arco2RotY = v;
            if (previewRoot) { previewEuler = new Vector3(arco2RotX, arco2RotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastArco2IconRt) lastArco2IconRt.localEulerAngles = new Vector3(0f, 0f, -arco2RotY);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotY = v;
        }, "F0");
        arco2VariantSwatch = BuildVariantColorRow(arco2OverlayGO.transform, Arco2VariantNames, () => arco2VariantIdx, SelectArco2Variant);
        arco2OverlayGO.SetActive(selectedType == "Segundo Arco");

        // ── Overlay propiedades de Flecha ─────────────────────────────────
        var flechaOverlayGO = new GameObject("FlechaPropsOverlay");
        flechaOverlayGO.transform.SetParent(outerGO.transform, false);
        flechaOverlayGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        var vlgFl = flechaOverlayGO.AddComponent<VerticalLayoutGroup>();
        vlgFl.spacing = 3f; vlgFl.childControlHeight = false; vlgFl.childForceExpandHeight = false;
        vlgFl.childControlWidth = true; vlgFl.childForceExpandWidth = true;
        vlgFl.padding = new RectOffset(6, 6, 5, 5);
        var flRt = flechaOverlayGO.GetComponent<RectTransform>();
        flRt.anchorMin = new Vector2(0f, 0f); flRt.anchorMax = new Vector2(1f, 0f);
        flRt.offsetMin = new Vector2(0f, 24f); flRt.offsetMax = new Vector2(0f, 168f);
        flechaPropsPanel = flechaOverlayGO;
        AddLabel(flechaOverlayGO.transform, "PROPIEDADES DE FLECHA", 13f, C_SUB, FontStyles.Bold, 15f);
        (flechaHeightSlider, flechaHeightLabel) = MakeSliderRow(flechaOverlayGO.transform, "Altura (m)", 0f, 10f, flechaHeight, v =>
        {
            flechaHeight = v;
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].height = v;
        }, "F1");
        (flechaSizeSlider, flechaSizeLabel) = MakeSliderRow(flechaOverlayGO.transform, "Tamaño", 0.1f, 5f, flechaSize, v =>
        {
            flechaSize = v;
            ApplyFlechaScale(flechaPreviewInstance, flechaGrosor, flechaLongHoriz, PREVIEW_REF_SIZE);
            RecenterInPlace(flechaPreviewInstance);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].diameter = v;
        }, "F2");
        (flechaGrosorSlider, flechaGrosorLabel) = MakeSliderRow(flechaOverlayGO.transform, "Grosor", 0.2f, 50f, flechaGrosor, v =>
        {
            flechaGrosor = v;
            ApplyFlechaScale(flechaPreviewInstance, flechaGrosor, flechaLongHoriz, PREVIEW_REF_SIZE);
            RecenterInPlace(flechaPreviewInstance);
            if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Flecha")
                placed[selectedPlacedIdx].flechaGrosor = flechaGrosor;
        }, "F2");
        (flechaLongHorizSlider, flechaLongHorizLabel) = MakeSliderRow(flechaOverlayGO.transform, "Long. horiz.", 0.5f, 10f, flechaLongHoriz, v =>
        {
            flechaLongHoriz = v;
            ApplyFlechaScale(flechaPreviewInstance, flechaGrosor, flechaLongHoriz, PREVIEW_REF_SIZE);
            RecenterInPlace(flechaPreviewInstance);
            if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Flecha")
                placed[selectedPlacedIdx].flechaLongHoriz = flechaLongHoriz;
        }, "F1");
        (flechaRotXSlider, flechaRotXLabel) = MakeSliderRow(flechaOverlayGO.transform, "Rot. X (°)", -180f, 180f, flechaRotX, v =>
        {
            flechaRotX = v;
            if (previewRoot) { previewEuler = new Vector3(flechaRotX, flechaRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotX = v;
        }, "F0");
        (flechaRotYSlider, flechaRotYLabel) = MakeSliderRow(flechaOverlayGO.transform, "Rot. Y (°)", -180f, 180f, flechaRotY, v =>
        {
            flechaRotY = v;
            if (previewRoot) { previewEuler = new Vector3(flechaRotX, flechaRotY, 0f); previewRoot.transform.eulerAngles = previewEuler; }
            if (lastFlechaIconRt) lastFlechaIconRt.localEulerAngles = new Vector3(0f, 0f, -flechaRotY + 90f);
            if (selectedPlacedIdx >= 0) placed[selectedPlacedIdx].rotY = v;
        }, "F0");
        flechaVariantSwatch = BuildVariantColorRow(flechaOverlayGO.transform, FlechaVariantNames, () => flechaVariantIdx, SelectFlechaVariant);
        flechaOverlayGO.SetActive(selectedType == "Flecha");

        // Crea la cámara y el objeto inicial
        SetupPreviewCamera();
        SpawnPreviewObject(selectedType);
    }

    void SetupPreviewCamera()
    {
 // Escena aislada igual que el visualizador de escena de Unity. Nombre único
        // por instancia (GetInstanceID) en vez de fijo: OnDestroy() descarga la
        // escena de forma ASÍNCRONA (UnloadSceneAsync), así que si se guarda una
        // misión y se reabre el editor antes de que termine esa descarga, un
        // nombre fijo chocaría con la escena todavía viva y CreateScene fallaría
        // en silencio, dejando la previsualización rota.
        previewScene = SceneManager.CreateScene("__AroPreview__" + GetInstanceID(),
            new CreateSceneParameters(LocalPhysicsMode.None));

        var active = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(previewScene);

        // Cámara
        var camGO = new GameObject("PreviewCamera");
        previewCam = camGO.AddComponent<Camera>();
        previewCam.targetTexture   = previewRT;
        previewCam.cullingMask     = 1 << OBJECT_PREVIEW_LAYER; // solo ve objetos de este previsualizador (ver SetLayerRecursively en SpawnPreviewObject)
        // SolidColor con alpha 0 (no Skybox): el fondo "calle desenfocada" ahora es una
        // imagen 2D de UI colocada detrás del RawImage (ver BuildPreviewArea), así que
        // siempre cubre el recuadro entero sea cual sea el zoom 3D. La cámara solo
        // necesita pintar el objeto y dejar transparente todo lo demás para que se
        // vea esa imagen de fondo a través del alpha del RawImage.
        previewCam.clearFlags      = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = new Color(0.04f, 0.06f, 0.14f, 0f);
        previewCam.fieldOfView     = 40f;
        previewCam.nearClipPlane   = 0.01f;
        previewCam.farClipPlane    = 500f;
        previewCam.depth           = -10f;
        previewCam.allowHDR        = false;
        previewCam.allowMSAA       = true;
        camGO.transform.SetPositionAndRotation(new Vector3(0f, 0.5f, -6f), Quaternion.identity);
        previewCam.transform.LookAt(Vector3.zero);

        // Luz principal (3-point lighting igual que Unity scene view)
        void MakeLight(Color col, float intensity, Vector3 euler)
        {
            var go = new GameObject("L");
            var l  = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = col; l.intensity = intensity;
            go.transform.rotation = Quaternion.Euler(euler);
        }
        MakeLight(new Color(1f, 0.95f, 0.88f), 1.4f, new Vector3(35f, -40f, 0f));
        MakeLight(new Color(0.55f, 0.75f, 1.0f), 0.6f, new Vector3(20f, 140f, 0f));
        MakeLight(new Color(0.20f, 0.90f, 1.0f), 0.4f, new Vector3(-20f, 170f, 0f));

        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.22f, 0.28f);

        SceneManager.SetActiveScene(active); // restaura la escena activa original
    }

    // =========================================================================
    //  Previsualización del escenario completo: todos los objetos ya colocados,
    //  con la misma instanciación que usa la misión real jugada de verdad
    //  (MapSceneController.PlaceAll) — cámara orbital propia, en una escena
    //  aislada aparte (misma técnica que el previsualizador de un solo objeto
    //  de arriba, pero sin acoplarse a él: aquí hay muchos objetos a la vez, a
    //  sus posiciones de mundo reales, no todos centrados en el origen).
    // =========================================================================

    void OpenScenarioPreview()
    {
        // Centro visible del mapa 2D ahora mismo (con el zoom/paneo que tenga en
        // este momento) -> mismas coordenadas de mundo que usan los objetos ya
        // colocados (MapSceneController.GPSToWorld), para que la cámara arranque
        // mirando justo a esa zona en vez de a un punto arbitrario.
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(null, mapAreaRt.position);
        scenarioPreviewLookAt = ScreenToLatLon(screenCenter, out double lat, out double lon)
            ? MapSceneController.GPSToWorld((float)lat, (float)lon)
            : Vector3.zero;

        scenarioPreviewYaw   = 0f;
        scenarioPreviewPitch = 35f;
        scenarioPreviewDist  = 60f;

        if (scenarioPreviewPanel == null) BuildScenarioPreviewPanel();
        scenarioPreviewPanel.transform.SetAsLastSibling();
        scenarioPreviewPanel.SetActive(true);

        // Siempre arranca en tamaño normal (no maximizado), aunque la última
        // vez se cerrase maximizada.
        scenarioPreviewMaximized = true; // fuerza a que el toggle de abajo la deje en "false" = normal
        ToggleScenarioPreviewMaximize();

        RebuildScenarioPreviewObjects();
        UpdateScenarioPreviewCameraTransform();
    }

    void CloseScenarioPreview()
    {
        if (scenarioPreviewPanel != null) scenarioPreviewPanel.SetActive(false);
        if (scenarioPreviewRoot != null) { Destroy(scenarioPreviewRoot); scenarioPreviewRoot = null; }
    }

    // Alterna entre el tamaño normal (centrado, movible con la barra de
    // título) y pantalla casi completa (anclado a los 4 bordes del bloqueador,
    // con un margen fijo) — mismo RectTransform, solo cambia su modo de anclaje.
    void ToggleScenarioPreviewMaximize()
    {
        if (scenarioPreviewPanelRt == null) return;
        scenarioPreviewMaximized = !scenarioPreviewMaximized;

        if (scenarioPreviewMaximized)
        {
            // Pantalla completa de verdad: sin margen, ocupa lo mismo que el
            // bloqueador entero.
            scenarioPreviewPanelRt.anchorMin = Vector2.zero;
            scenarioPreviewPanelRt.anchorMax = Vector2.one;
            scenarioPreviewPanelRt.offsetMin = Vector2.zero;
            scenarioPreviewPanelRt.offsetMax = Vector2.zero;
        }
        else
        {
            scenarioPreviewPanelRt.anchorMin = scenarioPreviewPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
            scenarioPreviewPanelRt.pivot = new Vector2(0.5f, 0.5f);
            scenarioPreviewPanelRt.anchoredPosition = Vector2.zero;
            scenarioPreviewPanelRt.sizeDelta = scenarioPreviewNormalSize;
        }

        // Icono: grande mientras la ventana está pequeña ("puedes agrandarla"),
        // pequeño en cuanto ya está maximizada ("puedes encogerla").
        if (scenarioPreviewMaxIconRt != null)
            scenarioPreviewMaxIconRt.sizeDelta =
                scenarioPreviewMaximized ? MAXICON_SIZE_MAXIMIZED : MAXICON_SIZE_WINDOWED;
    }

    void BuildScenarioPreviewPanel()
    {
        // Bloqueador a pantalla completa (oscurece el resto mientras está abierto).
        scenarioPreviewPanel = new GameObject("ScenarioPreviewModal");
        scenarioPreviewPanel.transform.SetParent(ownCanvas.transform, false);
        var blockerRt = scenarioPreviewPanel.AddComponent<RectTransform>();
        blockerRt.anchorMin = Vector2.zero; blockerRt.anchorMax = Vector2.one;
        blockerRt.offsetMin = blockerRt.offsetMax = Vector2.zero;
        scenarioPreviewPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        // Panel central
        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(scenarioPreviewPanel.transform, false);
        var panelRt = panelGO.AddComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = scenarioPreviewNormalSize;
        panelGO.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.09f, 1f);
        scenarioPreviewPanelRt = panelRt;

        // Barra de título: solo arriba (sin márgenes a los lados/abajo, ver
        // rawRt más abajo) — el título y el botón Cerrar viven dentro de ella,
        // no flotando encima de la previsualización.
        const float TOP_BAR_H = 36f;
        var titleBarGO = new GameObject("TitleBar");
        titleBarGO.transform.SetParent(panelGO.transform, false);
        titleBarGO.AddComponent<Image>().color = new Color(0.06f, 0.09f, 0.14f, 1f);
        var titleBarRt = titleBarGO.GetComponent<RectTransform>();
        titleBarRt.anchorMin = new Vector2(0f, 1f); titleBarRt.anchorMax = new Vector2(1f, 1f);
        titleBarRt.pivot = new Vector2(0.5f, 1f);
        titleBarRt.anchoredPosition = Vector2.zero; titleBarRt.sizeDelta = new Vector2(0f, TOP_BAR_H);
        var titleBarEt = titleBarGO.AddComponent<EventTrigger>();
        AddET(titleBarEt, EventTriggerType.Drag, d =>
        {
            // /scaleFactor: el delta del puntero viene en píxeles de pantalla,
            // pero anchoredPosition está en unidades de canvas — con
            // CanvasScaler.ScaleWithScreenSize ambas cosas solo coinciden 1:1
            // si la resolución real es igual a la de referencia.
            panelRt.anchoredPosition += ((PointerEventData)d).delta / ownCanvasComp.scaleFactor;
        });

        var titleTxtGO = new GameObject("Txt");
        titleTxtGO.transform.SetParent(titleBarGO.transform, false);
        var titleTmp = titleTxtGO.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "PREVISUALIZACIÓN DE LA MISIÓN";
        titleTmp.fontSize = 13f; titleTmp.color = C_ACCENT; titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Left; titleTmp.raycastTarget = false;
        var titleTxtRt = titleTxtGO.GetComponent<RectTransform>();
        titleTxtRt.anchorMin = Vector2.zero; titleTxtRt.anchorMax = Vector2.one;
        titleTxtRt.offsetMin = new Vector2(14f, 0f); titleTxtRt.offsetMax = new Vector2(-70f, 0f);

        // Maximizar/restaurar — icono de cuadrado hueco construido con dos
        // Image anidadas, no un glifo de fuente ("□"/"✕" no los soporta la
        // fuente de este proyecto: salían como el cuadrado de "carácter no
        // encontrado", justo el bug que se ve sin este cambio).
        var maxBtnGO = new GameObject("B_Max");
        maxBtnGO.transform.SetParent(titleBarGO.transform, false);
        Color maxBtnFill = new Color(0.06f, 0.18f, 0.35f);
        var maxBtnImg = maxBtnGO.AddComponent<Image>();
        maxBtnImg.color = maxBtnFill;
        var maxBtnRt = maxBtnGO.GetComponent<RectTransform>();
        maxBtnRt.anchorMin = maxBtnRt.anchorMax = new Vector2(1f, 0.5f); maxBtnRt.pivot = new Vector2(1f, 0.5f);
        maxBtnRt.anchoredPosition = new Vector2(-32f, 0f); maxBtnRt.sizeDelta = new Vector2(26f, 26f);
        var maxBtnComp = maxBtnGO.AddComponent<Button>();
        var maxCb = maxBtnComp.colors;
        maxCb.normalColor = maxBtnFill;
        maxCb.highlightedColor = Color.Lerp(maxBtnFill, Color.white, 0.2f);
        maxCb.pressedColor = Color.Lerp(maxBtnFill, Color.black, 0.2f);
        maxCb.selectedColor = maxBtnFill; // sin esto, tras el clic se queda con el "seleccionado" resaltado
        maxBtnComp.colors = maxCb;
        maxBtnComp.navigation = new Navigation { mode = Navigation.Mode.None }; // sin foco de teclado/mando, sin recuadro de selección

        // Icono: un cuadrado hueco cuyo tamaño cambia según el estado (grande
        // cuando la ventana está pequeña = "puedes agrandarla", pequeño cuando
        // ya está maximizada = "puedes encogerla") — ver ToggleScenarioPreviewMaximize.
        var maxIconGO = new GameObject("Icon");
        maxIconGO.transform.SetParent(maxBtnGO.transform, false);
        scenarioPreviewMaxIconRt = maxIconGO.AddComponent<RectTransform>();
        scenarioPreviewMaxIconRt.anchorMin = scenarioPreviewMaxIconRt.anchorMax = new Vector2(0.5f, 0.5f);
        scenarioPreviewMaxIconRt.sizeDelta = MAXICON_SIZE_WINDOWED; // estado inicial: ventana pequeña -> cuadrado grande

        var maxIconHoleGO = new GameObject("Hole");
        maxIconHoleGO.transform.SetParent(maxIconGO.transform, false);
        var maxIconHoleImg = maxIconHoleGO.AddComponent<Image>();
        maxIconHoleImg.color = maxBtnFill; // mismo color que el fondo del botón: simula el hueco
        maxIconHoleImg.raycastTarget = false;
        var maxIconHoleRt = maxIconHoleGO.GetComponent<RectTransform>();
        maxIconHoleRt.anchorMin = Vector2.zero; maxIconHoleRt.anchorMax = Vector2.one;
        maxIconHoleRt.offsetMin = new Vector2(1.2f, 1.2f); maxIconHoleRt.offsetMax = new Vector2(-1.2f, -1.2f);

        var maxIconFrameImg = maxIconGO.AddComponent<Image>();
        maxIconFrameImg.color = Color.white;
        maxIconFrameImg.raycastTarget = false;
        // El propio "Icon" (con el relleno) se dibuja antes que su hijo "Hole"
        // (mismo color que el fondo del botón), así que el hueco queda encima
        // recortando el marco sin necesidad de reordenar nada más.

        maxBtnComp.onClick.AddListener(ToggleScenarioPreviewMaximize);

        // Cerrar (cruz) — icono en vez de la palabra, para que quepa junto al de maximizar.
        var closeBtn = MakeLBtn(titleBarGO.transform, "X", new Color(0.35f, 0.10f, 0.10f), Color.white, 13f);
        var closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 0.5f); closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-6f, 0f); closeRt.sizeDelta = new Vector2(26f, 26f);
        closeBtn.onClick.AddListener(CloseScenarioPreview);

        // Área 3D (RenderTexture) — sin márgenes a los lados ni abajo, solo
        // deja hueco arriba para la barra de título.
        scenarioPreviewRT = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        scenarioPreviewRT.antiAliasing = 4;
        scenarioPreviewRT.filterMode = FilterMode.Trilinear;
        scenarioPreviewRT.Create();

        var rawGO = new GameObject("Preview3D");
        rawGO.transform.SetParent(panelGO.transform, false);
        scenarioPreviewRawImg = rawGO.AddComponent<RawImage>();
        scenarioPreviewRawImg.texture = scenarioPreviewRT;
        var rawRt = rawGO.GetComponent<RectTransform>();
        rawRt.anchorMin = Vector2.zero; rawRt.anchorMax = Vector2.one;
        rawRt.offsetMin = Vector2.zero; rawRt.offsetMax = new Vector2(0f, -TOP_BAR_H);

        // Arrastrar con el botón izquierdo para orbitar, con el DERECHO para
        // desplazar el punto que mira la cámara (pasear por el mapa) — directo,
        // sin necesitar ningún modificador — y rueda para zoom.
        var et = rawGO.AddComponent<EventTrigger>();
        AddET(et, EventTriggerType.Drag, d =>
        {
            var ped = (PointerEventData)d;
            var delta = ped.delta;
            bool pan = ped.button == PointerEventData.InputButton.Right;
            if (pan)
            {
                // Escalado con la distancia actual: así el paneo "agarra" el
                // mismo punto bajo el cursor sea cual sea el nivel de zoom.
                float panScale = scenarioPreviewDist * 0.0018f;
                Vector3 right  = scenarioPreviewCam.transform.right;
                scenarioPreviewLookAt -= right    * (delta.x * panScale);
                scenarioPreviewLookAt -= Vector3.up * (delta.y * panScale);
            }
            else
            {
                scenarioPreviewYaw   += delta.x * 0.4f;
                scenarioPreviewPitch = Mathf.Clamp(scenarioPreviewPitch - delta.y * 0.4f, -85f, 85f);
            }
            UpdateScenarioPreviewCameraTransform();
        });
        AddET(et, EventTriggerType.Scroll, d =>
        {
            float wheel = ((PointerEventData)d).scrollDelta.y;
            scenarioPreviewDist = Mathf.Clamp(scenarioPreviewDist - wheel * 4f, 5f, 400f);
            UpdateScenarioPreviewCameraTransform();
        });

        SetupScenarioPreviewCamera();
        scenarioPreviewPanel.SetActive(false);
    }

    void SetupScenarioPreviewCamera()
    {
        // Nombre único por instancia — mismo motivo que en SetupPreviewCamera:
        // la descarga en OnDestroy() es asíncrona, así que un nombre fijo podría
        // chocar con una escena anterior todavía sin descargar del todo.
        scenarioPreviewScene = SceneManager.CreateScene("__ScenarioPreview__" + GetInstanceID(),
            new CreateSceneParameters(LocalPhysicsMode.None));
        scenarioPreviewSceneReady = true;

        var active = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scenarioPreviewScene);

        var camGO = new GameObject("ScenarioPreviewCamera");
        scenarioPreviewCam = camGO.AddComponent<Camera>();
        scenarioPreviewCam.targetTexture   = scenarioPreviewRT;
        scenarioPreviewCam.cullingMask     = 1 << SCENARIO_PREVIEW_LAYER; // solo ve objetos de este previsualizador (ver SetLayerRecursively en RebuildScenarioPreviewObjects)
        scenarioPreviewCam.clearFlags      = CameraClearFlags.SolidColor;
        scenarioPreviewCam.backgroundColor = new Color(0.55f, 0.7f, 0.85f, 1f);
        scenarioPreviewCam.fieldOfView     = 50f;
        scenarioPreviewCam.nearClipPlane   = 0.05f;
        scenarioPreviewCam.farClipPlane    = 2000f;
        scenarioPreviewCam.allowHDR        = false;
        scenarioPreviewCam.allowMSAA       = true;

        void MakeLight(Color col, float intensity, Vector3 euler)
        {
            var go = new GameObject("L");
            var l  = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = col; l.intensity = intensity;
            go.transform.rotation = Quaternion.Euler(euler);
        }
        MakeLight(new Color(1f, 0.97f, 0.9f), 1.3f, new Vector3(45f, -30f, 0f));
        MakeLight(new Color(0.6f, 0.75f, 1f), 0.5f, new Vector3(30f, 150f, 0f));

        RenderSettings.ambientMode  = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.45f);

        SceneManager.SetActiveScene(active);
    }

    void RebuildScenarioPreviewObjects()
    {
        if (!scenarioPreviewSceneReady) SetupScenarioPreviewCamera();

        var active = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scenarioPreviewScene);

        if (scenarioPreviewRoot != null) Destroy(scenarioPreviewRoot);
        scenarioPreviewRoot = new GameObject("ScenarioPreviewRoot");

        // Suelo de referencia, grande y neutro, centrado en el punto con zoom
        // (no en el origen: los objetos están a sus coordenadas GPS reales, que
        // pueden estar lejos del origen de mundo según dónde esté la misión).
        var groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
        groundGO.name = "Ground";
        groundGO.transform.SetParent(scenarioPreviewRoot.transform, true);
        groundGO.transform.position   = new Vector3(scenarioPreviewLookAt.x, -0.02f, scenarioPreviewLookAt.z);
        groundGO.transform.localScale = new Vector3(40f, 1f, 40f); // plano por defecto 10x10 -> 400x400
        // El material por defecto de CreatePrimitive no entra en la build (sale
        // rosa en el .exe): se parte de defaultMat (URP Lit, en Resources), que
        // sí se incluye siempre.
        var groundRenderer = groundGO.GetComponent<Renderer>();
        var groundTemplate = Resources.Load<Material>("Materials/defaultMat");
        if (groundTemplate != null)
            groundRenderer.material = new Material(groundTemplate);
        groundRenderer.material.color = new Color(0.25f, 0.32f, 0.24f);

        MapSceneController.PlaceAll(placed, scenarioPreviewRoot.transform);
        SetLayerRecursively(scenarioPreviewRoot, SCENARIO_PREVIEW_LAYER);

        SceneManager.SetActiveScene(active);
    }

    void UpdateScenarioPreviewCameraTransform()
    {
        if (!scenarioPreviewCam) return;
        float yawRad   = scenarioPreviewYaw   * Mathf.Deg2Rad;
        float pitchRad = scenarioPreviewPitch * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
            Mathf.Sin(pitchRad),
            -Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)) * scenarioPreviewDist;
        scenarioPreviewCam.transform.position = scenarioPreviewLookAt + offset;
        scenarioPreviewCam.transform.LookAt(scenarioPreviewLookAt);
    }

    void SpawnPreviewObject(string type)
    {
        if (previewRoot != null) Destroy(previewRoot);

        var active = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(previewScene);

        previewRoot = new GameObject("PreviewRoot");
        previewRoot.transform.position = Vector3.zero;
        if (type == "Aro")
            previewEuler = new Vector3(aroRotX, aroRotY, aroRotZ);
        else if (type == "Bandera")
            previewEuler = new Vector3(banderaRotX, banderaRotY, 0f);
        else if (type == "Obstáculo")
            previewEuler = new Vector3(obstaculoRotX, obstaculoRotY, 0f);
        else if (type == "Moneda")
            previewEuler = new Vector3(monedaRotX, monedaRotY, 0f);
        else if (type == "Arco")
            previewEuler = new Vector3(arcoRotX, arcoRotY, 0f);
        else if (type == "Segundo Arco")
            previewEuler = new Vector3(arco2RotX, arco2RotY, 0f);
        else if (type == "Flecha")
            previewEuler = new Vector3(flechaRotX, flechaRotY, 0f);
        previewRoot.transform.eulerAngles = previewEuler;

        CreatePreviewMesh(type, previewRoot.transform);
        SetLayerRecursively(previewRoot, OBJECT_PREVIEW_LAYER);

        SceneManager.SetActiveScene(active);

        FitCameraToObject();
    }

    static Texture2D CreateStreetTexture()
    {
 // Textura de baja res → filtro bilineal la desenfoca automáticamente
        const int W = 16, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;

        // Columnas de colores de calle desenfocada (de izquierda a derecha)
        Color[] cols = {
            new Color(0.25f, 0.22f, 0.20f), // fachada oscura
            new Color(0.45f, 0.42f, 0.38f), // muro iluminado
            new Color(0.55f, 0.52f, 0.46f), // ventana clara
            new Color(0.30f, 0.28f, 0.25f), // sombra edificio
            new Color(0.48f, 0.44f, 0.40f), // siguiente fachada
            new Color(0.60f, 0.58f, 0.52f), // zona brillante
        };

        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1); // 0 = suelo, 1 = cielo
            // Cielo: azul grisáceo degradado
            Color sky   = Color.Lerp(new Color(0.45f, 0.50f, 0.62f), new Color(0.62f, 0.68f, 0.80f), t);
            // Suelo: asfalto mojado oscuro
            Color floor = Color.Lerp(new Color(0.10f, 0.09f, 0.09f), new Color(0.22f, 0.20f, 0.20f), t);
            // Edificios: zona media
            float buildT = Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.4f);

            for (int x = 0; x < W; x++)
            {
                float fx   = x / (float)(W - 1);
                // Mezcla columnas de edificios
                float ci   = fx * (cols.Length - 1);
                int   ci0  = Mathf.FloorToInt(ci);
                int   ci1  = Mathf.Min(ci0 + 1, cols.Length - 1);
                Color bld  = Color.Lerp(cols[ci0], cols[ci1], ci - ci0);

                Color pixel;
                if (t < 0.30f)
                    pixel = Color.Lerp(floor, bld, t / 0.30f);
                else if (t < 0.65f)
                    pixel = Color.Lerp(bld, sky, (t - 0.30f) / 0.35f);
                else
                    pixel = sky;

                tex.SetPixel(x, y, pixel);
            }
        }
        tex.Apply();
        return tex;
    }

    void FitCameraToObject()
    {
        if (!previewRoot || !previewCam) return;
        var renderers = previewRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        float size   = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        Vector3 center = bounds.center;

        // For the aro: the ring is a vertical circle so its X and Y extents both
        // equal the outer ring radius; any pole/base only extends extents.y downward,
        // so Min(extents.x, extents.y) still gives the ring radius even with a base.
        // Max(extents.x, extents.z) fails when the base is wider than the ring.
        float dist;
        Vector3 lookAt;
        if (selectedType == "Aro")
        {
            float ringRadius = Mathf.Min(bounds.extents.x, bounds.extents.y);
            float ringCenterY = Mathf.Max(bounds.max.y - ringRadius, center.y);
            lookAt = new Vector3(center.x, ringCenterY, center.z);
            dist = size * 2.2f; // extra margin so the ring is never clipped
        }
        else if (selectedType == "Moneda")
        {
            lookAt = center;
            dist = size * 3.0f; // la moneda es un disco plano y pequeño: distancia ajustada para un encuadre equilibrado
        }
        else
        {
            lookAt = center;
            dist = size * 1.8f;
        }

        previewCam.transform.position = lookAt + new Vector3(0f, 0f, -dist);
        previewCam.transform.LookAt(lookAt);
        previewCam.nearClipPlane = dist * 0.01f;
        previewCam.farClipPlane  = Mathf.Max(dist * 10f, 200f);
    }

    // Reajusta solo el zoom (distancia de la cámara a lo largo de la línea de mira,
    // que siempre apunta al origen) para que el objeto completo quepa en el encuadre.
    // Requiere que el objeto esté recentrado en el origen (ver RecenterInPlace) para
    // que el resultado se vea siempre centrado, no solo "sin clipping".
    void FitZoomToObject()
    {
        if (!previewRoot || !previewCam) return;
        var renderers = previewRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float dist = size * 1.8f;

        Vector3 dir = previewCam.transform.position;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
        dir.Normalize();

        previewCam.transform.position = dir * dist;
        previewCam.transform.LookAt(Vector3.zero);
        previewCam.nearClipPlane = dist * 0.01f;
        previewCam.farClipPlane  = Mathf.Max(dist * 10f, 200f);
    }

    // Recentra un objeto ya instanciado (y con su escala actual ya aplicada) para que
    // su centro geométrico quede exactamente en el origen local del padre. Necesario
    // tras aplicar un escalado no uniforme (longitud horizontal/vertical), ya que un
    // pivote que no coincide con el centro geométrico se desplaza al escalar de forma
    // distinta en cada eje.
    // internal: la usan también ScenarioSceneCreator y MapSceneController para
    // reproducir exactamente la composición del previsualizador (ver comentario
    // de GetObjectVariantName).
    internal static void RecenterInPlace(GameObject inst)
    {
        var rends = inst.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        var parent = inst.transform.parent;
        Vector3 centerInParent = parent != null ? parent.InverseTransformPoint(b.center) : b.center;
        inst.transform.localPosition -= centerInParent;
    }

    // Sube/baja el CONTENEDOR (wrapper) de un objeto ya instanciado, escalado
    // y posicionado en la escena final, para que su BASE (el punto más bajo
    // de sus bounds ya renderizados, no su centro) quede exactamente en la
    // posición Y que se le pasó como "altura" al colocarlo — así con
    // altura=0 la base del objeto toca el suelo en vez de quedar medio
    // enterrada (la mitad de su tamaño por debajo, que es lo que pasaba
    // antes: la "altura" posicionaba el CENTRO del objeto, no su base).
    // Actúa sobre el padre, no sobre "inst": si ya se llamó a
    // RecenterInPlace (para que el objeto gire sobre su propio eje en vez de
    // en órbita) esto no pisa esa corrección, solo traslada el conjunto
    // entero en bloque. Solo se usa en la instanciación real (Scenario-
    // SceneCreator/MapSceneController) — el previsualizador del creador de
    // misión no representa "altura sobre el suelo", así que no lo necesita.
    internal static void GroundWrapperToBase(GameObject inst)
    {
        var rends = inst.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        var parent = inst.transform.parent;
        if (parent == null) return;
        parent.position += Vector3.up * (parent.position.y - b.min.y);
    }

    // El mesh del Obstáculo trae la barra (submesh 0) y los soportes (submesh 1)
    // combinados en un único Renderer/transform, así que no se puede escalar solo
    // uno de los dos con transform.localScale. Se separan en dos hijos con mallas
    // independientes (mismo vertex buffer, solo cambian los triángulos) para poder
    // escalar únicamente el soporte en Z sin deformar la barra: la barra ("Body")
    // no se escala nunca (conserva su forma redonda), solo se reposiciona para
    // seguir el crecimiento del soporte (ver barRefZ).
    // Devuelve el hijo "Support"; deja el mesh combinado original vacío (sin malla)
    // para que no siga renderizando ni contando en cálculos de bounds.
    // internal: la usa también ScenarioSceneCreator para recolorear el soporte
    // igual que en el previsualizador (ver comentario de GetObjectVariantName).
    internal static GameObject SplitObstaculoSupport(GameObject inst, out GameObject bodyGO, out float barRefZ)
    {
        bodyGO = null; barRefZ = 0f;
        var mf = inst.GetComponentInChildren<MeshFilter>();
        var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
        if (mf == null || mr == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount < 2) return null;

        var srcMesh = mf.sharedMesh;
        var verts   = srcMesh.vertices;
        var normals = srcMesh.normals;
        var uvs     = srcMesh.uv;
        var mats    = mr.sharedMaterials;

        Mesh MakeSubMesh(int subIdx, string suffix)
        {
            var m = new Mesh { name = srcMesh.name + suffix };
            m.vertices = verts;
            if (normals.Length == verts.Length) m.normals = normals;
            if (uvs.Length == verts.Length) m.uv = uvs;
            m.triangles = srcMesh.GetTriangles(subIdx);
            m.RecalculateBounds();
            return m;
        }

        var bodyMesh    = MakeSubMesh(0, "_Body");
        var supportMesh = MakeSubMesh(1, "_Support");
        barRefZ = bodyMesh.bounds.center.z;

        mf.sharedMesh = null; // el original ya no renderiza nada ni aporta bounds

        bodyGO = new GameObject("Body");
        bodyGO.transform.SetParent(mf.transform, false);
        bodyGO.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
        bodyGO.AddComponent<MeshRenderer>().sharedMaterial = mats.Length > 0 ? mats[0] : null;

        var supportGO = new GameObject("Support");
        supportGO.transform.SetParent(mf.transform, false);
        supportGO.AddComponent<MeshFilter>().sharedMesh = supportMesh;
        supportGO.AddComponent<MeshRenderer>().sharedMaterial = mats.Length > 1 ? mats[1] : (mats.Length > 0 ? mats[0] : null);

        return supportGO;
    }

    // Instancia un prefab recentrando su pivote en su propio centro geométrico
    // antes de engancharlo al padre rotable: algunos modelos (p.ej. Arco2)
    // traen el pivote muy alejado del centro visual, así que si se instancian
    // directamente giran en órbita alrededor de ese pivote en vez de girar
    // sobre su propio eje.
    static GameObject InstantiateCentered(GameObject prefab, Transform parent)
    {
        var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
        var rends = inst.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            // El pivote debe moverse la diferencia entre su posición actual y el centro
            // geométrico medido (no simplemente a "-centro", que solo es correcto si el
            // pivote ya estaba en el origen antes de medir).
            inst.transform.position = inst.transform.position - b.center;
        }
        inst.transform.SetParent(parent, false);
        inst.transform.localScale = Vector3.one;
        return inst;
    }

    void CreatePreviewMesh(string type, Transform parent)
    {
        switch (type)
        {
            case "Aro":
            {
                GameObject aroInst = null;

                // Objeto de aro elegido con las flechas del previsualizador (Assets/Resources/Prefabs/Aro_<Variante>.prefab)
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Aro_" + AroVariantNames[aroVariantIdx]);
                if (variantPrefab != null)
                {
                    aroInst = Instantiate(variantPrefab, parent);
                    aroInst.transform.localPosition = Vector3.zero;
                    aroInst.transform.localScale    = Vector3.one; // FitCameraToObject ajusta la cámara
                }
                else
                {
                    // Carga el FBX real del proyecto desde Resources
                    var aroAsset = Resources.Load<GameObject>("Prefabs/Aro");
                    if (aroAsset != null)
                    {
                        aroInst = Instantiate(aroAsset, parent);
                        aroInst.transform.localPosition = Vector3.zero;
                        aroInst.transform.localScale    = Vector3.one; // FitCameraToObject ajusta la cámara
                        // Aplica el color rojo del material original (Assets/Shaders/Aro.mat)
                        var mat = Resources.Load<Material>("Materials/Aro");
                        if (mat != null)
                            foreach (var r in aroInst.GetComponentsInChildren<Renderer>())
                                r.material = mat;
                        else
                            foreach (var r in aroInst.GetComponentsInChildren<Renderer>())
                                r.material.color = new Color(0.906f, 0.146f, 0.212f);
                    }
                    else
                    {
 // Torus real con mesh igual que en el visualizador de Unity
                        aroInst = new GameObject("AroTorus");
                        aroInst.transform.SetParent(parent, false);
                        var mf = aroInst.AddComponent<MeshFilter>();
                        var mr = aroInst.AddComponent<MeshRenderer>();
                        mf.mesh = CreateTorusMesh(1.5f, 0.10f, 256, 64);
                        var shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (!shader) shader = Shader.Find("Standard");
                        var mat = new Material(shader ? shader : Shader.Find("Diffuse"));
                        mat.color = new Color(0.906f, 0.146f, 0.212f);
                        mat.SetFloat("_Metallic",   0.5f);
                        mat.SetFloat("_Smoothness", 0.75f);
                        mat.SetFloat("_Glossiness", 0.75f);
                        mr.material = mat;
                    }
                }

                aroPreviewInstance = aroInst;
                ApplyAroScale(aroPreviewInstance, PREVIEW_REF_DIAMETER, aroGrosor);
                AroHuecoUtil.ApplyHueco(aroPreviewInstance, aroHueco);
                RecenterInPlace(aroPreviewInstance);
                FitZoomToObject();
                break;
            }
            case "Bandera":
            {
                // Objeto de bandera elegido con la insignia del previsualizador (Assets/Resources/Prefabs/Bandera_<Variante>.prefab)
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Bandera_" + BanderaVariantNames[banderaVariantIdx]);
                GameObject banderaInst = variantPrefab != null
                    ? (GameObject)UnityEngine.Object.Instantiate(variantPrefab, parent)
                    : null;
                if (banderaInst == null)
                {
                    var prefab = Resources.Load<GameObject>("Prefabs/bandera");
                    if (prefab != null) banderaInst = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);
                }
                if (banderaInst != null)
                {
                    banderaInst.transform.localPosition = Vector3.zero;
                    banderaInst.transform.localScale    = Vector3.one * (PREVIEW_REF_SIZE * BANDERA_BASE_SCALE);
                    // El pivote de este prefab no está en su centro geométrico (la tela
                    // se extiende hacia un lado desde el asta) — sin recentrar, escalar
                    // lo aleja del centro del previsualizador en vez de crecer en el sitio.
                    RecenterInPlace(banderaInst);
                    banderaPreviewInstance = banderaInst;
                }
                break;
            }
            case "Obstáculo":
            {
                // Objeto de obstáculo elegido con la fila de color del panel (Assets/Resources/Prefabs/Obstaculo_<Variante>.prefab)
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Obstaculo_" + ObstaculoVariantNames[obstaculoVariantIdx]);
                var obstaculoInst = variantPrefab != null
                    ? (GameObject)UnityEngine.Object.Instantiate(variantPrefab, parent)
                    : null;
                if (obstaculoInst == null)
                {
                    var prefab = Resources.Load<GameObject>("Prefabs/obstaculo");
                    if (prefab != null) obstaculoInst = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);
                }
                if (obstaculoInst != null)
                {
                    obstaculoInst.transform.localPosition = Vector3.zero;
                    // X = longitud horizontal (afecta a toda la pieza por igual). La altura de
                    // los soportes corre por el eje Z local del modelo (no Y, pese a estar en
                    // pose vertical con Rot X = 0). La longitud vertical NO debe deformar la
                    // barra, así que el soporte se separa en un hijo aparte (ver
                    // SplitObstaculoSupport): solo el soporte se escala en Z, y la barra se
                    // reposiciona (sin escalarse) para seguir subiendo con él.
                    obstaculoSupportChild = SplitObstaculoSupport(obstaculoInst, out obstaculoBodyChild, out obstaculoBarRefZ);
                    obstaculoInst.transform.localScale = new Vector3(obstaculoLongHoriz / 3f, 1f, 1f);
                    if (obstaculoSupportChild != null)
                        obstaculoSupportChild.transform.localScale = new Vector3(1f, 1f, obstaculoLongVert / 3f);
                    if (obstaculoBodyChild != null)
                        obstaculoBodyChild.transform.localPosition = new Vector3(0f, 0f, obstaculoBarRefZ * (obstaculoLongVert / 3f - 1f));
                    RecenterInPlace(obstaculoInst);
                    obstaculoPreviewInstance = obstaculoInst;

                    // El hijo "Support" es el soporte: se sustituye por el color elegido en
                    // la fila "Soporte" (reutiliza los mismos materiales Obstaculo_<Color>).
                    var supportMat = Resources.Load<Material>("Materials/Obstaculo_" + ObstaculoVariantNames[obstaculoSupportVariantIdx]);
                    if (supportMat != null && obstaculoSupportChild != null)
                    {
                        var sr = obstaculoSupportChild.GetComponent<Renderer>();
                        if (sr) sr.sharedMaterial = supportMat;
                    }
                }
                break;
            }
            case "Moneda":
            {
                // Objeto de moneda elegido con la insignia del previsualizador (Assets/Resources/Prefabs/Moneda_<Variante>.prefab)
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Moneda_" + MonedaVariantNames[monedaVariantIdx]);
                GameObject monedaInst = variantPrefab != null
                    ? (GameObject)UnityEngine.Object.Instantiate(variantPrefab, parent)
                    : null;
                if (monedaInst == null)
                {
                    var prefab = Resources.Load<GameObject>("Prefabs/moneda");
                    if (prefab != null) monedaInst = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);
                }
                if (monedaInst != null)
                {
                    monedaInst.transform.localPosition = Vector3.zero;
                    ApplyMonedaScale(monedaInst, PREVIEW_REF_SIZE);
                    RecenterInPlace(monedaInst);
                    monedaPreviewInstance = monedaInst;
                }
                break;
            }
            case "Arco":
            {
                // Objeto de arco elegido con la fila de color del panel (Assets/Resources/Prefabs/Arco_<Variante>.prefab)
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Arco_" + ArcoVariantNames[arcoVariantIdx]);
                var arcoInst = variantPrefab != null
                    ? (GameObject)UnityEngine.Object.Instantiate(variantPrefab, parent)
                    : null;
                if (arcoInst == null)
                {
                    var arcoPrefab = Resources.Load<GameObject>("Prefabs/Arco");
                    if (arcoPrefab != null) arcoInst = (GameObject)UnityEngine.Object.Instantiate(arcoPrefab, parent);
                }
                if (arcoInst != null)
                {
                    arcoInst.transform.localPosition = Vector3.zero;
                    // El modelo está modelado tumbado en el plano XZ (ver comentario de arcoRotX): el eje X
                    // es la anchura (longitud horizontal, no afectada por la rotación de vista frontal) y el
                    // eje Z es la profundidad, que pasa a ser la altura vertical al rotar 90° en X. El eje Y
                    // escala con la longitud HORIZONTAL (no con la vertical) para que el grosor del tubo se
                    // mantenga proporcionado y redondo en las patas del arco (plano X-Y) sea cual sea la
                    // altura; si Y siguiera a la vertical, el tubo se afinaría mucho con valores bajos y
                    // engordaría de más con valores altos.
                    arcoInst.transform.localScale = new Vector3(arcoLongHoriz / 3f, arcoLongHoriz / 3f, arcoLongVert / 3f);
                    RecenterInPlace(arcoInst);
                    arcoPreviewInstance = arcoInst;
                }
                break;
            }
            case "Segundo Arco":
            {
                // Objeto de arco 2 elegido con la fila de color del panel (Assets/Resources/Prefabs/Arco2_<Variante>.prefab)
                // El pivote del modelo está muy lejos de su centro visual, así que se recentra al instanciar
                // (ver InstantiateCentered) para que gire sobre su propio eje en vez de en órbita.
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Arco2_" + Arco2VariantNames[arco2VariantIdx]);
                var arco2Inst = variantPrefab != null
                    ? InstantiateCentered(variantPrefab, parent)
                    : null;
                if (arco2Inst == null)
                {
                    var arco2Prefab = Resources.Load<GameObject>("Prefabs/Arco2");
                    if (arco2Prefab != null) arco2Inst = InstantiateCentered(arco2Prefab, parent);
                }
                if (arco2Inst != null)
                {
                    // Mismo criterio de ejes que Arco (ver comentario más arriba): X = longitud
                    // horizontal, Z = longitud vertical tras la rotación de vista frontal, e Y escala con
                    // la horizontal (no la vertical) para que el grosor del tubo se mantenga proporcionado.
                    arco2Inst.transform.localScale = new Vector3(arco2LongHoriz / 3f, arco2LongHoriz / 3f, arco2LongVert / 3f);
                    RecenterInPlace(arco2Inst);
                    arco2PreviewInstance = arco2Inst;
                }
                break;
            }
            case "Flecha":
            {
                GameObject flechaInst = null;

                // Objeto de flecha elegido con la fila de color del panel (Assets/Resources/Prefabs/Flecha_<Variante>.prefab)
                var variantPrefab = Resources.Load<GameObject>("Prefabs/Flecha_" + FlechaVariantNames[flechaVariantIdx]);
                // Los prefabs de color (Flecha_<Variante>) traen horneada una rotación
                // propia de -90° en X. Sin resetearla, se compone con rotY en vez de con
                // rotX (la rotación del prefab queda "dentro" de la de Y en la cadena de
                // cuaterniones), así que para rotY != 0 la punta deja de apuntar al
                // ángulo indicado. Al ponerla a identidad, la rotación final es
                // exactamente Euler(rotX, rotY, 0): rotY gira la punta como una brújula,
                // igual que ya hace LabyrinthGeofenceManager.SpawnExitArrows con este
                // mismo prefab.
                if (variantPrefab != null)
                {
                    flechaInst = (GameObject)UnityEngine.Object.Instantiate(variantPrefab, parent);
                    flechaInst.transform.localPosition = Vector3.zero;
                    flechaInst.transform.localRotation = Quaternion.identity;
                    flechaInst.transform.localScale    = Vector3.one;
                }
                else
                {
                    var prefab = Resources.Load<GameObject>("Prefabs/flecha4");
                    if (prefab != null)
                    {
                        flechaInst = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);
                        flechaInst.transform.localPosition = Vector3.zero;
                        flechaInst.transform.localRotation = Quaternion.identity;
                        flechaInst.transform.localScale    = Vector3.one;
                    }
                }

                flechaPreviewInstance = flechaInst;
                ApplyFlechaScale(flechaPreviewInstance, flechaGrosor, flechaLongHoriz, PREVIEW_REF_SIZE);
                RecenterInPlace(flechaPreviewInstance);
                FitZoomToObject();
                break;
            }
        }
    }

    // Diámetro real (unidades Unity = metros, escala 1) del propio objeto instanciado:
    // mide los bounds LOCALES de la malla (Mesh.bounds, independientes de rotación/
    // escala/jerarquía del GameObject en cada momento, así que da igual si "instance"
    // ya tiene aplicada otra escala anterior) y toma el eje más grande, que para un
    // aro (disco fino) es siempre el diámetro exterior, nunca el grosor del tubo.
    // Mismo criterio en MapSceneController.MeshNativeDiameter.
    static float MeasureMeshDiameter(GameObject instance)
    {
        float maxDim = 0f;
        foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var s = mf.sharedMesh.bounds.size;
            maxDim = Mathf.Max(maxDim, s.x, s.y, s.z);
        }
        return maxDim > 0.0001f ? maxDim : 3f;
    }

    // El diámetro escala X/Y (el ancho del aro en su plano) para que el resultado
    // final mida EXACTAMENTE "diameter" metros/unidades Unity, sea cual sea el tamaño
    // real al que esté modelado el asset (ver MeasureMeshDiameter); el "grosor" escala
    // además el eje Z local (el eje fino: la profundidad del aro fuera de su plano)
    // como factor multiplicativo sobre esa misma base, sin afectar X/Y.
    static void ApplyAroScale(GameObject go, float diameter, float grosor)
    {
        if (go == null) return;
        float scale = diameter / MeasureMeshDiameter(go);
        go.transform.localScale = new Vector3(scale, scale, scale * Mathf.Max(0.05f, grosor));
    }

    // Mismo criterio que ApplyAroScale: "diameter" es un valor real en metros;
    // se mide el diámetro nativo de la malla (a escala 1) y se escala
    // uniformemente para que el resultado final mida EXACTAMENTE "diameter"
    // metros/unidades Unity, sea cual sea el tamaño real al que esté
    // modelada la moneda. Antes se usaba "diameter" directamente como
    // factor de escala (Vector3.one * diameter), que solo coincidía con la
    // vida real si el modelo ya medía 1 unidad de diámetro.
    static void ApplyMonedaScale(GameObject go, float diameter)
    {
        if (go == null) return;
        float d = diameter > 0f ? diameter : 1f;
        float scale = d / MeasureMeshDiameter(go);
        go.transform.localScale = Vector3.one * scale;
    }

    // Igual criterio que MapSceneController.PlaceObject caso "Flecha": "size" escala
    // los tres ejes por igual (tamaño general) y luego X se escala además por
    // longHoriz/3 (punta a cola) y Z por grosor (espesor), con el mismo criterio de
    // normalización que arcoLongHoriz/arco2LongHoriz (3 = tamaño original del modelo).
    static void ApplyFlechaScale(GameObject go, float grosor, float longHoriz, float size)
    {
        if (go == null) return;
        float s = size > 0f ? size : 1f;
        go.transform.localScale = new Vector3(s * (longHoriz / 3f), s, s * Mathf.Max(0.05f, grosor));
    }

    static Mesh CreateTorusMesh(float R, float r, int majorSegs, int minorSegs)
    {
        var verts   = new Vector3[(majorSegs + 1) * (minorSegs + 1)];
        var normals = new Vector3[verts.Length];
        var uvs     = new Vector2[verts.Length];
        var tris    = new int[majorSegs * minorSegs * 6];

        for (int i = 0; i <= majorSegs; i++)
        {
            float u  = i / (float)majorSegs * Mathf.PI * 2f;
            float cu = Mathf.Cos(u), su = Mathf.Sin(u);
            for (int j = 0; j <= minorSegs; j++)
            {
                float v  = j / (float)minorSegs * Mathf.PI * 2f;
                float cv = Mathf.Cos(v), sv = Mathf.Sin(v);
                int   idx = i * (minorSegs + 1) + j;
                verts[idx]   = new Vector3((R + r * cv) * cu, r * sv, (R + r * cv) * su);
                normals[idx] = new Vector3(cv * cu, sv, cv * su);
                uvs[idx]     = new Vector2(i / (float)majorSegs, j / (float)minorSegs);
            }
        }

        int t = 0;
        for (int i = 0; i < majorSegs; i++)
        for (int j = 0; j < minorSegs; j++)
        {
            int a = i * (minorSegs + 1) + j;
            int b = a + minorSegs + 1;
            tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
            tris[t++] = b; tris[t++] = b + 1; tris[t++] = a + 1;
        }

        var mesh = new Mesh { name = "Torus" };
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    static void SetPreviewColor(GameObject go, Color col)
    {
        var r = go.GetComponent<Renderer>();
        if (r) r.material.color = col;
    }

    // ── Selector del objeto de aro (añadido; no toca nada de lo anterior) ──

    void SelectAroVariant(int idx)
    {
        if (idx < 0 || idx >= AroVariantNames.Length) return;
        aroVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Aro")
        {
            placed[selectedPlacedIdx].aroVariant = AroVariantNames[aroVariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateAroVariantLabel();
        if (selectedType == "Aro") SpawnPreviewObject("Aro");
    }

    void UpdateAroVariantLabel()
    {
        if (aroVariantSwatch != null) aroVariantSwatch.color = VariantColor(AroVariantNames[aroVariantIdx]);
    }

    void SelectBanderaVariant(int idx)
    {
        if (idx < 0 || idx >= BanderaVariantNames.Length) return;
        banderaVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Bandera")
        {
            placed[selectedPlacedIdx].banderaVariant = BanderaVariantNames[banderaVariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateBanderaVariantLabel();
        if (selectedType == "Bandera") SpawnPreviewObject("Bandera");
    }

    void UpdateBanderaVariantLabel()
    {
        if (banderaVariantSwatch != null) banderaVariantSwatch.color = VariantColor(BanderaVariantNames[banderaVariantIdx]);
    }

    void SelectObstaculoVariant(int idx)
    {
        if (idx < 0 || idx >= ObstaculoVariantNames.Length) return;
        obstaculoVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Obstáculo")
        {
            placed[selectedPlacedIdx].obstaculoVariant = ObstaculoVariantNames[obstaculoVariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateObstaculoVariantLabel();
        if (selectedType == "Obstáculo") SpawnPreviewObject("Obstáculo");
    }

    void UpdateObstaculoVariantLabel()
    {
        if (obstaculoVariantSwatch != null) obstaculoVariantSwatch.color = VariantColor(ObstaculoVariantNames[obstaculoVariantIdx]);
    }

    void SelectObstaculoSupportVariant(int idx)
    {
        if (idx < 0 || idx >= ObstaculoVariantNames.Length) return;
        obstaculoSupportVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Obstáculo")
            placed[selectedPlacedIdx].obstaculoSupportVariant = ObstaculoVariantNames[obstaculoSupportVariantIdx];

        UpdateObstaculoSupportVariantLabel();
        if (selectedType == "Obstáculo") SpawnPreviewObject("Obstáculo");
    }

    void UpdateObstaculoSupportVariantLabel()
    {
        if (obstaculoSupportVariantSwatch != null) obstaculoSupportVariantSwatch.color = VariantColor(ObstaculoVariantNames[obstaculoSupportVariantIdx]);
    }

    void SelectMonedaVariant(int idx)
    {
        if (idx < 0 || idx >= MonedaVariantNames.Length) return;
        monedaVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Moneda")
        {
            placed[selectedPlacedIdx].monedaVariant = MonedaVariantNames[monedaVariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateMonedaVariantLabel();
        if (selectedType == "Moneda") SpawnPreviewObject("Moneda");
    }

    void UpdateMonedaVariantLabel()
    {
        if (monedaVariantSwatch != null) monedaVariantSwatch.color = VariantColor(MonedaVariantNames[monedaVariantIdx]);
    }

    void SelectArcoVariant(int idx)
    {
        if (idx < 0 || idx >= ArcoVariantNames.Length) return;
        arcoVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Arco")
        {
            placed[selectedPlacedIdx].arcoVariant = ArcoVariantNames[arcoVariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateArcoVariantLabel();
        if (selectedType == "Arco") SpawnPreviewObject("Arco");
    }

    void UpdateArcoVariantLabel()
    {
        if (arcoVariantSwatch != null) arcoVariantSwatch.color = VariantColor(ArcoVariantNames[arcoVariantIdx]);
    }

    void SelectArco2Variant(int idx)
    {
        if (idx < 0 || idx >= Arco2VariantNames.Length) return;
        arco2VariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Segundo Arco")
        {
            placed[selectedPlacedIdx].arco2Variant = Arco2VariantNames[arco2VariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateArco2VariantLabel();
        if (selectedType == "Segundo Arco") SpawnPreviewObject("Segundo Arco");
    }

    void UpdateArco2VariantLabel()
    {
        if (arco2VariantSwatch != null) arco2VariantSwatch.color = VariantColor(Arco2VariantNames[arco2VariantIdx]);
    }

    void SelectFlechaVariant(int idx)
    {
        if (idx < 0 || idx >= FlechaVariantNames.Length) return;
        flechaVariantIdx = idx;

        if (selectedPlacedIdx >= 0 && selectedPlacedIdx < placed.Count && placed[selectedPlacedIdx].type == "Flecha")
        {
            placed[selectedPlacedIdx].flechaVariant = FlechaVariantNames[flechaVariantIdx];
            RefreshMarkerColor(selectedPlacedIdx);
        }

        UpdateFlechaVariantLabel();
        if (selectedType == "Flecha") SpawnPreviewObject("Flecha");
    }

    void UpdateFlechaVariantLabel()
    {
        if (flechaVariantSwatch != null) flechaVariantSwatch.color = VariantColor(FlechaVariantNames[flechaVariantIdx]);
    }

    void BuildActionRow(Transform parent)
    {
        var rowGO = NewHRow(parent, 38f);
        MakeLBtn(rowGO.transform, "Cancelar", C_BORDER, C_TEXT, 11f)
            .onClick.AddListener(DoCancel);
        MakeLBtn(rowGO.transform, isEditing ? "Guardar Cambios" : "Crear Misión",
            C_ACCENT, new Color(0.03f, 0.05f, 0.09f, 1f), 11f)
            .onClick.AddListener(DoCreate);
    }

    // =========================================================================
    //  Unity lifecycle
    // =========================================================================

    void Start()
    {
        StartCoroutine(InitScaleAfterLayout());
    }

    IEnumerator InitScaleAfterLayout()
    {
        yield return null; // espera un frame para que Unity calcule el layout

        // Calcula splitRatio dinámicamente para que el mapa encaje sin bandas:
 // scaleW == scaleH → areaW/rawW == areaH/rawH
        // areaW == areaH * GRID_W/GRID_H,  areaH == totalH
        var rootRt = transform as RectTransform;
        float totalW = rootRt.rect.width;
        float totalH = rootRt.rect.height;
        UpdateMapScale();
    }

    void Update()
    {
        if (mapAreaRt == null || dividerRt == null) return;

        // Las líneas de alineación de cualquier fila/columna ya colocada se
        // quedan dibujadas siempre (no solo mientras se arrastra/mueve algo),
        // recalculadas cada frame por si las posiciones cambian.
        ShowAllGroupLines();

        bool overMap     = RectTransformUtility.RectangleContainsScreenPoint(mapAreaRt, Input.mousePosition)
                         // El botón "Previsualización" es un overlay DENTRO de mapAreaRt (su rect lo
                         // incluye), así que sin esto un clic ahí también contaba como clic en el mapa
                         // y colocaba un objeto debajo del propio botón.
                         && !(scenPrevBtnRt != null && RectTransformUtility.RectangleContainsScreenPoint(scenPrevBtnRt, Input.mousePosition))
                         && !(geofenceBtnRt != null && RectTransformUtility.RectangleContainsScreenPoint(geofenceBtnRt, Input.mousePosition))
                         // Con el panel de previsualización del escenario abierto, este bloqueador
                         // tapa el mapa entero visualmente pero NO pasa por el sistema de eventos de
                         // UI (la colocación/paneo del mapa se hace con Input.* a pelo en Update()),
                         // así que sin esto seguía colocando objetos / paneando el mapa por debajo.
                         && !(scenarioPreviewPanel != null && scenarioPreviewPanel.activeSelf);
        // Zona de clic del divisor expandida ±16px para evitar fallos de detección
        bool overDivider = false;
        if (dividerRt != null)
        {
            var divRect = dividerRt.rect;
            var divCenter = (Vector2)dividerRt.TransformPoint(divRect.center);
            float divH = dividerRt.rect.height * dividerRt.lossyScale.y;
            float mouseX = Input.mousePosition.x;
            float mouseY = Input.mousePosition.y;
            float divScreenX = divCenter.x;
            overDivider = Mathf.Abs(mouseX - divScreenX) <= 16f && Mathf.Abs(mouseY - divCenter.y) <= divH * 0.5f;
        }
        overDivider = overDivider
                   || (dragTabRt != null && RectTransformUtility.RectangleContainsScreenPoint(dragTabRt, Input.mousePosition));

        // Coordenadas en tiempo real bajo el cursor
        if (overMap && coordsLabel)
        {
            if (ScreenToLatLon(Input.mousePosition, out double hLat, out double hLon))
                coordsLabel.text = $"Lat: {hLat:F5}°"; if (_lonLabel) _lonLabel.text = $"Lon: {hLon:F5}°";
        }

 // Divisor: SOLO botón izquierdo (0)
        if (Input.GetMouseButtonDown(0) && overDivider)
        {
            isDraggingDivider = true;
            dividerDragStartX = Input.mousePosition.x;
            dividerSplitStart = splitRatio;
        }
        if (Input.GetMouseButtonUp(0))
            isDraggingDivider = false;
        if (isDraggingDivider && Input.GetMouseButton(0))
            OnDividerDrag(Input.mousePosition);

        // Si hay menú contextual abierto, comprobar si el cursor está sobre él
        bool overCtxMenu = ctxMenu != null &&
            RectTransformUtility.RectangleContainsScreenPoint(
                ctxMenu.GetComponent<RectTransform>(), Input.mousePosition);

 // Arrastre de la línea de grupo (rota en bloque el conjunto alineado).
 // Se calcula antes que el resto de gestos: si el arrastre termina este
 // frame, el mouse-up genérico de más abajo no debe volver a "asentar" el
 // grupo (que tras rotar ya no está alineado a los ejes X/Y y desaparecería).
        bool groupDragJustEnded = false;
        if (draggingGroupLine)
        {
            if (Input.GetMouseButton(0))
                UpdateGroupLineDrag(Input.mousePosition);
            if (Input.GetMouseButtonUp(0))
            {
                draggingGroupLine = false;
                groupDragJustEnded = true;
                // UpdateGroupLineRotate/Move actualizan posiciones y groupDir
                // cada frame sin tocar settledGroups (para no recalcular de más
                // en cada frame de arrastre) — al soltar, se sincroniza una vez
                // con la dirección final para que la línea persistente no la
                // compare contra una dirección vieja y la disuelva.
                SyncSettledGroup();
            }
        }

 // Modo mover (botón "Mover"): sigue al ratón, click para parar
        if (isMovingMarker && selectedPlacedIdx >= 0 && overMap && !overDivider && !overCtxMenu)
            MoveSelectedMarker(Input.mousePosition);

        if (isMovingMarker && Input.GetMouseButtonDown(0) && overMap && !overDivider && !overCtxMenu)
        {
            isMovingMarker = false;
            SettleGroupLine(selectedPlacedIdx);
            if (movMarkerBtn)
            {
                var cb = movMarkerBtn.colors;
                cb.normalColor = new Color(0.06f, 0.18f, 0.35f);
                movMarkerBtn.colors = cb;
            }
        }

 // Click izquierdo sobre el mapa
        if (Input.GetMouseButtonDown(0) && overMap && !overDivider && !isMovingMarker && !overCtxMenu && !draggingGroupLine)
        {
            int hit = HitTestMarkers(Input.mousePosition);
            if (hit >= 0)
            {
                SelectPlaced(hit);
                dragMarkerIdx = hit;
                dragStartPos  = Input.mousePosition;
                dragActive    = false;
            }
            else if (HitTestAnyGroupLine(Input.mousePosition))
            {
                // HitTestAnyGroupLine ya deja como grupo activo la línea bajo el
                // cursor (aunque fuera solo una de referencia), así que arrastrar
                // aquí mueve/rota esa fila/columna en vez de colocar un objeto
                // nuevo encima.
                BeginGroupLineDrag(Input.mousePosition);
            }
            else if (selectedPlacedIdx >= 0)
            {
                DeselectPlaced();
            }
            else if (ScreenToLatLon(Input.mousePosition, out double lat, out double lon))
            {
                // Posición "marcador" del clic (mismo espacio que anchoredPosition
                // de los marcadores) — se calcula antes de crear el objeto para
                // poder engancharla a los objetos cercanos ya colocados (snap de
                // fila/columna/espaciado) antes de fijar su lat/lon definitivas.
                float rawW = GRID_W * TILE_PIX, rawH = GRID_H * TILE_PIX;
                float areaW = mapAreaRt.rect.width, areaH = mapAreaRt.rect.height;
                float scale  = Mathf.Max(areaW / (GRID_W * TILE_PIX), areaH / (GRID_H * TILE_PIX));
                float ox = (areaW - rawW * scale) / 2f, oy = (areaH - rawH * scale) / 2f;
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(mapAreaRt, Input.mousePosition, null, out local);
                float px = (local.x + areaW / 2f - ox) / scale;
                float py = (areaH / 2f - local.y - oy) / scale;
                Vector2 markerPos = new Vector2(px - rawW / 2f, -(py - rawH / 2f));

                var snap = TrySnap(markerPos, GetActiveTypeRotY(), excludeIdx: -1);
                markerPos = snap.pos;
                if (!snap.showColGuide && !snap.showRowGuide)
                    TrySnapToExistingGroupLine(ref markerPos, excludeIdx: -1);
                (lat, lon) = MarkerPosToLatLon(markerPos);
                if (snap.hasRotSnap) RefreshTypeRotY(selectedType, snap.rotY);

                if (coordsLabel) { coordsLabel.text = $"Lat: {lat:F5}°"; if (_lonLabel) _lonLabel.text = $"Lon: {lon:F5}°"; }
                var obj = new MapPlacedObject {
                    lat = (float)lat, lon = (float)lon, type = selectedType,
                    height   = selectedType == "Aro"      ? aroHeight      : selectedType == "Bandera" ? banderaHeight : selectedType == "Obstáculo" ? obstaculoHeight : selectedType == "Moneda" ? monedaHeight : selectedType == "Arco" ? arcoHeight : selectedType == "Segundo Arco" ? arco2Height : selectedType == "Flecha" ? flechaHeight : 0f,
                    diameter = selectedType == "Aro" ? aroDiameter : selectedType == "Bandera" ? banderaSize : selectedType == "Obstáculo" ? obstaculoSize : selectedType == "Moneda" ? monedaSize : selectedType == "Arco" ? arcoSize : selectedType == "Segundo Arco" ? arco2Size : selectedType == "Flecha" ? flechaSize : 0f,
                    rotX     = selectedType == "Aro"      ? aroRotX        : selectedType == "Bandera" ? banderaRotX : selectedType == "Obstáculo" ? obstaculoRotX : selectedType == "Moneda" ? monedaRotX : selectedType == "Arco" ? arcoRotX : selectedType == "Segundo Arco" ? arco2RotX : selectedType == "Flecha" ? flechaRotX : 0f,
                    rotY     = selectedType == "Aro"      ? aroRotY        : selectedType == "Bandera" ? banderaRotY   : selectedType == "Obstáculo" ? obstaculoRotY   : selectedType == "Moneda" ? monedaRotY : selectedType == "Arco" ? arcoRotY : selectedType == "Segundo Arco" ? arco2RotY : selectedType == "Flecha" ? flechaRotY : 0f,
                    rotZ     = selectedType == "Aro"      ? aroRotZ        : 0f,
                    aroVariant = selectedType == "Aro" ? AroVariantNames[aroVariantIdx] : "Rojo",
                    banderaVariant = selectedType == "Bandera" ? BanderaVariantNames[banderaVariantIdx] : "Rojo",
                    obstaculoVariant = selectedType == "Obstáculo" ? ObstaculoVariantNames[obstaculoVariantIdx] : "Rojo",
                    obstaculoSupportVariant = selectedType == "Obstáculo" ? ObstaculoVariantNames[obstaculoSupportVariantIdx] : "Negro",
                    monedaVariant = selectedType == "Moneda" ? MonedaVariantNames[monedaVariantIdx] : "Rojo",
                    arcoVariant = selectedType == "Arco" ? ArcoVariantNames[arcoVariantIdx] : "Rojo",
                    arco2Variant = selectedType == "Segundo Arco" ? Arco2VariantNames[arco2VariantIdx] : "Rojo",
                    flechaVariant = selectedType == "Flecha" ? FlechaVariantNames[flechaVariantIdx] : "Rojo",
                    arcoLongHoriz = selectedType == "Arco" ? arcoLongHoriz : 0f,
                    arcoLongVert  = selectedType == "Arco" ? arcoLongVert  : 0f,
                    arco2LongHoriz = selectedType == "Segundo Arco" ? arco2LongHoriz : 0f,
                    arco2LongVert  = selectedType == "Segundo Arco" ? arco2LongVert  : 0f,
                    obstaculoLongHoriz = selectedType == "Obstáculo" ? obstaculoLongHoriz : 0f,
                    obstaculoLongVert  = selectedType == "Obstáculo" ? obstaculoLongVert  : 0f,
                    aroGrosor    = selectedType == "Aro"    ? aroGrosor    : 1f,
                    flechaGrosor = selectedType == "Flecha" ? flechaGrosor : 1f,
                    flechaLongHoriz = selectedType == "Flecha" ? flechaLongHoriz : 0f,
                    aroHueco     = selectedType == "Aro"    ? aroHueco     : 0f,
                };
                placed.Add(obj);
                SpawnMarker(markerPos.x, markerPos.y, obj);
                // Selecciona el objeto recién colocado: si no, los sliders de
                // grosor/hueco/forma que se toquen justo después no tienen ningún
                // "selectedPlacedIdx" al que escribir y el ajuste se pierde en
                // silencio (parece que solo afecta al siguiente objeto que coloques).
                SelectPlaced(placed.Count - 1);
                SettleGroupLine(placed.Count - 1);
            }
        }

 // Drag manteniendo click sobre un marcador
        if (dragMarkerIdx >= 0 && Input.GetMouseButton(0))
        {
            if (!dragActive && Vector2.Distance(Input.mousePosition, dragStartPos) > DRAG_THRESHOLD)
                dragActive = true;
            if (dragActive && overMap && !overDivider)
                MoveSelectedMarker(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            dragMarkerIdx = -1;
            dragActive    = false;
            if (!groupDragJustEnded) SettleGroupLine(selectedPlacedIdx);
        }

 // Clic derecho sobre un objeto: abre el menú Mover/Borrar. Sobre CUALQUIER
        // línea (la activa/rotable o una de referencia ya asentada): la
        // convierte en el grupo activo (así "Copiar" siempre actúa sobre la
        // línea bajo el cursor, no sobre una desactualizada) y abre
        // "Copiar"/"Eliminar". Sobre una zona vacía del mapa, un solo clic
        // derecho panea como siempre; solo el SEGUNDO clic derecho seguido
        // (doble clic derecho, mismo sitio, poco tiempo entre ambos), y si ya
        // se copió una línea antes, abre "Pegar".
        bool rightDownOnMap = Input.GetMouseButtonDown(1) && overMap && !overDivider;
        int  rightHit = rightDownOnMap ? HitTestMarkers(Input.mousePosition) : -1;
        bool rightOnLine = rightDownOnMap && rightHit < 0 && HitTestAnyGroupLine(Input.mousePosition);
        bool rightOnEmptyMap = rightDownOnMap && rightHit < 0 && !rightOnLine;

        bool isRightDoubleClick = rightOnEmptyMap &&
                                   (Time.time - lastRightClickTime) <= RIGHT_DBLCLICK_TIME &&
                                   Vector2.Distance(Input.mousePosition, lastRightClickScreenPos) <= RIGHT_DBLCLICK_DIST_PX;
        if (rightOnEmptyMap)
        {
            if (isRightDoubleClick) lastRightClickTime = -10f; // consumido: hace falta otro par de clics
            else                    { lastRightClickTime = Time.time; lastRightClickScreenPos = Input.mousePosition; }
        }

        bool rightPasteAvailable = isRightDoubleClick &&
                                    lineClipboard != null && lineClipboard.Count > 0;

        if (rightDownOnMap && rightHit >= 0)
        {
            SelectPlaced(rightHit);
            BuildContextMenu(rightHit);
        }
        else if (rightOnLine)
        {
            BuildLineContextMenu(Input.mousePosition);
        }
        else if (rightPasteAvailable)
        {
            BuildPasteContextMenu(Input.mousePosition);
        }

 // Paneo: SOLO botón derecho (1, fuera de un objeto/línea/menú de pegar) o central (2)
        bool panStart = (rightDownOnMap && rightHit < 0 && !rightOnLine && !rightPasteAvailable) ||
                        (Input.GetMouseButtonDown(2) && overMap);
        bool panHeld  =  Input.GetMouseButton(1)     || Input.GetMouseButton(2);
        bool panEnd   =  Input.GetMouseButtonUp(1)   || Input.GetMouseButtonUp(2);

        if (panStart)
        {
            isPanning     = true;
            panMouseStart = Input.mousePosition;
            panLonStart   = centerLon;
            panLatStart   = centerLat;
            panLastReload = -1f;
        }

        if (isPanning && panHeld)
        {
            if (panLastReload < 0f || Time.unscaledTime - panLastReload >= 0.10f)
            {
                ApplyPanDelta((Vector2)Input.mousePosition - panMouseStart);
                panLastReload = Time.unscaledTime;
            }
        }

        if (panEnd && isPanning)
        {
            isPanning = false;
            ApplyPanDelta((Vector2)Input.mousePosition - panMouseStart);
        }
    }

    void ApplyPanDelta(Vector2 screenDelta)
    {
        float areaW = mapAreaRt.rect.width;
        float areaH = mapAreaRt.rect.height;
        if (areaW <= 0 || areaH <= 0) return;
        float scale = Mathf.Max(areaW / (GRID_W * TILE_PIX), areaH / (GRID_H * TILE_PIX));
        if (scale <= 0f) return;
        double dx = -(double)screenDelta.x / scale / TILE_PIX;
        double dy =  (double)screenDelta.y / scale / TILE_PIX;
        centerLon = TileXToLon(LonToTileX(panLonStart, zoom) + dx, zoom);
        centerLat = Math.Max(-85.0, Math.Min(85.0,
            TileYToLat(LatToTileY(panLatStart, zoom) + dy, zoom)));
        RefreshTiles();
        RedrawMarkers();
    }

    // =========================================================================
    //  Interacciones
    // =========================================================================

    void OnDividerDrag(Vector2 screenPos)
    {
        // Calcula el delta respecto al punto de inicio para evitar saltos
        var rootRt = transform as RectTransform;
        float canvasW = rootRt.rect.width;
        if (canvasW <= 0f) return;

        float screenW  = Screen.width;
        float deltaX   = screenPos.x - dividerDragStartX;
        float deltaNorm = deltaX / screenW * (canvasW / canvasW); // en espacio normalizado
        // Conversión correcta: delta en canvas units / canvas width
        Vector2 localStart, localCurrent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, new Vector2(dividerDragStartX, screenPos.y), ownCanvasComp.worldCamera, out localStart);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screenPos, ownCanvasComp.worldCamera, out localCurrent);
        float deltaCanvas = localCurrent.x - localStart.x;
        splitRatio = Mathf.Clamp(dividerSplitStart + deltaCanvas / canvasW, 0.15f, 0.82f);
        ApplySplit();
    }

    void ApplySplit()
    {
        if (leftPanelRt)  leftPanelRt.anchorMax  = new Vector2(splitRatio, 1f);
        if (rightPanelRt) { rightPanelRt.anchorMin = new Vector2(splitRatio, 0f); rightPanelRt.offsetMin = Vector2.zero; }
        if (dividerRt)    { dividerRt.anchorMin = new Vector2(splitRatio, 0f); dividerRt.anchorMax = new Vector2(splitRatio, 1f); }
        if (dragTabRt)    { dragTabRt.anchorMin = new Vector2(splitRatio, 0.5f); dragTabRt.anchorMax = new Vector2(splitRatio, 0.5f); }
        // Fuerza actualización inmediata del canvas para que RectangleContainsScreenPoint
        // use posiciones correctas en el mismo frame
        Canvas.ForceUpdateCanvases();
        StartCoroutine(DelayedScaleUpdate());
    }

    IEnumerator DelayedScaleUpdate()
    {
        yield return null;
        UpdateMapScale();
    }

    void UpdateMapScale()
    {
        if (!mapAreaRt || !mapContainerTr) return;
        float areaW = mapAreaRt.rect.width;
        float areaH = mapAreaRt.rect.height;
        if (areaW <= 0 || areaH <= 0) return;
        float scale = Mathf.Max(areaW / (GRID_W * TILE_PIX), areaH / (GRID_H * TILE_PIX));
        mapContainerTr.localScale = new Vector3(scale, scale, 1f);
    }

    void OnMapScroll(Vector2 delta)
    {
        int dir = delta.y > 0 ? 1 : -1;

        // Captura la posición geográfica bajo el cursor ANTES de cambiar el zoom.
        bool hasCursor = ScreenToLatLon(Input.mousePosition, out double cursorLat, out double cursorLon);

        int    oldZoom   = zoom;
        double oldCenLon = centerLon;
        double oldCenLat = centerLat;

        ChangeZoom(dir);

        // Si el cursor estaba sobre el mapa y el zoom cambió, desplaza el centro
        // para que el punto geográfico bajo el cursor quede en el mismo píxel.
 // Fórmula: newCenterTile(newZoom) = (2^d ˆ 1)·cursorTile(oldZoom) + centerTile(oldZoom)
        if (!hasCursor || zoom == oldZoom) return;

 int d = zoom - oldZoom; // +1 ó ˆ1
        double f = Math.Pow(2.0, d) - 1.0;
        double ncTileX = f * LonToTileX(cursorLon, oldZoom) + LonToTileX(oldCenLon, oldZoom);
        double ncTileY = f * LatToTileY(cursorLat, oldZoom) + LatToTileY(oldCenLat, oldZoom);
        centerLon = TileXToLon(ncTileX, zoom);
        centerLat = Math.Max(-85.0, Math.Min(85.0, TileYToLat(ncTileY, zoom)));
        panLonStart = centerLon;
        panLatStart = centerLat;
        RefreshTiles(true);
        RedrawMarkers();
    }

    // Convierte una posición de pantalla dentro del mapArea a lat/lon
    // (usado para mostrar coordenadas y, en el futuro, colocar objetos con otro gesto)
    bool ScreenToLatLon(Vector2 screenPos, out double lat, out double lon)
    {
        lat = lon = 0;
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(mapAreaRt, screenPos, null, out local);
        float areaW = mapAreaRt.rect.width;
        float areaH = mapAreaRt.rect.height;
        float rawW  = GRID_W * TILE_PIX;
        float rawH  = GRID_H * TILE_PIX;
        float scale = Mathf.Max(areaW / (GRID_W * TILE_PIX), areaH / (GRID_H * TILE_PIX));
        float px    = local.x + areaW / 2f;
        float py    = areaH  / 2f - local.y;
        float ox    = (areaW - rawW * scale) / 2f;
        float oy    = (areaH - rawH * scale) / 2f;
        float mx    = px - ox;
        float my    = py - oy;
        if (mx < 0 || my < 0 || mx > rawW * scale || my > rawH * scale) return false;
        double tx = startTileX + mx / (scale * TILE_PIX);
        double ty = startTileY + my / (scale * TILE_PIX);
        lat = TileYToLat(ty, zoom);
        lon = TileXToLon(tx, zoom);
        return true;
    }

    // Inverso de ScreenToLatLon para el espacio "marcador" (el anchoredPosition
    // que ya usan los propios marcadores, ver SpawnMarker/MoveSelectedMarker):
    // necesario para recalcular lat/lon cuando el snap desplaza la posición
    // respecto a la posición cruda del cursor.
    (double lat, double lon) MarkerPosToLatLon(Vector2 markerPos)
    {
        float rawW = GRID_W * TILE_PIX, rawH = GRID_H * TILE_PIX;
        float px = markerPos.x + rawW / 2f;
        float py = rawH / 2f - markerPos.y;
        double tx = startTileX + px / TILE_PIX;
        double ty = startTileY + py / TILE_PIX;
        return (TileYToLat(ty, zoom), TileXToLon(tx, zoom));
    }

    // =========================================================================
    //  Snapping (guías de alineación automática)
    // =========================================================================
    struct SnapResult
    {
        public Vector2 pos;
        public float   rotY;
        public bool    hasRotSnap;
        public bool    showColGuide, showRowGuide;
        public float   colX, rowY;
    }

    // Compara la posición/rotación candidatas contra los objetos ya colocados
    // (saltando excludeIdx) y devuelve la versión ajustada si algo engancha.
    // Todo en el mismo espacio "marcador" que markerGOs[i].anchoredPosition,
    // así no hace falta recalcular nada de tiles/escala para comparar.
    SnapResult TrySnap(Vector2 candidatePos, float candidateRotY, int excludeIdx)
    {
        var result = new SnapResult { pos = candidatePos, rotY = candidateRotY };

        int   colIdx = -1, rowIdx = -1;
        float colDist = POS_SNAP_PX, rowDist = POS_SNAP_PX;

        for (int i = 0; i < placed.Count; i++)
        {
            if (i == excludeIdx || markerGOs[i] == null) continue;
            Vector2 otherPos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;

            float dx = Mathf.Abs(candidatePos.x - otherPos.x);
            if (dx <= colDist) { colDist = dx; colIdx = i; }

            float dy = Mathf.Abs(candidatePos.y - otherPos.y);
            if (dy <= rowDist) { rowDist = dy; rowIdx = i; }
        }

        if (colIdx >= 0)
        {
            float colX = markerGOs[colIdx].GetComponent<RectTransform>().anchoredPosition.x;
            result.pos.x     = colX;
            result.showColGuide = true;
            result.colX       = colX;
            TrySnapSpacing(ref result.pos, axisIsX: true, anchorIdx: colIdx, excludeIdx: excludeIdx, rawAlongCoord: candidatePos.y);
        }
        if (rowIdx >= 0)
        {
            float rowY = markerGOs[rowIdx].GetComponent<RectTransform>().anchoredPosition.y;
            result.pos.y     = rowY;
            result.showRowGuide = true;
            result.rowY       = rowY;
            TrySnapSpacing(ref result.pos, axisIsX: false, anchorIdx: rowIdx, excludeIdx: excludeIdx, rawAlongCoord: candidatePos.x);
        }

        // Rotación: el vecino más cercano (dentro del radio de "zona") cuya
        // rotY esté a pocos grados de la candidata.
        float bestRotDist = ROT_SNAP_DEG;
        int   bestRotIdx  = -1;
        for (int i = 0; i < placed.Count; i++)
        {
            if (i == excludeIdx || markerGOs[i] == null) continue;
            Vector2 otherPos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            if (Vector2.Distance(candidatePos, otherPos) > ROT_SNAP_RADIUS_PX) continue;

            float dRot = Mathf.Abs(Mathf.DeltaAngle(candidateRotY, placed[i].rotY));
            if (dRot <= bestRotDist) { bestRotDist = dRot; bestRotIdx = i; }
        }
        if (bestRotIdx >= 0)
        {
            result.rotY       = placed[bestRotIdx].rotY;
            result.hasRotSnap = true;
        }

        return result;
    }

    // Si hay un segundo objeto (distinto de excludeIdx y de anchorIdx) alineado
    // en el mismo eje que anchorIdx con un hueco regular, y la posición cruda
    // del candidato continúa ese patrón, ajusta esa coordenada al siguiente
    // hueco exacto. Heurística simple: coge el primer par que encaje, no busca
    // la mejor secuencia posible.
    // axisIsX: true si el eje YA enganchado/compartido es X (caso columna:
    // mismo X, el hueco entre objetos se mide en Y). false si el compartido
    // es Y (caso fila: mismo Y, el hueco se mide en X). Antes tenía
    // "compartido" y "a lo largo" invertidos, por lo que nunca enganchaba
    // bien el espaciado en una fila horizontal.
    void TrySnapSpacing(ref Vector2 pos, bool axisIsX, int anchorIdx, int excludeIdx, float rawAlongCoord)
    {
        Vector2 anchorPos    = markerGOs[anchorIdx].GetComponent<RectTransform>().anchoredPosition;
        float   anchorShared = axisIsX ? anchorPos.x : anchorPos.y;
        float   anchorAlong  = axisIsX ? anchorPos.y : anchorPos.x;

        for (int i = 0; i < placed.Count; i++)
        {
            if (i == excludeIdx || i == anchorIdx || markerGOs[i] == null) continue;
            Vector2 otherPos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            float otherShared = axisIsX ? otherPos.x : otherPos.y;
            if (Mathf.Abs(otherShared - anchorShared) > POS_SNAP_PX) continue; // no está en la misma fila/columna

            float otherAlong = axisIsX ? otherPos.y : otherPos.x;
            float gap = anchorAlong - otherAlong; // con signo: dirección de la secuencia
            if (Mathf.Abs(gap) < 1f) continue;

            float continued = anchorAlong + gap; // siguiente punto de la secuencia, al otro lado del ancla
            if (Mathf.Abs(rawAlongCoord - continued) <= Mathf.Abs(gap) * SPACING_SNAP_FRAC)
            {
                if (axisIsX) pos.y = continued; else pos.x = continued;
                return;
            }
        }
    }

    // ── Línea de grupo: guía transitoria + tirador de rotación persistente ──

    void EnsureGroupLine()
    {
        if (groupLine != null) return;
        var go = new GameObject("GroupLine");
        go.transform.SetParent(markersParent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.65f);
        img.raycastTarget = false; // el hit-test se hace a mano, ver HitTestGroupLine
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        groupLine = rt;
    }

    // Centra la línea en "center", orientada según "dir" (vector unitario) y
    // con la longitud dada. dir=(0,1) → sin rotar (vertical); dir=(1,0) → -90°;
    // cualquier otro ángulo intermedio funciona igual (usado al rotar el grupo).
    void PositionGroupLine(Vector2 center, Vector2 dir, float length)
    {
        EnsureGroupLine();
        groupLine.gameObject.SetActive(true);
        groupLine.anchoredPosition = center;
        float angleDeg = Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg;
        groupLine.localEulerAngles = new Vector3(0f, 0f, angleDeg);
        groupLine.sizeDelta = new Vector2(1.2f, Mathf.Max(length, 8f));
    }

    // Solo oculta el dibujo — NO olvida groupIndices/centroide/dirección: una
    // línea ya rotada debe poder "reaparecer" más tarde si otro objeto pasa
    // cerca de ella (ver TrySnapToExistingGroupLine), así que su identidad se
    // conserva aunque esté oculta.
    void HideGroupLine()
    {
        if (groupLine) groupLine.gameObject.SetActive(false);
    }

    // Guía transitoria mientras se arrastra UN marcador (feedback inmediato de
    // TrySnap) — si no hay alineación de eje, intenta enganchar con la línea
    // de grupo ya existente (aunque esté rotada) antes de ocultarla del todo.
    void ShowTransientGuide(SnapResult snap, ref Vector2 pos, int excludeIdx)
    {
        if (snap.showColGuide)
            PositionGroupLine(new Vector2(snap.colX, 0f), Vector2.up, GRID_H * TILE_PIX);
        else if (snap.showRowGuide)
            PositionGroupLine(new Vector2(0f, snap.rowY), Vector2.right, GRID_W * TILE_PIX);
        else if (!TrySnapToExistingGroupLine(ref pos, excludeIdx))
            HideGroupLine();
    }

    // Si ya existe un grupo asentado (groupIndices/groupCentroid/groupDir de
    // una alineación o rotación anterior, aunque ahora esté oculto) y "pos"
    // pasa cerca de esa línea, la engancha y la vuelve a mostrar — así una
    // línea que ya rotaste "reaparece" en cuanto acercas otro objeto, en vez
    // de quedar olvidada para siempre. Si "pos" no cae sobre el grupo activo,
    // prueba también con el resto de líneas ya asentadas (settledGroups) —
    // igual que HitTestAnyGroupLine para los clics — así se puede seguir
    // enganchando/colocando objetos sobre CUALQUIER línea ya girada, no solo
    // sobre la última que se tocó.
    bool TrySnapToExistingGroupLine(ref Vector2 pos, int excludeIdx)
    {
        if (GroupIndicesValid() && !groupIndices.Contains(excludeIdx))
        {
            float activeMaxProj = GroupMaxProjection();
            if (TrySnapAgainstLine(groupIndices, groupCentroid, groupDir, activeMaxProj, ref pos))
            {
                PositionGroupLine(groupCentroid, groupDir, activeMaxProj * 2f + 40f);
                return true;
            }
        }

        foreach (var (indices, dir) in settledGroups)
        {
            if (indices.Count < 2 || indices.Contains(excludeIdx)) continue;

            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var i in indices)
            {
                if (i < 0 || i >= markerGOs.Count || markerGOs[i] == null) continue;
                sum += markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
                n++;
            }
            if (n == 0) continue;
            Vector2 centroid = sum / n;

            float maxProj = 0f;
            foreach (var i in indices)
            {
                if (i < 0 || i >= markerGOs.Count || markerGOs[i] == null) continue;
                Vector2 rel = markerGOs[i].GetComponent<RectTransform>().anchoredPosition - centroid;
                maxProj = Mathf.Max(maxProj, Mathf.Abs(Vector2.Dot(rel, dir)));
            }

            Vector2 candidatePos = pos;
            if (!TrySnapAgainstLine(indices, centroid, dir, maxProj, ref candidatePos)) continue;

            // Esta línea sí engancha: pasa a ser el grupo activo (igual que al
            // hacer clic sobre ella), así queda resaltada y lista para seguir
            // colocando objetos encima.
            groupIndices  = new List<int>(indices);
            groupDir      = dir;
            groupCentroid = centroid;
            pos = candidatePos;
            PositionGroupLine(groupCentroid, groupDir, maxProj * 2f + 40f);
            return true;
        }

        return false;
    }

    // Comprueba si "pos" cae sobre la línea (centroid, dir) dentro del umbral
    // de enganche, y si es así, la ajusta a esa línea (con el espaciado
    // regular del grupo, si lo hay). No toca ningún estado de grupo — solo
    // geometría — para poder probarla contra el grupo activo o contra
    // cualquiera de settledGroups con la misma lógica.
    bool TrySnapAgainstLine(List<int> indices, Vector2 centroid, Vector2 dir, float maxProj, ref Vector2 pos)
    {
        Vector2 toPoint = pos - centroid;
        float alongRaw = Vector2.Dot(toPoint, dir);
        Vector2 closest = centroid + dir * alongRaw;
        if (Vector2.Distance(pos, closest) > POS_SNAP_PX) return false;
        if (Mathf.Abs(alongRaw) > maxProj + 300f) return false; // no enganchar a puntos disparatadamente lejos

        float along = alongRaw;
        TrySnapSpacingAlongGroup(indices, centroid, dir, ref along, alongRaw);
        pos = centroid + dir * along;
        return true;
    }

    // Si en el grupo ya hay al menos dos miembros con un hueco regular entre
    // ellos hacia el lado por el que se acerca el candidato, ajusta su
    // coordenada a lo largo de la línea (rotada o no) para continuar ese
    // mismo hueco — mismo criterio que TrySnapSpacing, pero a lo largo de
    // groupDir en vez de limitado a los ejes X/Y.
    void TrySnapSpacingAlongGroup(List<int> indices, Vector2 centroid, Vector2 dir, ref float along, float rawAlong)
    {
        var projections = new List<float>();
        foreach (var i in indices)
        {
            if (i < 0 || i >= markerGOs.Count || markerGOs[i] == null) continue;
            Vector2 rel = markerGOs[i].GetComponent<RectTransform>().anchoredPosition - centroid;
            projections.Add(Vector2.Dot(rel, dir));
        }
        if (projections.Count < 2) return;
        projections.Sort();

        // Miembro más cercano al candidato ("ancla") a lo largo de la línea.
        float bestDist = float.MaxValue;
        int anchorIdx = -1;
        for (int k = 0; k < projections.Count; k++)
        {
            float d = Mathf.Abs(projections[k] - rawAlong);
            if (d < bestDist) { bestDist = d; anchorIdx = k; }
        }

        float anchor = projections[anchorIdx];
        bool candidateBeyond = rawAlong > anchor;
        int neighborIdx = candidateBeyond ? anchorIdx - 1 : anchorIdx + 1;
        if (neighborIdx < 0 || neighborIdx >= projections.Count) return;

        float gap = anchor - projections[neighborIdx]; // con signo: dirección de la secuencia
        if (Mathf.Abs(gap) < 1f) return;

        float continued = anchor + gap; // siguiente hueco exacto, al otro lado del ancla
        if (Mathf.Abs(rawAlong - continued) <= Mathf.Abs(gap) * SPACING_SNAP_FRAC)
            along = continued;
    }

    float GroupMaxProjection()
    {
        float maxProj = 0f;
        foreach (var i in groupIndices)
        {
            if (i < 0 || i >= markerGOs.Count || markerGOs[i] == null) continue;
            Vector2 rel = markerGOs[i].GetComponent<RectTransform>().anchoredPosition - groupCentroid;
            maxProj = Mathf.Max(maxProj, Mathf.Abs(Vector2.Dot(rel, groupDir)));
        }
        return maxProj;
    }

    // Todos los índices de `placed` alineados con seedIdx (columna si comparten
    // X, si no fila si comparten Y — mismo umbral que TrySnap). Vacío si no hay
    // al menos un vecino con quien alinearse.
    List<int> ComputeAlignedGroup(int seedIdx, out Vector2 dir)
    {
        dir = Vector2.up;
        var empty = new List<int>();
        if (seedIdx < 0 || seedIdx >= placed.Count || markerGOs[seedIdx] == null) return empty;

        Vector2 seedPos = markerGOs[seedIdx].GetComponent<RectTransform>().anchoredPosition;
        var colMatches = new List<int> { seedIdx };
        var rowMatches = new List<int> { seedIdx };

        for (int i = 0; i < placed.Count; i++)
        {
            if (i == seedIdx || markerGOs[i] == null) continue;
            Vector2 pos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            if (Mathf.Abs(pos.x - seedPos.x) <= POS_SNAP_PX) colMatches.Add(i);
            if (Mathf.Abs(pos.y - seedPos.y) <= POS_SNAP_PX) rowMatches.Add(i);
        }

        if (colMatches.Count >= 2) { dir = Vector2.up;    return colMatches; }
        if (rowMatches.Count >= 2) { dir = Vector2.right; return rowMatches; }
        return empty;
    }

    // Todas las filas/columnas alineadas que existen ahora mismo entre los
    // objetos colocados que no estén ya en "exclude" (normalmente los que ya
    // pertenecen a un grupo asentado) — cada objeto puede aparecer como mucho
    // en un grupo por eje, agrupando de forma voraz a partir del primero sin
    // asignar todavía.
    List<(List<int> indices, Vector2 dir)> ComputeAllAlignedGroups(HashSet<int> exclude = null)
    {
        var groups = new List<(List<int>, Vector2)>();
        var usedCol = new bool[placed.Count];
        var usedRow = new bool[placed.Count];

        for (int i = 0; i < placed.Count; i++)
        {
            if (usedCol[i] || markerGOs[i] == null || (exclude != null && exclude.Contains(i))) continue;
            Vector2 seedPos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            var col = new List<int> { i };
            for (int j = i + 1; j < placed.Count; j++)
            {
                if (usedCol[j] || markerGOs[j] == null || (exclude != null && exclude.Contains(j))) continue;
                Vector2 p = markerGOs[j].GetComponent<RectTransform>().anchoredPosition;
                if (Mathf.Abs(p.x - seedPos.x) <= POS_SNAP_PX) col.Add(j);
            }
            if (col.Count >= 2)
            {
                foreach (var k in col) usedCol[k] = true;
                groups.Add((col, Vector2.up));
            }
        }

        for (int i = 0; i < placed.Count; i++)
        {
            if (usedRow[i] || markerGOs[i] == null || (exclude != null && exclude.Contains(i))) continue;
            Vector2 seedPos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            var row = new List<int> { i };
            for (int j = i + 1; j < placed.Count; j++)
            {
                if (usedRow[j] || markerGOs[j] == null || (exclude != null && exclude.Contains(j))) continue;
                Vector2 p = markerGOs[j].GetComponent<RectTransform>().anchoredPosition;
                if (Mathf.Abs(p.y - seedPos.y) <= POS_SNAP_PX) row.Add(j);
            }
            if (row.Count >= 2)
            {
                foreach (var k in row) usedRow[k] = true;
                groups.Add((row, Vector2.right));
            }
        }

        return groups;
    }

    // Mantiene "settledGroups" al día: quita de cada grupo asentado los
    // índices que ya no existen (borrados) o que se han movido fuera de su
    // línea (más allá de POS_SNAP_PX de distancia perpendicular); un grupo
    // solo se olvida por completo cuando se queda sin NINGÚN miembro — con
    // uno solo, la línea se sigue dibujando (mismo criterio pedido para que
    // "mover un objeto" no borre la línea mientras quede algo sobre ella).
    // Después, detecta como nuevos grupos asentados cualquier fila/columna de
    // 2+ objetos que aún no pertenezca a ninguno.
    void UpdateSettledGroups()
    {
        for (int g = settledGroups.Count - 1; g >= 0; g--)
        {
            var (indices, dir) = settledGroups[g];
            indices.RemoveAll(i => i < 0 || i >= placed.Count || i >= markerGOs.Count || markerGOs[i] == null);
            if (indices.Count == 0) { settledGroups.RemoveAt(g); continue; }

            Vector2 sum = Vector2.zero;
            foreach (var i in indices) sum += markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            Vector2 centroid = sum / indices.Count;

            indices.RemoveAll(i =>
            {
                Vector2 pos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
                Vector2 closest = centroid + dir * Vector2.Dot(pos - centroid, dir);
                return Vector2.Distance(pos, closest) > POS_SNAP_PX;
            });

            if (indices.Count == 0) settledGroups.RemoveAt(g);
            else settledGroups[g] = (indices, dir);
        }

        var claimed = new HashSet<int>();
        foreach (var (indices, _) in settledGroups)
            foreach (var i in indices) claimed.Add(i);

        settledGroups.AddRange(ComputeAllAlignedGroups(claimed));
    }

    void EnsureAllGroupLinesCount(int count)
    {
        while (allGroupLines.Count < count)
        {
            var go = new GameObject("AllGroupLine");
            go.transform.SetParent(markersParent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.35f); // más tenue que la línea de grupo activa
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            go.SetActive(false);
            allGroupLines.Add(rt);
        }
    }

    // Muestra una línea de referencia por cada grupo asentado — se llama cada
    // frame desde Update() para que las líneas usadas para colocar objetos se
    // queden dibujadas de forma permanente (incluso si solo queda un objeto
    // sobre ellas, ver UpdateSettledGroups).
    void ShowAllGroupLines()
    {
        UpdateSettledGroups();
        var groups = settledGroups;
        EnsureAllGroupLinesCount(groups.Count);

        for (int g = 0; g < groups.Count; g++)
        {
            var (indices, dir) = groups[g];
            Vector2 sum = Vector2.zero;
            foreach (var i in indices) sum += markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            Vector2 centroid = sum / indices.Count;

            float maxProj = 0f;
            foreach (var i in indices)
            {
                Vector2 rel = markerGOs[i].GetComponent<RectTransform>().anchoredPosition - centroid;
                maxProj = Mathf.Max(maxProj, Mathf.Abs(Vector2.Dot(rel, dir)));
            }

            var line = allGroupLines[g];
            line.gameObject.SetActive(true);
            line.anchoredPosition = centroid;
            float angleDeg = Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg;
            line.localEulerAngles = new Vector3(0f, 0f, angleDeg);
            line.sizeDelta = new Vector2(1.2f, Mathf.Max(maxProj * 2f + 40f, 8f));
        }

        for (int g = groups.Count; g < allGroupLines.Count; g++)
            allGroupLines[g].gameObject.SetActive(false);
    }

    // Comprueba que todos los índices de groupIndices siguen siendo válidos
    // (placed/markerGOs pueden haber cambiado de tamaño, p. ej. al borrar un
    // objeto) — si no, olvida el grupo en vez de dejar que algo más adelante
    // reviente con un índice fuera de rango a media colocación/arrastre.
    bool GroupIndicesValid()
    {
        foreach (var i in groupIndices)
        {
            if (i < 0 || i >= placed.Count || i >= markerGOs.Count || markerGOs[i] == null)
            {
                groupIndices.Clear();
                HideGroupLine();
                return false;
            }
        }
        return groupIndices.Count >= 2;
    }

    // Guarda una copia independiente de cada objeto del grupo activo, junto
    // con su posición relativa al centroide (espacio "marcador") y la
    // dirección de la línea en ese momento — así "Pegar" puede reconstruir
    // el mismo grupo (mismas separaciones/orientación relativa) en cualquier
    // otro punto del mapa.
    void CopyGroupToClipboard()
    {
        if (!GroupIndicesValid()) return;

        lineClipboard = new List<(MapPlacedObject, Vector2)>();
        foreach (var i in groupIndices)
        {
            Vector2 pos = markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            lineClipboard.Add((placed[i].Clone(), pos - groupCentroid));
        }
        lineClipboardDir = groupDir;
    }

    // Copia un único objeto suelto (clic derecho sobre él → "Copiar", fuera de
    // cualquier línea/grupo) usando el mismo portapapeles que las líneas: un
    // "grupo" de un solo elemento con desplazamiento cero al centroide. Al
    // pegar (PasteGroupFromClipboard) se reconstruye igual, sin necesitar
    // ningún caso especial para N=1.
    void CopySingleToClipboard(int idx)
    {
        if (idx < 0 || idx >= placed.Count) return;
        lineClipboard = new List<(MapPlacedObject, Vector2)> { (placed[idx].Clone(), Vector2.zero) };
        lineClipboardDir = Vector2.up;
    }

    // Borra del mapa TODOS los objetos del grupo activo (fila/columna bajo la
    // línea sobre la que se hizo clic derecho), no solo uno.
    void DeleteGroupFromMap()
    {
        if (!GroupIndicesValid()) return;

        var toDelete = new List<int>(groupIndices);
        toDelete.Sort();
        toDelete.Reverse(); // de mayor a menor índice, para no desordenar los que faltan por borrar
        foreach (var i in toDelete) RemovePlacedAt(i);

        if (groupLine) groupLine.gameObject.SetActive(false);
        DeselectPlaced();
    }

    // Borra el objeto "i" de placed/markerGOs y REMAPEA los índices guardados
    // en groupIndices/settledGroups: cualquier índice mayor que "i" se
    // desplaza una posición hacia abajo, y el propio "i" se quita de donde
    // apareciera. Sin esto, un grupo asentado que apuntase a objetos
    // posteriores al borrado quedaría señalando al objeto equivocado (o fuera
    // de rango), porque a diferencia de antes ahora esos índices SÍ se
    // conservan de un frame a otro en vez de recalcularse desde cero.
    void RemovePlacedAt(int i)
    {
        if (markerGOs[i]) Destroy(markerGOs[i]);
        markerGOs.RemoveAt(i);
        placed.RemoveAt(i);

        RemapIndicesAfterDelete(groupIndices, i);
        for (int g = settledGroups.Count - 1; g >= 0; g--)
        {
            RemapIndicesAfterDelete(settledGroups[g].indices, i);
            if (settledGroups[g].indices.Count == 0) settledGroups.RemoveAt(g);
        }
    }

    static void RemapIndicesAfterDelete(List<int> indices, int deletedIdx)
    {
        for (int k = indices.Count - 1; k >= 0; k--)
        {
            if (indices[k] == deletedIdx) indices.RemoveAt(k);
            else if (indices[k] > deletedIdx) indices[k]--;
        }
    }

    // Recrea los objetos copiados centrados en "screenPos", con las mismas
    // separaciones/orientación relativa que tenían al copiarlos, y los deja
    // como el grupo activo (se puede rotar/mover igual que cualquier otro).
    void PasteGroupFromClipboard(Vector2 screenPos)
    {
        if (lineClipboard == null || lineClipboard.Count == 0) return;

        Vector2 targetCentroid = ScreenToMarkerPos(screenPos);
        var newIndices = new List<int>();

        foreach (var (obj, offset) in lineClipboard)
        {
            var clone = obj.Clone();
            Vector2 pos = targetCentroid + offset;
            var (la, lo) = MarkerPosToLatLon(pos);
            clone.lat = (float)la;
            clone.lon = (float)lo;

            placed.Add(clone);
            SpawnMarker(pos.x, pos.y, clone);
            newIndices.Add(placed.Count - 1);
        }

        groupIndices = newIndices;
        groupDir     = lineClipboardDir;
        RecomputeGroupLineFromIndices();
        SelectPlaced(newIndices[newIndices.Count - 1]);
    }

    // Al soltar tras mover/colocar: si el objeto quedó enganchado a la línea
    // de grupo YA existente (aunque esté rotada), se une a ese grupo en vez
    // de recalcular solo por ejes (ComputeAlignedGroup no reconocería una
    // línea rotada); si no, cae al criterio normal de alineación de ejes.
    void SettleGroupLine(int seedIdx)
    {
        if (seedIdx < 0 || seedIdx >= placed.Count || markerGOs[seedIdx] == null) { HideGroupLine(); return; }

        if (GroupIndicesValid() && !groupIndices.Contains(seedIdx))
        {
            Vector2 seedPos = markerGOs[seedIdx].GetComponent<RectTransform>().anchoredPosition;
            Vector2 closest = groupCentroid + groupDir * Vector2.Dot(seedPos - groupCentroid, groupDir);
            if (Vector2.Distance(seedPos, closest) <= POS_SNAP_PX)
            {
                groupIndices.Add(seedIdx);
                RecomputeGroupLineFromIndices();
                return;
            }
        }

        var group = ComputeAlignedGroup(seedIdx, out Vector2 dir);
        if (group.Count < 2) { HideGroupLine(); return; }

        groupIndices = group;
        groupDir     = dir;
        RecomputeGroupLineFromIndices();
    }

    void RecomputeGroupLineFromIndices()
    {
        Vector2 sum = Vector2.zero;
        int n = 0;
        foreach (var i in groupIndices)
        {
            if (i < 0 || i >= markerGOs.Count || markerGOs[i] == null) continue;
            sum += markerGOs[i].GetComponent<RectTransform>().anchoredPosition;
            n++;
        }
        if (n == 0) { HideGroupLine(); return; }
        groupCentroid = sum / n;
        PositionGroupLine(groupCentroid, groupDir, GroupMaxProjection() * 2f + 40f); // +40: sobresale un poco, más fácil de agarrar
        SyncSettledGroup();
    }

    // Refleja el grupo activo (groupIndices/groupDir) en settledGroups. Solo
    // sustituye una entrada existente si TODOS sus miembros están ya en el
    // grupo activo (o sea, es la misma línea recién extendida/rotada/vuelta a
    // agarrar) — así se actualiza correctamente en vez de duplicarse. Un
    // simple solape parcial (p. ej. un objeto "esquina" compartido entre una
    // fila y una columna que se cruzan en él) NO cuenta como la misma línea y
    // no debe borrar la otra: antes, borrar por cualquier solape hacía que
    // crear una línea nueva que tocase a una vieja en un solo punto borrase
    // esa línea vieja entera.
    void SyncSettledGroup()
    {
        if (groupIndices.Count < 2) return;
        settledGroups.RemoveAll(g => g.indices.TrueForAll(i => groupIndices.Contains(i)));
        settledGroups.Add((new List<int>(groupIndices), groupDir));
    }

    bool HitTestGroupLine(Vector2 screenPos)
    {
        if (groupLine == null || !groupLine.gameObject.activeSelf) return false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(groupLine, screenPos, null, out Vector2 local);
        var r = groupLine.rect;
        const float margin = 8f; // línea fina: margen generoso para poder agarrarla
        return local.x >= r.xMin - margin && local.x <= r.xMax + margin &&
               local.y >= r.yMin - margin && local.y <= r.yMax + margin;
    }

    // Como HitTestGroupLine, pero también reconoce cualquiera de las líneas
    // de referencia siempre visibles (allGroupLines), no solo la activa. Si
    // el punto cae sobre una de esas líneas de referencia, esa fila/columna
    // pasa a ser el grupo activo (groupIndices/groupCentroid/groupDir) — así
    // "Copiar"/"Eliminar" desde el menú contextual siempre actúan sobre la
    // línea que hay bajo el cursor, en vez de sobre un grupo activo antiguo.
    bool HitTestAnyGroupLine(Vector2 screenPos)
    {
        if (HitTestGroupLine(screenPos)) return true;

        // settledGroups ya está al día (ShowAllGroupLines se llama cada frame
        // al principio de Update(), antes de que se procesen los clics).
        var groups = settledGroups;
        for (int g = 0; g < groups.Count && g < allGroupLines.Count; g++)
        {
            var line = allGroupLines[g];
            if (line == null || !line.gameObject.activeSelf) continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(line, screenPos, null, out Vector2 local);
            var r = line.rect;
            const float margin = 8f;
            if (local.x >= r.xMin - margin && local.x <= r.xMax + margin &&
                local.y >= r.yMin - margin && local.y <= r.yMax + margin)
            {
                groupIndices = new List<int>(groups[g].indices);
                groupDir     = groups[g].dir;
                RecomputeGroupLineFromIndices();
                return true;
            }
        }
        return false;
    }

    // Posición "marcador" (mismo espacio que anchoredPosition de los
    // marcadores) bajo el cursor — mismas fórmulas que ScreenToLatLon/
    // MoveSelectedMarker, factorizadas aquí para el arrastre de la línea.
    Vector2 ScreenToMarkerPos(Vector2 screenPos)
    {
        float rawW = GRID_W * TILE_PIX, rawH = GRID_H * TILE_PIX;
        float areaW = mapAreaRt.rect.width, areaH = mapAreaRt.rect.height;
        float scale = Mathf.Max(areaW / rawW, areaH / rawH);
        float ox = (areaW - rawW * scale) / 2f, oy = (areaH - rawH * scale) / 2f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(mapAreaRt, screenPos, null, out Vector2 local);
        float px = (local.x + areaW / 2f - ox) / scale;
        float py = (areaH / 2f - local.y - oy) / scale;
        return new Vector2(px - rawW / 2f, -(py - rawH / 2f));
    }

    static Vector2 RotateVector(Vector2 v, float deg)
    {
        float rad = deg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    static float NormalizeAngle180(float deg)
    {
        deg %= 360f;
        if (deg > 180f)  deg -= 360f;
        if (deg < -180f) deg += 360f;
        return deg;
    }

    // Arrastrar la línea normalmente MUEVE todo el grupo; con Shift pulsado
    // (comprobado solo al empezar el arrastre, no se cambia de modo a media
    // maniobra) ROTA en su lugar, como antes.
    void BeginGroupLineDrag(Vector2 screenPos)
    {
        draggingGroupLine = true;
        groupDragIsRotate = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        Vector2 mousePos = ScreenToMarkerPos(screenPos);
        groupDragStartMouse      = mousePos;
        groupCentroidAtDragStart = groupCentroid;

        Vector2 rel = mousePos - groupCentroid;
        groupDragStartAngle = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;
        groupDirAtDragStart = groupDir;

        groupSnapshot = new List<(int idx, Vector2 relPos, float rotY)>();
        foreach (var idx in groupIndices)
        {
            Vector2 pos = markerGOs[idx].GetComponent<RectTransform>().anchoredPosition;
            groupSnapshot.Add((idx, pos - groupCentroid, placed[idx].rotY));
        }
    }

    void UpdateGroupLineDrag(Vector2 screenPos)
    {
        if (groupDragIsRotate) UpdateGroupLineRotate(screenPos);
        else                   UpdateGroupLineMove(screenPos);
    }

    // Gira todo el grupo alrededor de groupCentroid (fijo durante todo el
    // gesto) según el ángulo actual del ratón respecto al inicio del
    // arrastre. Cada miembro suma el mismo delta a su propia rotY (queda
    // "mirando" en la nueva dirección de la fila) — signo invertido respecto
    // al delta de posición porque el icono del marcador se dibuja con
    // localEulerAngles.z = -rotY (ver SpawnMarker).
    void UpdateGroupLineRotate(Vector2 screenPos)
    {
        Vector2 mousePos = ScreenToMarkerPos(screenPos);
        Vector2 rel = mousePos - groupCentroid;
        float curAngle = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;
        // Delta SIEMPRE respecto al ángulo de inicio del arrastre, nunca
        // acumulado frame a frame — igual que las posiciones, que parten de
        // groupSnapshot (fijo) en cada frame. Acumular aquí fue el bug: hacía
        // que la línea girase muchísimo más rápido que los aros y se
        // desalinease (¡de ahí el efecto de "girar sin control"!).
        float deltaDeg = NormalizeAngle180(curAngle - groupDragStartAngle);

        foreach (var (idx, relPos, rotY) in groupSnapshot)
        {
            Vector2 newPos  = groupCentroid + RotateVector(relPos, deltaDeg);
            float   newRotY = NormalizeAngle180(rotY - deltaDeg);

            var (la, lo) = MarkerPosToLatLon(newPos);
            placed[idx].lat  = (float)la;
            placed[idx].lon  = (float)lo;
            placed[idx].rotY = newRotY;

            var rt = markerGOs[idx].GetComponent<RectTransform>();
            rt.anchoredPosition = newPos;
            rt.localEulerAngles = new Vector3(0f, 0f, -newRotY);

            if (idx == selectedPlacedIdx) RefreshTypeRotY(placed[idx].type, newRotY);
        }

        groupDir = RotateVector(groupDirAtDragStart, deltaDeg).normalized;
        float maxProj = 0f;
        foreach (var (_, relPos, _) in groupSnapshot)
            maxProj = Mathf.Max(maxProj, RotateVector(relPos, deltaDeg).magnitude);
        PositionGroupLine(groupCentroid, groupDir, maxProj * 2f + 40f);
    }

    // Traslada todo el grupo en bloque según cuánto se ha movido el ratón
    // desde el inicio del arrastre — cada miembro conserva su posición
    // relativa (y su propia rotY) respecto al resto, solo cambia el centro.
    void UpdateGroupLineMove(Vector2 screenPos)
    {
        Vector2 mousePos = ScreenToMarkerPos(screenPos);
        Vector2 delta = mousePos - groupDragStartMouse;
        Vector2 newCentroid = groupCentroidAtDragStart + delta;

        foreach (var (idx, relPos, rotY) in groupSnapshot)
        {
            Vector2 newPos = newCentroid + relPos;
            var (la, lo) = MarkerPosToLatLon(newPos);
            placed[idx].lat = (float)la;
            placed[idx].lon = (float)lo;

            markerGOs[idx].GetComponent<RectTransform>().anchoredPosition = newPos;
        }

        groupCentroid = newCentroid;
        PositionGroupLine(groupCentroid, groupDir, GroupMaxProjection() * 2f + 40f);
    }

    // Lee/escribe el campo+slider de RotY del tipo dado — mismo patrón que ya
    // usa SelectPlaced para sincronizar los sliders con el objeto seleccionado.
    float GetActiveTypeRotY() =>
        selectedType == "Aro"          ? aroRotY :
        selectedType == "Bandera"      ? banderaRotY :
        selectedType == "Obstáculo"    ? obstaculoRotY :
        selectedType == "Moneda"       ? monedaRotY :
        selectedType == "Arco"         ? arcoRotY :
        selectedType == "Segundo Arco" ? arco2RotY :
        selectedType == "Flecha"       ? flechaRotY : 0f;

    void RefreshTypeRotY(string type, float rotY)
    {
        switch (type)
        {
            case "Aro":
                aroRotY = rotY;
                if (aroRotYSlider) { aroRotYSlider.SetValueWithoutNotify(rotY); aroRotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
            case "Bandera":
                banderaRotY = rotY;
                if (banderaRotYSlider) { banderaRotYSlider.SetValueWithoutNotify(rotY); banderaRotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
            case "Obstáculo":
                obstaculoRotY = rotY;
                if (obstaculoRotYSlider) { obstaculoRotYSlider.SetValueWithoutNotify(rotY); obstaculoRotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
            case "Moneda":
                monedaRotY = rotY;
                if (monedaRotYSlider) { monedaRotYSlider.SetValueWithoutNotify(rotY); monedaRotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
            case "Arco":
                arcoRotY = rotY;
                if (arcoRotYSlider) { arcoRotYSlider.SetValueWithoutNotify(rotY); arcoRotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
            case "Segundo Arco":
                arco2RotY = rotY;
                if (arco2RotYSlider) { arco2RotYSlider.SetValueWithoutNotify(rotY); arco2RotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
            case "Flecha":
                flechaRotY = rotY;
                if (flechaRotYSlider) { flechaRotYSlider.SetValueWithoutNotify(rotY); flechaRotYLabel.SetTextWithoutNotify(rotY.ToString("F0")); }
                break;
        }
    }

    // Cambia el tipo activo (dropdown, panel de propiedades visible, etc.)
    // sin tocar el previsualizador — factorizado aparte de SelectType porque
    // SelectPlaced también necesita esto (para que al seleccionar un objeto
    // ya colocado se vea SU panel de propiedades, no el del tipo que
    // estuviera activo antes), pero necesita hacerlo ANTES de rellenar los
    // valores concretos de ese objeto, no de golpe con SpawnPreviewObject(t)
    // (que usaría los valores globales actuales, todavía sin actualizar).
    void SwitchTypePanels(string t)
    {
        selectedType = t;
        if (typeDropdownLabel) { typeDropdownLabel.text = t; typeDropdownLabel.color = C_CYAN; }
        if (previewNameLabel) previewNameLabel.text = t;
        if (aroPropsPanel)       aroPropsPanel.SetActive(t == "Aro");
        if (banderaPropsPanel)   banderaPropsPanel.SetActive(t == "Bandera");
        if (obstaculoPropsPanel) obstaculoPropsPanel.SetActive(t == "Obstáculo");
        if (monedaPropsPanel)    monedaPropsPanel.SetActive(t == "Moneda");
        if (arcoPropsPanel)      arcoPropsPanel.SetActive(t == "Arco");
        if (arco2PropsPanel)     arco2PropsPanel.SetActive(t == "Segundo Arco");
        if (flechaPropsPanel)    flechaPropsPanel.SetActive(t == "Flecha");
    }

    void SelectType(string t)
    {
        SwitchTypePanels(t);
        SpawnPreviewObject(t);
    }

    void SwitchProvider(int idx)
    {
        providerIdx = idx;
        if (mapDropdownLabel) mapDropdownLabel.text = providers[providerIdx].name;
        if (attribLabel) attribLabel.text = providers[providerIdx].attrib;
        RefreshTiles(true);
    }

    void ChangeZoom(int delta)
    {
        zoom = Mathf.Clamp(zoom + delta, 3, MAX_ZOOM);
 if (zoomLabel) zoomLabel.text = $"Zoom: {zoom} (rueda del ratón o botones +/ˆ)";
        // Resetear referencia de paneo completa para evitar desfase entre
        // píxeles de pantalla y delta geográfico al cambiar la escala con el zoom
        panMouseStart = Input.mousePosition;
        panLonStart   = centerLon;
        panLatStart   = centerLat;
        panLastReload = -1f;
        RefreshTiles(true);
        RedrawMarkers();
    }

    // =========================================================================
    //  Marcadores
    // =========================================================================

    void SpawnMarker(float cx, float cy, MapPlacedObject obj)
    {
        var go = new GameObject("Mk_" + obj.type);
        go.transform.SetParent(markersParent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(cx, cy);
        rt.sizeDelta = new Vector2(36f, 36f); // aro de tipo (Ring+Arrow) es el exterior; el halo de color va dentro

        if (obj.type == "Aro")
        {
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY);
            lastAroIconRt = rt;
        }
        else if (obj.type == "Bandera")
        {
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY);
            lastBanderaIconRt = rt;
        }
        else if (obj.type == "Obstáculo")
        {
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY);
            lastObstaculoIconRt = rt;
        }
        else if (obj.type == "Moneda")
        {
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY);
            lastMonedaIconRt = rt;
        }
        else if (obj.type == "Arco")
        {
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY);
            lastArcoIconRt = rt;
        }
        else if (obj.type == "Segundo Arco")
        {
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY);
            lastArco2IconRt = rt;
        }
        else if (obj.type == "Flecha")
        {
            // +90 respecto al resto de tipos: el sprite de la flecha no descansa
            // "hacia arriba" a rotación 0 como los demás iconos, sino hacia abajo
            // (calibrado para que el ángulo Y de la flecha 3D corresponda al mismo
            // punto cardinal en el icono del mapa).
            rt.localEulerAngles = new Vector3(0f, 0f, -obj.rotY + 90f);
            lastFlechaIconRt = rt;
        }

        EnsureMarkerSprites();

        // Grosor absoluto (en unidades de UI), igual para el aro de color interior
        // y el aro de tipo exterior, aunque se muestren a tamaños distintos.
        const float ringLineThickness = 1.3f;

        // Aro de color interior — color elegido del propio objeto (distinto del aro
        // del icono, que se tiñe por TIPO de objeto, no por el color pintado). Va
        // detrás pero más pequeño: su borde exterior es tangente a la punta de la
        // flecha (radio medido directamente del sprite de flecha: 0.6721 del
        // semiancho del marcador, sin margen de antialiasing propio). El sprite del
        // aro sí tiene ese margen (RingFillRatio), así que se compensa dividiendo
        // por él para que el radio RENDERIZADO del aro caiga justo en la punta.
        const float arrowTipRatio = 0.6721f;
        string variantName = GetObjectVariantName(obj);
        if (variantName != null)
        {
            float haloOuterDiameter = rt.sizeDelta.x * arrowTipRatio / RingFillRatio;
            var haloGO = new GameObject("ColorHalo");
            haloGO.transform.SetParent(go.transform, false);
            var haloImg = haloGO.AddComponent<Image>();
            haloImg.raycastTarget = false;
            haloImg.sprite = GetRingSprite(RingRatioForThickness(haloOuterDiameter, ringLineThickness));
            haloImg.color  = VariantColor(variantName);
            var haloRt = haloGO.GetComponent<RectTransform>();
            haloRt.anchorMin = haloRt.anchorMax = new Vector2(0.5f, 0.5f);
            haloRt.pivot = new Vector2(0.5f, 0.5f);
            haloRt.anchoredPosition = Vector2.zero;
            haloRt.sizeDelta = new Vector2(haloOuterDiameter, haloOuterDiameter);
        }

        // Ring layer — tinted per object type. Usa el mismo sprite de aro generado
        // (no el aro recortado del icono AroIcon) para garantizar que su grosor
        // absoluto sea igual de fino que el aro de color interior.
        var ringGO = new GameObject("Ring");
        ringGO.transform.SetParent(go.transform, false);
        var ringImg = ringGO.AddComponent<Image>();
        ringImg.raycastTarget = false;
        ringImg.sprite = GetRingSprite(RingRatioForThickness(rt.sizeDelta.x, ringLineThickness));
        ringImg.color  = TypeColor(obj.type);
        var ringRt = ringGO.GetComponent<RectTransform>();
        ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
        ringRt.offsetMin = ringRt.offsetMax = Vector2.zero;

        // Arrow layer — always white
        var arrowGO = new GameObject("Arrow");
        arrowGO.transform.SetParent(go.transform, false);
        var arrowImg = arrowGO.AddComponent<Image>();
        arrowImg.raycastTarget = false;
        arrowImg.sprite = _markerArrowSprite;
        arrowImg.color  = Color.white;
        var arrowRt = arrowGO.GetComponent<RectTransform>();
        arrowRt.anchorMin = Vector2.zero; arrowRt.anchorMax = Vector2.one;
        arrowRt.offsetMin = arrowRt.offsetMax = Vector2.zero;

        markerGOs.Add(go);
    }

    static Sprite _markerRingSprite;
    static Sprite _markerArrowSprite;

    static void EnsureMarkerSprites()
    {
        if (_markerRingSprite != null) return;

        var src = Resources.Load<Texture2D>("Emoji/AroIcon");
        if (src == null) return;

        int W = src.width, H = src.height;
        var srcPx = src.GetPixels();

        var ringPx  = new Color[W * H];
        var arrowPx = new Color[W * H];

        for (int i = 0; i < srcPx.Length; i++)
        {
            var p = srcPx[i];
            if (p.a < 0.05f) continue;

            // Saturation: how colorful vs white/grey the pixel is
            float maxC = Mathf.Max(p.r, p.g, p.b);
            float minC = Mathf.Min(p.r, p.g, p.b);
            float sat  = maxC > 0.01f ? (maxC - minC) / maxC : 0f;

            if (sat > 0.25f)
                ringPx[i]  = new Color(1f, 1f, 1f, p.a); // colored pixel → ring (white for tinting)
            else
                arrowPx[i] = new Color(1f, 1f, 1f, p.a); // near-white pixel → arrow
        }

        var ringTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        ringTex.filterMode = FilterMode.Bilinear;
        ringTex.SetPixels(ringPx); ringTex.Apply();

        var arrowTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        arrowTex.filterMode = FilterMode.Bilinear;
        arrowTex.SetPixels(arrowPx); arrowTex.Apply();

        var r = new Rect(0, 0, W, H);
        var piv = new Vector2(0.5f, 0.5f);
        _markerRingSprite  = Sprite.Create(ringTex,  r, piv, 100f);
        _markerArrowSprite = Sprite.Create(arrowTex, r, piv, 100f);
    }

    // =========================================================================
    //  Selección de objetos colocados
    // =========================================================================

    int HitTestMarkers(Vector2 screenPos)
    {
        for (int i = 0; i < markerGOs.Count; i++)
        {
            if (!markerGOs[i]) continue;
            var rt = markerGOs[i].GetComponent<RectTransform>();
            if (rt == null) continue;
            // Se compara en el espacio LOCAL del propio marcador (igual que
            // ScreenToLatLon con mapAreaRt), no con el AABB de GetWorldCorners: los
            // marcadores se rotan según -obj.rotY, y el orden fijo de
            // GetWorldCorners (esquina inferior-izquierda/superior-derecha "de
            // fábrica" del rect) deja de ser un mínimo/máximo válido en cuanto la
            // rotación pasa de ~45°, dejando el objeto permanentemente imposible
            // de clicar (izquierda o derecha) a partir de ahí.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out Vector2 local);
            var r = rt.rect;
            const float margin = 6f; // igual margen que antes, ampliando el área de clic
            if (local.x >= r.xMin - margin && local.x <= r.xMax + margin &&
                local.y >= r.yMin - margin && local.y <= r.yMax + margin)
                return i;
        }
        return -1;
    }

    void SelectPlaced(int idx)
    {
        if (ctxMenu) { Destroy(ctxMenu); ctxMenu = null; }
        selectedPlacedIdx = idx;
        isMovingMarker = false;

        var obj = placed[idx];
        // El panel de propiedades visible y el tipo activo deben ser los de
        // ESTE objeto, no los del tipo que estuviera seleccionado en el
        // desplegable antes de hacer clic (si no, al seleccionar p. ej. una
        // Bandera con el desplegable en "Aro" seguía viéndose el panel/tipo
        // de Aro aunque el previsualizador y los datos internos sí fueran
        // los correctos).
        SwitchTypePanels(obj.type);
        if (obj.type == "Aro")
        {
            aroRotX = obj.rotX; aroRotY = obj.rotY; aroRotZ = obj.rotZ;
            aroDiameter = obj.diameter > 0 ? obj.diameter : 3f;
            aroGrosor   = obj.aroGrosor > 0f ? obj.aroGrosor : 1f;
            aroHueco    = obj.aroHueco;
            if (aroDiameterSlider) { aroDiameterSlider.SetValueWithoutNotify(aroDiameter); aroDiameterLabel.SetTextWithoutNotify(aroDiameter.ToString("F1")); }
            if (aroGrosorSlider)   { aroGrosorSlider.SetValueWithoutNotify(aroGrosor);     aroGrosorLabel.SetTextWithoutNotify(aroGrosor.ToString("F2")); }
            if (aroHuecoSlider)    { aroHuecoSlider.SetValueWithoutNotify(aroHueco);       aroHuecoLabel.SetTextWithoutNotify(aroHueco.ToString("F2")); }
            if (aroRotXSlider) { aroRotXSlider.SetValueWithoutNotify(aroRotX); aroRotXLabel.SetTextWithoutNotify(aroRotX.ToString("F0")); }
            if (aroRotYSlider) { aroRotYSlider.SetValueWithoutNotify(aroRotY); aroRotYLabel.SetTextWithoutNotify(aroRotY.ToString("F0")); }
            int foundVariantIdx = System.Array.IndexOf(AroVariantNames, obj.aroVariant);
            aroVariantIdx = foundVariantIdx >= 0 ? foundVariantIdx : 0;
            UpdateAroVariantLabel();
            // Recrea el previsualizador con el prefab de color correcto (Prefabs/Aro_<Variante>):
            // antes solo se reescalaba/rotaba la instancia YA existente, que se quedaba con el
            // color de lo último seleccionado en vez del color real de este objeto.
            SpawnPreviewObject("Aro");
            lastAroIconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
        else if (obj.type == "Bandera")
        {
            banderaHeight = obj.height;
            banderaSize   = obj.diameter > 0 ? obj.diameter : 1f;
            banderaRotX   = obj.rotX;
            banderaRotY   = obj.rotY;
            if (banderaHeightSlider) { banderaHeightSlider.SetValueWithoutNotify(banderaHeight); banderaHeightLabel.SetTextWithoutNotify(banderaHeight.ToString("F1")); }
            if (banderaSizeSlider)   { banderaSizeSlider.SetValueWithoutNotify(banderaSize);     banderaSizeLabel.SetTextWithoutNotify(banderaSize.ToString("F2")); }
            if (banderaRotXSlider)   { banderaRotXSlider.SetValueWithoutNotify(banderaRotX);     banderaRotXLabel.SetTextWithoutNotify(banderaRotX.ToString("F0")); }
            if (banderaRotYSlider)   { banderaRotYSlider.SetValueWithoutNotify(banderaRotY);     banderaRotYLabel.SetTextWithoutNotify(banderaRotY.ToString("F0")); }
            int foundBanderaVariantIdx = System.Array.IndexOf(BanderaVariantNames, obj.banderaVariant);
            banderaVariantIdx = foundBanderaVariantIdx >= 0 ? foundBanderaVariantIdx : 0;
            UpdateBanderaVariantLabel();
            SpawnPreviewObject("Bandera");
            lastBanderaIconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
        else if (obj.type == "Obstáculo")
        {
            obstaculoHeight = obj.height;
            obstaculoSize   = obj.diameter > 0 ? obj.diameter : 1f;
            obstaculoRotX   = obj.rotX;
            obstaculoRotY   = obj.rotY;
            obstaculoLongHoriz = obj.obstaculoLongHoriz > 0 ? obj.obstaculoLongHoriz : 3f;
            obstaculoLongVert  = obj.obstaculoLongVert  > 0 ? obj.obstaculoLongVert  : 3f;
            if (obstaculoHeightSlider) { obstaculoHeightSlider.SetValueWithoutNotify(obstaculoHeight); obstaculoHeightLabel.SetTextWithoutNotify(obstaculoHeight.ToString("F1")); }
            if (obstaculoSizeSlider)   { obstaculoSizeSlider.SetValueWithoutNotify(obstaculoSize);     obstaculoSizeLabel.SetTextWithoutNotify(obstaculoSize.ToString("F2")); }
            if (obstaculoRotXSlider)   { obstaculoRotXSlider.SetValueWithoutNotify(obstaculoRotX);     obstaculoRotXLabel.SetTextWithoutNotify(obstaculoRotX.ToString("F0")); }
            if (obstaculoRotYSlider)   { obstaculoRotYSlider.SetValueWithoutNotify(obstaculoRotY);     obstaculoRotYLabel.SetTextWithoutNotify(obstaculoRotY.ToString("F0")); }
            if (obstaculoLongHorizSlider) { obstaculoLongHorizSlider.SetValueWithoutNotify(obstaculoLongHoriz); obstaculoLongHorizLabel.SetTextWithoutNotify(obstaculoLongHoriz.ToString("F1")); }
            if (obstaculoLongVertSlider)  { obstaculoLongVertSlider.SetValueWithoutNotify(obstaculoLongVert);   obstaculoLongVertLabel.SetTextWithoutNotify(obstaculoLongVert.ToString("F1")); }
            int foundObstaculoVariantIdx = System.Array.IndexOf(ObstaculoVariantNames, obj.obstaculoVariant);
            obstaculoVariantIdx = foundObstaculoVariantIdx >= 0 ? foundObstaculoVariantIdx : 0;
            UpdateObstaculoVariantLabel();
            int foundObstaculoSupportVariantIdx = System.Array.IndexOf(ObstaculoVariantNames, obj.obstaculoSupportVariant);
            obstaculoSupportVariantIdx = foundObstaculoSupportVariantIdx >= 0 ? foundObstaculoSupportVariantIdx : 8;
            UpdateObstaculoSupportVariantLabel();
            SpawnPreviewObject("Obstáculo");
            lastObstaculoIconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
        else if (obj.type == "Moneda")
        {
            monedaHeight = obj.height;
            monedaSize   = obj.diameter > 0 ? obj.diameter : 1f;
            monedaRotX   = obj.rotX;
            monedaRotY   = obj.rotY;
            if (monedaHeightSlider) { monedaHeightSlider.SetValueWithoutNotify(monedaHeight); monedaHeightLabel.SetTextWithoutNotify(monedaHeight.ToString("F1")); }
            if (monedaSizeSlider)   { monedaSizeSlider.SetValueWithoutNotify(monedaSize);     monedaSizeLabel.SetTextWithoutNotify(monedaSize.ToString("F1")); }
            if (monedaRotXSlider)   { monedaRotXSlider.SetValueWithoutNotify(monedaRotX);     monedaRotXLabel.SetTextWithoutNotify(monedaRotX.ToString("F0")); }
            if (monedaRotYSlider)   { monedaRotYSlider.SetValueWithoutNotify(monedaRotY);     monedaRotYLabel.SetTextWithoutNotify(monedaRotY.ToString("F0")); }
            int foundMonedaVariantIdx = System.Array.IndexOf(MonedaVariantNames, obj.monedaVariant);
            monedaVariantIdx = foundMonedaVariantIdx >= 0 ? foundMonedaVariantIdx : 0;
            UpdateMonedaVariantLabel();
            SpawnPreviewObject("Moneda");
            lastMonedaIconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
        else if (obj.type == "Arco")
        {
            arcoHeight = obj.height;
            arcoSize   = obj.diameter > 0 ? obj.diameter : 1f;
            arcoRotX   = obj.rotX;
            arcoRotY   = obj.rotY;
            arcoLongHoriz = obj.arcoLongHoriz > 0 ? obj.arcoLongHoriz : 3f;
            arcoLongVert  = obj.arcoLongVert  > 0 ? obj.arcoLongVert  : 3f;
            if (arcoHeightSlider) { arcoHeightSlider.SetValueWithoutNotify(arcoHeight); arcoHeightLabel.SetTextWithoutNotify(arcoHeight.ToString("F1")); }
            if (arcoSizeSlider)   { arcoSizeSlider.SetValueWithoutNotify(arcoSize);     arcoSizeLabel.SetTextWithoutNotify(arcoSize.ToString("F2")); }
            if (arcoRotXSlider)   { arcoRotXSlider.SetValueWithoutNotify(arcoRotX);     arcoRotXLabel.SetTextWithoutNotify(arcoRotX.ToString("F0")); }
            if (arcoRotYSlider)   { arcoRotYSlider.SetValueWithoutNotify(arcoRotY);     arcoRotYLabel.SetTextWithoutNotify(arcoRotY.ToString("F0")); }
            if (arcoLongHorizSlider) { arcoLongHorizSlider.SetValueWithoutNotify(arcoLongHoriz); arcoLongHorizLabel.SetTextWithoutNotify(arcoLongHoriz.ToString("F1")); }
            if (arcoLongVertSlider)  { arcoLongVertSlider.SetValueWithoutNotify(arcoLongVert);   arcoLongVertLabel.SetTextWithoutNotify(arcoLongVert.ToString("F1")); }
            int foundArcoVariantIdx = System.Array.IndexOf(ArcoVariantNames, obj.arcoVariant);
            arcoVariantIdx = foundArcoVariantIdx >= 0 ? foundArcoVariantIdx : 0;
            UpdateArcoVariantLabel();
            SpawnPreviewObject("Arco");
            lastArcoIconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
        else if (obj.type == "Segundo Arco")
        {
            arco2Height = obj.height;
            arco2Size   = obj.diameter > 0 ? obj.diameter : 1f;
            arco2RotX   = obj.rotX;
            arco2RotY   = obj.rotY;
            arco2LongHoriz = obj.arco2LongHoriz > 0 ? obj.arco2LongHoriz : 3f;
            arco2LongVert  = obj.arco2LongVert  > 0 ? obj.arco2LongVert  : 3f;
            if (arco2HeightSlider) { arco2HeightSlider.SetValueWithoutNotify(arco2Height); arco2HeightLabel.SetTextWithoutNotify(arco2Height.ToString("F1")); }
            if (arco2SizeSlider)   { arco2SizeSlider.SetValueWithoutNotify(arco2Size);     arco2SizeLabel.SetTextWithoutNotify(arco2Size.ToString("F2")); }
            if (arco2RotXSlider)   { arco2RotXSlider.SetValueWithoutNotify(arco2RotX);     arco2RotXLabel.SetTextWithoutNotify(arco2RotX.ToString("F0")); }
            if (arco2RotYSlider)   { arco2RotYSlider.SetValueWithoutNotify(arco2RotY);     arco2RotYLabel.SetTextWithoutNotify(arco2RotY.ToString("F0")); }
            if (arco2LongHorizSlider) { arco2LongHorizSlider.SetValueWithoutNotify(arco2LongHoriz); arco2LongHorizLabel.SetTextWithoutNotify(arco2LongHoriz.ToString("F1")); }
            if (arco2LongVertSlider)  { arco2LongVertSlider.SetValueWithoutNotify(arco2LongVert);   arco2LongVertLabel.SetTextWithoutNotify(arco2LongVert.ToString("F1")); }
            int foundArco2VariantIdx = System.Array.IndexOf(Arco2VariantNames, obj.arco2Variant);
            arco2VariantIdx = foundArco2VariantIdx >= 0 ? foundArco2VariantIdx : 0;
            UpdateArco2VariantLabel();
            SpawnPreviewObject("Segundo Arco");
            lastArco2IconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
        else if (obj.type == "Flecha")
        {
            flechaHeight = obj.height;
            flechaSize   = obj.diameter > 0 ? obj.diameter : 1f;
            flechaRotX   = obj.rotX;
            flechaRotY   = obj.rotY;
            flechaGrosor = obj.flechaGrosor > 0f ? obj.flechaGrosor : 1f;
            flechaLongHoriz = obj.flechaLongHoriz > 0f ? obj.flechaLongHoriz : 3f;
            if (flechaHeightSlider) { flechaHeightSlider.SetValueWithoutNotify(flechaHeight); flechaHeightLabel.SetTextWithoutNotify(flechaHeight.ToString("F1")); }
            if (flechaSizeSlider)   { flechaSizeSlider.SetValueWithoutNotify(flechaSize);     flechaSizeLabel.SetTextWithoutNotify(flechaSize.ToString("F1")); }
            if (flechaGrosorSlider) { flechaGrosorSlider.SetValueWithoutNotify(flechaGrosor); flechaGrosorLabel.SetTextWithoutNotify(flechaGrosor.ToString("F2")); }
            if (flechaLongHorizSlider) { flechaLongHorizSlider.SetValueWithoutNotify(flechaLongHoriz); flechaLongHorizLabel.SetTextWithoutNotify(flechaLongHoriz.ToString("F1")); }
            if (flechaRotXSlider)   { flechaRotXSlider.SetValueWithoutNotify(flechaRotX);     flechaRotXLabel.SetTextWithoutNotify(flechaRotX.ToString("F0")); }
            if (flechaRotYSlider)   { flechaRotYSlider.SetValueWithoutNotify(flechaRotY);     flechaRotYLabel.SetTextWithoutNotify(flechaRotY.ToString("F0")); }
            int foundFlechaVariantIdx = System.Array.IndexOf(FlechaVariantNames, obj.flechaVariant);
            flechaVariantIdx = foundFlechaVariantIdx >= 0 ? foundFlechaVariantIdx : 0;
            UpdateFlechaVariantLabel();
            SpawnPreviewObject("Flecha");
            lastFlechaIconRt = markerGOs[idx].GetComponent<RectTransform>();
        }
 // El menú de Mover/Borrar ya no se abre aquí ahora lo dispara el
        // clic derecho específicamente (ver más abajo), para que el
        // izquierdo se quede libre para seleccionar/colocar sin que salte
        // el menú de golpe.
    }

    void DeselectPlaced()
    {
        selectedPlacedIdx = -1;
        isMovingMarker = false;
        movMarkerBtn = null;
        if (ctxMenu) { Destroy(ctxMenu); ctxMenu = null; }
    }

    // Menú de un solo botón ("Copiar" sobre la línea de grupo, o "Pegar"
    // sobre una zona vacía si ya hay algo copiado) — mismo estilo/patrón que
    // BuildContextMenu pero con un único botón de ancho completo.
    void BuildSingleActionContextMenu(Vector2 screenPos, string label, Action onClick)
    {
        if (ctxMenu) Destroy(ctxMenu);

        Vector2 pos = ScreenToMarkerPos(screenPos);

        ctxMenu = new GameObject("CtxMenu_" + label);
        ctxMenu.transform.SetParent(markersParent, false);

        var menuRt = ctxMenu.AddComponent<RectTransform>();
        menuRt.anchorMin = menuRt.anchorMax = new Vector2(0.5f, 0.5f);
        menuRt.pivot = new Vector2(0f, 1f);
        menuRt.anchoredPosition = pos + new Vector2(10f, 10f);
        menuRt.sizeDelta = new Vector2(90f, 34f);

        ctxMenu.AddComponent<Image>().color = new Color(0.06f, 0.12f, 0.22f, 0.96f);

        var btn = MakeLBtn(ctxMenu.transform, label, new Color(0.06f, 0.18f, 0.35f), new Color(0.4f, 0.85f, 1f), 9f);
        var btnRt = btn.GetComponent<RectTransform>();
        btnRt.anchorMin = Vector2.zero; btnRt.anchorMax = Vector2.one;
        btnRt.offsetMin = new Vector2(5f, 5f); btnRt.offsetMax = new Vector2(-5f, -5f);
        btn.onClick.AddListener(() =>
        {
            onClick();
            if (ctxMenu) { Destroy(ctxMenu); ctxMenu = null; }
        });
    }

    void BuildLineContextMenu(Vector2 screenPos)
    {
        if (ctxMenu) Destroy(ctxMenu);

        Vector2 pos = ScreenToMarkerPos(screenPos);

        ctxMenu = new GameObject("CtxMenu_Line");
        ctxMenu.transform.SetParent(markersParent, false);

        var menuRt = ctxMenu.AddComponent<RectTransform>();
        menuRt.anchorMin = menuRt.anchorMax = new Vector2(0.5f, 0.5f);
        menuRt.pivot = new Vector2(0f, 1f);
        menuRt.anchoredPosition = pos + new Vector2(10f, 10f);
        menuRt.sizeDelta = new Vector2(150f, 34f);

        ctxMenu.AddComponent<Image>().color = new Color(0.06f, 0.12f, 0.22f, 0.96f);

        var copyBtn = MakeLBtn(ctxMenu.transform, "Copiar", new Color(0.06f, 0.18f, 0.35f), new Color(0.4f, 0.85f, 1f), 9f);
        var copyRt = copyBtn.GetComponent<RectTransform>();
        copyRt.anchorMin = new Vector2(0f, 0f); copyRt.anchorMax = new Vector2(0.5f, 1f);
        copyRt.offsetMin = new Vector2(5f, 5f); copyRt.offsetMax = new Vector2(-2f, -5f);
        copyBtn.onClick.AddListener(() =>
        {
            CopyGroupToClipboard();
            if (ctxMenu) { Destroy(ctxMenu); ctxMenu = null; }
        });

        var delBtn = MakeLBtn(ctxMenu.transform, "Eliminar", new Color(0.55f, 0.08f, 0.08f), Color.white, 9f);
        var delRt = delBtn.GetComponent<RectTransform>();
        delRt.anchorMin = new Vector2(0.5f, 0f); delRt.anchorMax = new Vector2(1f, 1f);
        delRt.offsetMin = new Vector2(2f, 5f); delRt.offsetMax = new Vector2(-5f, -5f);
        delBtn.onClick.AddListener(() =>
        {
            DeleteGroupFromMap();
            if (ctxMenu) { Destroy(ctxMenu); ctxMenu = null; }
        });
    }

    void BuildPasteContextMenu(Vector2 screenPos) =>
        BuildSingleActionContextMenu(screenPos, "Pegar", () => PasteGroupFromClipboard(screenPos));

    void BuildContextMenu(int idx)
    {
        if (ctxMenu) Destroy(ctxMenu);
        var obj = placed[idx];
        var markerRt = markerGOs[idx].GetComponent<RectTransform>();

        ctxMenu = new GameObject("CtxMenu");
        ctxMenu.transform.SetParent(markersParent, false);

        var menuRt = ctxMenu.AddComponent<RectTransform>();
        menuRt.anchorMin = menuRt.anchorMax = new Vector2(0.5f, 0.5f);
        menuRt.pivot = new Vector2(0f, 1f);
        menuRt.anchoredPosition = markerRt.anchoredPosition + new Vector2(18f, 4f);
        menuRt.sizeDelta = new Vector2(100f, 88f);

        ctxMenu.AddComponent<Image>().color = new Color(0.06f, 0.12f, 0.22f, 0.96f);

        // Título
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(ctxMenu.transform, false);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        titleTmp.text = obj.type;
        titleTmp.fontSize = 9f; titleTmp.color = new Color(0.4f, 0.85f, 1f);
        titleTmp.fontStyle = FontStyles.Bold; titleTmp.alignment = TextAlignmentOptions.Left;
        titleTmp.raycastTarget = false;
        var titleRt = titleGO.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f); titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.anchoredPosition = new Vector2(6f, -4f); titleRt.sizeDelta = new Vector2(-26f, 14f);

        // Color del objeto (cuadradito junto al título)
        string ctxVariantName = GetObjectVariantName(obj);
        if (ctxVariantName != null)
        {
            var ctxSwGO = new GameObject("ColorSwatch");
            ctxSwGO.transform.SetParent(ctxMenu.transform, false);
            var ctxSwImg = ctxSwGO.AddComponent<Image>();
            ctxSwImg.color = VariantColor(ctxVariantName);
            ctxSwImg.raycastTarget = false;
            var ctxSwRt = ctxSwImg.GetComponent<RectTransform>();
            ctxSwRt.anchorMin = ctxSwRt.anchorMax = new Vector2(1f, 1f);
            ctxSwRt.pivot = new Vector2(1f, 1f);
            ctxSwRt.anchoredPosition = new Vector2(-6f, -5f);
            ctxSwRt.sizeDelta = new Vector2(10f, 10f);
        }

        // Tres botones apilados uno debajo del otro, delgados (en vez de dos a
        // media anchura como antes) — Copiar, Borrar, Mover en ese orden.
        const float ROW_H = 16f, ROW_STEP = 18f, ROW_TOP = 20f;

        // Botón Copiar — mismo mecanismo de portapapeles que ya usa la línea de
        // grupo (lineClipboard/PasteGroupFromClipboard): un solo objeto es,
        // simplemente, un "grupo" de un elemento con desplazamiento cero.
        var copyBtn = MakeLBtn(ctxMenu.transform, "Copiar", new Color(0.06f, 0.18f, 0.35f), new Color(0.4f, 0.85f, 1f), 8f);
        var copyRt = copyBtn.GetComponent<RectTransform>();
        copyRt.anchorMin = new Vector2(0f, 1f); copyRt.anchorMax = new Vector2(1f, 1f);
        copyRt.pivot = new Vector2(0.5f, 1f);
        copyRt.anchoredPosition = new Vector2(0f, -ROW_TOP); copyRt.sizeDelta = new Vector2(-10f, ROW_H);
        copyBtn.onClick.AddListener(() =>
        {
            CopySingleToClipboard(idx);
            if (ctxMenu) { Destroy(ctxMenu); ctxMenu = null; }
        });

        // Botón Borrar
        var delBtn = MakeLBtn(ctxMenu.transform, "Borrar", new Color(0.55f, 0.08f, 0.08f), Color.white, 8f);
        var delRt = delBtn.GetComponent<RectTransform>();
        delRt.anchorMin = new Vector2(0f, 1f); delRt.anchorMax = new Vector2(1f, 1f);
        delRt.pivot = new Vector2(0.5f, 1f);
        delRt.anchoredPosition = new Vector2(0f, -(ROW_TOP + ROW_STEP)); delRt.sizeDelta = new Vector2(-10f, ROW_H);
        delBtn.onClick.AddListener(() =>
        {
            RemovePlacedAt(selectedPlacedIdx);
            DeselectPlaced();
        });

        // Botón Mover
        var movBtn = MakeLBtn(ctxMenu.transform, "Mover", new Color(0.06f, 0.18f, 0.35f), new Color(0.4f, 0.85f, 1f), 8f);
        var movRt = movBtn.GetComponent<RectTransform>();
        movRt.anchorMin = new Vector2(0f, 1f); movRt.anchorMax = new Vector2(1f, 1f);
        movRt.pivot = new Vector2(0.5f, 1f);
        movRt.anchoredPosition = new Vector2(0f, -(ROW_TOP + ROW_STEP * 2f)); movRt.sizeDelta = new Vector2(-10f, ROW_H);
        movMarkerBtn = movBtn;
        movBtn.onClick.AddListener(() =>
        {
            isMovingMarker = !isMovingMarker;
            var cb = movBtn.colors;
            cb.normalColor = isMovingMarker ? new Color(0.05f, 0.40f, 0.20f) : new Color(0.06f, 0.18f, 0.35f);
            movBtn.colors = cb;
        });
    }

    void MoveSelectedMarker(Vector2 screenPos)
    {
        if (!ScreenToLatLon(screenPos, out double lat, out double lon)) return;

        float rawW = GRID_W * TILE_PIX, rawH = GRID_H * TILE_PIX;
        float areaW = mapAreaRt.rect.width, areaH = mapAreaRt.rect.height;
        float scale = Mathf.Max(areaW / rawW, areaH / rawH);
        float ox = (areaW - rawW * scale) / 2f, oy = (areaH - rawH * scale) / 2f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(mapAreaRt, screenPos, null, out Vector2 local);
        float px = (local.x + areaW / 2f - ox) / scale;
        float py = (areaH / 2f - local.y - oy) / scale;
        var newPos = new Vector2(px - rawW / 2f, -(py - rawH / 2f));

        var snap = TrySnap(newPos, placed[selectedPlacedIdx].rotY, excludeIdx: selectedPlacedIdx);
        newPos = snap.pos;
        ShowTransientGuide(snap, ref newPos, selectedPlacedIdx);
        (lat, lon) = MarkerPosToLatLon(newPos);

        placed[selectedPlacedIdx].lat = (float)lat;
        placed[selectedPlacedIdx].lon = (float)lon;

        var markerRt = markerGOs[selectedPlacedIdx].GetComponent<RectTransform>();
        markerRt.anchoredPosition = newPos;

        if (snap.hasRotSnap)
        {
            placed[selectedPlacedIdx].rotY = snap.rotY;
            markerRt.localEulerAngles = new Vector3(0f, 0f, -snap.rotY);
            RefreshTypeRotY(placed[selectedPlacedIdx].type, snap.rotY);
        }

        if (ctxMenu)
            ctxMenu.GetComponent<RectTransform>().anchoredPosition = newPos + new Vector2(18f, 4f);
    }

    void RedrawMarkers()
    {
        DeselectPlaced();
        foreach (var g in markerGOs) if (g) Destroy(g);
        markerGOs.Clear();
        SpawnLocationMarker();

        float rawW = GRID_W * TILE_PIX;
        float rawH = GRID_H * TILE_PIX;
        foreach (var obj in placed)
        {
            double tx = LonToTileX(obj.lon, zoom);
            double ty = LatToTileY(obj.lat, zoom);
            float cx  = (float)((tx - startTileX) * TILE_PIX - rawW / 2.0);
            float cy  = -(float)((ty - startTileY) * TILE_PIX - rawH / 2.0);
            SpawnMarker(cx, cy, obj);
        }
    }

    // =========================================================================
    //  Tiles
    // =========================================================================

    void RefreshTiles(bool resetColors = false)
    {
        double fx = LonToTileX(centerLon, zoom);
        double fy = LatToTileY(centerLat, zoom);
        startTileX = (int)Math.Floor(fx - GRID_W / 2.0);
        startTileY = (int)Math.Floor(fy - GRID_H / 2.0);

        for (int tx = 0; tx < GRID_W; tx++)
        for (int ty = 0; ty < GRID_H; ty++)
        {
            if (resetColors && tiles[tx, ty] != null)
            {
                // Destruir la textura descargada antes de soltar la referencia:
                // si no, el Texture2D queda huérfano en memoria de vídeo (Unity
                // no lo libera solo porque ya no haya ninguna referencia en C#)
                // y se va acumulando en cada pan/zoom hasta agotar la memoria.
                if (tiles[tx, ty].texture != null) Destroy(tiles[tx, ty].texture);
                tiles[tx, ty].texture = null;
                tiles[tx, ty].color   = new Color(0.25f, 0.28f, 0.35f, 1f);
            }
            StartCoroutine(FetchTile(startTileX + tx, startTileY + ty, tx, ty));
        }
    }

    IEnumerator FetchTile(int tileX, int tileY, int gx, int gy)
    {
        int max  = 1 << zoom;
        tileX    = ((tileX % max) + max) % max;
        tileY    = Mathf.Clamp(tileY, 0, max - 1);

        // Zoom "artificial" más allá de MAX_REAL_ZOOM: en vez de pedir un nivel
        // que el proveedor no tiene (falla o no da más detalle en muchas zonas,
        // p. ej. la del DronLab), se pide la tesela real de MAX_REAL_ZOOM que
        // contiene este punto y se recorta/estira (uvRect) el trozo que le
        // corresponde para simular el acercamiento.
        int fetchZoom = Mathf.Min(zoom, MAX_REAL_ZOOM);
        int zoomDiff  = zoom - fetchZoom;
        int realTileX = tileX >> zoomDiff;
        int realTileY = tileY >> zoomDiff;

        string url = providers[providerIdx].url
            .Replace("{z}", fetchZoom.ToString())
            .Replace("{x}", realTileX.ToString())
            .Replace("{y}", realTileY.ToString());
        var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success &&
            tiles != null && tiles[gx, gy] != null)
        {
            // Igual que en RefreshTiles: destruir la textura anterior de este
            // tile antes de reemplazarla, para no dejarla huérfana en memoria.
            if (tiles[gx, gy].texture != null) Destroy(tiles[gx, gy].texture);
            tiles[gx, gy].texture = DownloadHandlerTexture.GetContent(req);
            tiles[gx, gy].color   = Color.white;

            if (zoomDiff > 0)
            {
                // Sub-celda dentro de la tesela real que corresponde a este tile
                // virtual: subX/subY son los bits bajos descartados por el >>
                // de arriba. V invertida porque en UV de Unity 0 = abajo,
                // mientras que en coordenadas de tesela 0 = arriba.
                int n    = 1 << zoomDiff;
                int subX = tileX & (n - 1);
                int subY = tileY & (n - 1);
                float size = 1f / n;
                tiles[gx, gy].uvRect = new Rect(subX * size, 1f - (subY + 1) * size, size, size);
            }
            else
            {
                // Zoom real: sin recorte. Necesario resetearlo explícitamente,
                // si no un tile que antes estuvo en zoom artificial se queda
                // con el uvRect recortado de antes al volver a alejar.
                tiles[gx, gy].uvRect = new Rect(0f, 0f, 1f, 1f);
            }
        }
        req.Dispose();
    }

    // =========================================================================
    //  Geolocalización
    // =========================================================================

    void LocateMe() { StartCoroutine(LocateMeCoroutine()); }

    double _foundLat, _foundLon;
    bool   _locationFound;

    IEnumerator LocateMeCoroutine()
    {
        SetLocBtnText("Buscando ubicación...");
        _locationFound = false;

        // Se salta el GPS del dispositivo y va directo a geolocalización por IP.
        // Se prueban los servicios EN ORDEN (no en paralelo): antes se lanzaban
        // los 4 a la vez y se quedaba con el que respondiera primero, lo cual
        // daba resultados poco fiables/aleatorios. ip-api.com suele ser el más
        // preciso de los cuatro, así que va primero; solo se prueba el
        // siguiente si el anterior falla o no da un resultado válido.
        string[] urls    = { "http://ip-api.com/json/", "https://ipwho.is/", "https://freeipapi.com/api/json", "https://ipapi.co/json/" };
        string[] latKeys = { "lat", "latitude", "latitude", "latitude" };
        string[] lonKeys = { "lon", "longitude", "longitude", "longitude" };
        for (int i = 0; i < urls.Length && !_locationFound; i++)
            yield return StartCoroutine(TryGeoService(urls[i], latKeys[i], lonKeys[i]));

        if (_locationFound)
        {
            // La geolocalización por IP resuelve esta conexión a un punto de la
            // red del ISP a decenas de km de la ubicación real, así que un
            // pequeño desvío fijo no basta para corregirlo. En su lugar se usa
            // directamente la ubicación real conocida (Vilanova i la Geltrú,
            // 41°13'00.34"N 1°43'51.32"E), ignorando la lat/lon que devuelva
            // el servicio de turno.
            centerLat = 41.216761;
            centerLon = 1.730922;
            ApplyNewCenter(13);
        }
        else
        { SetLocBtnText("No se pudo detectar la ubicación"); }
    }

#if UNITY_ANDROID || UNITY_IOS
    IEnumerator TryDeviceLocation()
    {
        SetLocBtnText("Solicitando acceso a ubicación...");

#if UNITY_ANDROID
        // Android 6+: pedir permiso en tiempo de ejecución si todavía no está concedido
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.FineLocation))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.FineLocation);

            var permTick = new WaitForSeconds(0.3f);
            float permWait = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                       UnityEngine.Android.Permission.FineLocation) && permWait < 12f)
            { yield return permTick; permWait += 0.3f; }
        }

        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.FineLocation))
            yield break; // usuario denegó el permiso
#endif
        // En iOS, Input.location.Start() dispara el diálogo de permisos automáticamente

        Input.location.Start(10f, 10f);

        var tick = new WaitForSeconds(0.25f);
        float waited = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && waited < 10f)
        { yield return tick; waited += 0.25f; }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            _foundLat = Input.location.lastData.latitude;
            _foundLon = Input.location.lastData.longitude;
            _locationFound = true;
        }

        Input.location.Stop();
    }
#endif // UNITY_ANDROID || UNITY_IOS

    IEnumerator TryGeoService(string url, string latKey, string lonKey)
    {
        var req = UnityWebRequest.Get(url);
        req.timeout = 9;
        req.SetRequestHeader("User-Agent", "Mozilla/5.0");
        yield return req.SendWebRequest();
        if (!_locationFound && req.result == UnityWebRequest.Result.Success)
        {
            double la = ParseJsonDouble(req.downloadHandler.text, latKey);
            double lo = ParseJsonDouble(req.downloadHandler.text, lonKey);
            if (la >= -90.0 && la <= 90.0 && (la != 0.0 || lo != 0.0))
            { _foundLat = la; _foundLon = lo; _locationFound = true; }
        }
        req.Dispose();
    }

    void ApplyNewCenter(int newZoom)
    {
        zoom = newZoom;
 if (zoomLabel) zoomLabel.text = $"Zoom: {zoom} (rueda del ratón o botones +/ˆ)";
        locationLat = centerLat;
        locationLon = centerLon;
        RefreshTiles(true);
        RedrawMarkers();
        SetLocBtnText("Ubicación encontrada");
    }

    void SpawnLocationMarker()
    {
        if (markersParent == null || double.IsNaN(locationLat)) return;

        if (locationMarker != null) Destroy(locationMarker);

        float rawW = GRID_W * TILE_PIX;
        float rawH = GRID_H * TILE_PIX;
        double tx  = LonToTileX(locationLon, zoom);
        double ty  = LatToTileY(locationLat, zoom);
        float cx   = (float)((tx - startTileX) * TILE_PIX - rawW / 2.0);
        float cy   = -(float)((ty - startTileY) * TILE_PIX - rawH / 2.0);

        locationMarker = new GameObject("LocationPin");
        locationMarker.transform.SetParent(markersParent, false);
        var pinRt = locationMarker.AddComponent<RectTransform>();
        pinRt.anchorMin = pinRt.anchorMax = new Vector2(0.5f, 0.5f);
        pinRt.pivot     = new Vector2(0.5f, 0f);
        pinRt.anchoredPosition = new Vector2(cx, cy);
        pinRt.sizeDelta = Vector2.zero; // contenedor vacío

        var tex = Resources.Load<Texture2D>("Emoji/ubi");
        Sprite pinSprite = tex != null
            ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f)
            : null;

 // Capa 1 sombra oscura desplazada (efecto profundidad)
        void MakeLayer(Vector2 offset, Color col, Vector2 size, int sibling)
        {
            var go  = new GameObject("Layer");
            go.transform.SetParent(locationMarker.transform, false);
            go.transform.SetSiblingIndex(sibling);
            var img = go.AddComponent<Image>();
            img.color = col; img.raycastTarget = false;
            if (pinSprite != null) img.sprite = pinSprite;
            var rt  = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            // Pivote abajo (no al centro): la imagen "ubi" tiene la punta en su
            // borde inferior, y el contenedor padre (pinRt) también pivota desde
            // abajo — así la punta, no el centro del icono, queda sobre las
            // coordenadas exactas de la ubicación.
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        // El sprite "ubi" es prácticamente cuadrado (981x980); usar tamaños
        // cuadrados aquí evita estirarlo. La capa rosa pálida de antes
        // cubría casi toda la forma y diluía el rojo, así que se queda
        // pequeña y discreta, solo como un toque de brillo.
        MakeLayer(new Vector2(2f, -3f),   new Color(0.30f, 0.00f, 0.00f, 0.80f), new Vector2(28f, 28f), 0);
        MakeLayer(new Vector2(1f, -1.5f), new Color(0.65f, 0.00f, 0.00f, 0.90f), new Vector2(28f, 28f), 1);
        MakeLayer(Vector2.zero,           new Color(1.00f, 0.00f, 0.00f, 1.00f), new Vector2(28f, 28f), 2);
        MakeLayer(new Vector2(-2f, 3f),   new Color(1.00f, 0.55f, 0.55f, 0.45f), new Vector2(10f, 10f), 3);
    }

    static double ParseJsonDouble(string json, string key)
    {
        int idx = json.IndexOf($"\"{key}\"");
        if (idx < 0) return 0.0;
        idx += key.Length + 2;
        while (idx < json.Length && (json[idx] == ':' || json[idx] == ' ')) idx++;
        int end = idx;
        while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
        if (double.TryParse(json.Substring(idx, end - idx).Trim(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            return val;
        return 0.0;
    }

    // =========================================================================
    //  Acciones finales
    // =========================================================================

    void OnDestroy()
    {
        if (previewRT) { previewRT.Release(); Destroy(previewRT); }
        if (previewScene.IsValid())
            SceneManager.UnloadSceneAsync(previewScene);

        // Las texturas de teselas descargadas (ver FetchTile/RefreshTiles) son
        // Texture2D sueltos, no hijos del GameObject del RawImage: destruir el
        // Canvas no las libera solo. Sin esto, cada apertura del editor dejaba
        // hasta GRID_W*GRID_H texturas huérfanas en memoria de vídeo.
        if (tiles != null)
            foreach (var t in tiles)
                if (t != null && t.texture != null) Destroy(t.texture);
    }

    void DoCancel() { onCancel?.Invoke(); Destroy(ownCanvas); }

    void DoCreate()
    {
        string name = inputName != null ? inputName.text.Trim() : "";
        if (string.IsNullOrEmpty(name))
            name = "Misión " + DateTime.Now.ToString("HH:mm");
        var lineGroups = new List<LineGroup>();
        foreach (var (indices, dir) in settledGroups)
        {
            if (indices.Count < 2) continue;
            lineGroups.Add(new LineGroup { indices = new List<int>(indices), dir = dir });
        }
        onCreate?.Invoke(placed, name, centerLat, centerLon, geofenceEnabled, lineGroups);
        Destroy(ownCanvas);
    }

    // =========================================================================
    //  Tile math (OSM / Web Mercator)
    // =========================================================================

    static double LonToTileX(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);

    static double LatToTileY(double lat, int z)
    {
        double r = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1 << z);
    }

    static double TileXToLon(double x, int z) => x / (1 << z) * 360.0 - 180.0;

    static double TileYToLat(double y, int z)
    {
        double n = Math.PI - 2.0 * Math.PI * y / (1 << z);
        return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
    }

    // =========================================================================
    //  Colores por tipo
    // =========================================================================

    // internal: la usa también ScenarioSceneCreator para que el escenario
    // generado cargue el mismo prefab de color que se eligió en el previsualizador.
    internal static string GetObjectVariantName(MapPlacedObject obj) => obj.type switch
    {
        "Aro"          => obj.aroVariant,
        "Bandera"      => obj.banderaVariant,
        "Obstáculo"    => obj.obstaculoVariant,
        "Moneda"       => obj.monedaVariant,
        "Arco"         => obj.arcoVariant,
        "Segundo Arco" => obj.arco2Variant,
        "Flecha"       => obj.flechaVariant,
        _              => null
    };

    // Actualiza el color del icono ya colocado en el mapa (el halo interior,
    // ver SpawnMarker) sin recrear el marcador entero — se llama al cambiar
    // el color de un objeto ya puesto desde el selector de variante, para que
    // el icono deje de mostrar el color viejo.
    void RefreshMarkerColor(int idx)
    {
        if (idx < 0 || idx >= markerGOs.Count || markerGOs[idx] == null) return;
        string variantName = GetObjectVariantName(placed[idx]);
        if (variantName == null) return;
        var halo = markerGOs[idx].transform.Find("ColorHalo");
        if (halo != null)
        {
            var img = halo.GetComponent<Image>();
            if (img != null) img.color = VariantColor(variantName);
        }
    }

    static Color TypeColor(string t) => t switch
    {
        "Aro"       => new Color(1.00f, 0.15f, 0.10f),
        "Bandera"   => new Color(0.00f, 0.00f, 0.00f),
        "Obstáculo" => new Color(0.20f, 0.85f, 0.20f),
        "Moneda"    => new Color(1.00f, 0.80f, 0.10f),
        "Arco"      => new Color(0.60f, 0.30f, 1.00f),
        "Segundo Arco"    => new Color(0.30f, 0.60f, 1.00f),
        "Flecha"    => new Color(0.50f, 0.50f, 0.50f),
        _        => new Color(0.00f, 0.95f, 1.00f),
    };

    // =========================================================================
    //  UI helpers
    // =========================================================================

    static void AddET(EventTrigger et, EventTriggerType type,
        UnityEngine.Events.UnityAction<BaseEventData> cb)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(cb);
        et.triggers.Add(e);
    }

    static TextMeshProUGUI AddLabel(Transform parent, string text, float size,
        Color color, FontStyles style, float height)
    {
        var go  = new GameObject("L");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.fontStyle = style; tmp.alignment = TextAlignmentOptions.Left;
        tmp.raycastTarget = false;
        SetLE(go, height);
        return tmp;
    }

    static float WrapAngle(float a) { a %= 360f; if (a > 180f) a -= 360f; else if (a < -180f) a += 360f; return a; }

    static void AddSep(Transform parent)
    {
        var go = new GameObject("Sep");
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.7f);
        SetLE(go, 1f);
    }

    static void SetLE(GameObject go, float h)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt) rt.sizeDelta = new Vector2(0f, h);
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h;
    }

    static GameObject NewHRow(Transform parent, float height)
    {
        var go  = new GameObject("Row");
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 6f;
        hlg.childAlignment        = TextAnchor.MiddleCenter;
        hlg.childControlWidth     = true;
        hlg.childControlHeight    = false;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = false;
        SetLE(go, height);
        return go;
    }

    static Button MakeLBtn(Transform parent, string label, Color fill, Color textColor, float fontSize = 11f)
    {
        var go  = new GameObject("B_" + label);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>(); img.color = fill;
        var rt  = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(0f, 32f);
        var le  = go.AddComponent<LayoutElement>(); le.minHeight = 32f; le.preferredHeight = 32f;

        var tgo = new GameObject("Txt"); tgo.transform.SetParent(go.transform, false);
        var tmp = tgo.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = fontSize; tmp.fontStyle = FontStyles.Bold;
        tmp.color = textColor; tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        var trt = tgo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        var btn = go.AddComponent<Button>();
        var cb  = btn.colors;
        cb.normalColor      = fill;
        cb.highlightedColor = Color.Lerp(fill, Color.white, 0.2f);
        cb.pressedColor     = Color.Lerp(fill, Color.black, 0.2f);
        btn.colors = cb;
        return btn;
    }

    // Pin de mapa (cabeza redonda + punta inferior + agujero), construido con
    // primitivas de UI (sin sprites) imitando el icono clásico de ubicación.
    // Cambia el texto del botón de ubicación y reubica el icono junto a él,
    // porque los distintos mensajes ("Usar mi ubicación", "Ubicación
    // encontrada", "Buscando ubicación...") tienen anchos distintos y el
    // texto va centrado.
    void SetLocBtnText(string text)
    {
        if (locateBtnLabel == null) return;
        locateBtnLabel.text = text;
        RepositionLocationIcon();
    }

    void RepositionLocationIcon()
    {
        if (locationIconRt == null || locateBtnLabel == null) return;
        float halfTextWidth = locateBtnLabel.GetPreferredValues(locateBtnLabel.text).x * 0.5f;
        locationIconRt.anchoredPosition = new Vector2(-(halfTextWidth + 14f), 0f);
    }

    // Devuelve el RectTransform raíz para poder reposicionar el icono entero
 // luego (el botón cambia de texto "Usar mi ubicación" / "Ubicación
 // encontrada" / etc. y el icono tiene que moverse para no tapar nada).
    static RectTransform BuildLocationPinIcon(Transform parent, Vector2 pos, Color color, Color holeColor)
    {
        const float headRadius = 5f;

        var rootGO = new GameObject("LocationPinIcon");
        rootGO.transform.SetParent(parent, false);
        var rootRt = rootGO.AddComponent<RectTransform>();
        rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = pos;
        rootRt.sizeDelta = Vector2.zero;

        // Cabeza + punta como una sola forma (círculo ∪ triángulo) generada en un único
        // sprite con antialiasing real. Antes la punta era un cuadrado rotado 45° aparte:
        // sus esquinas laterales sobresalían del círculo a la altura del cuello (el
        // cuadrado es más ancho en diagonal de lo que el círculo mide en esa franja),
        // dejando una costura de píxeles sin cubrir del todo por ninguna de las dos formas.
        BuildFilledPin(rootGO.transform, Vector2.zero, headRadius, color);

        // Agujero central, del color de fondo del botón, para el efecto "donut".
        BuildFilledCircle(rootGO.transform, Vector2.zero, headRadius * 0.42f, holeColor);

        return rootRt;
    }

    static TMP_FontAsset _orbitronFontAsset;
    static bool _orbitronLoadAttempted;

    // Carga Orbitron-Bold (Assets/Resources/Fonts) y lo envuelve en un
    // TMP_FontAsset dinámico (genera el atlas SDF sobre la marcha, sin tener
    // que crearlo a mano con el Font Asset Creator del Editor).
    public static TMP_FontAsset GetOrbitronFontAsset()
    {
        if (_orbitronLoadAttempted) return _orbitronFontAsset;
        _orbitronLoadAttempted = true;

        var font = Resources.Load<Font>("Fonts/Orbitron-Bold");
        if (font == null)
        {
            Debug.LogWarning("[MapCreatorUI] No se encontró Fonts/Orbitron-Bold en Resources.");
            return null;
        }

        _orbitronFontAsset = TMP_FontAsset.CreateFontAsset(font);
        return _orbitronFontAsset;
    }

    static Sprite _circleSprite;

    // Círculo con antialiasing real, generado una vez como textura/sprite en
    // vez de aproximarlo con franjas (que se veían escalonadas a tamaños pequeños).
    static Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;

        const int size = 256;
        const float aaWidth = 10f; // ancho del degradado del borde, en píxeles de la textura
        // mipChain=true + Trilinear: el icono se dibuja muy pequeño en pantalla,
        // así que sin mipmaps el filtrado al reducir tanto el tamaño se ve
        // escalonado otra vez (solo muestrea, no promedia el área reducida).
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.filterMode = FilterMode.Trilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f - aaWidth;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01((radius + aaWidth - dist) / aaWidth);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply(true);

        _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _circleSprite;
    }

    const float RingFillRatio = (256f / 2f - 4f) / (256f / 2f); // (semiancho - aaWidth) / semiancho, ver GetRingSprite

    // Ratio interior/exterior necesario para que, al mostrar el aro con un
    // sizeDelta dado, su grosor en unidades de UI (constante, no proporcional)
    // sea "lineThicknessUI". Así dos aros de tamaños distintos pueden verse
    // igual de finos en vez de "igual de finos en proporción a su propio radio"
    // (que a tamaños distintos da grosores absolutos distintos).
    static float RingRatioForThickness(float sizeDelta, float lineThicknessUI)
    {
        float outerUIRadius = (sizeDelta / 2f) * RingFillRatio;
        return Mathf.Clamp01(1f - lineThicknessUI / outerUIRadius);
    }

    static readonly Dictionary<float, Sprite> _ringSpriteCache = new Dictionary<float, Sprite>();

    // Aro hueco (anillo) con antialiasing real en el borde exterior Y en el
    // interior del agujero, generado igual que GetCircleSprite. Lo usan tanto el
    // aro de color interior como el aro de tipo exterior de los marcadores del
    // mapa; innerRatio se calcula con RingRatioForThickness para que ambos
    // tengan el mismo grosor absoluto pese a mostrarse a tamaños distintos.
    static Sprite GetRingSprite(float innerRatio)
    {
        if (_ringSpriteCache.TryGetValue(innerRatio, out var cached)) return cached;

        const int size = 256;
        // aaWidth más estrecho que en GetCircleSprite: con un aro tan fino, un degradado
        // de borde ancho se solaparía entero (borde exterior + interior) y el aro nunca
        // llegaría a alpha=1 en ningún punto, viéndose como una mancha difusa en vez de
        // una línea nítida.
        const float aaWidth = 4f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.filterMode = FilterMode.Trilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerRadius = size / 2f - aaWidth;
        float innerRadius = outerRadius * innerRatio;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float outerAlpha = Mathf.Clamp01((outerRadius + aaWidth - dist) / aaWidth);
                float innerAlpha = Mathf.Clamp01((dist - innerRadius + aaWidth) / aaWidth);
                float alpha = Mathf.Min(outerAlpha, innerAlpha);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply(true);

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        _ringSpriteCache[innerRatio] = sprite;
        return sprite;
    }

    static void BuildFilledCircle(Transform parent, Vector2 centerPos, float radius, Color color)
    {
        var go = new GameObject("Circle");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = GetCircleSprite();
        img.color = color; img.raycastTarget = false;
        var rt = img.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = centerPos;
        rt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
    }

    const float PinHeadRFrac = 72f / 256f;
    const float PinHeadCyFrac = 160f / 256f;

    static Sprite _pinSprite;

    // Forma de pin (cabeza circular ∪ punta triangular) como un único sprite con
    // antialiasing real, en vez de componer un círculo y un cuadrado rotado 45°
    // por separado: la base del triángulo coincide exactamente con el ecuador
    // del círculo (mismo ancho), así la unión no deja ninguna costura entre las
    // dos piezas ni esquinas del cuadrado sobresaliendo del círculo.
    static Sprite GetPinSprite()
    {
        if (_pinSprite != null) return _pinSprite;

        const int size = 256;
        const float aaWidth = 10f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.filterMode = FilterMode.Trilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        Vector2 headCenter = new Vector2(size * 0.5f, size * PinHeadCyFrac);
        float headR = size * PinHeadRFrac;
        Vector2 apex  = new Vector2(headCenter.x, headCenter.y - headR * 1.8f);
        Vector2 baseL = new Vector2(headCenter.x - headR, headCenter.y);
        Vector2 baseR = new Vector2(headCenter.x + headR, headCenter.y);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float dCircle = Vector2.Distance(p, headCenter) - headR;

                float triUnsigned = DistToTriangleBoundary(p, apex, baseL, baseR);
                bool  triInside   = PointInTriangle(p, apex, baseL, baseR);
                float dTri = triInside ? -triUnsigned : triUnsigned;

                float dUnion = Mathf.Min(dCircle, dTri);
                float alpha  = Mathf.Clamp01((aaWidth - dUnion) / aaWidth);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply(true);

        _pinSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, PinHeadCyFrac));
        return _pinSprite;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
        Vector2 proj = a + ab * t;
        return Vector2.Distance(p, proj);
    }

    static float DistToTriangleBoundary(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d0 = DistToSegment(p, a, b);
        float d1 = DistToSegment(p, b, c);
        float d2 = DistToSegment(p, c, a);
        return Mathf.Min(d0, Mathf.Min(d1, d2));
    }

    static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    static void BuildFilledPin(Transform parent, Vector2 headCenterPos, float headRadius, Color color)
    {
        var go = new GameObject("Pin");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = GetPinSprite();
        img.color = color; img.raycastTarget = false;
        var rt = img.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, PinHeadCyFrac);
        rt.anchoredPosition = headCenterPos;
        float side = headRadius / PinHeadRFrac;
        rt.sizeDelta = new Vector2(side, side);
    }

    static TMP_InputField BuildInputField(Transform parent)
    {
        var go  = new GameObject("Input");
        go.transform.SetParent(parent, false);
        var bg  = go.AddComponent<Image>(); bg.color = new Color(0.05f, 0.08f, 0.14f, 1f);
        SetLE(go, 34f);

        var tgo  = new GameObject("Text"); tgo.transform.SetParent(go.transform, false);
        var tTmp = tgo.AddComponent<TextMeshProUGUI>();
        tTmp.fontSize = 10f; tTmp.color = new Color(0.90f, 0.95f, 1.00f, 1f);
        tTmp.alignment = TextAlignmentOptions.Left;
        var trt = tgo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6, 2); trt.offsetMax = new Vector2(-6, -2);

        var pgo  = new GameObject("Ph"); pgo.transform.SetParent(go.transform, false);
        var pTmp = pgo.AddComponent<TextMeshProUGUI>();
        pTmp.text = "Mi misión..."; pTmp.fontSize = 10f;
        pTmp.color = new Color(0.42f, 0.58f, 0.72f, 1f);
        pTmp.fontStyle = FontStyles.Italic; pTmp.alignment = TextAlignmentOptions.Left;
        var prt = pgo.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(6, 2); prt.offsetMax = new Vector2(-6, -2);

        var field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;
        field.textComponent = tTmp;
        field.placeholder   = pTmp;
        return field;
    }
}
