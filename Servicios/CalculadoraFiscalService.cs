namespace SistemaContable.Servicios;

public record LiquidacionFiscal(decimal Subtotal, decimal Iva, decimal Retencion, decimal TotalNeto);

public class CalculadoraFiscalService(
    bool aplicarImpuestos = false,
    decimal tasaIva = 0.15m,
    decimal tasaRetencion = 0.02m,
    decimal umbralRetencion = 1000m)
{
    public bool AplicarImpuestos { get; set; } = aplicarImpuestos;
    public decimal TasaIva { get; set; } = tasaIva;
    public decimal TasaRetencion { get; set; } = tasaRetencion;
    public decimal UmbralRetencion { get; set; } = umbralRetencion;

    public LiquidacionFiscal Liquidar(decimal monto, bool incluyeIva)
    {
        if (!AplicarImpuestos || monto <= 0)
            return new LiquidacionFiscal(monto, 0m, 0m, monto);

        var subtotal = incluyeIva
            ? Math.Round(monto / (1m + TasaIva), 2, MidpointRounding.AwayFromZero)
            : monto;

        var iva = incluyeIva
            ? monto - subtotal
            : Math.Round(subtotal * TasaIva, 2, MidpointRounding.AwayFromZero);

        var retencion = subtotal > UmbralRetencion
            ? Math.Round(subtotal * TasaRetencion, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new LiquidacionFiscal(subtotal, iva, retencion, subtotal + iva - retencion);
    }
}
