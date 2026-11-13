using System.Security.Cryptography;

namespace SistemaContable.Servicios;

public class SeguridadServicio
{
    private const int SaltSize = 16;      // 16 bytes
    private const int HashSize = 32;      // 32 bytes
    private const int Iterations = 100000; // 100,000 iteraciones

    public static (string Hash, string Salt) CrearHashConSalt(string textoPlano)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            textoPlano,
            saltBytes,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize
        );

        return (Convert.ToHexString(hashBytes), Convert.ToHexString(saltBytes));
    }

    public static bool VerificarHash(string textoPlano, string hashHex, string saltHex)
    {
        try
        {
            byte[] saltBytes = Convert.FromHexString(saltHex);
            byte[] expectedHashBytes = Convert.FromHexString(hashHex);

            byte[] actualHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                textoPlano,
                saltBytes,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize
            );

            return CryptographicOperations.FixedTimeEquals(actualHashBytes, expectedHashBytes);
        }
        catch
        {
            return false;
        }
    }

    public static string GenerarTokenSesion()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    public static string NormalizarRespuesta(string respuesta)
    {
        return (respuesta ?? string.Empty).Trim().ToLowerInvariant();
    }
}

