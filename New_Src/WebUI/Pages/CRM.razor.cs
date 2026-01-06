using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using WebUI.Services;

namespace WebUI.Pages
{
    public partial class CRM
    {
        private List<CrmListItemDto> _crmList = new();
        private List<CrmListItemDto> _filteredCrmList = new();

        // ✅ لیست جدید برای نمایش صفحه جاری
        private List<CrmListItemDto> _pagedRows = new();

        private List<string> _analysisMethods = new();
        private string _searchText = "";
        private string? _selectedMethod;
        private bool _isLoading = false;
        private bool _showDialog = false;
        private bool _showViewDialog = false;
        private bool _isEditMode = false;
        private bool _isSaving = false;
        private CrmListItemDto _editingCrm = new();
        private CrmListItemDto? _viewingCrm;
        private List<ElementValue> _editingElements = new();

        // ✅ متغیرهای پیجینیشن
        private int _currentPage = 1;
        private int _pageSize = 10;
        private int _totalCount = 0;
        private int _totalPages => _totalCount == 0 ? 1 : (int)Math.Ceiling((double)_totalCount / _pageSize);

        // متغیرهای جدید برای مدیریت پاپ‌آپ متدها
        private bool _isMethodPopoverOpen;
        private string _methodSearchText = "";
        private HashSet<string> _selectedMethods = new(); // لیست متدهای انتخاب شده نهایی
        private HashSet<string> _tempSelectedMethods = new(); // لیست موقت هنگام باز بودن پاپ‌آپ

        // متن نمایشی داخل اینپوت
        private string _methodDisplayValue => _selectedMethods.Any()
            ? $"{_selectedMethods.Count} selected" // یا string.Join(", ", _selectedMethods)
            : "All Methods";

        // 1. باز کردن پاپ‌آپ
        private void OpenMethodFilter()
        {
            _isMethodPopoverOpen = true;
            _methodSearchText = "";
            // کپی کردن انتخاب‌های فعلی به لیست موقت
            _tempSelectedMethods = new HashSet<string>(_selectedMethods);
        }

        // بستن پاپ‌آپ
        private void CloseMethodFilter()
        {
            _isMethodPopoverOpen = false;
        }

        // 2. انتخاب همه
        private void SelectAllMethods()
        {
            // فیلتر کردن بر اساس سرچ اگر متنی جستجو شده باشد
            var visibleMethods = _analysisMethods
                .Where(m => string.IsNullOrEmpty(_methodSearchText) || m.Contains(_methodSearchText, StringComparison.OrdinalIgnoreCase));

            foreach (var method in visibleMethods)
            {
                _tempSelectedMethods.Add(method);
            }
        }

        // 3. هیچکدام (Deselect All)
        private void DeselectAllMethods()
        {
            // فقط آنهایی که در جستجو دیده می‌شوند یا کل لیست (بسته به نیاز)
            // اینجا کل لیست را پاک میکنیم طبق استاندارد اکسل
            _tempSelectedMethods.Clear();
        }

        // 4. دکمه کنسل
        private void CancelMethodFilter()
        {
            _isMethodPopoverOpen = false;
            // هیچ تغییری در _selectedMethods اصلی نمی‌دهیم
        }

        // 5. دکمه تایید (OK)
        private async Task ApplyMethodFilter()
        {
            _selectedMethods = new HashSet<string>(_tempSelectedMethods);
            _isMethodPopoverOpen = false;

            // فراخوانی مجدد دیتا یا اعمال فیلتر روی لیست موجود
            // اگر فیلتر سمت سرور است:
            await LoadCrmData();

            // اگر فیلتر سمت کلاینت است (مثل کد فعلی شما):
            // ApplyFilters(); 
        }

        // تغییر وضعیت چک‌باکس تکی
        private void ToggleMethodSelection(string method, bool? isChecked)
        {
            if (isChecked == true)
                _tempSelectedMethods.Add(method);
            else
                _tempSelectedMethods.Remove(method);
        }

        // *مهم*: بروزرسانی متد ApplyFilters برای پشتیبانی از چند انتخابی
        private void ApplyFilters()
        {
            var filtered = _crmList.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(c =>
                    c.CrmId.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }

            // تغییر منطق از == به Contains برای پشتیبانی از چند انتخاب
            if (_selectedMethods.Any())
            {
                filtered = filtered.Where(c => c.AnalysisMethod != null && _selectedMethods.Contains(c.AnalysisMethod));
            }

            _filteredCrmList = filtered.ToList();

            _totalCount = _filteredCrmList.Count;
            _currentPage = 1;
            UpdatePagedRows();
        }
        protected override async Task OnInitializedAsync()
        {
            await LoadAnalysisMethods();
            await LoadCrmData();
        }

        private async Task LoadAnalysisMethods()
        {
            var result = await CrmService.GetAnalysisMethodsAsync();
            if (result.Succeeded && result.Data != null)
            {
                _analysisMethods = result.Data;
            }
        }

