using SistemaContable.Dominio;

namespace SistemaContable.Servicios;

public record FilaEstadoResultados(
    string Codigo,
    string Nombre,
    TipoCuenta Tipo,
    decimal Saldo
);

public record EstadoResultadosReporte(
    List<FilaEstadoResultados> Ingresos,
    List<FilaEstadoResultados> Costos,
    List<FilaEstadoResultados> Gastos,
    decimal TotalIngresos,
    decimal TotalCostos,
    decimal UtilidadBruta,
    decimal TotalGastos,
    decimal UtilidadOperativa,
    decimal UtilidadNeta
);

public static class PeriodoHelper
{
    public static (DateTime? Desde, DateTime? Hasta, string Descripcion) ResolverRango(string? tipoPeriodo, DateTime? fechaReferencia)
    {
        var refDate = fechaReferencia ?? DateTime.Today;
        var tipo = (tipoPeriodo ?? "todo").Trim().ToLowerInvariant();

        switch (tipo)
        {
            case "mes":
            {
                var inicio = new DateTime(refDate.Year, refDate.Month, 1, 0, 0, 0, DateTimeKind.Local);
                var fin = inicio.AddMonths(1).AddTicks(-1);
                var desc = $"{inicio:MMMM yyyy}".ToUpperInvariant();
                return (inicio, fin, desc);
            }
            case "semestre":
            {
                int startMonth = refDate.Month <= 6 ? 1 : 7;
                var inicio = new DateTime(refDate.Year, startMonth, 1, 0, 0, 0, DateTimeKind.Local);
                var fin = startMonth == 1
                    ? new DateTime(refDate.Year, 6, 30, 23, 59, 59, 999, DateTimeKind.Local)
                    : new DateTime(refDate.Year, 12, 31, 23, 59, 59, 999, DateTimeKind.Local);
                var semStr = startMonth == 1 ? "1er Semestre" : "2do Semestre";
                var desc = $"{semStr} {refDate.Year} ({inicio:dd/MM} al {fin:dd/MM/yyyy})";
                return (inicio, fin, desc);
            }
            case "anio":
            case "año":
            {
                var inicio = new DateTime(refDate.Year, 1, 1, 0, 0, 0, DateTimeKind.Local);
                var fin = new DateTime(refDate.Year, 12, 31, 23, 59, 59, 999, DateTimeKind.Local);
                var desc = $"Año {refDate.Year}";
                return (inicio, fin, desc);
            }
            case "todo":
            default:
            {
                return (null, null, "Histórico Completo");
            }
        }
    }
}

