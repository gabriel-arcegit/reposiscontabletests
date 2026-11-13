using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SistemaContable.Dominio;

namespace SistemaContable.AccesoDatos;

public class SqliteRepositorioContable : IRepositorioContable
{
    private readonly string _connectionString;

    private record CuentaSeedDto(
        [property: JsonPropertyName("codigo")] string? Codigo,
        [property: JsonPropertyName("nombre")] string? Nombre,
        [property: JsonPropertyName("tipo")] string? Tipo,
        [property: JsonPropertyName("naturaleza")] string? Naturaleza,
        [property: JsonPropertyName("permiteMovimientos")] bool PermiteMovimientos = true,
        [property: JsonPropertyName("estaActiva")] bool EstaActiva = false
    );

    public SqliteRepositorioContable(string connectionString = "Data Source=contabilidad.db")
    {
        _connectionString = connectionString;
        InicializarBD();
    }

    public void InicializarBD()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Cuentas (
                    Codigo TEXT PRIMARY KEY,
                    Nombre TEXT NOT NULL,
                    Tipo INTEGER NOT NULL,
                    Naturaleza INTEGER NOT NULL,
                    PermiteMovimientos INTEGER NOT NULL DEFAULT 1,
                    EstaActiva INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS Asientos (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Fecha TEXT NOT NULL,
                    Concepto TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS LineasAsiento (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AsientoId INTEGER NOT NULL,
                    CodigoCuenta TEXT NOT NULL,
                    NombreCuenta TEXT NOT NULL,
                    Debe REAL NOT NULL,
                    Haber REAL NOT NULL,
                    FOREIGN KEY (AsientoId) REFERENCES Asientos(Id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS Usuarios (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NombreCompleto TEXT NOT NULL,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    Salt TEXT NOT NULL,
                    Rol TEXT NOT NULL DEFAULT 'Admin',
                    PreguntaSeguridad TEXT NOT NULL,
                    RespuestaHash TEXT NOT NULL,
                    RespuestaSalt TEXT NOT NULL,
                    TokenSesion TEXT NULL,
                    ExpiracionSesion TEXT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        // Seeder inicial de usuario si la tabla Usuarios está vacía
        using (var checkUserCmd = connection.CreateCommand())
        {
            checkUserCmd.CommandText = "SELECT COUNT(*) FROM Usuarios;";
            var userCount = Convert.ToInt32(checkUserCmd.ExecuteScalar());
            if (userCount == 0)
            {
                var (passHash, passSalt) = SistemaContable.Servicios.SeguridadServicio.CrearHashConSalt("Metro2026*");
                var respNormalizada = SistemaContable.Servicios.SeguridadServicio.NormalizarRespuesta("metro repuestos");
                var (respHash, respSalt) = SistemaContable.Servicios.SeguridadServicio.CrearHashConSalt(respNormalizada);

                using var insertUserCmd = connection.CreateCommand();
                insertUserCmd.CommandText = @"
                    INSERT INTO Usuarios (NombreCompleto, Username, PasswordHash, Salt, Rol, PreguntaSeguridad, RespuestaHash, RespuestaSalt)
                    VALUES ($nombre, $username, $pHash, $pSalt, $rol, $pregunta, $rHash, $rSalt);";
                insertUserCmd.Parameters.AddWithValue("$nombre", "Administrador Metro Repuestos");
                insertUserCmd.Parameters.AddWithValue("$username", "admin");
                insertUserCmd.Parameters.AddWithValue("$pHash", passHash);
                insertUserCmd.Parameters.AddWithValue("$pSalt", passSalt);
                insertUserCmd.Parameters.AddWithValue("$rol", "Admin");
                insertUserCmd.Parameters.AddWithValue("$pregunta", "¿Nombre del negocio?");
                insertUserCmd.Parameters.AddWithValue("$rHash", respHash);
                insertUserCmd.Parameters.AddWithValue("$rSalt", respSalt);
                insertUserCmd.ExecuteNonQuery();
            }
        }

        // Migración preventiva: si la tabla Cuentas existía previamente, asegurar la columna EstaActiva
        using (var checkColCmd = connection.CreateCommand())
        {
            checkColCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cuentas') WHERE name = 'EstaActiva';";
            var existeColumna = Convert.ToInt32(checkColCmd.ExecuteScalar()) > 0;
            if (!existeColumna)
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Cuentas ADD COLUMN EstaActiva INTEGER NOT NULL DEFAULT 1;";
                alterCmd.ExecuteNonQuery();
            }
        }

        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM Cuentas;";
            var count = Convert.ToInt32(checkCmd.ExecuteScalar());
            if (count == 0)
            {
                // Buscar si existe el archivo catalogo_extraido.json
                string[] posiblesRutas = [
                    Path.Combine(Directory.GetCurrentDirectory(), "catalogo_extraido.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalogo_extraido.json"),
                    "catalogo_extraido.json"
                ];

                string? rutaJson = posiblesRutas.FirstOrDefault(File.Exists);

                if (rutaJson != null)
                {
                    try
                    {
                        var jsonContenido = File.ReadAllText(rutaJson);
                        var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var cuentasJson = JsonSerializer.Deserialize<List<CuentaSeedDto>>(jsonContenido, opciones);

                        if (cuentasJson != null && cuentasJson.Count > 0)
                        {
                            using var seedTx = connection.BeginTransaction();
                            using var insertCmd = connection.CreateCommand();
                            insertCmd.Transaction = seedTx;
                            insertCmd.CommandText = @"
                                INSERT INTO Cuentas (Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos, EstaActiva)
                                VALUES ($cod, $nom, $tipo, $nat, $permite, $activa);";

                            var pCod = insertCmd.Parameters.Add("$cod", SqliteType.Text);
                            var pNom = insertCmd.Parameters.Add("$nom", SqliteType.Text);
                            var pTipo = insertCmd.Parameters.Add("$tipo", SqliteType.Integer);
                            var pNat = insertCmd.Parameters.Add("$nat", SqliteType.Integer);
                            var pPermite = insertCmd.Parameters.Add("$permite", SqliteType.Integer);
                            var pActiva = insertCmd.Parameters.Add("$activa", SqliteType.Integer);

                            foreach (var c in cuentasJson)
                            {
                                if (string.IsNullOrWhiteSpace(c.Codigo) || string.IsNullOrWhiteSpace(c.Nombre))
                                    continue;

                                if (!Enum.TryParse<TipoCuenta>(c.Tipo, true, out var tipoEnum))
                                {
                                    var primerDigito = c.Codigo.Trim().FirstOrDefault(char.IsDigit);
                                    tipoEnum = primerDigito switch
                                    {
                                        '1' => TipoCuenta.Activo,
                                        '2' => TipoCuenta.Pasivo,
                                        '3' => TipoCuenta.Capital,
                                        '4' => TipoCuenta.Ingreso,
                                        '5' => TipoCuenta.Costo,
                                        '6' => TipoCuenta.Gasto,
                                        _ => TipoCuenta.Activo
                                    };
                                }

                                if (!Enum.TryParse<NaturalezaSaldo>(c.Naturaleza, true, out var natEnum))
                                {
                                    natEnum = (tipoEnum == TipoCuenta.Activo || tipoEnum == TipoCuenta.Costo || tipoEnum == TipoCuenta.Gasto)
                                        ? NaturalezaSaldo.Deudora
                                        : NaturalezaSaldo.Acreedora;
                                }

                                pCod.Value = c.Codigo.Trim();
                                pNom.Value = c.Nombre.Trim();
                                pTipo.Value = (int)tipoEnum;
                                pNat.Value = (int)natEnum;
                                pPermite.Value = c.PermiteMovimientos ? 1 : 0;
                                pActiva.Value = c.EstaActiva ? 1 : 0;

                                insertCmd.ExecuteNonQuery();
                            }

                            seedTx.Commit();
                            return;
                        }
                    }
                    catch
                    {
                        // En caso de cualquier error al leer el JSON, proceder con semilla por defecto
                    }
                }

                // Semilla por defecto si no existe catalogo_extraido.json
                var semilla = new (string Codigo, string Nombre, TipoCuenta Tipo, NaturalezaSaldo Naturaleza)[]
                {
                    ("1101", "Caja", TipoCuenta.Activo, NaturalezaSaldo.Deudora),
                    ("1102", "Bancos", TipoCuenta.Activo, NaturalezaSaldo.Deudora),
                    ("1103", "Clientes", TipoCuenta.Activo, NaturalezaSaldo.Deudora),
                    ("1104", "IVA Acreditable", TipoCuenta.Activo, NaturalezaSaldo.Deudora),
                    ("2101", "Proveedores", TipoCuenta.Pasivo, NaturalezaSaldo.Acreedora),
                    ("2102", "IVA Débito", TipoCuenta.Pasivo, NaturalezaSaldo.Acreedora),
                    ("3101", "Capital", TipoCuenta.Capital, NaturalezaSaldo.Acreedora),
                    ("4101", "Ventas", TipoCuenta.Ingreso, NaturalezaSaldo.Acreedora),
                    ("5101", "Costos", TipoCuenta.Costo, NaturalezaSaldo.Deudora),
                    ("6101", "Gastos", TipoCuenta.Gasto, NaturalezaSaldo.Deudora)
                };

                using var fallbackTx = connection.BeginTransaction();
                foreach (var c in semilla)
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = fallbackTx;
                    insertCmd.CommandText = @"
                        INSERT INTO Cuentas (Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos, EstaActiva)
                        VALUES ($cod, $nom, $tipo, $nat, 1, 1);";
                    insertCmd.Parameters.AddWithValue("$cod", c.Codigo);
                    insertCmd.Parameters.AddWithValue("$nom", c.Nombre);
                    insertCmd.Parameters.AddWithValue("$tipo", (int)c.Tipo);
                    insertCmd.Parameters.AddWithValue("$nat", (int)c.Naturaleza);
                    insertCmd.ExecuteNonQuery();
                }
                fallbackTx.Commit();
            }
        }
    }

    public IEnumerable<CuentaContable> ObtenerCuentas()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos, EstaActiva FROM Cuentas ORDER BY Codigo;";
        using var reader = cmd.ExecuteReader();
        var cuentas = new List<CuentaContable>();
        while (reader.Read())
        {
            cuentas.Add(new CuentaContable(
                reader.GetString(0),
                reader.GetString(1),
                (TipoCuenta)reader.GetInt32(2),
                (NaturalezaSaldo)reader.GetInt32(3),
                reader.GetInt32(4) == 1,
                reader.GetInt32(5) == 1
            ));
        }
        return cuentas;
    }

