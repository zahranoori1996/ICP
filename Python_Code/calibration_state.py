from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set


@dataclass
class RangeSettings:
    """Holds acceptable range thresholds."""
    range_low: float = 2.0
    range_mid: float = 20.0
    range_high1: float = 10.0
    range_high2: float = 8.0
    range_high3: float = 5.0
    range_high4: float = 3.0


@dataclass
class PreviewSettings:
    """Holds preview-related parameters."""
    preview_blank: float = 0.0
    preview_scale: float = 1.0
    excluded_outliers: Dict[str, Set[str]] = field(default_factory=dict)
    excluded_from_correct: Set[str] = field(default_factory=set)
    scale_range_min: Optional[float] = None
    scale_range_max: Optional[float] = None
    scale_above_50: bool = False
    calibration_range: str = "[0 to 0]"
    blank_labels: List[str] = field(default_factory=list)


def make_default_params(file_ranges: List[dict], elements: List[str]) -> Dict[int, dict]:
    """
    Create the per-file params structure that was previously built inline.
    Keeps the same defaults but concentrates them in one place.
    """
    empty_outliers = {el: set() for el in elements} if elements else {}
    params: Dict[int, dict] = {}
    for i in range(-1, len(file_ranges)):
        params[i] = {
            "range_low": 2.0,
            "range_mid": 20.0,
            "range_high1": 10.0,
            "range_high2": 8.0,
            "range_high3": 5.0,
            "range_high4": 3.0,
            "preview_blank": 0.0,
            "preview_scale": 1.0,
            "excluded_outliers": empty_outliers.copy(),
            "excluded_from_correct": set(),
            "scale_above_50": False,
            "scale_range_min": None,
            "scale_range_max": None,
        }
    return params

