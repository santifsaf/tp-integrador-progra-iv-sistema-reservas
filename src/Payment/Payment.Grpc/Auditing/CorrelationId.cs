using Grpc.Core;

namespace Payment.Grpc.Auditing;

// El correlation id es el mismo RequestId que usa Availability y que Booking manda en el
// mensaje del broker. Booking lo reenvía en la metadata gRPC para que los logs de Payment
// se puedan unir con los suyos.
//
// Si el cliente no lo manda (por ejemplo, una prueba manual con grpcurl), se usa el trace
// id de la actividad actual o se genera uno. Nunca se deja vacío: sin correlation id no se
// puede reconstruir la cadena de sucesos que pide la consigna 3f.
public static class CorrelationId
{
    public const string MetadataKey = "x-correlation-id";
    public const string RequestIdMetadataKey = "x-request-id";
    public const int MaxLength = 128;

    public static string Resolve(Metadata requestHeaders, string? fallback = null)
    {
        var fromMetadata = GetValue(requestHeaders, MetadataKey)
            ?? GetValue(requestHeaders, RequestIdMetadataKey);

        return Normalize(fromMetadata)
            ?? Normalize(fallback)
            ?? Guid.NewGuid().ToString("N");
    }

    private static string? GetValue(Metadata headers, string key)
    {
        foreach (var entry in headers)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            return null;
        }

        // Un cliente no debería poder inyectar saltos de línea ni basura en los logs.
        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
            {
                return null;
            }
        }

        return trimmed;
    }
}