    public IEnumerable<CuentaContable> ObtenerCuentasOperativas()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos, EstaActiva FROM Cuentas WHERE PermiteMovimientos = 1 AND EstaActiva = 1 ORDER BY Codigo;";
        using var reader = cmd.ExecuteReader();
        var cuentas = new List<CuentaContable>();
        while (reader.Read())
        {
            cuentas.Add(new CuentaContable(
                reader.GetString(0),
                reader.GetString(1),
                (TipoCuenta)reader.GetInt32(2),
                (NaturalezaSaldo)reader.GetInt32(3),
                reader.GetInt32(4) == 1,
                reader.GetInt32(5) == 1
            ));
        }
        return cuentas;
    }

    public void ActualizarEstadoCuenta(string codigo, bool estaActiva)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El código de la cuenta no puede estar vacío.", nameof(codigo));

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Cuentas SET EstaActiva = @estado WHERE Codigo = @codigo;";
        cmd.Parameters.AddWithValue("@estado", estaActiva ? 1 : 0);
        cmd.Parameters.AddWithValue("@codigo", codigo.Trim());
        cmd.ExecuteNonQuery();
    }

    public void AgregarCuenta(CuentaContable cuenta)
    {
        if (cuenta == null)
            throw new ArgumentNullException(nameof(cuenta));

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Cuentas (Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos, EstaActiva)
            VALUES (@cod, @nom, @tipo, @nat, @permite, @activa);";
        cmd.Parameters.AddWithValue("@cod", cuenta.Codigo);
        cmd.Parameters.AddWithValue("@nom", cuenta.Nombre);
        cmd.Parameters.AddWithValue("@tipo", (int)cuenta.Tipo);
        cmd.Parameters.AddWithValue("@nat", (int)cuenta.Naturaleza);
        cmd.Parameters.AddWithValue("@permite", cuenta.PermiteMovimientos ? 1 : 0);
        cmd.Parameters.AddWithValue("@activa", cuenta.EstaActiva ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void EliminarCuenta(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El código de la cuenta no puede estar vacío.", nameof(codigo));

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Cuentas WHERE Codigo = @codigo;";
        cmd.Parameters.AddWithValue("@codigo", codigo.Trim());
        cmd.ExecuteNonQuery();
    }

    public void GuardarAsiento(AsientoDiario asiento)
    {
        asiento.ValidarYCuadrar();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Asientos (Fecha, Concepto) VALUES ($fecha, $concepto);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$fecha", asiento.Fecha.ToString("o"));
            cmd.Parameters.AddWithValue("$concepto", asiento.Concepto);

            var asientoId = Convert.ToInt32(cmd.ExecuteScalar());
            asiento.Id = asientoId;

            foreach (var linea in asiento.Lineas)
            {
                using var cmdLinea = connection.CreateCommand();
                cmdLinea.Transaction = transaction;
                cmdLinea.CommandText = @"
                    INSERT INTO LineasAsiento (AsientoId, CodigoCuenta, NombreCuenta, Debe, Haber)
                    VALUES ($asientoId, $codigo, $nombre, $debe, $haber);";
                cmdLinea.Parameters.AddWithValue("$asientoId", asientoId);
                cmdLinea.Parameters.AddWithValue("$codigo", linea.CodigoCuenta);
                cmdLinea.Parameters.AddWithValue("$nombre", linea.NombreCuenta);
                cmdLinea.Parameters.AddWithValue("$debe", (double)linea.Debe);
                cmdLinea.Parameters.AddWithValue("$haber", (double)linea.Haber);
                cmdLinea.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public IEnumerable<AsientoDiario> ObtenerAsientos()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var asientos = new List<AsientoDiario>();
        var map = new Dictionary<int, AsientoDiario>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Fecha, Concepto FROM Asientos ORDER BY Fecha ASC, Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fecha = DateTime.Parse(reader.GetString(1));
                var concepto = reader.GetString(2);
                var asiento = new AsientoDiario(id, fecha, concepto, []);
                asientos.Add(asiento);
                map[id] = asiento;
            }
        }

        if (map.Count > 0)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT AsientoId, CodigoCuenta, NombreCuenta, Debe, Haber FROM LineasAsiento ORDER BY Id ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var asientoId = reader.GetInt32(0);
                if (map.TryGetValue(asientoId, out var asiento))
                {
                    asiento.Lineas.Add(new LineaAsiento(
                        reader.GetString(1),
                        reader.GetString(2),
                        Convert.ToDecimal(reader.GetDouble(3)),
                        Convert.ToDecimal(reader.GetDouble(4))
                    ));
                }
            }
        }

        return asientos;
    }

    public UsuarioSistema? ObtenerUsuarioPorUsername(string username)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, NombreCompleto, Username, PasswordHash, Salt, Rol, 
                   PreguntaSeguridad, RespuestaHash, RespuestaSalt, TokenSesion, ExpiracionSesion
            FROM Usuarios 
            WHERE LOWER(Username) = LOWER($username)
            LIMIT 1;";
        cmd.Parameters.AddWithValue("$username", username.Trim());

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new UsuarioSistema
            {
                Id = reader.GetInt32(0),
                NombreCompleto = reader.GetString(1),
                Username = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                Salt = reader.GetString(4),
                Rol = reader.GetString(5),
                PreguntaSeguridad = reader.GetString(6),
                RespuestaHash = reader.GetString(7),
                RespuestaSalt = reader.GetString(8),
                TokenSesion = reader.IsDBNull(9) ? null : reader.GetString(9),
                ExpiracionSesion = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10))
            };
        }

        return null;
    }

    public UsuarioSistema? ObtenerUsuarioPorToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, NombreCompleto, Username, PasswordHash, Salt, Rol, 
                   PreguntaSeguridad, RespuestaHash, RespuestaSalt, TokenSesion, ExpiracionSesion
            FROM Usuarios 
            WHERE TokenSesion = $token
            LIMIT 1;";
        cmd.Parameters.AddWithValue("$token", token.Trim());

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var expStr = reader.IsDBNull(10) ? null : reader.GetString(10);
            DateTime? expiracion = expStr != null ? DateTime.Parse(expStr) : null;

            // Si ha expirado, no es válido
            if (expiracion.HasValue && expiracion.Value < DateTime.UtcNow)
            {
                return null;
            }

            return new UsuarioSistema
            {
                Id = reader.GetInt32(0),
                NombreCompleto = reader.GetString(1),
                Username = reader.GetString(2),
                PasswordHash = reader.GetString(3),
                Salt = reader.GetString(4),
                Rol = reader.GetString(5),
                PreguntaSeguridad = reader.GetString(6),
                RespuestaHash = reader.GetString(7),
                RespuestaSalt = reader.GetString(8),
                TokenSesion = reader.IsDBNull(9) ? null : reader.GetString(9),
                ExpiracionSesion = expiracion
            };
        }

        return null;
    }

    public void ActualizarTokenSesion(int usuarioId, string? token, DateTime? expiracion)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE Usuarios 
            SET TokenSesion = $token, ExpiracionSesion = $expiracion
            WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$token", (object?)token ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$expiracion", expiracion.HasValue ? expiracion.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$id", usuarioId);

        cmd.ExecuteNonQuery();
    }

    public void ActualizarPassword(int usuarioId, string passwordHash, string salt, string? pregunta = null, string? respuestaHash = null, string? respuestaSalt = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        if (!string.IsNullOrWhiteSpace(pregunta) && !string.IsNullOrWhiteSpace(respuestaHash) && !string.IsNullOrWhiteSpace(respuestaSalt))
        {
            cmd.CommandText = @"
                UPDATE Usuarios 
                SET PasswordHash = $pHash, Salt = $salt,
                    PreguntaSeguridad = $pregunta, RespuestaHash = $rHash, RespuestaSalt = $rSalt
                WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$pregunta", pregunta);
            cmd.Parameters.AddWithValue("$rHash", respuestaHash);
            cmd.Parameters.AddWithValue("$rSalt", respuestaSalt);
        }
        else
        {
            cmd.CommandText = @"
                UPDATE Usuarios 
                SET PasswordHash = $pHash, Salt = $salt
                WHERE Id = $id;";
        }

        cmd.Parameters.AddWithValue("$pHash", passwordHash);
        cmd.Parameters.AddWithValue("$salt", salt);
        cmd.Parameters.AddWithValue("$id", usuarioId);

        cmd.ExecuteNonQuery();
    }
}
