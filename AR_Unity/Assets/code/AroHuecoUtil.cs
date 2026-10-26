using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// Ajusta el tamaño del hueco del Aro de forma independiente del diámetro
/// general y del grosor de profundidad (ver MapCreatorUI.ApplyGrosorScale):
/// desplaza solo los vértices de la cara INTERIOR del hueco (la que mira
/// hacia el centro del aro) a lo largo de la componente XY (dentro del plano
/// del aro) de su normal soldada. La cara exterior del aro no se toca nunca,
/// así que el diámetro exterior se queda fijo y solo cambia el hueco: positivo
/// lo encoge (banda más ancha hacia dentro); negativo lo agranda.
///
/// Usa el mismo patrón ya validado para el grosor: cantidad relativa al
/// tamaño natural de la propia malla (no metros de mundo, que se rompía con
/// el factor de escala forzado del previsualizador), normales soldadas por
/// posición (evita grietas en costuras de UV), y la malla ORIGINAL cacheada
/// por MeshFilter para que llamadas sucesivas (arrastrar el slider) no se
/// acumulen entre sí.
public static class AroHuecoUtil
{
    static readonly ConditionalWeakTable<MeshFilter, Mesh> _originals = new ConditionalWeakTable<MeshFilter, Mesh>();

    static Mesh Inflate(Mesh source, float localAmount)
    {
        var mesh = Object.Instantiate(source);
        if (Mathf.Approximately(localAmount, 0f)) return mesh;

        var verts   = mesh.vertices;
        var normals = mesh.normals;
        Vector3 center = mesh.bounds.center;

        var weldedNormal = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < verts.Length; i++)
        {
            weldedNormal.TryGetValue(verts[i], out var acc);
            weldedNormal[verts[i]] = acc + normals[i];
        }

        for (int i = 0; i < verts.Length; i++)
        {
            var n = weldedNormal[verts[i]];
            n.z = 0f; // solo la componente dentro del plano del aro (X/Y), no la profundidad
            if (n.sqrMagnitude <= 1e-10f) continue;

            // Dirección radial hacia fuera del eje central del aro, en el plano X/Y.
            var radialOut = verts[i] - center;
            radialOut.z = 0f;
            if (radialOut.sqrMagnitude <= 1e-10f) continue;

            // Peso suave según cuánto mira la normal hacia el centro (1 = cara
            // interior del hueco, 0 = cara exterior del aro), en vez de un
            // corte binario: cerca del "ecuador" del tubo (normal casi
            // perpendicular a la radial) la clasificación es ambigua vértice a
            // vértice, y un corte binario ahí deja una costura dentada visible
            // entre vértices vecinos que caen a un lado u otro por casualidad.
            float t = Vector3.Dot(n.normalized, -radialOut.normalized);
            float weight = Mathf.Clamp01(t);
            if (weight > 0f)
                verts[i] += n.normalized * (localAmount * weight);
        }

        mesh.vertices = verts;
        // Las normales originales ya no coinciden con la forma inflada (se
        // notaría como abolladuras/caras no planas al iluminarse mal), así
        // que se recalculan sobre la geometría nueva.
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static void ApplyHueco(GameObject go, float hueco)
    {
        if (go == null) return;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (!_originals.TryGetValue(mf, out var original))
            {
                original = mf.sharedMesh;
                _originals.Add(mf, original);
            }

            var e = original.bounds.extents;
            float baseThickness = Mathf.Min(e.x > 0.0001f ? e.x : float.MaxValue,
                                    Mathf.Min(e.y > 0.0001f ? e.y : float.MaxValue,
                                              e.z > 0.0001f ? e.z : float.MaxValue));
            if (baseThickness == float.MaxValue || baseThickness <= 0f) baseThickness = 1f;

            mf.mesh = Inflate(original, hueco * baseThickness);
        }
    }
}
