using Microsoft.AspNetCore.Components;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class ChangeLog
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private List<ChangeLogEntry> _changeLogs = new();
        private List<ChangeLogEntry> _filteredLogs => ApplyFilters();
        private bool _isLoading = false;
        private string? _filterType;
        private DateTime? _filterFromDate;
        private DateTime? _filterToDate;
        private string? _filterUser;
        private int _currentPage = 1;
        private int _pageSize = 20;
        private int _totalPages => (int)Math.Ceiling((double)_changeLogs.Count / _pageSize);

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            if (_projectId.HasValue)
            {
                await LoadChangeLogs();
            }
        }

        private async Task LoadChangeLogs()
        {
            _isLoading = true;
            StateHasChanged();

            try
            {
                // Simulate loading - in real app this would call API
                await Task.Delay(500);

                _changeLogs = new List<ChangeLogEntry>
            {
                new ChangeLogEntry
                {
                    Id = Guid.NewGuid(),
                    ChangeType = "Import",
                    Description = "Imported data from Excel file: Sample_Data_2024.xlsx",
                    UserName = "admin",
                    Timestamp = DateTime.Now.AddHours(-2),
                    AffectedRows = 250
                },
                new ChangeLogEntry
                {
                    Id = Guid.NewGuid(),
                    ChangeType = "WeightCorrection",
                    Description = "Applied weight correction to 15 samples",
                    UserName = "admin",
                    Timestamp = DateTime.Now.AddHours(-1),
                    AffectedRows = 15,
                    BeforeValue = "0.2500g",
                    AfterValue = "0.2510g"
                },
                new ChangeLogEntry
                {
                    Id = Guid.NewGuid(),
                    ChangeType = "DriftCorrection",
                    Description = "Applied linear drift correction (3 segments detected)",
                    UserName = "admin",
                    Timestamp = DateTime.Now.AddMinutes(-30),
                    AffectedRows = 200
                },
                new ChangeLogEntry
                {
                    Id = Guid.NewGuid(),
                    ChangeType = "CrmCalibration",
                    Description = "Optimized Blank & Scale for 10 elements",
                    UserName = "admin",
                    Timestamp = DateTime.Now.AddMinutes(-15),
                    AffectedRows = 50
                },
                new ChangeLogEntry
                {
                    Id = Guid.NewGuid(),
                    ChangeType = "Export",
                    Description = "Exported final results to Excel",
                    UserName = "admin",
                    Timestamp = DateTime.Now.AddMinutes(-5),
                    AffectedRows = 250
                }
            };
            }
            finally
            {
                _isLoading = false;
            }
        }

        private List<ChangeLogEntry> ApplyFilters()
        {
            var filtered = _changeLogs.AsEnumerable();

            if (!string.IsNullOrEmpty(_filterType))
            {
                filtered = filtered.Where(l => l.ChangeType == _filterType);
            }

            if (_filterFromDate.HasValue)
            {
                filtered = filtered.Where(l => l.Timestamp >= _filterFromDate.Value);
            }

            if (_filterToDate.HasValue)
            {
                filtered = filtered.Where(l => l.Timestamp <= _filterToDate.Value.AddDays(1));
            }

            if (!string.IsNullOrEmpty(_filterUser))
            {
                filtered = filtered.Where(l => l.UserName.Contains(_filterUser, StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderByDescending(l => l.Timestamp)
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();
        }

        private void ToggleDetails(ChangeLogEntry log)
        {
            log.ShowDetails = !log.ShowDetails;
        }

        private Color GetChangeColor(string changeType) => changeType switch
        {
            "Import" => Color.Primary,
            "WeightCorrection" => Color.Warning,
            "VolumeCorrection" => Color.Warning,
            "DriftCorrection" => Color.Info,
            "CrmCalibration" => Color.Success,
            "Delete" => Color.Error,
            "Export" => Color.Secondary,
            _ => Color.Default
        };

        private string GetChangeIcon(string changeType) => changeType switch
        {
            "Import" => Icons.Material.Filled.CloudUpload,
            "WeightCorrection" => Icons.Material.Filled.FitnessCenter,
            "VolumeCorrection" => Icons.Material.Filled.Water,
            "DriftCorrection" => Icons.Material.Filled.TrendingUp,
            "CrmCalibration" => Icons.Material.Filled.Verified,
            "Delete" => Icons.Material.Filled.Delete,
            "Export" => Icons.Material.Filled.FileDownload,
            _ => Icons.Material.Filled.Edit
        };

        public class ChangeLogEntry
        {
            public Guid Id { get; set; }
            public string ChangeType { get; set; } = "";
            public string Description { get; set; } = "";
            public string UserName { get; set; } = "";
            public DateTime Timestamp { get; set; }
            public int AffectedRows { get; set; }
            public string? BeforeValue { get; set; }
            public string? AfterValue { get; set; }
            public bool ShowDetails { get; set; }
        }
    }
}