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

// Forzar instanciación e inicialización de la BD al arranque
app.Services.GetRequiredService<IRepositorioContable>();

app.UseDefaultFiles();
app.UseStaticFiles();

// ==========================================
// Middleware de Autenticación / Autorización
// ==========================================
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

    // Proteger estrictamente /api/cuentas, /api/asientos y /api/reportes
    bool requiereAutenticacion = path.StartsWith("/api/cuentas") ||
                                path.StartsWith("/api/asientos") ||
                                path.StartsWith("/api/reportes");

    if (requiereAutenticacion)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Acceso no autorizado. Debe proporcionar un token de sesión válido." });
            return;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var repo = context.RequestServices.GetRequiredService<IRepositorioContable>();
        var usuario = repo.ObtenerUsuarioPorToken(token);

        if (usuario == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Sesión inválida o expirada. Por favor inicie sesión nuevamente." });
            return;
        }

        // Adjuntar usuario al contexto HTTP para endpoints que lo necesiten
        context.Items["UsuarioActual"] = usuario;
    }

    await next(context);
});

// ==========================================
// 0. Endpoints de Autenticación y Seguridad
// ==========================================
app.MapPost("/api/auth/login", (LoginRequest req, IRepositorioContable repo) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "El usuario y la contraseña son requeridos." });
    }

    var usuario = repo.ObtenerUsuarioPorUsername(req.Username.Trim());
    if (usuario == null)
    {
        return Results.Unauthorized();
    }

    bool passValido = SeguridadServicio.VerificarHash(req.Password, usuario.PasswordHash, usuario.Salt);
    if (!passValido)
    {
        return Results.Unauthorized();
    }

    // Generar token seguro con expiración de 8 horas
    string token = SeguridadServicio.GenerarTokenSesion();
    DateTime expiracion = DateTime.UtcNow.AddHours(8);
    repo.ActualizarTokenSesion(usuario.Id, token, expiracion);

    return Results.Ok(new
    {
        token,
        username = usuario.Username,
        nombreCompleto = usuario.NombreCompleto,
        rol = usuario.Rol
    });
});

app.MapPost("/api/auth/logout", (HttpContext context, IRepositorioContable repo) =>
{
    var authHeader = context.Request.Headers["Authorization"].ToString();
    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        var token = authHeader.Substring("Bearer ".Length).Trim();
        var usuario = repo.ObtenerUsuarioPorToken(token);
        if (usuario != null)
        {
            repo.ActualizarTokenSesion(usuario.Id, null, null);
        }
    }

    return Results.Ok(new { mensaje = "Sesión cerrada correctamente." });
});

app.MapPost("/api/auth/cambiar-password", (CambiarPasswordRequest req, HttpContext context, IRepositorioContable repo) =>
{
    var authHeader = context.Request.Headers["Authorization"].ToString();
    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var token = authHeader.Substring("Bearer ".Length).Trim();
    var usuario = repo.ObtenerUsuarioPorToken(token);
    if (usuario == null)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(req.PasswordActual) || string.IsNullOrWhiteSpace(req.PasswordNueva))
    {
        return Results.BadRequest(new { error = "La contraseña actual y la nueva contraseña son requeridas." });
    }

    if (!SeguridadServicio.VerificarHash(req.PasswordActual, usuario.PasswordHash, usuario.Salt))
    {
        return Results.BadRequest(new { error = "La contraseña actual es incorrecta." });
    }

    if (req.PasswordNueva.Trim().Length < 6)
    {
        return Results.BadRequest(new { error = "La nueva contraseña debe tener al menos 6 caracteres." });
    }

    var (nuevoHash, nuevoSalt) = SeguridadServicio.CrearHashConSalt(req.PasswordNueva.Trim());

    string? nuevaPregunta = null;
    string? nuevaRespHash = null;
    string? nuevaRespSalt = null;

    if (!string.IsNullOrWhiteSpace(req.PreguntaSeguridad) && !string.IsNullOrWhiteSpace(req.RespuestaSeguridad))
    {
        nuevaPregunta = req.PreguntaSeguridad.Trim();
        var respNorm = SeguridadServicio.NormalizarRespuesta(req.RespuestaSeguridad);
        var (rHash, rSalt) = SeguridadServicio.CrearHashConSalt(respNorm);
        nuevaRespHash = rHash;
        nuevaRespSalt = rSalt;
    }

    repo.ActualizarPassword(usuario.Id, nuevoHash, nuevoSalt, nuevaPregunta, nuevaRespHash, nuevaRespSalt);

    return Results.Ok(new { mensaje = "Contraseña y datos de seguridad actualizados exitosamente." });
});

