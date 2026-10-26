using System.Text.Json.Serialization;

namespace SistemaContable.Dominio;


/// <summary>
/// Excepción de negocio para violaciones de reglas en el dominio contable.
/// </summary>
public class DominioContableException(string mensaje) : Exception(mensaje);

/// <summary>
/// Clasificación contable según la estructura financiera estándar.
/// </summary>
public enum TipoCuenta
{
    Activo,
    Pasivo,
    Capital,
    Ingreso,
    Costo,
    Gasto
}

/// <summary>
/// Naturaleza del saldo contable.
/// </summary>
public enum NaturalezaSaldo
{
    Deudora,
    Acreedora
}

/// <summary>
/// Representa una cuenta del catálogo contable.
/// </summary>
public class CuentaContable(string codigo, string nombre, TipoCuenta tipo, NaturalezaSaldo naturaleza, bool permiteMovimientos = true)
{
    public string Codigo { get; } = !string.IsNullOrWhiteSpace(codigo)
        ? codigo.Trim()
        : throw new DominioContableException("El código de la cuenta no puede estar vacío.");

    public string Nombre { get; } = !string.IsNullOrWhiteSpace(nombre)
        ? nombre.Trim()
        : throw new DominioContableException("El nombre de la cuenta no puede estar vacío.");

    public TipoCuenta Tipo { get; } = tipo;
    public NaturalezaSaldo Naturaleza { get; } = naturaleza;
    public bool PermiteMovimientos { get; } = permiteMovimientos;
}

/// <summary>
/// Representa una partida o renglón dentro de un asiento contable.
/// </summary>
public class LineaAsiento(string codigoCuenta, string nombreCuenta, decimal debe, decimal haber)
{
    public string CodigoCuenta { get; } = !string.IsNullOrWhiteSpace(codigoCuenta)
        ? codigoCuenta.Trim()
        : throw new DominioContableException("El código de cuenta es obligatorio en la línea de asiento.");

    public string NombreCuenta { get; } = !string.IsNullOrWhiteSpace(nombreCuenta)
        ? nombreCuenta.Trim()
        : throw new DominioContableException("El nombre de cuenta es obligatorio en la línea de asiento.");

    public decimal Debe { get; } = debe < 0
        ? throw new DominioContableException("El importe en Debe no puede ser negativo.")
        : (debe > 0 && haber > 0)
            ? throw new DominioContableException("Una línea de asiento no puede tener importes simultáneos en Debe y Haber.")
            : debe;

    public decimal Haber { get; } = haber < 0
        ? throw new DominioContableException("El importe en Haber no puede ser negativo.")
        : haber;
}

/// <summary>
/// Representa una transacción contable con partida doble balanceada.
/// </summary>
public class AsientoDiario
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string Concepto { get; set; }
    public List<LineaAsiento> Lineas { get; init; } = [];

    [JsonConstructor]
    public AsientoDiario(int id, DateTime fecha, string concepto, List<LineaAsiento>? lineas = null)
    {
        Id = id;
        Fecha = fecha;
        Concepto = concepto;
        Lineas = lineas ?? [];
    }

    public AsientoDiario(DateTime fecha, string concepto, List<LineaAsiento>? lineas = null)
        : this(0, fecha, concepto, lineas) { }



    /// <summary>
    /// Valida que el asiento cumpla con las reglas contables de partida doble.
    /// </summary>
    public void ValidarYCuadrar()
    {
        if (string.IsNullOrWhiteSpace(Concepto))
            throw new DominioContableException("El concepto del asiento no puede estar vacío.");

        if (Lineas.Count < 2)
            throw new DominioContableException("El asiento debe contener al menos 2 líneas de movimiento.");

        var totalDebe = Lineas.Sum(l => l.Debe);
        var totalHaber = Lineas.Sum(l => l.Haber);

        if (totalDebe != totalHaber)
            throw new DominioContableException($"El asiento está descuadrado: Total Debe ({totalDebe:N2}) != Total Haber ({totalHaber:N2}).");
    }
}

/// <summary>
/// Contrato para el repositorio de persistencia contable.
/// </summary>
public interface IRepositorioContable
{
    IEnumerable<CuentaContable> ObtenerCuentas();
    IEnumerable<CuentaContable> ObtenerCuentasOperativas();
    void GuardarAsiento(AsientoDiario asiento);
    IEnumerable<AsientoDiario> ObtenerAsientos();
}
