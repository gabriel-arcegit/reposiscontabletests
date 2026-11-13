using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public record FilaLibroMayor(
    string Codigo,
    string Nombre,
    TipoCuenta Tipo,
    NaturalezaSaldo Naturaleza,
    decimal TotalDebe,
    decimal TotalHaber,
    decimal SaldoFinal,
    decimal SaldoDeudor = 0m,
    decimal SaldoAcreedor = 0m,
    bool EsSaldoAnormal = false
);

public record FilaBalanzaComprobacion(
    string Codigo,
    string Nombre,
    decimal TotalDebe,
    decimal TotalHaber,
    decimal SaldoDeudor,
    decimal SaldoAcreedor,
    bool EsSaldoAnormal = false
);

public record BalanceGeneralReporte(
    decimal Activo,
    decimal Pasivo,
    decimal Capital,
    decimal UtilidadNeta,
    decimal CapitalAportado = 0m,
    bool TieneSaldosAnormales = false,
    DateTime? FechaCorte = null,
    string PeriodoDescripcion = ""
);

public class ReportesService(IRepositorioContable repositorio)
{
    public List<FilaLibroMayor> ObtenerLibroMayor(DateTime? hastaFecha = null)
    {
        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);
        var asientos = repositorio.ObtenerAsientos();
        if (hastaFecha.HasValue)
        {
            asientos = asientos.Where(a => a.Fecha <= hastaFecha.Value);
        }
        var lineas = asientos.SelectMany(a => a.Lineas);

        return lineas
            .GroupBy(l => l.CodigoCuenta)
            .Select(g =>
            {
                cuentas.TryGetValue(g.Key, out var c);
                var debe = g.Sum(l => l.Debe);
                var haber = g.Sum(l => l.Haber);
                var tipo = c?.Tipo ?? (g.Key.StartsWith("1") ? TipoCuenta.Activo :
                                       g.Key.StartsWith("2") ? TipoCuenta.Pasivo :
                                       g.Key.StartsWith("3") ? TipoCuenta.Capital :
                                       g.Key.StartsWith("4") ? TipoCuenta.Ingreso :
                                       g.Key.StartsWith("5") ? TipoCuenta.Costo : TipoCuenta.Gasto);
                var naturaleza = c?.Naturaleza ?? (tipo is TipoCuenta.Activo or TipoCuenta.Costo or TipoCuenta.Gasto
                    ? NaturalezaSaldo.Deudora
                    : NaturalezaSaldo.Acreedora);
                var nombre = c?.Nombre ?? g.First().NombreCuenta;

                // Regla de saldos no negativos:
                decimal dif = debe - haber;
                decimal saldoDeudor = dif > 0 ? dif : 0m;
                decimal saldoAcreedor = dif < 0 ? Math.Abs(dif) : 0m;
                decimal saldoFinal = Math.Abs(dif);

                bool esSaldoAnormal = tipo is TipoCuenta.Activo or TipoCuenta.Costo or TipoCuenta.Gasto
                    ? saldoAcreedor > 0m
                    : saldoDeudor > 0m;

                return new FilaLibroMayor(
                    g.Key,
                    nombre,
                    tipo,
                    naturaleza,
                    debe,
                    haber,
                    saldoFinal,
                    saldoDeudor,
                    saldoAcreedor,
                    esSaldoAnormal
                );
            })
            .OrderBy(m => m.Codigo)
            .ToList();
    }

    public List<FilaBalanzaComprobacion> ObtenerBalanzaComprobacion(DateTime? hastaFecha = null)
    {
        var mayor = ObtenerLibroMayor(hastaFecha);

        var balanza = mayor.Select(m =>
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
        }).ToList();

        var totalDeudores = balanza.Sum(b => b.SaldoDeudor);
        var totalAcreedores = balanza.Sum(b => b.SaldoAcreedor);

        if (totalDeudores != totalAcreedores)
            throw new DominioContableException($"Balanza de comprobación descuadrada: Saldos Deudores ({totalDeudores:N2}) != Saldos Acreedores ({totalAcreedores:N2}).");

        return balanza;
    }

    public BalanceGeneralReporte ObtenerBalanceGeneral(DateTime? hastaFecha = null, string periodoDescripcion = "")
    {
        var mayor = ObtenerLibroMayor(hastaFecha);

        var ingresos = mayor.Where(m => m.Tipo == TipoCuenta.Ingreso).Sum(m => m.TotalHaber - m.TotalDebe);
        var costos = mayor.Where(m => m.Tipo == TipoCuenta.Costo).Sum(m => m.TotalDebe - m.TotalHaber);
        var gastos = mayor.Where(m => m.Tipo == TipoCuenta.Gasto).Sum(m => m.TotalDebe - m.TotalHaber);
        var utilidadNeta = ingresos - costos - gastos;

        // Posición algebraica real para mantener Activo = Pasivo + Capital:
        // Activo = Suma de (Debe - Haber) de Activos + cuentas de Pasivo/Capital con saldo Deudor neto (anticipos a proveedores/socios)
        var activoNeto = mayor.Where(m => m.Tipo == TipoCuenta.Activo).Sum(m => m.TotalDebe - m.TotalHaber);
        var pasivosDeudores = mayor.Where(m => m.Tipo == TipoCuenta.Pasivo && (m.TotalDebe - m.TotalHaber) > 0m)
            .Sum(m => m.TotalDebe - m.TotalHaber);
        var activo = activoNeto + pasivosDeudores;

        // Pasivo = Suma de (Haber - Debe) de Pasivos exclusivamente con saldo acreedor
        var pasivo = mayor.Where(m => m.Tipo == TipoCuenta.Pasivo && (m.TotalHaber - m.TotalDebe) > 0m)
            .Sum(m => m.TotalHaber - m.TotalDebe);

        // Capital = Suma de (Haber - Debe) de Capital + Utilidad Neta
        var capitalAportado = mayor.Where(m => m.Tipo == TipoCuenta.Capital).Sum(m => m.TotalHaber - m.TotalDebe);
        var capital = capitalAportado + utilidadNeta;

        bool tieneSaldosAnormales = mayor.Any(m => m.EsSaldoAnormal);

        return new BalanceGeneralReporte(activo, pasivo, capital, utilidadNeta, capitalAportado, tieneSaldosAnormales, hastaFecha, periodoDescripcion);
    }
}
