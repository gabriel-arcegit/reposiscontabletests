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

// ==========================================
// 1. Catálogo de Cuentas
// ==========================================
app.MapGet("/api/cuentas", (IRepositorioContable repo) =>
    Results.Ok(repo.ObtenerCuentas()));

app.MapPost("/api/cuentas", (CrearCuentaDto dto, GestionCuentasService service) =>
{
    try
    {
        var cuenta = service.CrearCuenta(dto);
        return Results.Created($"/api/cuentas/{cuenta.Codigo}", cuenta);
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

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

// ==========================================
// 2. Asientos / Libro Diario
// ==========================================
app.MapGet("/api/asientos", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.ObtenerAsientosFiltrados(tipoPeriodo, fechaReferencia)));

app.MapGet("/api/asientos/libro-diario", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.ObtenerAsientosFiltrados(tipoPeriodo, fechaReferencia)));

app.MapPost("/api/asientos", (AsientoDiario asiento, ContabilidadServicios service) =>
{
    try
    {
        service.RegistrarAsiento(asiento);
        return Results.Created($"/api/asientos/{asiento.Id}", asiento);
    }
    catch (BadHttpRequestException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ==========================================
// 3. Reportes Contables y Financieros
// ==========================================
app.MapGet("/api/reportes/mayor", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.CalcularLibroMayor(tipoPeriodo, fechaReferencia)));

app.MapGet("/api/reportes/libro-mayor", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.CalcularLibroMayor(tipoPeriodo, fechaReferencia)));

app.MapGet("/api/reportes/balanza", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanzaComprobacion(tipoPeriodo, fechaReferencia));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/balanza-comprobacion", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanzaComprobacion(tipoPeriodo, fechaReferencia));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/balance", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanceGeneral(tipoPeriodo, fechaReferencia));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/balance-general", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanceGeneral(tipoPeriodo, fechaReferencia));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/estado-resultados", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.CalcularEstadoResultados(tipoPeriodo, fechaReferencia)));

app.MapGet("/api/reportes/flujo-efectivo", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.CalcularFlujoEfectivo(tipoPeriodo, fechaReferencia)));

app.MapGet("/api/reportes/variacion-capital", (string? tipoPeriodo, DateTime? fechaReferencia, ContabilidadServicios service) =>
    Results.Ok(service.CalcularVariacionCapital(tipoPeriodo, fechaReferencia)));

app.Run();
