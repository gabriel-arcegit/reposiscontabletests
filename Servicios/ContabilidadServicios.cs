using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public record BalanzaComprobacionReporte(
    List<FilaBalanzaComprobacion> Cuentas,
    decimal TotalDebe,
    decimal TotalHaber,
    decimal TotalSaldoDeudor,
    decimal TotalSaldoAcreedor,
    bool EstaCuadrada,
    decimal Diferencia = 0m
)
{
    public List<FilaBalanzaComprobacion> Filas => Cuentas;
}

public record MovimientoFlujo(
    int AsientoId,
    DateTime Fecha,
    string Concepto,
    string Categoria,
    decimal Monto
);

public record FlujoEfectivoReporte(
    decimal TotalEntradas,
    decimal TotalSalidas,
    decimal VariacionNeta,
    decimal SaldoInicial,
    decimal SaldoFinal,
    List<MovimientoFlujo> Entradas,
    List<MovimientoFlujo> Salidas
);

public record VariacionCapitalReporte(
    decimal CapitalInicial,
    decimal Aportes,
    decimal UtilidadEjercicio,
    decimal CapitalFinal
);

public class ContabilidadServicios(IRepositorioContable repositorio, ReportesService reportesService)
{
    /// <summary>
    /// Retorna la Balanza de Comprobación para cada cuenta con movimientos:
    /// { Codigo, Nombre, TotalDebe, TotalHaber, SaldoDeudor, SaldoAcreedor } y la comprobación de que sum(Deudor) == sum(Acreedor).
    /// </summary>
    public BalanzaComprobacionReporte CalcularBalanzaComprobacion()
    {
        var mayor = reportesService.ObtenerLibroMayor();

        var filas = mayor
            .Where(m => m.TotalDebe > 0 || m.TotalHaber > 0)
            .Select(m => new FilaBalanzaComprobacion(
                m.Codigo,
                m.Nombre,
                m.TotalDebe,
                m.TotalHaber,
                m.TotalDebe > m.TotalHaber ? m.TotalDebe - m.TotalHaber : 0m,
                m.TotalHaber > m.TotalDebe ? m.TotalHaber - m.TotalDebe : 0m
            ))
            .ToList();

        var totalDebe = filas.Sum(f => f.TotalDebe);
        var totalHaber = filas.Sum(f => f.TotalHaber);
        var totalDeudor = filas.Sum(f => f.SaldoDeudor);
        var totalAcreedor = filas.Sum(f => f.SaldoAcreedor);
        var estaCuadrada = totalDeudor == totalAcreedor;
        var diferencia = Math.Abs(totalDeudor - totalAcreedor);

        return new BalanzaComprobacionReporte(
            filas,
            totalDebe,
            totalHaber,
            totalDeudor,
            totalAcreedor,
            estaCuadrada,
            diferencia
        );
    }

    /// <summary>
    /// Analiza los asientos que impactaron cuentas de efectivo/bancos (códigos que inicien con 1101 o 1102).
    /// Separa las entradas (cobros/ventas, aportes) de las salidas (compras, gastos) y calcula la variación neta y el saldo final de efectivo.
    /// </summary>
    public FlujoEfectivoReporte CalcularFlujoEfectivo()
    {
        var asientos = repositorio.ObtenerAsientos().OrderBy(a => a.Fecha).ThenBy(a => a.Id).ToList();
        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);

        static bool EsEfectivo(string cod) => cod.StartsWith("1101") || cod.StartsWith("1102");

        var entradas = new List<MovimientoFlujo>();
        var salidas = new List<MovimientoFlujo>();

        foreach (var asiento in asientos)
        {
            var lineasEfectivo = asiento.Lineas.Where(l => EsEfectivo(l.CodigoCuenta)).ToList();
            if (lineasEfectivo.Count == 0) continue;

            var contrapartidas = asiento.Lineas.Where(l => !EsEfectivo(l.CodigoCuenta)).ToList();
            var debeEfectivo = lineasEfectivo.Sum(l => l.Debe);
            var haberEfectivo = lineasEfectivo.Sum(l => l.Haber);

            if (debeEfectivo > haberEfectivo)
            {
                var monto = debeEfectivo - haberEfectivo;
                var categoria = DeterminarCategoriaEntrada(contrapartidas, cuentas);
                entradas.Add(new MovimientoFlujo(asiento.Id, asiento.Fecha, asiento.Concepto, categoria, monto));
            }
            else if (haberEfectivo > debeEfectivo)
            {
                var monto = haberEfectivo - debeEfectivo;
                var categoria = DeterminarCategoriaSalida(contrapartidas, cuentas);
                salidas.Add(new MovimientoFlujo(asiento.Id, asiento.Fecha, asiento.Concepto, categoria, monto));
            }
            // Si debeEfectivo == haberEfectivo es un traspaso interno entre cuentas de efectivo/bancos
        }

