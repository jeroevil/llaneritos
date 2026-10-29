#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramienta de un solo uso (se puede borrar despues): copia los sprites de un arbol de ejemplo
/// a todos los arboles hijos de un padre (p. ej. "arboles buenos" con el "Moriche bueno" de ejemplo).
///
/// - Copiar: cada SpriteRenderer recibe el sprite del renderer equivalente del ejemplo
///   (mismo nombre de hijo; si no coincide, misma posicion en la jerarquia).
/// - Randomizar: cada SpriteRenderer recibe uno al azar entre los sprites del ejemplo, para romper el patron.
///
/// Todo se puede deshacer con Ctrl+Z. Menu: Llaneritos/Herramientas/Sprites de Arboles.
/// </summary>
public class HerramientaSpritesArboles : EditorWindow
{
    Transform padre;
    Transform ejemplo;

    [MenuItem("Llaneritos/Herramientas/Sprites de Arboles")]
    static void Abrir()
    {
        GetWindow<HerramientaSpritesArboles>("Sprites de Arboles");
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("1. Arrastra el padre (arboles buenos) y el arbol de ejemplo.\n2. Elige Copiar o Randomizar. Ctrl+Z para deshacer.", MessageType.Info);

        padre = (Transform)EditorGUILayout.ObjectField("Padre de los arboles", padre, typeof(Transform), true);
        ejemplo = (Transform)EditorGUILayout.ObjectField("Arbol de ejemplo", ejemplo, typeof(Transform), true);

        var sprites = SpritesDelEjemplo();
        EditorGUILayout.LabelField("Sprites en el ejemplo", sprites.Count > 0 ? string.Join(", ", sprites.ConvertAll(s => s.name)) : "ninguno");

        bool listo = padre != null && ejemplo != null && sprites.Count > 0;
        if (padre != null && ejemplo != null && !ejemplo.IsChildOf(padre))
            EditorGUILayout.HelpBox("El ejemplo no es hijo del padre: igual funciona, pero revisa que sea el arbol correcto.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!listo))
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Copiar sprites del ejemplo", GUILayout.Height(30))) Aplicar(false);
            if (GUILayout.Button("Randomizar entre los sprites del ejemplo", GUILayout.Height(30))) Aplicar(true);
        }
    }

    List<Sprite> SpritesDelEjemplo()
    {
        var lista = new List<Sprite>();
        if (ejemplo == null) return lista;
        foreach (var sr in ejemplo.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.sprite != null && !lista.Contains(sr.sprite)) lista.Add(sr.sprite);
        return lista;
    }

    void Aplicar(bool randomizar)
    {
        var renderersEjemplo = ejemplo.GetComponentsInChildren<SpriteRenderer>(true);
        var sprites = SpritesDelEjemplo();
        int arboles = 0, cambiados = 0, sinPareja = 0;

        Undo.SetCurrentGroupName(randomizar ? "Randomizar sprites de arboles" : "Copiar sprites de arboles");
        int grupo = Undo.GetCurrentGroup();

        for (int i = 0; i < padre.childCount; i++)
        {
            Transform arbol = padre.GetChild(i);
            if (arbol == ejemplo) continue;
            arboles++;

            var renderers = arbol.GetComponentsInChildren<SpriteRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Sprite nuevo;
                if (randomizar)
                {
                    nuevo = sprites[Random.Range(0, sprites.Count)];
                }
                else
                {
                    var pareja = BuscarPareja(renderers[r], arbol, renderersEjemplo, r);
                    if (pareja == null) { sinPareja++; continue; }
                    nuevo = pareja.sprite;
                }

                if (renderers[r].sprite == nuevo) continue;
                Undo.RecordObject(renderers[r], "Sprite de arbol");
                renderers[r].sprite = nuevo;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderers[r]);
                cambiados++;
            }
        }

        Undo.CollapseUndoOperations(grupo);
        EditorSceneManager.MarkSceneDirty(padre.gameObject.scene);
        Debug.Log($"[Sprites Arboles] {(randomizar ? "Randomizados" : "Copiados")}: {cambiados} renderers en {arboles} arboles" +
                  (sinPareja > 0 ? $" ({sinPareja} sin equivalente en el ejemplo, sin tocar)." : ".") + " Guarda la escena para conservarlo.");
    }

    // Busca en el ejemplo el renderer con la misma ruta de nombres; si no hay, el de la misma posicion
    SpriteRenderer BuscarPareja(SpriteRenderer sr, Transform arbol, SpriteRenderer[] renderersEjemplo, int indice)
    {
        string ruta = RutaRelativa(sr.transform, arbol);
        foreach (var e in renderersEjemplo)
            if (RutaRelativa(e.transform, ejemplo) == ruta) return e;
        return indice < renderersEjemplo.Length ? renderersEjemplo[indice] : null;
    }

    static string RutaRelativa(Transform t, Transform raiz)
    {
        if (t == raiz) return "";
        string ruta = t.name;
        for (var p = t.parent; p != null && p != raiz; p = p.parent) ruta = p.name + "/" + ruta;
        return ruta;
    }
}
#endif
