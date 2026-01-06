namespace Application.DTOs;

public record CrmSelectionRowDto(
    string SolutionLabel,
    int RowIndex,
    string CrmId,
    List<string> PreferredOptions,
    List<string> AllOptions,
    string? SelectedOption);

public record CrmSelectionOptionsResult(
    List<CrmSelectionRowDto> Items
);

public record CrmSelectionItemDto(
    string SolutionLabel,
    int RowIndex,
    string SelectedCrmKey
);

public record CrmSelectionSaveRequest(
    Guid ProjectId,
    List<CrmSelectionItemDto> Selections
);
