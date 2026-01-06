namespace Application.DTOs;

public record CrmMethodOptionDto(
    string CrmId,
    List<string> Methods,
    string? DefaultMethod = null
);

public record CrmOptionsResult(
    List<CrmMethodOptionDto> Items
);
