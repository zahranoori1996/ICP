using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class Report
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private string _selectedFormat = "excel";
        private bool _includeSummary = true;
        private bool _includeRawData = false;
        private bool _includeProcessedData = true;
        private bool _includeCrmAnalysis = true;
        private bool _includeDriftAnalysis = true;
        private bool _includeCharts = false;
        private IEnumerable<string> _selectedElements = new HashSet<string>();
        private List<string> _allElements = new();
        private List<ReportHistoryItem> _recentReports = new();
        private bool _isGenerating = false;

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (_projectId.HasValue)
            {
                await LoadElements();
                await LoadRecentReports();
            }
        }

        private async Task LoadElements()
        {
            // Load elements from API
            _allElements = new List<string>
        {
            "SiO2", "Al2O3", "Fe2O3", "CaO", "MgO", "Na2O", "K2O", "TiO2", "P2O5", "MnO"
        };
        }

        private async Task LoadRecentReports()
        {
            // This would load from API in real implementation
            _recentReports = new List<ReportHistoryItem>();
        }

        private async Task GenerateReport()
        {
            if (_projectId == null) return;

            _isGenerating = true;
            StateHasChanged();

            try
            {
                var request = new ReportRequest
                {
                    ProjectId = _projectId.Value,
                    Format = _selectedFormat,
                    IncludeSummary = _includeSummary,
                    IncludeCharts = _includeCharts,
                    Elements = _selectedElements.Any() ? _selectedElements.ToList() : null
                };

                var result = await ReportService.GenerateReportAsync(request);

                if (result.Succeeded && result.Data != null)
                {
                    await JSRuntime.InvokeVoidAsync("downloadFile",
                        result.Data.FileName,
                        result.Data.ContentType,
                        result.Data.Base64Data);

                    Snackbar.Add("Report generated successfully", Severity.Success);

                    _recentReports.Insert(0, new ReportHistoryItem
                    {
                        FileName = result.Data.FileName,
                        Format = _selectedFormat,
                        GeneratedAt = DateTime.Now
                    });
                }
                else
                {
                    Snackbar.Add(result.Message ?? "Failed to generate report", Severity.Error);
                }
            }
            finally
            {
                _isGenerating = false;
            }
        }

        private async Task GenerateQuickReport(string template)
        {
            switch (template)
            {
                case "full":
                    _includeSummary = true;
                    _includeRawData = true;
                    _includeProcessedData = true;
                    _includeCrmAnalysis = true;
                    _includeDriftAnalysis = true;
                    _includeCharts = true;
                    break;
                case "crm":
                    _includeSummary = true;
                    _includeRawData = false;
                    _includeProcessedData = false;
                    _includeCrmAnalysis = true;
                    _includeDriftAnalysis = false;
                    _includeCharts = true;
                    break;
                case "drift":
                    _includeSummary = true;
                    _includeRawData = false;
                    _includeProcessedData = false;
                    _includeCrmAnalysis = false;
                    _includeDriftAnalysis = true;
                    _includeCharts = true;
                    break;
                case "data":
                    _includeSummary = false;
                    _includeRawData = false;
                    _includeProcessedData = true;
                    _includeCrmAnalysis = false;
                    _includeDriftAnalysis = false;
                    _includeCharts = false;
                    break;
            }

            await GenerateReport();
        }

        private string GetFormatIcon(string format) => format switch
        {
            "excel" => Icons.Material.Filled.TableChart,
            "csv" => Icons.Material.Filled.Description,
            "json" => Icons.Material.Filled.DataObject,
            "pdf" => Icons.Material.Filled.PictureAsPdf,
            _ => Icons.Material.Filled.InsertDriveFile
        };

        private async Task DownloadReport(ReportHistoryItem report)
        {
            // Re-download from cached data or re-generate
            Snackbar.Add("Downloading...", Severity.Info);
        }

        public class ReportHistoryItem
        {
            public string FileName { get; set; } = "";
            public string Format { get; set; } = "";
            public DateTime GeneratedAt { get; set; }
        }
        private async Task OnBeforeNavigation(LocationChangingContext context)
        {
            if (_isGenerating)
            {
                context.PreventNavigation();
            }
        }
    }
}