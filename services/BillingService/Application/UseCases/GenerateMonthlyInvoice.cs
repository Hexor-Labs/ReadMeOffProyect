using HubNegocios.BillingService.Domain.Entities;
using HubNegocios.BillingService.Domain.Ports;
using HubNegocios.BillingService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Outbox;

using Microsoft.Extensions.Logging;

namespace HubNegocios.BillingService.Application.UseCases;

public sealed record GenerateMonthlyInvoiceCommand(
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    DateTime DueDate);

/// <param name="AlreadyExisted">
/// <c>true</c> cuando el ciclo ya estaba facturado y no se hizo nada. No es un
/// error: el aviso de renovación se reentrega y esa es la respuesta correcta.
/// </param>
public sealed record GenerateMonthlyInvoiceResult(Guid InvoiceId, decimal Amount, string Currency, bool AlreadyExisted);

/// <summary>
/// Emite la factura del ciclo de una suscripción.
///
/// Lo dispara el evento <c>subscription.renewal_due</c> de tenant-service, que
/// llega «al menos una vez». Facturar dos veces el mismo ciclo sería cobrar dos
/// veces, así que la primera cosa que hace este caso de uso es comprobar si ya
/// hay factura para esa suscripción y ese vencimiento. Ese hueco entre la
/// comprobación y el guardado lo cierra el índice único de la tabla, que es lo
/// único que aguanta si dos instancias procesan el mismo aviso a la vez.
/// </summary>
public sealed class GenerateMonthlyInvoiceHandler(
    IBillingRepository repository,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<GenerateMonthlyInvoiceHandler> logger)
{
    public async Task<GenerateMonthlyInvoiceResult> HandleAsync(
        GenerateMonthlyInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Valida importe y moneda antes de tocar la base: una factura con un
        // importe negativo o una moneda inventada no debería ni llegar a
        // consultarse.
        var importe = Money.Create(command.Amount, command.Currency);

        var yaFacturado = await repository
            .ExistsInvoiceForCycleAsync(command.SubscriptionId, command.DueDate, cancellationToken)
            .ConfigureAwait(false);

        if (yaFacturado)
        {
            logger.LogInformation(
                "La suscripción {SubscriptionId} ya tenía factura con vencimiento {DueDate}; no se emite otra",
                command.SubscriptionId,
                command.DueDate);

            return new GenerateMonthlyInvoiceResult(Guid.Empty, importe.Amount, importe.Currency, AlreadyExisted: true);
        }

        var factura = Invoice.Issue(Guid.NewGuid(), command.SubscriptionId, importe, command.DueDate);

        // Se emite en el mismo paso en que se crea: el estado Draft solo existe
        // durante esta transacción, para que no haya forma de cobrar un importe
        // que todavía se estaba calculando.
        factura.Send();

        repository.AddInvoice(factura);

        /*
          El evento va en la misma transacción que la factura. Es el que hace que
          notification-service avise al negocio; si se publicara fuera, un fallo
          entre ambas cosas dejaría una deuda que nadie reclamó o un aviso de una
          factura que no existe.
        */
        outbox.Enqueue(
            factura.Id,
            nameof(Invoice),
            "invoice.generated",
            new
            {
                invoiceId = factura.Id,
                subscriptionId = factura.SubscriptionId,
                amount = factura.Amount,
                currency = factura.Currency,
                dueDate = factura.DueDate,
                generatedAt = clock.GetUtcNow().UtcDateTime,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Factura {InvoiceId} emitida por {Amount} {Currency} con vencimiento {DueDate}",
            factura.Id,
            factura.Amount,
            factura.Currency,
            factura.DueDate);

        return new GenerateMonthlyInvoiceResult(factura.Id, factura.Amount, factura.Currency, AlreadyExisted: false);
    }
}