        private async Task LoadCrmData()
        {
            _isLoading = true;
            StateHasChanged();

            try
            {
                var result = await CrmService.GetCrmListAsync(
                    analysisMethod: _selectedMethod,
                    search: _searchText,
                    page: 1,
                    pageSize: 0);

                if (result.Succeeded && result.Data != null)
                {
                    _crmList = result.Data.Items;
                    ApplyFilters(); // فیلتر و صفحه‌بندی اولیه
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void OnSearchChanged(KeyboardEventArgs e)
        {
            ApplyFilters();
        }

        private async Task OnMethodChanged(string method)
        {
            _selectedMethod = method;
            await LoadCrmData();
        }

        //private void ApplyFilters()
        //{
        //    var filtered = _crmList.AsEnumerable();

        //    if (!string.IsNullOrWhiteSpace(_searchText))
        //    {
        //        filtered = filtered.Where(c =>
        //            c.CrmId.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        //    }

        //    if (!string.IsNullOrWhiteSpace(_selectedMethod))
        //    {
        //        filtered = filtered.Where(c => c.AnalysisMethod == _selectedMethod);
        //    }

        //    _filteredCrmList = filtered.ToList();

        //    // ✅ آپدیت لاجیک پیجینیشن بعد از فیلتر
        //    _totalCount = _filteredCrmList.Count;
        //    _currentPage = 1; // بازگشت به صفحه اول
        //    UpdatePagedRows();
        //}

        // ✅ متدهای جدید برای پیجینیشن (دقیقاً کپی شده از WeightCheck)
        private void UpdatePagedRows()
        {
            _pagedRows = _filteredCrmList
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();
        }

        private void OnPageChanged(int page)
        {
            _currentPage = page;
            UpdatePagedRows();
            StateHasChanged();
        }

        private void OnPageSizeChanged(int size)
        {
            _pageSize = size;
            _currentPage = 1;
            UpdatePagedRows();
            StateHasChanged();
        }


        private void OpenAddDialog()
        {
            _isEditMode = false;
            _editingCrm = new CrmListItemDto();
            _editingElements = new List<ElementValue> { new ElementValue() };
            _showDialog = true;
        }

        private void EditCrm(CrmListItemDto crm)
        {
            _isEditMode = true;
            _editingCrm = new CrmListItemDto
            {
                Id = crm.Id,
                CrmId = crm.CrmId,
                AnalysisMethod = crm.AnalysisMethod,
                Type = crm.Type,
                IsOurOreas = crm.IsOurOreas,
                Elements = new Dictionary<string, decimal>(crm.Elements)
            };
            _editingElements = crm.Elements
                .Select(e => new ElementValue { Name = e.Key, Value = e.Value })
                .ToList();
            _showDialog = true;
        }

        private void ViewCrm(CrmListItemDto crm)
        {
            _viewingCrm = crm;
            _showViewDialog = true;
        }

        private async Task DeleteCrm(CrmListItemDto crm)
        {
            var confirm = await DialogService.ShowMessageBox(
                "Delete CRM",
                $"Are you sure you want to delete CRM '{crm.CrmId}'?",
                yesText: "Delete",
                cancelText: "Cancel");

            if (confirm == true)
            {
                var result = await CrmService.DeleteCrmAsync(crm.Id);
                if (result.Succeeded)
                {
                    Snackbar.Add("CRM deleted successfully", Severity.Success);
                    await LoadCrmData();
                }
                else
                {
                    Snackbar.Add(result.Message ?? "Failed to delete CRM", Severity.Error);
                }
            }
        }

        private void CloseDialog()
        {
            _showDialog = false;
        }

        private void AddElement()
        {
            _editingElements.Add(new ElementValue());
        }

        private void RemoveElement(ElementValue el)
        {
            _editingElements.Remove(el);
        }

        private async Task SaveCrm()
        {
            if (string.IsNullOrWhiteSpace(_editingCrm.CrmId))
            {
                Snackbar.Add("CRM ID is required", Severity.Warning);
                return;
            }

            _isSaving = true;
            StateHasChanged();

            try
            {
                _editingCrm.Elements = _editingElements
                    .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                    .ToDictionary(e => e.Name, e => e.Value);

                var result = await CrmService.SaveCrmAsync(_editingCrm);
                if (result.Succeeded)
                {
                    Snackbar.Add(_isEditMode ? "CRM updated successfully" : "CRM created successfully", Severity.Success);
                    _showDialog = false;
                    await LoadCrmData();
                }
                else
                {
                    Snackbar.Add(result.Message ?? "Failed to save CRM", Severity.Error);
                }
            }
            finally
            {
                _isSaving = false;
            }
        }

        public class ElementValue
        {
            public string Name { get; set; } = "";
            public decimal Value { get; set; }
        }
    }
}
