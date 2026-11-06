using UnityEngine;

/// <summary>
/// Tipo de un pez pescado (va en la raiz del prefab que se agarra y se lleva al barril).
///
/// - Cachama: es la que hace falta. Cuenta para el Silbon, para la mejora de la red y para la persecucion (10 en total).
/// - Bagre amarillo y curito: no cuentan. Solo mueven la barra de progreso (efecto placebo): la llenan hasta justo
///   antes de la siguiente raya, pero nunca activan al Silbon ni mejoran la red.
///
/// Lo lee FishBarrelTrigger al caer el pez al barril, y RedPescaController al repartir los tipos en la red.
/// </summary>
public class TipoPez : MonoBehaviour
{
    public enum Tipo { Cachama, BagreAmarillo, Curito }

    [SerializeField] Tipo tipo = Tipo.Cachama;

    [Tooltip("Si esta activo, el pez cuenta de verdad: Silbon, mejora de la red y persecucion (se necesitan 10). " +
             "Si no, solo mueve la barra de progreso.")]
    [SerializeField] bool cuentaParaProgreso = true;

    [Tooltip("Unidades de pez que suma a la barra cuando NO cuenta (1 = lo mismo que una cachama). " +
             "El bagre amarillo es el que mas aporta; el curito, poco.")]
    [SerializeField, Min(0f)] float aporteBarra = 0.5f;

    public Tipo TipoDePez => tipo;
    public bool CuentaParaProgreso => cuentaParaProgreso;
    public float AporteBarra => aporteBarra;
}
