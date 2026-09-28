using HubNegocios.OrderService.Domain.Entities;
using HubNegocios.OrderService.Domain.Ports;
using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;

using Microsoft.Extensions.Logging;

namespace HubNegocios.OrderService.Application.UseCases;

public sealed record OrderLineRequest(Guid ItemId, int Quantity);

public sealed record CreateOrderCommand(
    Guid CustomerId,
    OrderType Type,
    string? Notes,
    IReadOnlyList<OrderLineRequest> Lines);

public sealed record CreateOrderResult(Guid OrderId, string OrderNumber, decimal Total, string Currency);

/// <summary>
/// Crea una orden validando antes cada línea contra catalog-service.
///
/// El paso de validación es lo que impide dos cosas: que se compre algo que no
/// existe o está desactivado, y que el precio lo ponga el cliente. La respuesta
/// de catalog-service es la que manda; lo que venga en la petición solo dice
/// QUÉ y CUÁNTO, nunca a qué precio.
/// </summary>
public sealed class CreateOrderHandler(
    IOrderRepository repository,
    ICatalogClient catalog,
    IOutboxWriter outbox,
    TimeProvider clock,
    ILogger<CreateOrderHandler> logger)
{
    public async Task<CreateOrderResult> HandleAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Lines.Count == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["lines"] = ["Hay que pedir al menos una cosa."],
            });
        }

        // Agrupado: si el cliente manda el mismo item en dos líneas, se
        // consulta una sola vez y se suman las cantidades.
        var cantidades = command.Lines
            .GroupBy(line => line.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var snapshots = await catalog
            .GetItemsAsync(cantidades.Keys.ToList(), cancellationToken)
            .ConfigureAwait(false);

        var porId = snapshots.ToDictionary(snapshot => snapshot.ItemId);

        var faltantes = cantidades.Keys.Where(id => !porId.ContainsKey(id)).ToList();
        if (faltantes.Count > 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["lines"] = [$"Estos productos ya no existen: {string.Join(", ", faltantes)}"],
            });
        }

        var inactivos = porId.Values.Where(snapshot => !snapshot.IsActive).Select(s => s.Name).ToList();
        if (inactivos.Count > 0)
        {
            throw new ConflictException(
                "ORDER_ITEM_UNAVAILABLE",
                $"Ya no están disponibles: {string.Join(", ", inactivos)}");
        }

        var monedas = porId.Values.Select(snapshot => snapshot.Currency).Distinct().ToList();
        if (monedas.Count > 1)
        {
            // Sumar pesos con dólares da un número sin significado. Mejor
            // rechazar que guardar un total que no quiere decir nada.
            throw new ConflictException(
                "ORDER_MIXED_CURRENCIES",
                "No se puede mezclar monedas distintas en una misma orden.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var orderId = Guid.NewGuid();

        var lineas = cantidades.Select(par => OrderItem.Create(
            Guid.NewGuid(),
            par.Key,
            porId[par.Key].Name,
            porId[par.Key].Sku,
            par.Value,
            porId[par.Key].Price,
            porId[par.Key].AttributesJson));

        var secuencia = await repository.NextSequenceAsync(now.Year, cancellationToken).ConfigureAwait(false);

        var order = Order.Place(
            orderId,
            command.CustomerId,
            $"ORD-{now.Year}-{secuencia:D6}",
            command.Type,
            monedas[0],
            command.Notes,
            lineas);

        repository.Add(order);

        /*
          El evento va en la misma transacción que la orden. Es el que despierta
          a media plataforma: reservations-service lo consume si el tipo es
          reserva, inventory-service aparta stock y notification-service manda
          la confirmación. Publicarlo fuera de la transacción abriría la puerta
          a reservar una mesa para una orden que nunca llegó a guardarse.
        */
        outbox.Enqueue(
            order.Id,
            nameof(Order),
            "order.created",
            new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                customerId = order.CustomerId,
                orderType = order.Type.ToString(),
                total = order.TotalAmount,
                currency = order.Currency,
                items = order.Items.Select(item => new
                {
                    itemId = item.ItemId,
                    quantity = item.Quantity,
                    unitPrice = item.UnitPrice,
                }),
                createdAt = now,
            });

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Orden {OrderNumber} creada con {Lineas} líneas por un total de {Total} {Moneda}",
            order.OrderNumber,
            order.Items.Count,
            order.TotalAmount,
            order.Currency);

        return new CreateOrderResult(order.Id, order.OrderNumber, order.TotalAmount, order.Currency);
    }
}
