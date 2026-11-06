using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public class GestionCuentasService(IRepositorioContable repositorio, ReportesService reportesService)
{
    /// <summary>
    /// Alterna el estado de activación de una cuenta contable aplicando validaciones de integridad de negocio.
    /// </summary>
    /// <param name="codigo">Código de la cuenta a alternar.</param>
    /// <returns>El nuevo estado de la cuenta (true si está activa, false si está inactiva).</returns>
    /// <exception cref="DominioContableException">Lanzada cuando se violan reglas de integridad contable.</exception>
    public bool AlternarEstadoCuenta(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new DominioContableException("El código de la cuenta no puede estar vacío.");

        var cuentas = repositorio.ObtenerCuentas().ToList();
        var cuenta = cuentas.FirstOrDefault(c => c.Codigo.Equals(codigo.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DominioContableException($"La cuenta '{codigo}' no existe en el catálogo contable.");

        bool nuevoEstado = !cuenta.EstaActiva;

        // Si la operación es de DESACTIVACIÓN (pasa a false), se validan reglas de integridad
        if (!nuevoEstado)
        {
            // 1. Validar movimientos en Libro Mayor con saldo neto distinto de cero (Debe != Haber)
            var mayor = reportesService.ObtenerLibroMayor();
            var filaMayor = mayor.FirstOrDefault(m => m.Codigo.Equals(cuenta.Codigo, StringComparison.OrdinalIgnoreCase));
            if (filaMayor != null && (filaMayor.TotalDebe != filaMayor.TotalHaber || filaMayor.SaldoFinal != 0m))
            {
                throw new DominioContableException("No se puede desactivar la cuenta porque mantiene un saldo contable activo");
            }

            // 2. Validar si es cuenta cabecera con subcuentas hijas activas
            var codigoNormalizado = cuenta.Codigo.Trim();
            var soloDigitosPadre = new string(codigoNormalizado.Where(char.IsDigit).ToArray());

            bool tieneHijasActivas = cuentas.Any(c =>
                !c.Codigo.Equals(cuenta.Codigo, StringComparison.OrdinalIgnoreCase) &&
                c.EstaActiva &&
                EsSubcuenta(codigoNormalizado, soloDigitosPadre, c.Codigo));

            if (tieneHijasActivas)
            {
                throw new DominioContableException("No se puede desactivar la cuenta porque contiene subcuentas hijas que siguen activas.");
            }
        }

        // Si pasa las validaciones (o si se va a activar), actualizar persistencia
        repositorio.ActualizarEstadoCuenta(cuenta.Codigo, nuevoEstado);
        return nuevoEstado;
    }

    private static bool EsSubcuenta(string codigoPadre, string digitosPadre, string codigoHija)
    {
        var codigoHijaNormalizado = codigoHija.Trim();

        // Relación por prefijo directo (ej. 1101 -> 1101.01, 1101-01)
        if (codigoHijaNormalizado.StartsWith(codigoPadre, StringComparison.OrdinalIgnoreCase) &&
            codigoHijaNormalizado.Length > codigoPadre.Length)
        {
            return true;
        }

        // Relación por jerarquía numérica (ej. 1 -> 11, o 11 -> 1101)
        if (!string.IsNullOrEmpty(digitosPadre))
        {
            var digitosHija = new string(codigoHijaNormalizado.Where(char.IsDigit).ToArray());
            if (digitosHija.StartsWith(digitosPadre, StringComparison.Ordinal) &&
                digitosHija.Length > digitosPadre.Length)
            {
                return true;
            }
        }

        return false;
    }
}

