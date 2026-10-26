using Microsoft.Data.Sqlite;
using SistemaContable.Dominio;

namespace SistemaContable.AccesoDatos;

public class SqliteRepositorioContable : IRepositorioContable
{
    private readonly string _connectionString;

    public SqliteRepositorioContable(string connectionString = "Data Source=contabilidad.db")
    {
        _connectionString = connectionString;
        InicializarBaseDatos();
    }

    private void InicializarBaseDatos()
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
                    PermiteMovimientos INTEGER NOT NULL DEFAULT 1
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
                );";
            cmd.ExecuteNonQuery();
        }

        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT COUNT(*) FROM Cuentas;";
            var count = Convert.ToInt32(checkCmd.ExecuteScalar());
            if (count == 0)
            {
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

                using var seedTx = connection.BeginTransaction();
                foreach (var c in semilla)
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = seedTx;
                    insertCmd.CommandText = @"
                        INSERT INTO Cuentas (Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos)
                        VALUES ($cod, $nom, $tipo, $nat, 1);";
                    insertCmd.Parameters.AddWithValue("$cod", c.Codigo);
                    insertCmd.Parameters.AddWithValue("$nom", c.Nombre);
                    insertCmd.Parameters.AddWithValue("$tipo", (int)c.Tipo);
                    insertCmd.Parameters.AddWithValue("$nat", (int)c.Naturaleza);
                    insertCmd.ExecuteNonQuery();
                }
                seedTx.Commit();
            }
        }
    }

    public IEnumerable<CuentaContable> ObtenerCuentas()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos FROM Cuentas ORDER BY Codigo;";
        using var reader = cmd.ExecuteReader();
        var cuentas = new List<CuentaContable>();
        while (reader.Read())
        {
            cuentas.Add(new CuentaContable(
                reader.GetString(0),
                reader.GetString(1),
                (TipoCuenta)reader.GetInt32(2),
                (NaturalezaSaldo)reader.GetInt32(3),
                reader.GetInt32(4) == 1
            ));
        }
        return cuentas;
    }

    public IEnumerable<CuentaContable> ObtenerCuentasOperativas()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Codigo, Nombre, Tipo, Naturaleza, PermiteMovimientos FROM Cuentas WHERE PermiteMovimientos = 1 ORDER BY Codigo;";
        using var reader = cmd.ExecuteReader();
        var cuentas = new List<CuentaContable>();
        while (reader.Read())
        {
            cuentas.Add(new CuentaContable(
                reader.GetString(0),
                reader.GetString(1),
                (TipoCuenta)reader.GetInt32(2),
                (NaturalezaSaldo)reader.GetInt32(3),
                true
            ));
        }
        return cuentas;
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
}

