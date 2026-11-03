#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Arma la escena fonAuds (en la carpeta de Fon) juntando las dos escenas de Sebas:
/// - Base: Sebas.unity (la mas nueva: objetos, teletransportes, botones, final).
/// - De "Sebas - jero.unity" se traen las burbujas de dialogo nuevas de los tres grupos de "dialogos".
///
/// Las burbujas viejas se borran y cualquier referencia a ellas (por ejemplo el Director_juego)
/// se reconecta a la burbuja nueva que usa la escena de Jero en ese mismo campo.
/// Al final llena la lista de dialogos de cada DialogueController en orden y pone los clips de voz.
/// No modifica ninguna de las dos escenas de Sebas.
/// </summary>
public static class HerramientaEscenaFonAuds
{
    const string EscenaBase = "Assets/Mundo/Equipo/Desarrolladores/Sebas/Sebas.unity";
    const string EscenaBurbujas = "Assets/Mundo/Equipo/Desarrolladores/Sebas/Sebas - jero.unity";
    const string CarpetaFon = "Assets/Mundo/Equipo/Desarrolladores/Fon";
    const string Carpeta = CarpetaFon + "/fonAuds";
    const string EscenaNueva = Carpeta + "/fonAuds.unity";

    const string AudioTutorial = "Assets/Sonidos/dialgos hermano/audio tutorial.mp3";
    const string AudioMetaContinuar = "Assets/Sonidos/dialgos hermano/audio meta para continuar.mp3";

    static readonly string[] Grupos =
    {
        "dialogos/Hermana primeros dialogos",
        "dialogos/Hermano en la banca",
        "dialogos/Hermanos hablando de la red",
    };

