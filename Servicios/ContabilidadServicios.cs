using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public record BalanzaComprobacionReporte(
    List<FilaBalanzaComprobacion> Cuentas,
    decimal TotalDebe,
    decimal TotalHaber,
    decimal TotalSaldoDeudor,
    decimal TotalSaldoAcreedor,
    bool EstaCuadrada,
    decimal Diferencia = 0m,
    DateTime? FechaCorte = null,
    string PeriodoDescripcion = ""
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
    List<MovimientoFlujo> Salidas,
    bool EsSobregiro = false,
    decimal MontoSobregiro = 0m,
    decimal SaldoDisponible = 0m,
    string PeriodoDescripcion = ""
);

public record VariacionCapitalReporte(
    decimal CapitalInicial,
    decimal Aportes,
    decimal UtilidadEjercicio,
    decimal CapitalFinal
);

public class ContabilidadServicios(IRepositorioContable repositorio, ReportesService reportesService)
{
    public List<FilaLibroMayor> CalcularLibroMayor(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var (desde, hasta, _) = PeriodoHelper.ResolverRango(tipoPeriodo, fechaReferencia);
        // Para el Mayor acumulativo a la fecha de corte
        return reportesService.ObtenerLibroMayor(hasta);
    }

    public BalanzaComprobacionReporte CalcularBalanzaComprobacion(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var (desde, hasta, desc) = PeriodoHelper.ResolverRango(tipoPeriodo, fechaReferencia);
        var mayor = reportesService.ObtenerLibroMayor(hasta);

        var filas = mayor
            .Where(m => m.TotalDebe > 0 || m.TotalHaber > 0)
            .Select(m =>
            {
                decimal dif = m.TotalDebe - m.TotalHaber;
                decimal saldoDeudor = dif > 0 ? dif : 0m;
                decimal saldoAcreedor = dif < 0 ? Math.Abs(dif) : 0m;

                bool esSaldoAnormal = m.Tipo is TipoCuenta.Activo or TipoCuenta.Costo or TipoCuenta.Gasto
                    ? saldoAcreedor > 0m
                    : saldoDeudor > 0m;

                return new FilaBalanzaComprobacion(
                    m.Codigo,
                    m.Nombre,
                    m.TotalDebe,
                    m.TotalHaber,
                    saldoDeudor,
                    saldoAcreedor,
                    esSaldoAnormal
                );
            })
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
            diferencia,
            hasta,
            desc
        );
    }

    public BalanceGeneralReporte CalcularBalanceGeneral(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var (desde, hasta, desc) = PeriodoHelper.ResolverRango(tipoPeriodo, fechaReferencia);
        return reportesService.ObtenerBalanceGeneral(hasta, desc);
    }

    public EstadoResultadosReporte CalcularEstadoResultados(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var (desde, hasta, _) = PeriodoHelper.ResolverRango(tipoPeriodo, fechaReferencia);

        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);
        var asientos = repositorio.ObtenerAsientos();

        if (desde.HasValue)
            asientos = asientos.Where(a => a.Fecha >= desde.Value);
        if (hasta.HasValue)
            asientos = asientos.Where(a => a.Fecha <= hasta.Value);

        var lineas = asientos.SelectMany(a => a.Lineas).ToList();

        var agrupado = lineas
            .GroupBy(l => l.CodigoCuenta)
            .Select(g =>
            {
                cuentas.TryGetValue(g.Key, out var c);
                var debe = g.Sum(l => l.Debe);
                var haber = g.Sum(l => l.Haber);
                var tipo = c?.Tipo ?? (g.Key.StartsWith("4") ? TipoCuenta.Ingreso :
                                       g.Key.StartsWith("5") ? TipoCuenta.Costo :
                                       g.Key.StartsWith("6") ? TipoCuenta.Gasto : TipoCuenta.Activo);
                var nombre = c?.Nombre ?? g.First().NombreCuenta;
                decimal saldo = tipo switch
                {
                    TipoCuenta.Ingreso => haber - debe,
                    TipoCuenta.Costo or TipoCuenta.Gasto => debe - haber,
                    _ => 0m
                };
                return new FilaEstadoResultados(g.Key, nombre, tipo, Math.Max(0m, saldo));
            })
            .Where(f => f.Saldo > 0m)
            .OrderBy(f => f.Codigo)
            .ToList();

        var ingresos = agrupado.Where(f => f.Tipo == TipoCuenta.Ingreso || f.Codigo.StartsWith("4")).ToList();
        var costos = agrupado.Where(f => f.Tipo == TipoCuenta.Costo || f.Codigo.StartsWith("5")).ToList();
        var gastos = agrupado.Where(f => f.Tipo == TipoCuenta.Gasto || f.Codigo.StartsWith("6")).ToList();

        var totalIngresos = ingresos.Sum(i => i.Saldo);
        var totalCostos = costos.Sum(c => c.Saldo);
        var utilidadBruta = totalIngresos - totalCostos;
        var totalGastos = gastos.Sum(g => g.Saldo);
        var utilidadOperativa = utilidadBruta - totalGastos;
        var utilidadNeta = utilidadOperativa;

        return new EstadoResultadosReporte(
            ingresos,
            costos,
            gastos,
            totalIngresos,
            totalCostos,
            utilidadBruta,
            totalGastos,
            utilidadOperativa,
            utilidadNeta
        );
    }

    public List<AsientoDiario> ObtenerAsientosFiltrados(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var (desde, hasta, _) = PeriodoHelper.ResolverRango(tipoPeriodo, fechaReferencia);
        var asientos = repositorio.ObtenerAsientos();
        if (desde.HasValue)
            asientos = asientos.Where(a => a.Fecha >= desde.Value);
        if (hasta.HasValue)
            asientos = asientos.Where(a => a.Fecha <= hasta.Value);

        return asientos.OrderBy(a => a.Fecha).ThenBy(a => a.Id).ToList();
    }

    public void RegistrarAsiento(AsientoDiario asiento)
    {
        // 1. Validar partida doble y estructura del asiento
        asiento.ValidarYCuadrar();

        var asientosExistentes = repositorio.ObtenerAsientos().ToList();
        var lineasHistoricas = asientosExistentes.SelectMany(a => a.Lineas).ToList();
        var catalogo = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo, StringComparer.OrdinalIgnoreCase);

        // 2. Validación de Tesorería (No permitir gastar lo que no existe)
        static bool EsCuentaTesoreria(string cod) => cod.StartsWith("1101") || cod.StartsWith("1102");

        var lineasTesoreriaConSalida = asiento.Lineas
            .Where(l => EsCuentaTesoreria(l.CodigoCuenta) && l.Haber > 0m)
            .GroupBy(l => l.CodigoCuenta)
            .ToList();

        if (lineasTesoreriaConSalida.Count > 0)
        {
            foreach (var grupo in lineasTesoreriaConSalida)
            {
                var codigo = grupo.Key;
                var nombre = grupo.First().NombreCuenta;

                var montoEgreso = grupo.Sum(l => l.Haber);
                var cargosMismoAsiento = asiento.Lineas
                    .Where(l => l.CodigoCuenta == codigo)
                    .Sum(l => l.Debe);

                var lineasCuenta = lineasHistoricas.Where(l => l.CodigoCuenta == codigo).ToList();
                var debeHistorico = lineasCuenta.Sum(l => l.Debe);
                var haberHistorico = lineasCuenta.Sum(l => l.Haber);
                var saldoDisponible = debeHistorico - haberHistorico;

                if (montoEgreso > (saldoDisponible + cargosMismoAsiento))
                {
                    throw new BadHttpRequestException(
                        $"Error de disponibilidad: La cuenta '{nombre}' solo cuenta con un saldo disponible de C$ {saldoDisponible:N2}. No es posible registrar una salida de C$ {montoEgreso:N2} sin registrar fondos o apertura previa."
                    );
                }
            }
        }

        // 3. Validación de Pagos a Pasivos / Capital (No pagar deudas que no existen)
        var concepto = asiento.Concepto ?? string.Empty;
        bool esAnticipo = concepto.Contains("anticipo", StringComparison.OrdinalIgnoreCase);

        var lineasPasivoCapitalConDebito = asiento.Lineas
            .Where(l =>
            {
                if (l.Debe <= 0m) return false;
                if (catalogo.TryGetValue(l.CodigoCuenta, out var cuenta))
                {
                    return cuenta.Tipo is TipoCuenta.Pasivo or TipoCuenta.Capital;
                }
                return l.CodigoCuenta.StartsWith("2") || l.CodigoCuenta.StartsWith("3");
            })
            .GroupBy(l => l.CodigoCuenta)
            .ToList();

        if (lineasPasivoCapitalConDebito.Count > 0 && !esAnticipo)
        {
            foreach (var grupo in lineasPasivoCapitalConDebito)
            {
                var codigo = grupo.Key;
                var nombre = grupo.First().NombreCuenta;
                var montoDebe = grupo.Sum(l => l.Debe);

                var abonosMismoAsiento = asiento.Lineas
                    .Where(l => l.CodigoCuenta == codigo)
                    .Sum(l => l.Haber);

                var lineasCuenta = lineasHistoricas.Where(l => l.CodigoCuenta == codigo).ToList();
                var debeHistorico = lineasCuenta.Sum(l => l.Debe);
                var haberHistorico = lineasCuenta.Sum(l => l.Haber);
                var saldoAcreedorActual = haberHistorico - debeHistorico;

                if (saldoAcreedorActual <= 0m || montoDebe > (saldoAcreedorActual + abonosMismoAsiento))
                {
                    throw new BadHttpRequestException(
                        $"Operación inválida: No se puede registrar un abono/pago de C$ {montoDebe:N2} a '{nombre}' porque la deuda actual registrada es de C$ {saldoAcreedorActual:N2}."
                    );
                }
            }
        }

        // 4. Persistir asiento
        repositorio.GuardarAsiento(asiento);
    }

    public FlujoEfectivoReporte CalcularFlujoEfectivo(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var (desde, hasta, desc) = PeriodoHelper.ResolverRango(tipoPeriodo, fechaReferencia);
        var todosAsientos = repositorio.ObtenerAsientos().OrderBy(a => a.Fecha).ThenBy(a => a.Id).ToList();
        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);

        static bool EsEfectivo(string cod) => cod.StartsWith("1101") || cod.StartsWith("1102");

        // Saldo inicial previo al período seleccionado
        decimal saldoInicial = 0m;
        if (desde.HasValue)
        {
            var asientosPrevios = todosAsientos.Where(a => a.Fecha < desde.Value);
            foreach (var a in asientosPrevios)
            {
                var lineasEf = a.Lineas.Where(l => EsEfectivo(l.CodigoCuenta));
                saldoInicial += lineasEf.Sum(l => l.Debe - l.Haber);
            }
        }

        var asientosPeriodo = todosAsientos.AsEnumerable();
        if (desde.HasValue)
            asientosPeriodo = asientosPeriodo.Where(a => a.Fecha >= desde.Value);
        if (hasta.HasValue)
            asientosPeriodo = asientosPeriodo.Where(a => a.Fecha <= hasta.Value);

        var entradas = new List<MovimientoFlujo>();
        var salidas = new List<MovimientoFlujo>();

        foreach (var asiento in asientosPeriodo)
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
        }

        var totalEntradas = entradas.Sum(e => e.Monto);
        var totalSalidas = salidas.Sum(s => s.Monto);
        var variacionNeta = totalEntradas - totalSalidas;
        var saldoFinal = saldoInicial + variacionNeta;

        var esSobregiro = saldoFinal < 0m;
        var montoSobregiro = esSobregiro ? Math.Abs(saldoFinal) : 0m;
        var saldoDisponible = Math.Max(0m, saldoFinal);

        return new FlujoEfectivoReporte(
            totalEntradas,
            totalSalidas,
            variacionNeta,
            saldoInicial,
            saldoFinal,
            entradas,
            salidas,
            esSobregiro,
            montoSobregiro,
            saldoDisponible,
            desc
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

    public VariacionCapitalReporte CalcularVariacionCapital(string? tipoPeriodo = null, DateTime? fechaReferencia = null)
    {
        var mayor = CalcularLibroMayor(tipoPeriodo, fechaReferencia);
        var asientos = repositorio.ObtenerAsientos().ToList();
        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);

        var ingresos = mayor.Where(m => m.Tipo == TipoCuenta.Ingreso).Sum(m => m.SaldoAcreedor - m.SaldoDeudor);
        var costos = mayor.Where(m => m.Tipo == TipoCuenta.Costo).Sum(m => m.SaldoDeudor - m.SaldoAcreedor);
        var gastos = mayor.Where(m => m.Tipo == TipoCuenta.Gasto).Sum(m => m.SaldoDeudor - m.SaldoAcreedor);
        var utilidadEjercicio = ingresos - costos - gastos;

        var totalCapitalMayor = mayor.Where(m => m.Tipo == TipoCuenta.Capital).Sum(m => m.SaldoAcreedor - m.SaldoDeudor);

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

        if (capitalInicial == 0m && aportes == 0m && totalCapitalMayor != 0m)
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
