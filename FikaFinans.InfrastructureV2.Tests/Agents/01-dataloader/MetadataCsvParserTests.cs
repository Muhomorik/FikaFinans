using FikaFinans.Application.Paths;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using AutoFixture;
using AutoFixture.AutoMoq;
using CsvHelper;
using FikaFinans.Domain.Funds;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

[TestFixture]
public class MetadataCsvParserTests
{
    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
    }

    [Test]
    public void Parse_TwoRows_ReturnsTwoRecordsWithAllFieldsPopulated()
    {
        const string csv = """
            isin,name,company_name,currency_code,category,fund_type,is_index_fund,managed_type,total_fee,management_fee,risk,rating,sharpe_ratio,standard_deviation,recommended_holding_period,capital,number_of_owners
            LU0106252389,Schroder ISF Em Mkts A Acc USD,Schroder,USD,Tillväxtmarknader,EQUITY_FUND,false,ACTIVE,2.17,1.5,4,3,0.68,14.28,FIVE_YEAR,70121004753.02,410
            LU0106817157,Schroder ISF Emerging Europe A Acc EUR,Schroder,EUR,Östeuropa ex Ryssland,EQUITY_FUND,false,ACTIVE,2.42,1.5,6,4,1.53,14.25,FIVE_YEAR,17123096340.44,3092
            """;
        var sut = _fixture.Create<MetadataCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].Isin.Value, Is.EqualTo("LU0106252389"));
            Assert.That(result[0].Name, Is.EqualTo("Schroder ISF Em Mkts A Acc USD"));
            Assert.That(result[0].IsIndexFund, Is.False);
            Assert.That(result[0].ManagedType, Is.EqualTo("ACTIVE"));
            Assert.That(result[0].TotalFee, Is.EqualTo(2.17m));
            Assert.That(result[0].Risk, Is.EqualTo(4));
            Assert.That(result[0].Rating, Is.EqualTo(3));
            Assert.That(result[0].SharpeRatioStatic, Is.EqualTo(0.68m));
            Assert.That(result[0].StandardDeviationStatic, Is.EqualTo(14.28m));
            Assert.That(result[0].Capital, Is.EqualTo(70121004753.02m));
            Assert.That(result[0].NumberOfOwners, Is.EqualTo(410));
            Assert.That(result[0].Category, Is.EqualTo("Tillväxtmarknader"));
        });
    }

    [Test]
    public void Parse_EmptyRatingCell_ReturnsNullRating()
    {
        const string csv = """
            isin,name,company_name,currency_code,category,fund_type,is_index_fund,managed_type,total_fee,management_fee,risk,rating,sharpe_ratio,standard_deviation,recommended_holding_period,capital,number_of_owners
            LU0000000001,Foo,Bar,SEK,Cat,EQUITY_FUND,false,ACTIVE,1.0,0.5,3,,0.5,10.0,FIVE_YEAR,1000000,100
            """;
        var sut = _fixture.Create<MetadataCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result[0].Rating, Is.Null);
    }

    [Test]
    public void Parse_EmptySharpeRatioCell_ReturnsNullSharpeStatic()
    {
        const string csv = """
            isin,name,company_name,currency_code,category,fund_type,is_index_fund,managed_type,total_fee,management_fee,risk,rating,sharpe_ratio,standard_deviation,recommended_holding_period,capital,number_of_owners
            LU0000000001,Foo,Bar,SEK,Cat,EQUITY_FUND,false,ACTIVE,1.0,0.5,3,4,,,FIVE_YEAR,1000000,100
            """;
        var sut = _fixture.Create<MetadataCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result[0].SharpeRatioStatic, Is.Null);
        Assert.That(result[0].StandardDeviationStatic, Is.Null);
    }

    [Test]
    public void Parse_EmptyIsIndexFundCell_ReturnsNullBool()
    {
        const string csv = """
            isin,name,company_name,currency_code,category,fund_type,is_index_fund,managed_type,total_fee,management_fee,risk,rating,sharpe_ratio,standard_deviation,recommended_holding_period,capital,number_of_owners
            LU0000000001,Foo,Bar,SEK,Cat,EQUITY_FUND,,ACTIVE,1.0,0.5,3,4,0.5,10.0,FIVE_YEAR,1000000,100
            """;
        var sut = _fixture.Create<MetadataCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result[0].IsIndexFund, Is.Null);
    }

    [Test]
    public void Parse_MissingRequiredColumn_Throws()
    {
        const string csv = """
            isin,name,company_name,currency_code,category,fund_type,is_index_fund,managed_type,total_fee,management_fee,risk,rating,sharpe_ratio,standard_deviation,recommended_holding_period,capital
            LU0000000001,Foo,Bar,SEK,Cat,EQUITY_FUND,false,ACTIVE,1.0,0.5,3,4,0.5,10.0,FIVE_YEAR,1000000
            """;
        var sut = _fixture.Create<MetadataCsvParser>();

        Assert.That(() => sut.Parse(new StringReader(csv)), Throws.InstanceOf<HeaderValidationException>()
            .Or.InstanceOf<CsvHelper.MissingFieldException>()
            .Or.InstanceOf<CsvHelperException>());
    }
}
