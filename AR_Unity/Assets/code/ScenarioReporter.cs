using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARDashboard
{
    /// Informa a la webapp (dron/<id>/scenario, vía DroneMqttBridge) de la escena
    /// activa, para que pueda mostrar/ocultar controles especificos (p.ej. el
    /// boton de disparo del shooter).
    /// Reenvía periódicamente para que el servidor siempre sepa la escena activa
    /// incluso si Flask se reinició después de que Unity cargó la escena.
    public class ScenarioReporter : MonoBehaviour
    {
        const float REPEAT_INTERVAL = 3f;   // reenvía cada 3 s

        string currentScene = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var go = new GameObject("[ScenarioReporter]");
            Object.DontDestroyOnLoad(go);
            var reporter = go.AddComponent<ScenarioReporter>();
            SceneManager.sceneLoaded += reporter.OnSceneLoaded;
            reporter.currentScene = SceneManager.GetActiveScene().name;
            reporter.Report(reporter.currentScene);
            reporter.StartCoroutine(reporter.PeriodicReport());
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            currentScene = scene.name;
            Report(currentScene);
        }

        void Report(string sceneName) => DroneMqttBridge.Instance?.PublishScenario(sceneName);

        IEnumerator PeriodicReport()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(REPEAT_INTERVAL);
                Report(currentScene);
            }
        }
    }
}
