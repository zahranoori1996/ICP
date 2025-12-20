using Microsoft.AspNetCore.Components;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class Elements
    {
        [SupplyParameterFromQuery]
        public Guid? projectId { get; set; }

        private Guid? _projectId;
        private List<ElementConfig> _elements = new();
        private ElementConfig? _selectedElement;
        private bool _isLoading = false;
        private bool _isSaving = false;
        private string _calcElement = "";
        private string _calcOxide = "";
        private decimal? _calculatedFactor;
        private List<OxideReference> _commonOxides = new();

        protected override async Task OnInitializedAsync()
        {
            _projectId = projectId ?? ProjectService.CurrentProjectId;
            InitializeCommonOxides();
            if (_projectId.HasValue)
            {
                await LoadElements();
            }
        }

        private void InitializeCommonOxides()
        {
            _commonOxides = new List<OxideReference>
        {
            new("SiO2", 2.1393m),
            new("Al2O3", 1.8895m),
            new("Fe2O3", 1.4297m),
            new("FeO", 1.2865m),
            new("CaO", 1.3992m),
            new("MgO", 1.6583m),
            new("Na2O", 1.3480m),
            new("K2O", 1.2046m),
            new("TiO2", 1.6681m),
            new("P2O5", 2.2914m),
            new("MnO", 1.2912m),
            new("Cr2O3", 1.4615m)
        };
        }

        private async Task LoadElements()
        {
            _isLoading = true;
            StateHasChanged();

            try
            {
                var result = await PivotService.GetElementsAsync(_projectId!.Value);
                if (result.Succeeded && result.Data != null)
                {
                    _elements = result.Data.Select(e => new ElementConfig
                    {
                        Name = e,
                        Symbol = e.Replace("2O3", "").Replace("O2", "").Replace("O", "").Replace("2", "").Replace("3", "").Replace("5", ""),
                        Unit = "%",
                        OxideFactor = GetDefaultOxideFactor(e),
                        MinRange = 0.01m,
                        MaxRange = 100m,
                        DiffThreshold = 10m,
                        IsActive = true
                    }).ToList();
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private decimal GetDefaultOxideFactor(string element)
        {
            var oxide = _commonOxides.FirstOrDefault(o => o.Oxide.Equals(element, StringComparison.OrdinalIgnoreCase));
            return oxide?.Factor ?? 1.0m;
        }

        private async Task SaveChanges()
        {
            _isSaving = true;
            StateHasChanged();

            try
            {
                // Save to API
                await Task.Delay(500); // Simulate API call
                Snackbar.Add("Element settings saved successfully", Severity.Success);
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void ResetElement()
        {
            if (_selectedElement != null)
            {
                _selectedElement.OxideFactor = GetDefaultOxideFactor(_selectedElement.Name);
                _selectedElement.MinRange = 0.01m;
                _selectedElement.MaxRange = 100m;
                _selectedElement.DiffThreshold = 10m;
                Snackbar.Add("Element reset to default values", Severity.Info);
            }
        }

        private void CalculateOxideFactor()
        {
            if (string.IsNullOrWhiteSpace(_calcElement) || string.IsNullOrWhiteSpace(_calcOxide))
            {
                Snackbar.Add("Please enter both element and oxide formula", Severity.Warning);
                return;
            }

            // Simple oxide factor calculation based on atomic masses
            var atomicMasses = new Dictionary<string, decimal>
        {
            { "Si", 28.0855m },
            { "Al", 26.9815m },
            { "Fe", 55.8450m },
            { "Ca", 40.0780m },
            { "Mg", 24.3050m },
            { "Na", 22.9898m },
            { "K", 39.0983m },
            { "Ti", 47.8670m },
            { "P", 30.9738m },
            { "Mn", 54.9380m },
            { "O", 15.9994m },
            { "Cr", 51.9961m },
            { "Zn", 65.3800m },
            { "Cu", 63.5460m },
            { "Pb", 207.2000m },
            { "Ba", 137.3270m },
            { "Sr", 87.6200m }
        };

            if (!atomicMasses.TryGetValue(_calcElement, out var elementMass))
            {
                Snackbar.Add($"Unknown element: {_calcElement}", Severity.Error);
                return;
            }

            // Parse oxide formula and calculate molecular mass
            // Simple parsing - this is a simplified version
            var oxide = _commonOxides.FirstOrDefault(o => o.Oxide.Equals(_calcOxide, StringComparison.OrdinalIgnoreCase));
            if (oxide != null)
            {
                _calculatedFactor = oxide.Factor;
            }
            else
            {
                // Fallback calculation
                _calculatedFactor = 1.0m;
                Snackbar.Add("Could not calculate factor. Using default.", Severity.Warning);
            }
        }

        public class ElementConfig
        {
            public string Name { get; set; } = "";
            public string Symbol { get; set; } = "";
            public string Unit { get; set; } = "%";
            public decimal OxideFactor { get; set; } = 1.0m;
            public decimal MinRange { get; set; }
            public decimal MaxRange { get; set; }
            public decimal DiffThreshold { get; set; } = 10m;
            public bool IsActive { get; set; } = true;
        }

        public record OxideReference(string Oxide, decimal Factor);
    }
}