using FluentAssertions;

using HubNegocios.CatalogService.Domain.Entities;
using HubNegocios.CatalogService.Domain.ValueObjects;
using HubNegocios.SharedKernel.Http;

using Xunit;

namespace HubNegocios.CatalogService.Tests;

/// <summary>
/// Pruebas del dominio puro.
///
/// No hay base de datos, ni dobles de EF Core, ni contenedor de dependencias:
/// esa es exactamente la ventaja que se compra con la arquitectura hexagonal. Si
/// estos tests necesitaran simular un <c>DbContext</c>, querría decir que el
/// dominio se ensució con infraestructura.
/// </summary>
public sealed class SkuTests
{
    [Theory]
    [InlineData("CAFE-01")]
    [InlineData("A1")]
    [InlineData("MESA.GRANDE_02")]
    public void AceptaSkusValidos(string entrada) =>
        Sku.Create(entrada).Value.Should().Be(entrada);

    [Fact]
    public void NormalizaAMayusculasYQuitaEspacios() =>
        Sku.Create("  cafe-01  ").Value.Should().Be("CAFE-01");

    [Theory]
    [InlineData("")]
    [InlineData("A")]                // demasiado corto
    [InlineData("-empieza-en-guion")]
    [InlineData("termina-en-guion-")]
    [InlineData("con espacio")]
    [InlineData("acentuadó")]
    public void RechazaSkusInvalidos(string entrada)
    {
        var acto = () => Sku.Create(entrada);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ITEM_SKU_INVALID");
    }

    [Fact]
    public void RechazaSkuDemasiadoLargo()
    {
        var acto = () => Sku.Create(new string('A', Sku.MaxLength + 1));

        acto.Should().Throw<DomainException>();
    }
}

public sealed class MoneyTests
{
    [Fact]
    public void NormalizaLaMonedaAMayusculas() =>
        Money.Create(1000m, " cop ").Currency.Should().Be("COP");

    [Fact]
    public void RedondeaADosDecimalesAlCrearse()
    {
        // Se redondea aquí y no al guardar: si lo hiciera la columna
        // numeric(18,2), el precio publicado en el evento no sería el guardado.
        Money.Create(1000.005m, "COP").Amount.Should().Be(1000.00m);
        Money.Create(1000.015m, "COP").Amount.Should().Be(1000.02m);
    }

    [Fact]
    public void RechazaImportesNegativos()
    {
        var acto = () => Money.Create(-0.01m, "COP");

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ITEM_PRICE_NEGATIVE");
    }

    [Theory]
    [InlineData("")]
    [InlineData("CO")]
    [InlineData("PESOS")]
    [InlineData("C0P")]
    public void RechazaMonedasQueNoSonIso4217(string moneda)
    {
        var acto = () => Money.Create(1000m, moneda);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ITEM_CURRENCY_INVALID");
    }
}

public sealed class ItemTests
{
    [Fact]
    public void NaceActivoYSinBorrar()
    {
        var item = Escenario.CrearItem();

        item.IsActive.Should().BeTrue();
        item.IsDeleted.Should().BeFalse();
        item.AttributesJson.Should().Be("{}", "una cadena vacía rompería la columna jsonb");
    }

    [Fact]
    public void NormalizaEtiquetasSinRepetidasYEnMinusculas()
    {
        var item = Escenario.CrearItem(tags: ["Vegano", "vegano", "  ", "Sin-Gluten"]);

        item.Tags.Should().Equal("vegano", "sin-gluten");
    }

    [Fact]
    public void CambiarElPrecioDevuelveElAnteriorParaElHistorial()
    {
        var item = Escenario.CrearItem(price: 5000m);

        var anterior = item.ChangePrice(Money.Create(7500m, "COP"));

        anterior.Should().Be(5000m);
        item.PriceAmount.Should().Be(7500m);
    }

    [Fact]
    public void NoAdmiteCambiarDeMonedaDisfrazadoDeCambioDePrecio()
    {
        var item = Escenario.CrearItem();

        var acto = () => item.ChangePrice(Money.Create(10m, "USD"));

        acto.Should().Throw<ConflictException>()
            .Which.Code.Should().Be("ITEM_CURRENCY_MISMATCH");
    }

    [Fact]
    public void ElModoIlimitadoNoGuardaCantidad()
    {
        var item = Escenario.CrearItem(mode: AvailabilityMode.StockBased, quantity: 5);

        item.ChangeAvailability(AvailabilityMode.Unlimited, 5);

        // Un 0 o un 5 en modo ilimitado se leerían como un cupo que no existe.
        item.AvailableQuantity.Should().BeNull();
    }

    [Theory]
    [InlineData(AvailabilityMode.StockBased)]
    [InlineData(AvailabilityMode.SlotBased)]
    [InlineData(AvailabilityMode.FirstCome)]
    public void LosModosConCupoExigenCantidad(AvailabilityMode modo)
    {
        var item = Escenario.CrearItem();

        var acto = () => item.ChangeAvailability(modo, null);

        acto.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ITEM_QUANTITY_REQUIRED");
    }

    [Fact]
    public void AdmiteCantidadCeroQueEsAgotado()
    {
        var item = Escenario.CrearItem();

        item.ChangeAvailability(AvailabilityMode.StockBased, 0);

        item.AvailableQuantity.Should().Be(0);
    }

    [Fact]
    public void UnItemBorradoYaNoSeDejaTocar()
    {
        var item = Escenario.CrearItem();
        item.SoftDelete();

        var cambiarPrecio = () => item.ChangePrice(Money.Create(1m, "COP"));
        var cambiarCupo = () => item.ChangeAvailability(AvailabilityMode.StockBased, 1);

        cambiarPrecio.Should().Throw<ConflictException>().Which.Code.Should().Be("ITEM_DELETED");
        cambiarCupo.Should().Throw<ConflictException>().Which.Code.Should().Be("ITEM_DELETED");
    }
}