    [MenuItem("Herramientas/Fon/Crear escena fonAuds (Sebas + burbujas de Jero)")]
    static void Crear()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EscenaNueva) != null &&
            !EditorUtility.DisplayDialog("fonAuds", "Ya existe fonAuds.unity. ¿Reemplazarla?", "Reemplazar", "Cancelar"))
            return;

        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder(CarpetaFon, "fonAuds");
        AssetDatabase.DeleteAsset(EscenaNueva);
        if (!AssetDatabase.CopyAsset(EscenaBase, EscenaNueva))
        {
            Debug.LogError("[fonAuds] No se pudo copiar " + EscenaBase);
            return;
        }

        Scene destino = EditorSceneManager.OpenScene(EscenaNueva, OpenSceneMode.Single);
        Scene origen = EditorSceneManager.OpenScene(EscenaBurbujas, OpenSceneMode.Additive);

        var copiaDe = new Dictionary<GameObject, GameObject>();
        var viejas = new HashSet<GameObject>();
        var nuevasPorGrupo = new Dictionary<DialogueController, List<GameObject>>();

        foreach (string ruta in Grupos)
        {
            Transform grupoDestino = Buscar(destino, ruta);
            Transform grupoOrigen = Buscar(origen, ruta);
            if (grupoDestino == null || grupoOrigen == null)
            {
                Debug.LogWarning("[fonAuds] No encontre el grupo " + ruta + " en alguna de las escenas.");
                continue;
            }

            foreach (GameObject vieja in Tarjetas(grupoDestino)) viejas.Add(vieja);

            var nuevas = new List<GameObject>();
            foreach (GameObject tarjeta in Tarjetas(grupoOrigen))
            {
                GameObject copia = Object.Instantiate(tarjeta, grupoDestino, false);
                copia.name = tarjeta.name;
                copiaDe[tarjeta] = copia;
                nuevas.Add(copia);
            }

            var controlador = grupoDestino.GetComponent<DialogueController>();
            if (controlador != null) nuevasPorGrupo[controlador] = nuevas;
        }

        ReconectarReferencias(destino, origen, viejas, copiaDe);

        foreach (GameObject vieja in viejas) Object.DestroyImmediate(vieja);

        foreach (var par in nuevasPorGrupo)
        {
            List<GameObject> lista = par.Value;
            lista.Sort(CompararPorNumero);
            par.Key.listaDialogos = lista;
            par.Key.audiosDialogos = new List<AudioClip>(new AudioClip[lista.Count]);
            EditorUtility.SetDirty(par.Key);
        }

        PonerVozHermano(destino);

        EditorSceneManager.CloseScene(origen, true);
        EditorSceneManager.MarkSceneDirty(destino);
        EditorSceneManager.SaveScene(destino);
        Debug.Log("[fonAuds] Escena creada en " + EscenaNueva + ": " + copiaDe.Count + " burbujas nuevas, " + viejas.Count + " viejas quitadas.");
    }

    /// <summary>
    /// Burbujas de un grupo: hijos directos con SpriteRenderer que no son flechas ni prefabs.
    /// </summary>
    static List<GameObject> Tarjetas(Transform grupo)
    {
        var tarjetas = new List<GameObject>();
        foreach (Transform hijo in grupo)
        {
            if (hijo.GetComponent<SpriteRenderer>() == null) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(hijo.gameObject)) continue;
            if (hijo.GetComponentInChildren<FlechaDialogo>(true) != null) continue;
            tarjetas.Add(hijo.gameObject);
        }
        return tarjetas;
    }

    /// <summary>
    /// Todo campo de la escena destino que apunte a una burbuja vieja pasa a apuntar a la copia
    /// de la burbuja que usa la escena de Jero en ese mismo campo (mismo objeto, componente y propiedad).
    /// </summary>
    static void ReconectarReferencias(Scene destino, Scene origen, HashSet<GameObject> viejas, Dictionary<GameObject, GameObject> copiaDe)
    {
        foreach (GameObject raiz in destino.GetRootGameObjects())
        {
            foreach (Component componente in raiz.GetComponentsInChildren<Component>(true))
            {
                if (componente == null || componente is Transform || viejas.Contains(componente.gameObject)) continue;

                var so = new SerializedObject(componente);
                SerializedObject soOrigen = null;
                bool cambio = false;
                SerializedProperty prop = so.GetIterator();
                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    GameObject apuntado = ObjetoDe(prop.objectReferenceValue);
                    if (apuntado == null || !viejas.Contains(apuntado)) continue;

                    if (soOrigen == null)
                    {
                        Component contraparte = Contraparte(componente, origen);
                        if (contraparte != null) soOrigen = new SerializedObject(contraparte);
                    }

                    GameObject nuevo = null;
                    SerializedProperty propOrigen = soOrigen?.FindProperty(prop.propertyPath);
                    GameObject enOrigen = propOrigen != null && propOrigen.propertyType == SerializedPropertyType.ObjectReference
                        ? ObjetoDe(propOrigen.objectReferenceValue) : null;
                    if (enOrigen != null) copiaDe.TryGetValue(enOrigen, out nuevo);

                    if (nuevo == null)
                    {
                        foreach (GameObject copia in copiaDe.Values)
                            if (copia.name == apuntado.name && copia.transform.parent == apuntado.transform.parent) { nuevo = copia; break; }
                    }

                    Object valor = null;
                    if (nuevo != null)
                        valor = prop.objectReferenceValue is GameObject ? nuevo : (Object)nuevo.GetComponent(prop.objectReferenceValue.GetType());
                    if (valor == null)
                        Debug.LogWarning("[fonAuds] " + Ruta(componente.transform) + " (" + componente.GetType().Name + "." + prop.propertyPath + ") apuntaba a la burbuja vieja '" + apuntado.name + "' y quedo vacio.", componente);

                    prop.objectReferenceValue = valor;
                    cambio = true;
                }
                if (cambio) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    static void PonerVozHermano(Scene destino)
    {
        Transform grupo = Buscar(destino, "dialogos/Hermano en la banca");
        var controlador = grupo != null ? grupo.GetComponent<DialogueController>() : null;
        if (controlador == null || controlador.audiosDialogos.Count == 0) return;

        controlador.audiosDialogos[0] = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioTutorial);
        controlador.audiosDialogos[controlador.audiosDialogos.Count - 1] = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioMetaContinuar);
        if (controlador.audiosDialogos[0] == null) Debug.LogWarning("[fonAuds] No encontre el AudioClip " + AudioTutorial);
        EditorUtility.SetDirty(controlador);
    }

    static GameObject ObjetoDe(Object obj)
    {
        if (obj is GameObject go) return go;
        if (obj is Component c) return c.gameObject;
        return null;
    }

    static Component Contraparte(Component componente, Scene otra)
    {
        Transform t = Buscar(otra, Ruta(componente.transform));
        if (t == null) return null;
        Component[] propios = componente.GetComponents(componente.GetType());
        Component[] ajenos = t.GetComponents(componente.GetType());
        int indice = System.Array.IndexOf(propios, componente);
        return indice >= 0 && indice < ajenos.Length ? ajenos[indice] : null;
    }

    static Transform Buscar(Scene escena, string ruta)
    {
        int barra = ruta.IndexOf('/');
        string raiz = barra < 0 ? ruta : ruta.Substring(0, barra);
        foreach (GameObject go in escena.GetRootGameObjects())
        {
            if (go.name != raiz) continue;
            if (barra < 0) return go.transform;
            Transform t = go.transform.Find(ruta.Substring(barra + 1));
            if (t != null) return t;
        }
        return null;
    }

    static string Ruta(Transform t)
    {
        return t.parent == null ? t.name : Ruta(t.parent) + "/" + t.name;
    }

    /// <summary>
    /// Ordena por el ultimo numero del nombre ("dialogo 2" antes que "dialogo 10"); los que no tienen numero van al final.
    /// </summary>
    static int CompararPorNumero(GameObject a, GameObject b)
    {
        int comparacion = Numero(a.name).CompareTo(Numero(b.name));
        return comparacion != 0 ? comparacion : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
    }

    static int Numero(string nombre)
    {
        // Quita el " (1)" que Unity pone al duplicar, para que "dialogo 7 tutorial (1)" cuente como 7.
        nombre = Regex.Replace(nombre, @"\s*\(\d+\)\s*$", "");
        Match m = Regex.Match(nombre, @"(\d+)\D*$");
        return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
    }
}
#endif
