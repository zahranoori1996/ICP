using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Api.Tests.Integration
{
    /// <summary>
    /// Conservative integration test skeleton. 
    /// Requires an already-running API on http://localhost:5268 and TEST_PROJECT_ID env var set.
    /// </summary>
    public class ProcessWorkflowTests
    {
        private const string BaseUrl = "http://localhost:5268";

        [Fact]
        public async Task Enqueue_Process_And_Poll_Status_Should_Return_Completed_Or_Failed()
        {
            var projectIdStr = Environment.GetEnvironmentVariable("TEST_PROJECT_ID");
            if (string.IsNullOrWhiteSpace(projectIdStr))
            {
                // Skip test if no project id is provided to avoid failures in CI without fixture. 
                return;
            }

            if (!Guid.TryParse(projectIdStr, out var projectId))
            {
                throw new InvalidOperationException("TEST_PROJECT_ID is not a valid GUID.");
            }

            using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            // Enqueue process
            var enqueueResp = await client.PostAsync($"/api/projects/{projectId}/process? background=true", null);
            enqueueResp.EnsureSuccessStatusCode();

            var enqueueBody = await enqueueResp.Content.ReadFromJsonAsync<JsonElement>();

            // Extract jobId safely using JsonElement
            string? jobId = null;
            if (enqueueBody.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("jobId", out var jobIdElement))
            {
                jobId = jobIdElement.GetString();
            }

            Assert.False(string.IsNullOrWhiteSpace(jobId), "jobId should be returned");

            // Poll status with timeout
            var timeout = TimeSpan.FromSeconds(60);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                var statusResp = await client.GetAsync($"/api/projects/import/{jobId}/status");
                if (!statusResp.IsSuccessStatusCode)
                {
                    await Task.Delay(1000);
                    continue;
                }

                var statusBody = await statusResp.Content.ReadFromJsonAsync<JsonElement>();

                // Extract state safely using JsonElement
                string? state = null;
                if (statusBody.TryGetProperty("data", out var statusDataElement) &&
                    statusDataElement.TryGetProperty("state", out var stateElement))
                {
                    state = stateElement.GetString();
                }

                if (state == "Completed" || state == "Failed")
                {
                    Assert.True(true);
                    return;
                }

                await Task.Delay(1000);
            }

            throw new TimeoutException("Polling status timed out.");
        }
    }
}