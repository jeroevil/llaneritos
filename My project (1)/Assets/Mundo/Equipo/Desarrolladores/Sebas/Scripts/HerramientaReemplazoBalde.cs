#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramienta de un solo uso (se puede borrar despues): pone el balde nuevo del disenador
/// (bueno y malvado) en la escena de Sebas sin perder nada de lo que hace el balde viejo.
///
/// Que hace el balde viejo y como se conserva:
/// - Tiene el collider trigger y el FishBarrelTrigger (cuenta los peces que caen y avisa al director).
///   El objeto viejo se queda como "Balde - Trigger" con todo eso intacto: solo se apaga su malla.
///   Asi el conteo, la barra, la mejora de la red y el Silbon siguen funcionando igual.
/// - Pone el balde bueno y el malvado (el malvado apagado) en la pose indicada.
/// - Registra el par (bueno -> malvado) en CambioMalvado para que cambien en el climax.
///
/// Todo se puede deshacer con Ctrl+Z. Menu: Llaneritos/Herramientas/Reemplazar Balde.
/// </summary>
public class HerramientaReemplazoBalde : EditorWindow
{
    const string RutaBueno = "Assets/Mundo/Sprites 2D/sprites 3d/balde.fbx";
    const string RutaMalvado = "Assets/Mundo/Sprites 2D/sprites 3d/balde malvado.fbx";

    FishBarrelTrigger baldeViejo;
    CambioMalvado cambioMalvado;
    GameObject prefabBueno;
    GameObject prefabMalvado;

    // Pose que le dio el disenador en "Sebas - jero"
    Vector3 posicion = new Vector3(9.08f, 0.05f, 20.38f);
    Vector3 rotacionEuler = new Vector3(0f, 109.44f, 0f);
    float escala = 54.922f;

    bool ocultarMallaVieja = true;
    bool renombrarViejo = true;

    [MenuItem("Llaneritos/Herramientas/Reemplazar Balde")]
    static void Abrir()
    {
        var ventana = GetWindow<HerramientaReemplazoBalde>("Reemplazar Balde");
        ventana.Autocompletar();
    }

    void OnEnable()
    {
        Autocompletar();
    }

    void Autocompletar()
    {
        if (baldeViejo == null) baldeViejo = FindFirstObjectByType<FishBarrelTrigger>();
        if (cambioMalvado == null) cambioMalvado = FindFirstObjectByType<CambioMalvado>();
        if (prefabBueno == null) prefabBueno = AssetDatabase.LoadAssetAtPath<GameObject>(RutaBueno);
        if (prefabMalvado == null) prefabMalvado = AssetDatabase.LoadAssetAtPath<GameObject>(RutaMalvado);
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Abre la escena de Sebas, revisa los campos y pulsa Reemplazar.\n" +
            "El balde viejo se queda como trigger invisible (conserva el conteo de peces); " +
            "los baldes nuevos son solo visuales. Ctrl+Z para deshacer.", MessageType.Info);

        baldeViejo = (FishBarrelTrigger)EditorGUILayout.ObjectField("Balde viejo (con FishBarrelTrigger)", baldeViejo, typeof(FishBarrelTrigger), true);
        cambioMalvado = (CambioMalvado)EditorGUILayout.ObjectField("CambioMalvado de la escena", cambioMalvado, typeof(CambioMalvado), true);
        prefabBueno = (GameObject)EditorGUILayout.ObjectField("Balde bueno (fbx)", prefabBueno, typeof(GameObject), false);
        prefabMalvado = (GameObject)EditorGUILayout.ObjectField("Balde malvado (fbx)", prefabMalvado, typeof(GameObject), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pose de los baldes nuevos (la del disenador)", EditorStyles.boldLabel);
        posicion = EditorGUILayout.Vector3Field("Posicion", posicion);
        rotacionEuler = EditorGUILayout.Vector3Field("Rotacion", rotacionEuler);
        escala = EditorGUILayout.FloatField("Escala", escala);

        EditorGUILayout.Space();
        ocultarMallaVieja = EditorGUILayout.Toggle("Apagar la malla del balde viejo", ocultarMallaVieja);
        renombrarViejo = EditorGUILayout.Toggle("Renombrar el viejo a 'Balde - Trigger'", renombrarViejo);

        if (baldeViejo != null)
        {
            var col = baldeViejo.GetComponent<Collider>();
            if (col == null || !col.isTrigger)
                EditorGUILayout.HelpBox("El balde viejo no tiene un Collider marcado como Trigger: sin eso no cuenta los peces.", MessageType.Warning);
            if (baldeViejo.progressionDirector == null)
                EditorGUILayout.HelpBox("El FishBarrelTrigger no tiene director asignado (lo busca solo al iniciar, pero conviene asignarlo).", MessageType.Warning);
        }
        if (cambioMalvado == null)
            EditorGUILayout.HelpBox("No hay CambioMalvado en la escena: el balde malvado no se activara en el climax.", MessageType.Warning);

        bool listo = baldeViejo != null && prefabBueno != null && prefabMalvado != null;
        using (new EditorGUI.DisabledScope(!listo))
        {
            if (GUILayout.Button("Reemplazar", GUILayout.Height(32))) Reemplazar();
        }
    }

    void Reemplazar()
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Reemplazar balde");
        int grupo = Undo.GetCurrentGroup();

        var escena = baldeViejo.gameObject.scene;
        GameObject bueno = Crear(prefabBueno, escena, "balde");
        GameObject malvado = Crear(prefabMalvado, escena, "balde malvado");
        malvado.SetActive(false);

        // El viejo conserva trigger + FishBarrelTrigger: solo se apaga lo visual
        var viejo = baldeViejo.gameObject;
        if (ocultarMallaVieja)
        {
            var malla = viejo.GetComponent<MeshRenderer>();
            if (malla != null)
            {
                Undo.RecordObject(malla, "Apagar malla");
                malla.enabled = false;
            }
        }
        if (renombrarViejo)
        {
            Undo.RecordObject(viejo, "Renombrar balde viejo");
            viejo.name = "Balde - Trigger";
        }

        RegistrarPar(bueno, malvado);

        Undo.CollapseUndoOperations(grupo);
        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = bueno;
        Debug.Log("[Reemplazar Balde] Listo: balde bueno y malvado puestos; el trigger del balde viejo sigue contando los peces. Guarda la escena.", bueno);
    }

    GameObject Crear(GameObject prefab, UnityEngine.SceneManagement.Scene escena, string nombre)
    {
        var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, escena);
        Undo.RegisterCreatedObjectUndo(obj, "Crear " + nombre);
        obj.name = nombre;
        obj.transform.SetPositionAndRotation(posicion, Quaternion.Euler(rotacionEuler));
        obj.transform.localScale = Vector3.one * escala;
        return obj;
    }

    void RegistrarPar(GameObject bueno, GameObject malvado)
    {
        if (cambioMalvado == null) return;

        var so = new SerializedObject(cambioMalvado);
        var pares = so.FindProperty("pares");
        if (pares == null)
        {
            Debug.LogWarning("[Reemplazar Balde] No encontre la lista 'pares' en CambioMalvado: agrega el par a mano.", cambioMalvado);
            return;
        }

        int i = pares.arraySize;
        pares.InsertArrayElementAtIndex(i);
        var par = pares.GetArrayElementAtIndex(i);
        par.FindPropertyRelative("normal").objectReferenceValue = bueno;
        par.FindPropertyRelative("malvado").objectReferenceValue = malvado;
        so.ApplyModifiedProperties();
    }
}
#endif
