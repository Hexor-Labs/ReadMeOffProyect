using HubNegocios.BillingService.Domain.Entities;

namespace HubNegocios.BillingService.Domain.Ports;

/// <summary>
/// Comprueba que un webhook viene de verdad del proveedor.
///
/// Es un puerto y no una clase concreta por dos razones: cada pasarela firma a
/// su manera, y las pruebas necesitan poder afirmar que el caso de uso rechaza
/// una firma mala SIN depender de un secreto de configuración.
///
/// El contrato recibe el CUERPO EN BRUTO, no un objeto ya deserializado. Es
/// deliberado: la firma cubre los bytes exactos que mandó el proveedor, y
/// cualquier ida y vuelta por un deserializador (reordenar claves, normalizar
/// números, cambiar el escapado) los altera y la firma deja de cuadrar. Además
/// obliga a verificar ANTES de interpretar nada.
/// </summary>
public interface IWebhookSignatureVerifier
{
    /// <summary>
    /// <c>true</c> solo si <paramref name="signature"/> es el HMAC correcto de
    /// <paramref name="rawBody"/>. Ante cualquier duda —secreto sin configurar,
    /// cabecera ausente o mal formada— devuelve <c>false</c>: en un endpoint
    /// público el estado por defecto es rechazar.
    /// </summary>
    bool IsSignatureValid(PaymentProvider provider, ReadOnlyMemory<byte> rawBody, string? signature);
}
