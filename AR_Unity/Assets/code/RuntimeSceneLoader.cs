using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ARDashboard;

/// Monta la partida de un escenario personalizado reutilizando la propia
/// escena activa (Configuración): borra sus objetos y trae encima toda la
/// infraestructura de Circuito1 (dron, MQTT, EventSystem, pause...) más los
/// objetos del escenario. No crea ninguna escena nueva -- necesario porque
/// una build no puede registrar escenas nuevas en tiempo de ejecución (a
/// diferencia de ScenarioSceneCreator, que solo existe en el Editor).
public class RuntimeSceneLoader : MonoBehaviour
{
    public static void Launch(ScenarioData scenario)
    {
        var go = new GameObject("RuntimeSceneLoader");
        DontDestroyOnLoad(go);
        go.AddComponent<RuntimeSceneLoader>().StartCoroutine(
            go.GetComponent<RuntimeSceneLoader>().Build(scenario));
    }

    IEnumerator Build(ScenarioData scenario)
    {
        var currentScene = SceneManager.GetActiveScene();
        // Instantánea de lo que hay en la escena ANTES de traer nada nuevo,
        // para poder borrarlo luego sin tocar lo que sí queremos conservar
        // (los objetos recién movidos desde Circuito1).
        var oldRoots = new HashSet<GameObject>(currentScene.GetRootGameObjects());

        // Cargar Circuito1 de forma aditiva -- solo como fuente temporal de
        // la que copiar la infraestructura (dron, MQTT, EventSystem, pause,
        // cámara, luz...); nunca llega a ser la escena activa final.
        yield return SceneManager.LoadSceneAsync("Circuito1", LoadSceneMode.Additive);
        var template = SceneManager.GetSceneByName("Circuito1");

        // Leer los RenderSettings de Circuito1 antes de descargarlo
        SceneManager.SetActiveScene(template);
        var ambientMode          = RenderSettings.ambientMode;
        var ambientLight         = RenderSettings.ambientLight;
        var ambientSkyColor      = RenderSettings.ambientSkyColor;
        var ambientEquatorColor  = RenderSettings.ambientEquatorColor;
        var ambientGroundColor   = RenderSettings.ambientGroundColor;
        var ambientIntensity     = RenderSettings.ambientIntensity;
        var skybox               = RenderSettings.skybox;
        var fog                  = RenderSettings.fog;
        var fogColor             = RenderSettings.fogColor;
        var fogMode              = RenderSettings.fogMode;
        var fogDensity           = RenderSettings.fogDensity;
        var fogStart             = RenderSettings.fogStartDistance;
        var fogEnd               = RenderSettings.fogEndDistance;
        var reflectionIntensity  = RenderSettings.reflectionIntensity;
        var sun                  = RenderSettings.sun;

        // Mover a la escena ACTUAL (Configuración) todos los objetos raíz de
        // Circuito1 excepto aros y dronelab -- la misma infraestructura que
        // antes se copiaba a una escena nueva, ahora directamente aquí, sin
        // crear ningún contenedor nuevo.
        foreach (var root in template.GetRootGameObjects())
        {
            string n = root.name.ToLowerInvariant();
            if (n.Contains("aro") || n.Contains("dronelab"))
            { Destroy(root); continue; }
            SceneManager.MoveGameObjectToScene(root, currentScene);
        }
        yield return SceneManager.UnloadSceneAsync(template);

        // Reactivar la escena actual y aplicar los mismos RenderSettings
        SceneManager.SetActiveScene(currentScene);
        RenderSettings.ambientMode         = ambientMode;
        RenderSettings.ambientLight        = ambientLight;
        RenderSettings.ambientSkyColor     = ambientSkyColor;
        RenderSettings.ambientEquatorColor = ambientEquatorColor;
        RenderSettings.ambientGroundColor  = ambientGroundColor;
        RenderSettings.ambientIntensity    = ambientIntensity;
        RenderSettings.skybox              = skybox;
        RenderSettings.fog                 = fog;
        RenderSettings.fogColor            = fogColor;
        RenderSettings.fogMode             = fogMode;
        RenderSettings.fogDensity          = fogDensity;
        RenderSettings.fogStartDistance    = fogStart;
        RenderSettings.fogEndDistance      = fogEnd;
        RenderSettings.reflectionIntensity = reflectionIntensity;
        RenderSettings.sun                 = sun;

        // Ahora sí: borrar lo que hubiera en Configuración (su cámara, luz,
        // EventSystem, el propio Canvas de selección de puerto/cámara...) --
        // ya no hace falta nada de eso, la infraestructura de Circuito1 trae
        // lo suyo propio.
        foreach (var root in oldRoots)
            if (root != null) Destroy(root);

        // Colocar los objetos del mapa custom encima y subir el geofence de
        // seguridad -- antes esto último dependía del Start() de MapSceneController,
        // que nunca se ejecutaba porque nada lo adjuntaba a ningún GameObject.
        var list = JsonUtility.FromJson<MapObjectList>(scenario.mapObjectsJson);
        if (list?.objects != null && list.objects.Count > 0)
            MapSceneController.PlaceAll(list.objects);
        MapSceneController.UploadGeofence();

        Destroy(gameObject);
    }
}
