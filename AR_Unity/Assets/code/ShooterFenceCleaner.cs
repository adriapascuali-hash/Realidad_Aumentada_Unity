using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARDashboard
{
    /// Limpia las geofences del autopiloto al entrar y al salir de la escena shooter.
    /// No crea ninguna fence nueva.
    public class ShooterFenceCleaner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Subscribe() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "shooter") return;
            var go = new GameObject("[ShooterFenceCleaner]");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<ShooterFenceCleaner>();
        }

        csDronLink.Dron dron;

        void Start()
        {
            dron = DroneMqttBridge.Instance?.GetDron();
            if (dron == null) { Debug.LogWarning("[FenceCleaner] GetDron() = null."); return; }

            // Limpiar fences residuales de sesiones anteriores
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try { dron.BorraEscenario(); Debug.Log("[FenceCleaner] Fences borradas al iniciar."); }
                catch (System.Exception e) { Debug.LogWarning($"[FenceCleaner] Error al borrar al iniciar: {e.Message}"); }
            });
        }

        void OnApplicationQuit() => ClearFences(waitMs: 2000);
        void OnDestroy()         => ClearFences(waitMs: 0);

        void ClearFences(int waitMs)
        {
            if (dron == null) return;
            var d = dron; dron = null;
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try { d.BorraEscenario(); Debug.Log("[FenceCleaner] Fences borradas al salir."); }
                catch (System.Exception e) { Debug.LogWarning($"[FenceCleaner] Error al borrar al salir: {e.Message}"); }
            });
            if (waitMs > 0) task.Wait(waitMs);
        }
    }
}