app.MapGet("/api/auth/pregunta/{username}", (string username, IRepositorioContable repo) =>
{
    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.NotFound(new { error = "Usuario no especificado." });
    }

    var usuario = repo.ObtenerUsuarioPorUsername(username.Trim());
    if (usuario == null)
    {
        return Results.NotFound(new { error = "Usuario no encontrado." });
    }

    return Results.Ok(new
    {
        username = usuario.Username,
        preguntaSeguridad = usuario.PreguntaSeguridad
    });
});

app.MapPost("/api/auth/recuperar-password", (RecuperarPasswordRequest req, IRepositorioContable repo) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.RespuestaSeguridad) || string.IsNullOrWhiteSpace(req.PasswordNueva))
    {
        return Results.BadRequest(new { error = "Todos los campos son obligatorios." });
    }

    var usuario = repo.ObtenerUsuarioPorUsername(req.Username.Trim());
    if (usuario == null)
    {
        return Results.NotFound(new { error = "Usuario no encontrado." });
    }

    var respNorm = SeguridadServicio.NormalizarRespuesta(req.RespuestaSeguridad);
    bool coincide = SeguridadServicio.VerificarHash(respNorm, usuario.RespuestaHash, usuario.RespuestaSalt);

    if (!coincide)
    {
        return Results.BadRequest(new { error = "La respuesta a la pregunta de seguridad es incorrecta." });
    }

    if (req.PasswordNueva.Trim().Length < 6)
    {
        return Results.BadRequest(new { error = "La nueva contraseña debe tener al menos 6 caracteres." });
    }

    var (nuevoHash, nuevoSalt) = SeguridadServicio.CrearHashConSalt(req.PasswordNueva.Trim());
    repo.ActualizarPassword(usuario.Id, nuevoHash, nuevoSalt);

    return Results.Ok(new { mensaje = "Contraseña restablecida exitosamente. Ahora puede iniciar sesión con su nueva clave." });
});

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

app.MapDelete("/api/cuentas/{codigo}", (string codigo, GestionCuentasService service) =>
{
    try
    {
        service.EliminarCuenta(codigo);
        return Results.Ok(new { mensaje = $"Cuenta [{codigo}] eliminada exitosamente." });
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ==========================================
// 2. Asientos / Libro Diario
// ==========================================
app.MapGet("/api/asientos", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.ObtenerAsientosFiltrados(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

app.MapGet("/api/asientos/libro-diario", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.ObtenerAsientosFiltrados(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

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
app.MapGet("/api/reportes/mayor", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.CalcularLibroMayor(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

app.MapGet("/api/reportes/libro-mayor", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.CalcularLibroMayor(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

app.MapGet("/api/reportes/balanza", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanzaComprobacion(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/balanza-comprobacion", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanzaComprobacion(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/balance", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanceGeneral(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/balance-general", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
{
    try
    {
        return Results.Ok(service.CalcularBalanceGeneral(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin));
    }
    catch (DominioContableException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/reportes/estado-resultados", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.CalcularEstadoResultados(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

app.MapGet("/api/reportes/flujo-efectivo", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.CalcularFlujoEfectivo(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

app.MapGet("/api/reportes/variacion-capital", (string? tipoPeriodo, DateTime? fechaReferencia, DateTime? fechaInicio, DateTime? fechaFin, ContabilidadServicios service) =>
    Results.Ok(service.CalcularVariacionCapital(tipoPeriodo, fechaReferencia, fechaInicio, fechaFin)));

app.Run();

// ==========================================
// DTOs de Autenticación
// ==========================================
record LoginRequest(string? Username, string? Password);
record CambiarPasswordRequest(string? PasswordActual, string? PasswordNueva, string? PreguntaSeguridad, string? RespuestaSeguridad);
record RecuperarPasswordRequest(string? Username, string? RespuestaSeguridad, string? PasswordNueva);
