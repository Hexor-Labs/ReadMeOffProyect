using FluentAssertions;

using HubNegocios.SharedKernel.Http;
using HubNegocios.TenantService.Domain.Entities;
using HubNegocios.TenantService.Domain.ValueObjects;

using Xunit;

namespace HubNegocios.TenantService.Tests;

/// <summary>
/// Pruebas del dominio puro.
///
/// No hay base de datos, ni dobles de EF Core, ni contenedor de dependencias:
/// esa es exactamente la ventaja que se compra con la arquitectura hexagonal.
/// Si estos tests necesitaran simular un <c>DbContext</c>, querría decir que el
/// dominio se ensució con infraestructura.
/// </summary>
public sealed class SlugTests
{
    [Theory]
    [InlineData("mi-restaurante")]
    [InlineData("bar123")]
    [InlineData("a-b-c")]
    public void AceptaSlugsValidos(string entrada) =>
        Slug.Create(entrada).Value.Should().Be(entrada);

    [Fact]
    public void NormalizaMayusculasYEspacios() =>
        Slug.Create("  Mi-Restaurante  ").Value.Should().Be("mi-restaurante");

    [Theory]
    [InlineData("")]                 // vacío
    [InlineData("ab")]               // demasiado corto
    [InlineData("-empieza-en-guion")]
    [InlineData("termina-en-guion-")]
    [InlineData("con espacio")]
    [InlineData("con_guion_bajo")]   // válido en C#, inválido en DNS
    [InlineData("Ñandú")]            // fuera del juego de caracteres de un subdominio
    public void RechazaSlugsInvalidos(string entrada)
    {
        var acto = () => Slug.Create(entrada);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("TENANT_SLUG_INVALID");
    }

    [Fact]
    public void RechazaSlugMasLargoQueUnaEtiquetaDns()
    {
        var acto = () => Slug.Create(new string('a', Slug.MaxLength + 1));

        acto.Should().Throw<DomainException>();
    }
}

public sealed class TenantTests
{
    private static Tenant CrearTenant() => Tenant.Register(
        Guid.NewGuid(),
        "Bar Nocturno",
        "bar",
        Slug.Create("bar-nocturno"),
        "  HOLA@BAR.COM  ",
        phone: null,
        description: null);

    [Fact]
    public void NaceActivoYEnElPlanDeEntrada()
    {
        var tenant = CrearTenant();

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.Plan.Should().Be(Tenant.DefaultPlan);
        tenant.Email.Should().Be("hola@bar.com", "el correo se normaliza al registrar");
    }

    [Fact]
    public void SuspenderDevuelveElEstadoAnteriorParaElHistorial()
    {
        var tenant = CrearTenant();

        var anterior = tenant.Suspend();

        anterior.Should().Be(TenantStatus.Active);
        tenant.Status.Should().Be(TenantStatus.Suspended);
    }

    [Fact]
    public void ReactivarSoloValeSobreUnSuspendido()
    {
        var tenant = CrearTenant();

        var acto = () => tenant.Reactivate();

        acto.Should().Throw<ConflictException>()
            .Which.Code.Should().Be("TENANT_NOT_SUSPENDED");
    }

    [Fact]
    public void ElDominioPropioSeNormalizaAMinusculas()
    {
        var tenant = CrearTenant();

        tenant.AssignCustomDomain("  WWW.BarNocturno.CO  ", isWhiteLabel: true);

        tenant.CustomDomain.Should().Be("www.barnocturno.co");
        tenant.IsWhiteLabel.Should().BeTrue();
    }

    [Fact]
    public void UnBrandingVacioNoDejaLaColumnaJsonbRota()
    {
        var tenant = CrearTenant();

        tenant.UpdateBranding("   ", logoUrl: null);

        // Si esto guardara cadena vacía, Postgres rechazaría el INSERT en una
        // columna jsonb y el fallo aparecería lejos de su causa.
        tenant.BrandingJson.Should().Be("{}");
    }
}
