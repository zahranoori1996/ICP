using System.Net.Http.Json;
using Application.Services;
using Domain.Entities;
using Shared.Wrapper;

namespace WebUI.Services
{
    public class VersionService : IVersionService
    {
        private readonly HttpClient _httpClient;

        public VersionService(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient("Api");
        }

        public async Task<Result<ProjectState>> CreateVersionAsync(CreateVersionDto dto, CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/versions", dto, ct);
                var result = await response.Content.ReadFromJsonAsync<Result<ProjectState>>(cancellationToken: ct);
                // Change: Use Fail without await
                return result ?? Result<ProjectState>.Fail("Failed to create version.");
            }
            catch (Exception ex)
            {
                return Result<ProjectState>.Fail(ex.Message);
            }
        }

        public async Task<Result<List<VersionNodeDto>>> GetVersionTreeAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<Result<List<VersionNodeDto>>>($"api/versions/tree/{projectId}", ct);
                return result ?? Result<List<VersionNodeDto>>.Fail("Failed to load version tree.");
            }
            catch (Exception ex)
            {
                return Result<List<VersionNodeDto>>.Fail(ex.Message);
            }
        }

        public async Task<Result<List<ProjectState>>> GetAllVersionsAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<Result<List<ProjectState>>>($"api/versions/project/{projectId}", ct);
                return result ?? Result<List<ProjectState>>.Fail("Failed to load versions.");
            }
            catch (Exception ex)
            {
                return Result<List<ProjectState>>.Fail(ex.Message);
            }
        }

        public async Task<Result<ProjectState?>> GetActiveVersionAsync(Guid projectId, CancellationToken ct = default)
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<Result<ProjectState?>>($"api/versions/active/{projectId}", ct);
                return result ?? Result<ProjectState?>.Fail("Failed to get active version.");
            }
            catch (Exception ex)
            {
                return Result<ProjectState?>.Fail(ex.Message);
            }
        }

        public async Task<Result<bool>> SwitchToVersionAsync(Guid projectId, int stateId, CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.PostAsync($"api/versions/switch/{projectId}/{stateId}", null, ct);
                var result = await response.Content.ReadFromJsonAsync<Result<bool>>(cancellationToken: ct);
                return result ?? Result<bool>.Fail("Failed to switch version.");
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail(ex.Message);
            }
        }

        public async Task<Result<ProjectState?>> GetVersionAsync(int stateId, CancellationToken ct = default)
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<Result<ProjectState?>>($"api/versions/{stateId}", ct);
                return result ?? Result<ProjectState?>.Fail("Failed to get version.");
            }
            catch (Exception ex)
            {
                return Result<ProjectState?>.Fail(ex.Message);
            }
        }

        public async Task<Result<List<ProjectState>>> GetVersionPathAsync(int stateId, CancellationToken ct = default)
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<Result<List<ProjectState>>>($"api/versions/path/{stateId}", ct);
                return result ?? Result<List<ProjectState>>.Fail("Failed to get version path.");
            }
            catch (Exception ex)
            {
                return Result<List<ProjectState>>.Fail(ex.Message);
            }
        }

        public async Task<Result<bool>> DeleteVersionAsync(int stateId, bool deleteChildren = false, CancellationToken ct = default)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/versions/{stateId}?deleteChildren={deleteChildren}", ct);
                var result = await response.Content.ReadFromJsonAsync<Result<bool>>(cancellationToken: ct);
                return result ?? Result<bool>.Fail("Failed to delete version.");
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail(ex.Message);
            }
        }

        public async Task<Result<ProjectState>> ForkVersionAsync(int parentStateId, string processingType, string data, string? description = null, CancellationToken ct = default)
        {
            var dto = new CreateVersionDto(
                Guid.Empty,
                parentStateId,
                processingType,
                data,
                description
            );

            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/versions/fork", dto, ct);
                var result = await response.Content.ReadFromJsonAsync<Result<ProjectState>>(cancellationToken: ct);
                return result ?? Result<ProjectState>.Fail("Failed to fork version.");
            }
            catch (Exception ex)
            {
                return Result<ProjectState>.Fail(ex.Message);
            }
        }
    }
}