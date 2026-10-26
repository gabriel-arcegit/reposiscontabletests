using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public record FilaLibroMayor(string Codigo, string Nombre, TipoCuenta Tipo, NaturalezaSaldo Naturaleza, decimal TotalDebe, decimal TotalHaber, decimal SaldoFinal);

public record FilaBalanzaComprobacion(string Codigo, string Nombre, decimal TotalDebe, decimal TotalHaber, decimal SaldoDeudor, decimal SaldoAcreedor);

public record BalanceGeneralReporte(decimal Activo, decimal Pasivo, decimal Capital, decimal UtilidadNeta);

public class ReportesService(IRepositorioContable repositorio)
{
    public List<FilaLibroMayor> ObtenerLibroMayor()
    {
        var cuentas = repositorio.ObtenerCuentas().ToDictionary(c => c.Codigo);
        var lineas = repositorio.ObtenerAsientos().SelectMany(a => a.Lineas);

        return lineas
            .GroupBy(l => l.CodigoCuenta)
            .Select(g =>
            {
                cuentas.TryGetValue(g.Key, out var c);
                var debe = g.Sum(l => l.Debe);
                var haber = g.Sum(l => l.Haber);
                var naturaleza = c?.Naturaleza ?? (debe >= haber ? NaturalezaSaldo.Deudora : NaturalezaSaldo.Acreedora);
                var tipo = c?.Tipo ?? TipoCuenta.Activo;
                var nombre = c?.Nombre ?? g.First().NombreCuenta;
                var saldo = naturaleza == NaturalezaSaldo.Deudora ? debe - haber : haber - debe;

                return new FilaLibroMayor(g.Key, nombre, tipo, naturaleza, debe, haber, saldo);
            })
            .OrderBy(m => m.Codigo)
            .ToList();
    }

    public List<FilaBalanzaComprobacion> ObtenerBalanzaComprobacion()
    {
        var mayor = ObtenerLibroMayor();

        var balanza = mayor.Select(m => new FilaBalanzaComprobacion(
            m.Codigo,
            m.Nombre,
            m.TotalDebe,
            m.TotalHaber,
            m.TotalDebe > m.TotalHaber ? m.TotalDebe - m.TotalHaber : 0m,
            m.TotalHaber > m.TotalDebe ? m.TotalHaber - m.TotalDebe : 0m
        )).ToList();

        var totalDeudores = balanza.Sum(b => b.SaldoDeudor);
        var totalAcreedores = balanza.Sum(b => b.SaldoAcreedor);

        if (totalDeudores != totalAcreedores)
            throw new DominioContableException($"Balanza de comprobación descuadrada: Saldos Deudores ({totalDeudores:N2}) != Saldos Acreedores ({totalAcreedores:N2}).");

        return balanza;
    }

    public BalanceGeneralReporte ObtenerBalanceGeneral()
    {
        var mayor = ObtenerLibroMayor();

        var ingresos = mayor.Where(m => m.Tipo == TipoCuenta.Ingreso).Sum(m => m.SaldoFinal);
        var costos = mayor.Where(m => m.Tipo == TipoCuenta.Costo).Sum(m => m.SaldoFinal);
        var gastos = mayor.Where(m => m.Tipo == TipoCuenta.Gasto).Sum(m => m.SaldoFinal);
        var utilidadNeta = ingresos - costos - gastos;

        var activo = mayor.Where(m => m.Tipo == TipoCuenta.Activo).Sum(m => m.SaldoFinal);
        var pasivo = mayor.Where(m => m.Tipo == TipoCuenta.Pasivo).Sum(m => m.SaldoFinal);
        var capital = mayor.Where(m => m.Tipo == TipoCuenta.Capital).Sum(m => m.SaldoFinal) + utilidadNeta;

        if (activo != pasivo + capital)
            throw new DominioContableException($"Descuadre en Balance General: Activo ({activo:N2}) != Pasivo ({pasivo:N2}) + Capital ({capital:N2}).");

        return new BalanceGeneralReporte(activo, pasivo, capital, utilidadNeta);
    }
}
