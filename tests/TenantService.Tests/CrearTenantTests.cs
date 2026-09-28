using FluentAssertions;

using HubNegocios.SharedKernel.Http;
using HubNegocios.SharedKernel.Outbox;
using HubNegocios.TenantService.Application.UseCases;
using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.Ports;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace HubNegocios.TenantService.Tests;

/// <summary>
/// Doble del repositorio, escrito a mano.
///
/// Sin librería de mocks a propósito: un doble de veinte líneas se lee de un
/// vistazo, mientras que tres llamadas encadenadas a un framework de simulación
/// hay que descifrarlas. Además obliga a que el puerto siga siendo pequeño: el
/// día que esta clase empiece a doler, será señal de que la interfaz creció
/// demasiado.
/// </summary>
internal sealed class RepositorioEnMemoria : ITenantRepository
{
    private readonly List<Tenant> _tenants = [];

    public List<Subscription> Suscripciones { get; } = [];
    public List<TenantStatusHistory> Historial { get; } = [];
    public int VecesGuardado { get; private set; }

    public void Precargar(Tenant tenant) => _tenants.Add(tenant);

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.Find(t => t.Id == id));

    public Task<Tenant?> GetBySlugOrDomainAsync(string slugOrDomain, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.Find(t => t.Slug == slugOrDomain || t.CustomDomain == slugOrDomain));

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.Exists(t => t.Slug == slug));

    public void Add(Tenant tenant) => _tenants.Add(tenant);

    public void AddSubscription(Subscription subscription) => Suscripciones.Add(subscription);

    public void AddStatusHistory(TenantStatusHistory history) => Historial.Add(history);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        VecesGuardado++;
        return Task.CompletedTask;
    }
}

internal sealed record EventoEncolado(Guid TenantId, Guid AggregateId, string EventType);

internal sealed class OutboxEnMemoria : IOutboxWriter
{
    public List<EventoEncolado> Eventos { get; } = [];

    public void Enqueue<TPayload>(Guid aggregateId, string aggregateType, string eventType, TPayload payload) =>
        Eventos.Add(new EventoEncolado(Guid.Empty, aggregateId, eventType));

    public void EnqueueFor<TPayload>(Guid tenantId, Guid aggregateId, string aggregateType, string eventType, TPayload payload) =>
        Eventos.Add(new EventoEncolado(tenantId, aggregateId, eventType));
}

public sealed class CrearTenantTests
{
    private readonly RepositorioEnMemoria _repositorio = new();
    private readonly OutboxEnMemoria _outbox = new();
    private readonly CreateTenantHandler _handler;

    public CrearTenantTests() => _handler = new CreateTenantHandler(
        _repositorio,
        _outbox,
        // Reloj fijo: un test que dependa de la hora real falla solo, un día
        // cualquiera, por motivos que no tienen que ver con el código.
        new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)),
        NullLogger<CreateTenantHandler>.Instance);

    private static CreateTenantCommand Comando(string slug = "bar-nocturno") =>
        new("Bar Nocturno", "bar", slug, "hola@bar.com", null, null);

    [Fact]
    public async Task CreaElTenantConSuSuscripcionDePrueba()
    {
        var resultado = await _handler.HandleAsync(Comando());

        resultado.Slug.Should().Be("bar-nocturno");
        resultado.Plan.Should().Be(Tenant.DefaultPlan);
        _repositorio.Suscripciones.Should().ContainSingle()
            .Which.TenantId.Should().Be(resultado.TenantId);
    }

    [Fact]
    public async Task EmiteTenantCreatedConElTenantReciénCreado()
    {
        var resultado = await _handler.HandleAsync(Comando());

        var evento = _outbox.Eventos.Should().ContainSingle().Subject;
        evento.EventType.Should().Be("tenant.created");

        // Lo importante de este caso: el evento lleva el tenant nuevo aunque en
        // el contexto de ejecución no hubiera ninguno cuando se encoló.
        evento.TenantId.Should().Be(resultado.TenantId);
    }

    [Fact]
    public async Task ElEventoYElTenantSeGuardanDeUnaSolaVez()
    {
        await _handler.HandleAsync(Comando());

        // Un único SaveChanges es lo que hace que el evento y el tenant caigan
        // en la misma transacción. Dos llamadas aquí serían dos transacciones y
        // el patrón Outbox dejaría de garantizar nada.
        _repositorio.VecesGuardado.Should().Be(1);
    }

    [Fact]
    public async Task RechazaUnSlugYaUsado()
    {
        await _handler.HandleAsync(Comando());

        var acto = async () => await _handler.HandleAsync(Comando());

        (await acto.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("TENANT_SLUG_TAKEN");
    }

    [Fact]
    public async Task NoDejaRastroSiElSlugEsInvalido()
    {
        var acto = async () => await _handler.HandleAsync(Comando("no válido"));

        await acto.Should().ThrowAsync<DomainException>();

        _outbox.Eventos.Should().BeEmpty("un slug inválido no puede emitir eventos");
        _repositorio.VecesGuardado.Should().Be(0);
    }
}

/// <summary>Reloj fijo para los tests.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
