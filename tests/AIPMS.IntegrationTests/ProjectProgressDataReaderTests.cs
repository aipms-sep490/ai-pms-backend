using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Repositories;
using Xunit;

namespace AIPMS.IntegrationTests;

public class ProjectProgressDataReaderTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ProjectProgressDataReaderTests(DbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProjectProgressFactsAsync_NonexistentProject_ReturnsNull()
    {
        using var context = _fixture.CreateContext();
        var reader = new ProjectProgressDataReader(context);

        var facts = await reader.GetProjectProgressFactsAsync(999999, CancellationToken.None);

        Assert.Null(facts);
    }
}