        var totalEntradas = entradas.Sum(e => e.Monto);
        var totalSalidas = salidas.Sum(s => s.Monto);
        var variacionNeta = totalEntradas - totalSalidas;
        var saldoInicial = 0m;
        var saldoFinal = saldoInicial + variacionNeta;

        return new FlujoEfectivoReporte(
            totalEntradas,
            totalSalidas,
            variacionNeta,
            saldoInicial,
            saldoFinal,
            entradas,
            salidas
        );
    }

    private static string DeterminarCategoriaEntrada(List<LineaAsiento> contrapartidas, Dictionary<string, CuentaContable> cuentas)
    {
        if (contrapartidas.Any(cp => cp.CodigoCuenta.StartsWith("4") || (cuentas.TryGetValue(cp.CodigoCuenta, out var c) && c.Tipo == TipoCuenta.Ingreso)))
            return "Cobros / Ventas";

        if (contrapartidas.Any(cp => cp.CodigoCuenta.StartsWith("3") || (cuentas.TryGetValue(cp.CodigoCuenta, out var c) && c.Tipo == TipoCuenta.Capital)))
            return "Aportes de Capital";

        if (contrapartidas.Any(cp => cp.CodigoCuenta.StartsWith("1103")))
            return "Cobros a Clientes";

        return "Otras Entradas";
    }

    private static string DeterminarCategoriaSalida(List<LineaAsiento> contrapartidas, Dictionary<string, CuentaContable> cuentas)
    {
        if (contrapartidas.Any(cp => cp.CodigoCuenta.StartsWith("5") || (cuentas.TryGetValue(cp.CodigoCuenta, out var c) && c.Tipo == TipoCuenta.Costo)))
            return "Compras / Costos";

        if (contrapartidas.Any(cp => cp.CodigoCuenta.StartsWith("6") || (cuentas.TryGetValue(cp.CodigoCuenta, out var c) && c.Tipo == TipoCuenta.Gasto)))
            return "Gastos Operativos";

        if (contrapartidas.Any(cp => cp.CodigoCuenta.StartsWith("2") || (cuentas.TryGetValue(cp.CodigoCuenta, out var c) && c.Tipo == TipoCuenta.Pasivo)))
            return "Pago a Proveedores / Pasivos";

        return "Otras Salidas";
    }

    /// <summary>
    /// Retorna { CapitalInicial, Aportes, UtilidadEjercicio, CapitalFinal } tomando el capital aportado de las cuentas tipo Capital
    /// y sumando la Utilidad Neta generada.
    /// </summary>
    public VariacionCapitalReporte CalcularVariacionCapital()
    {
        var mayor = reportesService.ObtenerLibroMayor();
        var asientos = repositorio.ObtenerAsientos().ToList();
        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);

        var ingresos = mayor.Where(m => m.Tipo == TipoCuenta.Ingreso).Sum(m => m.SaldoFinal);
        var costos = mayor.Where(m => m.Tipo == TipoCuenta.Costo).Sum(m => m.SaldoFinal);
        var gastos = mayor.Where(m => m.Tipo == TipoCuenta.Gasto).Sum(m => m.SaldoFinal);
        var utilidadEjercicio = ingresos - costos - gastos;

        var totalCapitalMayor = mayor.Where(m => m.Tipo == TipoCuenta.Capital).Sum(m => m.SaldoFinal);

        var lineasCapital = asientos
            .SelectMany(a => a.Lineas.Select(l => new { Asiento = a, Linea = l }))
            .Where(x => cuentas.TryGetValue(x.Linea.CodigoCuenta, out var c) ? c.Tipo == TipoCuenta.Capital : x.Linea.CodigoCuenta.StartsWith("3"))
            .ToList();

        decimal capitalInicial = 0m;
        decimal aportes = 0m;

        foreach (var item in lineasCapital)
        {
            var neto = item.Linea.Haber - item.Linea.Debe;
            var cto = item.Asiento.Concepto.ToLowerInvariant();
            if (cto.Contains("inicial") || cto.Contains("apertura"))
            {
                capitalInicial += neto;
            }
            else
            {
                aportes += neto;
            }
        }

        if (capitalInicial == 0m && aportes == 0m && totalCapitalMayor > 0m)
        {
            aportes = totalCapitalMayor;
        }

        var capitalFinal = capitalInicial + aportes + utilidadEjercicio;

        return new VariacionCapitalReporte(
            capitalInicial,
            aportes,
            utilidadEjercicio,
            capitalFinal
        );
    }
}

