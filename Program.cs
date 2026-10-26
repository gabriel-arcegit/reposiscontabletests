using System.Text.Json.Serialization;
using SistemaContable.AccesoDatos;
using SistemaContable.Dominio;
using SistemaContable.Servicios;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<IRepositorioContable, SqliteRepositorioContable>();
builder.Services.AddSingleton<CalculadoraFiscalService>();
builder.Services.AddTransient<ReportesService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/cuentas/operativas", (IRepositorioContable repo) =>
    Results.Ok(repo.ObtenerCuentasOperativas()));

app.MapGet("/api/asientos", (IRepositorioContable repo) =>
    Results.Ok(repo.ObtenerAsientos()));

app.MapPost("/api/asientos", (AsientoDiario asiento, IRepositorioContable repo) =>
{
    try
    {
        asiento.ValidarYCuadrar();
        repo.GuardarAsiento(asiento);
        return Results.Created($"/api/asientos/{asiento.Id}", asiento);
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/mayor", (ReportesService reportes) =>
    Results.Ok(reportes.ObtenerLibroMayor()));

app.MapGet("/api/reportes/balance", (ReportesService reportes) =>
{
    try
    {
        return Results.Ok(reportes.ObtenerBalanceGeneral());
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/fiscal/calcular", (CalculoFiscalRequest req, CalculadoraFiscalService calc) =>
{
    if (req.AplicarImpuestos.HasValue) calc.AplicarImpuestos = req.AplicarImpuestos.Value;
    if (req.TasaIva.HasValue) calc.TasaIva = req.TasaIva.Value;
    if (req.TasaRetencion.HasValue) calc.TasaRetencion = req.TasaRetencion.Value;
    if (req.UmbralRetencion.HasValue) calc.UmbralRetencion = req.UmbralRetencion.Value;

    return Results.Ok(calc.Liquidar(req.Monto, req.IncluyeIva));
});

app.Run();

public record CalculoFiscalRequest(
    decimal Monto,
    bool IncluyeIva = false,
    bool? AplicarImpuestos = null,
    decimal? TasaIva = null,
    decimal? TasaRetencion = null,
    decimal? UmbralRetencion = null);
