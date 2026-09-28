using HubNegocios.NotificationService.Domain.Entities;

namespace HubNegocios.NotificationService.Domain.Ports;

/// <summary>Lo que sale por el canal. Se construye, se manda y no se guarda.</summary>
public sealed record OutgoingMessage(Guid? RecipientId, string Destination, string Subject, string Body);

/// <summary>
/// Un canal de salida. Es el patrón Strategy, y aquí paga por algo concreto.
///
/// Los casos de uso no saben si detrás hay SendGrid, Twilio o un adaptador que
/// solo escribe en el log: piden «manda esto por este canal» y se acabó. El día
/// que se conecte el proveedor real se escribe una implementación nueva, se
/// cambia una línea del <c>Program.cs</c> y no se toca ni un caso de uso ni un
/// test.
///
/// <see cref="Channel"/> no es decorativo: es lo que permite resolver el canal a
/// partir de la plantilla sin un <c>switch</c> que haya que ampliar cada vez que
/// aparezca un canal nuevo. Ver <c>SendNotificationHandler</c>.
/// </summary>
public interface IChannelSender
{
    /// <summary>Canal que atiende esta implementación.</summary>
    NotificationType Channel { get; }

    /// <summary>
    /// Manda el mensaje. Una excepción aquí significa «no salió», y el caso de
    /// uso la convierte en una fila <c>Failed</c> del log; no se reintenta dentro
    /// del envío porque quien decide si se reintenta es el consumidor de eventos.
    /// </summary>
    Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Canal de correo. Se separa de <see cref="ISmsSender"/> para poder inyectar y sustituir cada uno por su lado.</summary>
public interface IEmailSender : IChannelSender
{
}

/// <summary>Canal de SMS.</summary>
public interface ISmsSender : IChannelSender
{
}
