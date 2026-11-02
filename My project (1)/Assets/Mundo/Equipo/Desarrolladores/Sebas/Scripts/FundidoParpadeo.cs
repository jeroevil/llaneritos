using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fundido a negro con el mismo sistema del parpadeo del Silbon: el volumen URP "Volume_Blink_Runtime"
/// (post-exposicion al minimo) del FishingSilbonDirector. No hay que asignar nada: se busca solo.
/// Si el director todavia no lo creo, lo crea igual que el (mismo nombre), y el director lo reutiliza.
///
/// Peso 0 = pantalla normal, peso 1 = negro.
/// </summary>
public static class FundidoParpadeo
{
    const string NombreVolumen = "Volume_Blink_Runtime";

    static Volume volumen;

    static Volume Obtener()
    {
        if (volumen != null) return volumen;

        var director = Object.FindFirstObjectByType<FishingSilbonDirector>();
        if (director != null && director.volumeBlink != null)
            return volumen = director.volumeBlink;

        var existente = GameObject.Find(NombreVolumen);
        if (existente != null && existente.TryGetComponent(out Volume v))
            return volumen = v;

        var nuevo = new GameObject(NombreVolumen);
        if (director != null) nuevo.transform.SetParent(director.transform);
        volumen = nuevo.AddComponent<Volume>();
        volumen.isGlobal = true;
        volumen.priority = 1000;

        var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
        perfil.name = "BlinkProfile_Generated";
        var ajuste = perfil.Add<ColorAdjustments>(true);
        ajuste.postExposure.overrideState = true;
        ajuste.postExposure.value = -15f; // Negro absoluto
        volumen.profile = perfil;
        volumen.weight = 0f;
        return volumen;
    }

    /// <summary>Pone el peso del parpadeo (0 = normal, 1 = negro).</summary>
    public static void Poner(float peso)
    {
        var v = Obtener();
        if (v != null) v.weight = Mathf.Clamp01(peso);
    }

    /// <summary>Corrutina: lleva el parpadeo de un peso a otro en 'duracion' segundos.</summary>
    public static IEnumerator Fundir(float desde, float hasta, float duracion)
    {
        if (duracion <= 0.001f)
        {
            Poner(hasta);
            yield break;
        }
        float t = 0f;
        while (t < duracion)
        {
            t += Time.deltaTime;
            Poner(Mathf.Lerp(desde, hasta, t / duracion));
            yield return null;
        }
        Poner(hasta);
    }
}
