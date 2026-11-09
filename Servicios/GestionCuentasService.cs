using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public record CrearCuentaDto(
    string Codigo,
    string Nombre,
    string Tipo,
    string Naturaleza,
    string? Nivel = "Subcuenta",
    string? CuentaPadreCodigo = null
);

public class GestionCuentasService(IRepositorioContable repositorio, ReportesService reportesService)
{
    /// <summary>
    /// Crea y registra una nueva cuenta contable aplicando validaciones de unicidad,
    /// formato de prefijo por tipo y coherencia contable.
    /// </summary>
    public CuentaContable CrearCuenta(CrearCuentaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Codigo))
            throw new DominioContableException("El código de la cuenta es obligatorio.");

        if (string.IsNullOrWhiteSpace(dto.Nombre))
            throw new DominioContableException("El nombre de la cuenta es obligatorio.");

        if (string.IsNullOrWhiteSpace(dto.Tipo))
            throw new DominioContableException("El tipo de cuenta es obligatorio.");

        if (string.IsNullOrWhiteSpace(dto.Naturaleza))
            throw new DominioContableException("La naturaleza de la cuenta es obligatoria.");

        var codigoLimpio = dto.Codigo.Trim();
        var nombreLimpio = dto.Nombre.Trim();

        var cuentas = repositorio.ObtenerCuentas().ToList();

        // 1. Validación de unicidad de código
        if (cuentas.Any(c => c.Codigo.Equals(codigoLimpio, StringComparison.OrdinalIgnoreCase)))
            throw new DominioContableException($"El código de cuenta '{codigoLimpio}' ya existe en el catálogo contable.");

        // 2. Validación de unicidad de nombre
        if (cuentas.Any(c => c.Nombre.Equals(nombreLimpio, StringComparison.OrdinalIgnoreCase)))
            throw new DominioContableException($"El nombre de cuenta '{nombreLimpio}' ya existe en el catálogo contable.");

        // 3. Parsear Tipo y Naturaleza
        if (!Enum.TryParse<TipoCuenta>(dto.Tipo, true, out var tipoEnum))
            throw new DominioContableException($"El tipo de cuenta '{dto.Tipo}' no es válido.");

        if (!Enum.TryParse<NaturalezaSaldo>(dto.Naturaleza, true, out var natEnum))
            throw new DominioContableException($"La naturaleza '{dto.Naturaleza}' no es válida.");

        // 4. Validación de formato: El código debe coincidir con el prefijo de su tipo
        // (1 = Activo, 2 = Pasivo, 3 = Capital, 4 = Ingreso, 5 = Costo, 6 = Gasto)
        char prefijoEsperado = tipoEnum switch
        {
            TipoCuenta.Activo => '1',
            TipoCuenta.Pasivo => '2',
            TipoCuenta.Capital => '3',
            TipoCuenta.Ingreso => '4',
            TipoCuenta.Costo => '5',
            TipoCuenta.Gasto => '6',
            _ => '0'
        };

        var primerDigito = codigoLimpio.FirstOrDefault(char.IsDigit);
        if (primerDigito != prefijoEsperado)
        {
            throw new DominioContableException(
                $"Inconsistencia de formato: Las cuentas de tipo '{tipoEnum}' deben iniciar con el dígito '{prefijoEsperado}' (1=Activo, 2=Pasivo, 3=Capital, 4=Ingreso, 5=Costo, 6=Gasto). Código ingresado: '{codigoLimpio}'."
            );
        }

        // 5. Nivel / Jerarquía (Mayor / Subcuenta)
        bool esMayor = (dto.Nivel ?? "").Equals("Mayor", StringComparison.OrdinalIgnoreCase);
        bool permiteMovimientos = !esMayor; // Subcuentas son transaccionales/operativas

        // 6. Cuenta Padre (opcional)
        if (!string.IsNullOrWhiteSpace(dto.CuentaPadreCodigo))
        {
            var padre = cuentas.FirstOrDefault(c => c.Codigo.Equals(dto.CuentaPadreCodigo.Trim(), StringComparison.OrdinalIgnoreCase));
            if (padre == null)
                throw new DominioContableException($"La cuenta padre '{dto.CuentaPadreCodigo}' no existe en el catálogo.");
        }

        var nuevaCuenta = new CuentaContable(codigoLimpio, nombreLimpio, tipoEnum, natEnum, permiteMovimientos, true);
        repositorio.AgregarCuenta(nuevaCuenta);
        return nuevaCuenta;
    }
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

