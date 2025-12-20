using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Tests;

/// <summary>
/// Integration tests for API endpoints. 
/// Tests actual HTTP requests against the test server.
/// </summary>
public class IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public IntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Pivot Endpoints

    [Fact(DisplayName = "Pivot endpoint with invalid project returns error")]
    public async Task Pivot_InvalidProject_ReturnsError()
    {
        var invalidProjectId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/pivot/{invalidProjectId}");

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (content.TryGetProperty("succeeded", out var succeeded))
        {
            succeeded.GetBoolean().Should().BeFalse();
        }
    }

    [Fact(DisplayName = "Pivot POST endpoint accepts request")]
    public async Task Pivot_Post_AcceptsRequest()
    {
        var request = new { ProjectId = Guid.NewGuid(), Page = 1, PageSize = 10 };

        var response = await _client.PostAsJsonAsync("/api/pivot", request);

        // Should return OK or BadRequest, not 500
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Report Endpoints

    [Fact(DisplayName = "Report Excel endpoint with invalid project returns error")]
    public async Task Report_Excel_InvalidProject_ReturnsError()
    {
        var invalidProjectId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/reports/{invalidProjectId}/excel");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Report CSV endpoint with invalid project returns error")]
    public async Task Report_Csv_InvalidProject_ReturnsError()
    {
        var invalidProjectId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/reports/{invalidProjectId}/csv");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Report JSON endpoint with invalid project returns error")]
    public async Task Report_Json_InvalidProject_ReturnsError()
    {
        var invalidProjectId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/reports/{invalidProjectId}/json");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "Report HTML endpoint with invalid project returns error")]
    public async Task Report_Html_InvalidProject_ReturnsError()
    {
        var invalidProjectId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/reports/{invalidProjectId}/html");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region RM Check Endpoints

    [Fact(DisplayName = "RM Check endpoint accepts POST request")]
    public async Task RmCheck_Post_AcceptsRequest()
    {
        var request = new { ProjectId = Guid.NewGuid() };

        var response = await _client.PostAsJsonAsync("/api/rmcheck", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact(DisplayName = "RM Check Weight-Volume endpoint accepts POST request")]
    public async Task RmCheck_WeightVolume_AcceptsRequest()
    {
        var request = new { ProjectId = Guid.NewGuid() };

        var response = await _client.PostAsJsonAsync("/api/rmcheck/weight-volume", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region CRM Endpoints

    [Fact(DisplayName = "CRM Diff endpoint accepts POST request")]
    public async Task CrmDiff_Post_AcceptsRequest()
    {
        var request = new { ProjectId = Guid.NewGuid() };

        var response = await _client.PostAsJsonAsync("/api/crm/diff", request);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    #endregion
}