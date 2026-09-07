using System.Text.Json;
using LMS.Api.domain.Enums.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LMS.Domain.Tests.Integration;

public sealed class JsonContractTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonContractTests(ApiWebApplicationFactory factory)
    {
        _jsonOptions = factory.Services.GetRequiredService<IOptions<JsonOptions>>()
            .Value.JsonSerializerOptions;
    }

    [Fact]
    public void EnumsUseNamesInRequestsAndResponses()
    {
        Assert.Equal("\"Student\"", JsonSerializer.Serialize(Role.Student, _jsonOptions));
        Assert.Equal(Role.Teacher, JsonSerializer.Deserialize<Role>("\"Teacher\"", _jsonOptions));
    }
}
