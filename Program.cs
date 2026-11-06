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
builder.Services.AddTransient<ReportesService>();
builder.Services.AddTransient<GestionCuentasService>();
builder.Services.AddTransient<ContabilidadServicios>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/cuentas", (IRepositorioContable repo) =>
    Results.Ok(repo.ObtenerCuentas()));

app.MapGet("/api/cuentas/operativas", (IRepositorioContable repo) =>
    Results.Ok(repo.ObtenerCuentasOperativas()));

app.MapPut("/api/cuentas/{codigo}/toggle-estado", (string codigo, GestionCuentasService service) =>
{
    try
    {
        var nuevoEstado = service.AlternarEstadoCuenta(codigo);
        return Results.Ok(new { codigo, estaActiva = nuevoEstado });
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

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

app.MapGet("/api/reportes/balanza", (ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanzaComprobacion());
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/flujo-efectivo", (ContabilidadServicios service) =>
    Results.Ok(service.CalcularFlujoEfectivo()));

app.MapGet("/api/reportes/variacion-capital", (ContabilidadServicios service) =>
    Results.Ok(service.CalcularVariacionCapital()));

app.Run();
