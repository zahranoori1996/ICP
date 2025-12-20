import re
import pandas as pd
import logging
from utils.var_main import BLANK_PATTERN,CRM_PATTERN
from utils.utils import is_numeric,get_concentration_column,format_number
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class CRMVerificationDataManager:
    """Handles data logic for CRM verification, separate from UI."""

    def __init__(self):
        self.crm_undo_stack = []
        self.crm_backup_columns = {}  # ذخیره backup ستون‌ها

    def extract_crm_id(self, label):
        """Extract CRM ID from label."""
        # Handle tuple keys (label, row_index)
        if isinstance(label, tuple):
            label = label[0]
        m = re.search(CRM_PATTERN, str(label))
        return m.group(1) if m else str(label)

    def get_solution_concentration_range(self, original_df, selected_element):
        """Calculate solution concentration range."""
        concentration_column = get_concentration_column(original_df) if original_df is not None else None
        if original_df is not None and not original_df.empty and concentration_column:
            sample_data = original_df[
                (original_df['Type'].isin(['Samp', 'Sample'])) &
                (original_df['Element'] == selected_element)
            ][concentration_column]
            sample_data_numeric = [float(x) for x in sample_data if is_numeric(x)]
            if not sample_data_numeric:
                return '---', '---', '---', False
            soln_conc_min = min(sample_data_numeric)
            soln_conc_max = max(sample_data_numeric)
            soln_conc_range = f"[{format_number(soln_conc_min)} to {format_number(soln_conc_max)}]"
            in_calibration_range_soln = (
                float(calibration_range.split(' to ')[0][1:]) <= soln_conc_min <= float(calibration_range.split(' to ')[1][:-1]) and
                float(calibration_range.split(' to ')[0][1:]) <= soln_conc_max <= float(calibration_range.split(' to ')[1][:-1])
            ) if calibration_range != "[0 to 0]" else False
            return soln_conc_min, soln_conc_max, soln_conc_range, in_calibration_range_soln
        return '---', '---', '---', False

    def get_best_blank(self, pivot_df, selected_element, app_crm_check, excluded_outliers, calibration_range, in_calibration_range_soln, wavelength, analysis_date):
        """Find best blank value and label."""
        blank_rows = pivot_df[
            pivot_df['Solution Label'].str.contains(BLANK_PATTERN, case=False, na=False, regex=True)
        ]
        blank_val = 0
        blank_correction_status = "Not Applied"
        selected_blank_label = "None"
        blank_labels = []

        blank_labels_set = set(blank_rows['Solution Label'].values)

        if not blank_rows.empty:
            best_blank_val = 0
            best_blank_label = "None"
            min_distance = float('inf')
            in_range_found = False

            for _, row in blank_rows.iterrows():
                candidate_blank = row[selected_element] if pd.notna(row[selected_element]) else 0
                candidate_label = row['Solution Label']
                if not is_numeric(candidate_blank):
                    continue
                candidate_blank = float(candidate_blank)
                blank_labels.append(f"{candidate_label}: {format_number(candidate_blank)}")

                in_range = False
                for row_key in app_crm_check._inline_crm_rows_display.keys():
                    sol_label = row_key[0] if isinstance(row_key, tuple) else row_key
                    if sol_label in blank_labels_set:
                        continue

                    pivot_row = pivot_df[pivot_df['Solution Label'] == sol_label]
                    if pivot_row.empty:
                        continue

                    pivot_val = pivot_row.iloc[0][selected_element]
                    if not is_numeric(pivot_val):
                        continue
                    pivot_val_float = float(pivot_val)

                    for row_data, _ in app_crm_check._inline_crm_rows_display[row_key]:
                        if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                            val = row_data[pivot_df.columns.get_loc(selected_element)] if selected_element in pivot_df.columns else ""
                            if is_numeric(val):
                                crm_val = float(val)
                                range_val = self.calculate_dynamic_range(crm_val)
                                lower, upper = crm_val - range_val, crm_val + range_val
                                corrected_pivot = pivot_val_float - candidate_blank
                                if lower <= corrected_pivot <= upper:
                                    in_range = True
                                    break
                    if in_range:
                        break

                if in_range:
                    best_blank_val = candidate_blank
                    best_blank_label = candidate_label
                    in_range_found = True
                    break

            if not in_range_found:
                for row_key in app_crm_check._inline_crm_rows_display.keys():
                    sol_label = row_key[0] if isinstance(row_key, tuple) else row_key
                    if sol_label in blank_labels_set:
                        continue

                    pivot_row = pivot_df[pivot_df['Solution Label'] == sol_label]
                    if pivot_row.empty:
                        continue

                    pivot_val = pivot_row.iloc[0][selected_element]
                    if not is_numeric(pivot_val):
                        continue
                    pivot_val_float = float(pivot_val)

                    for row_data, _ in app_crm_check._inline_crm_rows_display[row_key]:
                        if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                            val = row_data[pivot_df.columns.get_loc(selected_element)] if selected_element in pivot_df.columns else ""
                            if not is_numeric(val):
                                continue
                            crm_val = float(val)
                            corrected_pivot = pivot_val_float - candidate_blank
                            distance = abs(corrected_pivot - crm_val)
                            if distance < min_distance:
                                min_distance = distance
                                best_blank_val = candidate_blank
                                best_blank_label = candidate_label

            blank_val = best_blank_val
            selected_blank_label = best_blank_label
            blank_correction_status = "Applied" if blank_val != 0 else "Not Applied"

        return blank_val, blank_correction_status, selected_blank_label, blank_labels

    def get_crm_labels(self, app_crm_check, blank_labels_set):
        """Get CRM labels, excluding blanks."""
        crm_labels = []
        for row_key in app_crm_check._inline_crm_rows_display.keys():
            sol_label = row_key[0] if isinstance(row_key, tuple) else row_key
            if sol_label in blank_labels_set:
                continue
            checkbox_key = f"{sol_label}_{row_key[1]}" if isinstance(row_key, tuple) else sol_label
            if checkbox_key in app_crm_check.included_crms and app_crm_check.included_crms[checkbox_key].isChecked():
                crm_labels.append(row_key)
        return crm_labels

    def group_crm_labels(self, crm_labels, extract_crm_id_func):
        """Group CRM labels by ID."""
        crm_id_to_labels = {}
        for row_key in crm_labels:
            sol_label = row_key[0] if isinstance(row_key, tuple) else row_key
            crm_id = extract_crm_id_func(sol_label)
            if crm_id not in crm_id_to_labels:
                crm_id_to_labels[crm_id] = []
            crm_id_to_labels[crm_id].append(row_key)
        return sorted(crm_id_to_labels.keys()), crm_id_to_labels

    def collect_crm_data(self, unique_crm_ids, crm_id_to_labels, pivot_df, selected_element, original_df, 
                         excluded_outliers, excluded_from_correct, scale_range_min, scale_range_max, scale_above_50_cb, 
                         preview_blank, preview_scale, app_crm_check):
        """Collect CRM values, bounds, etc."""
        certificate_values = {crm_id: [] for crm_id in unique_crm_ids}
        sample_values = {crm_id: [] for crm_id in unique_crm_ids}
        outlier_values = {crm_id: [] for crm_id in unique_crm_ids}
        lower_bounds = {crm_id: [] for crm_id in unique_crm_ids}
        upper_bounds = {crm_id: [] for crm_id in unique_crm_ids}
        soln_concs = {crm_id: [] for crm_id in unique_crm_ids}
        int_values = {crm_id: [] for crm_id in unique_crm_ids}

        concentration_column = get_concentration_column(original_df) if original_df is not None else None
        element_name = selected_element.split()[0]

        for crm_id in unique_crm_ids:
            for row_key in crm_id_to_labels[crm_id]:
                sol_label = row_key[0] if isinstance(row_key, tuple) else row_key

                pivot_row = pivot_df[pivot_df['Solution Label'] == sol_label]
                if pivot_row.empty:
                    continue

                pivot_val = pivot_row.iloc[0][selected_element]
                if pd.isna(pivot_val) or not is_numeric(pivot_val):
                    pivot_val = 0
                else:
                    pivot_val = float(pivot_val)

                if original_df is not None and not original_df.empty and concentration_column:
                    sample_rows = original_df[
                        (original_df['Solution Label'] == sol_label) &
                        (original_df['Element'].str.startswith(element_name)) &
                        (original_df['Type'].isin(['Samp', 'Sample']))
                    ]
                    soln_conc = sample_rows[concentration_column].iloc[0] if not sample_rows.empty else '---'
                    int_val = sample_rows['Int'].iloc[0] if not sample_rows.empty else '---'
                else:
                    soln_conc = '---'
                    int_val = '---'

                for row_data, _ in app_crm_check._inline_crm_rows_display[row_key]:
                    if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                        val = row_data[pivot_df.columns.get_loc(selected_element)] if selected_element in pivot_df.columns else ""
                        if not val or not is_numeric(val):
                            continue

                        crm_val = float(val)
                        pivot_val_float = float(pivot_val)
                        corrected_val = pivot_val_float

                        if (sol_label not in excluded_from_correct and
                            is_numeric(pivot_val) and
                            (scale_range_min is None or scale_range_max is None or
                             scale_range_min <= float(pivot_val) <= scale_range_max) and
                            (not scale_above_50_cb or float(pivot_val) > 50)):
                            corrected_val = (pivot_val_float - preview_blank) * preview_scale

                        range_val = self.calculate_dynamic_range(crm_val)
                        lower = crm_val - range_val
                        upper = crm_val + range_val

                        certificate_values[crm_id].append(crm_val)
                        if sol_label in excluded_outliers.get(selected_element, set()):
                            outlier_values[crm_id].append(corrected_val)
                        else:
                            sample_values[crm_id].append(corrected_val)
                        lower_bounds[crm_id].append(lower)
                        upper_bounds[crm_id].append(upper)
                        soln_concs[crm_id].append(soln_conc)
                        int_values[crm_id].append(int_val)

        return certificate_values, sample_values, outlier_values, lower_bounds, upper_bounds, soln_concs, int_values

    def build_annotations(self, unique_crm_ids, crm_id_to_labels, certificate_values, sample_values, outlier_values, 
                          lower_bounds, upper_bounds, soln_concs, int_values, pivot_df, selected_element, 
                          excluded_outliers, blank_val, selected_blank_label, blank_correction_status, 
                          in_calibration_range_soln, calibration_range, wavelength, analysis_date, app_crm_check):
        """Build annotations for plot."""
        annotations = []
        for crm_id in unique_crm_ids:
            for idx, row_key in enumerate(crm_id_to_labels[crm_id]):
                sol_label = row_key[0] if isinstance(row_key, tuple) else row_key

                pivot_row = pivot_df[pivot_df['Solution Label'] == sol_label]
                if pivot_row.empty:
                    continue

                pivot_val = pivot_row.iloc[0][selected_element]
                if pd.isna(pivot_val) or not is_numeric(pivot_val):
                    pivot_val = 0
                else:
                    pivot_val = float(pivot_val)

                for row_data, _ in app_crm_check._inline_crm_rows_display[row_key]:
                    if isinstance(row_data, list) and row_data and row_data[0].endswith("CRM"):
                        val = row_data[pivot_df.columns.get_loc(selected_element)] if selected_element in pivot_df.columns else ""
                        if not val or not is_numeric(val):
                            if sol_label not in excluded_outliers.get(selected_element, set()):
                                annotation = f"Verification ID: {crm_id} (Label: {sol_label})\n - Certificate Value: {val or 'N/A'}\n - Sample Value: {format_number(pivot_val)}\n - Acceptable Range: [N/A]\n - Status: Out of range (non-numeric data).\n - Blank Value: {format_number(blank_val)}\n - Blank Label: {selected_blank_label}\n - Blank Correction Status: {blank_correction_status}\n - Sample Value - Blank: {format_number(pivot_val)}\n - Corrected Range: [N/A]\n - Status after Blank Subtraction: Out of range (non-numeric data).\n - Soln Conc: {soln_concs[crm_id][idx] if isinstance(soln_concs[crm_id][idx], str) else format_number(soln_concs[crm_id][idx])} {'in_range' if in_calibration_range_soln else 'out_range'}\n - Int: {int_values[crm_id][idx] if isinstance(int_values[crm_id][idx], str) else format_number(int_values[crm_id][idx])}\n - Calibration Range: {calibration_range} {'in_range' if in_calibration_range_soln else 'out_range'}\n - CRM Source: NIST\n - Sample Matrix: Soil\n - Element Wavelength: {wavelength}\n - Analysis Date: {analysis_date}"
                                annotations.append(annotation)
                            continue

                        crm_val = float(val)
                        pivot_val_float = float(pivot_val)
                        corrected_val = sample_values[crm_id][idx] if idx < len(sample_values[crm_id]) else outlier_values[crm_id][idx]
                        lower = lower_bounds[crm_id][idx]
                        upper = upper_bounds[crm_id][idx]
                        in_range = lower <= corrected_val <= upper

                        if sol_label not in excluded_outliers.get(selected_element, set()):
                            annotation = f"Verification ID: {crm_id} (Label: {sol_label})\n - Certificate Value: {format_number(crm_val)}\n - Sample Value: {format_number(pivot_val_float)}\n - Acceptable Range: [{format_number(lower)} to {format_number(upper)}]"
                            if in_range:
                                annotation += f"\n - Status: In range (no adjustment needed)."
                                annotation += f"\n - Blank Value: {format_number(blank_val)}\n - Blank Label: {selected_blank_label}\n - Blank Correction Status: Not Applied (in range)\n - Sample Value - Blank: {format_number(corrected_val)}\n - Corrected Range: [{format_number(lower)} to {format_number(upper)}]\n - Status after Blank Subtraction: In range."
                            else:
                                annotation += f"\n - Status: Out of range without adjustment."
                                annotation += f"\n - Blank Value: {format_number(blank_val)}\n - Blank Label: {selected_blank_label}\n - Blank Correction Status: {blank_correction_status}\n - Sample Value - Blank: {format_number(corrected_val)}\n - Corrected Range: [{format_number(lower)} to {format_number(upper)}]"
                                corrected_in_range = lower <= corrected_val <= upper
                                if corrected_in_range:
                                    annotation += f"\n - Status after Blank Subtraction: In range."
                                else:
                                    annotation += f"\n - Status after Blank Subtraction: Out of range."
                                    if corrected_val != 0:
                                        if corrected_val < lower:
                                            scale_factor = lower / corrected_val
                                            direction = "increase"
                                        elif corrected_val > upper:
                                            scale_factor = upper / corrected_val
                                            direction = "decrease"
                                        else:
                                            scale_factor = 1.0
                                            direction = ""
                                        scale_percent = abs((scale_factor - 1) * 100)
                                        annotation += f"\n - Required Scaling: {scale_percent:.2f}% {direction} to fit within range."
                                        if scale_percent > 32:
                                            annotation += f"\n - Warning: Scaling exceeds 32% ({scale_percent:.2f}%)."
                                    else:
                                        annotation += f"\n - Scaling not applicable (corrected sample value is zero)."

                            annotation += f"\n - Soln Conc: {soln_concs[crm_id][idx] if isinstance(soln_concs[crm_id][idx], str) else format_number(soln_concs[crm_id][idx])} {'in_range' if in_calibration_range_soln else 'out_range'}\n - Int: {int_values[crm_id][idx] if isinstance(int_values[crm_id][idx], str) else format_number(int_values[crm_id][idx])}\n - Calibration Range: {calibration_range} {'in_range' if in_calibration_range_soln else 'out_range'}\n - CRM Source: NIST\n - Sample Matrix: Soil\n - Element Wavelength: {wavelength}\n - Analysis Date: {analysis_date}"
                            annotations.append(annotation)

        return annotations

    def get_plot_data_bounds(self, certificate_values, sample_values, outlier_values, lower_bounds, upper_bounds, unique_crm_ids):
        """Get min/max for plot."""
        all_y_values = []
        for crm_id in unique_crm_ids:
            all_y_values.extend(certificate_values.get(crm_id, []))
            all_y_values.extend(sample_values.get(crm_id, []))
            all_y_values.extend(outlier_values.get(crm_id, []))
            all_y_values.extend(lower_bounds.get(crm_id, []))
            all_y_values.extend(upper_bounds.get(crm_id, []))
        if all_y_values:
            y_min, y_max = min(all_y_values), max(all_y_values)
            margin = (y_max - y_min) * 0.1
            return y_min - margin, y_max + margin
        return None, None

    def get_filtered_data(self, pivot_df, selected_element, filter_text):
        """Get filtered data for secondary plot."""
        if 'pivot_index' not in pivot_df.columns:
            pivot_df['pivot_index'] = pivot_df.index
        filtered_data = pivot_df.copy()
        if filter_text:
            filtered_data = filtered_data[filtered_data['Solution Label'].str.lower().str.contains(filter_text, na=False)]
        x_sec = filtered_data['pivot_index'].values
        y_sec = pd.to_numeric(filtered_data[selected_element], errors='coerce').fillna(0).values
        return x_sec, y_sec

    def update_calibration_range(self, original_df, selected_element):
        """Update calibration range data."""
        concentration_column = get_concentration_column(original_df)
        if concentration_column:
            element_name = selected_element[:-2] if len(selected_element) >= 2 and selected_element[-2] == '_' else selected_element
            std_data = original_df[
                (original_df['Type'] == 'Std') &
                (original_df['Element'] == element_name)
            ][concentration_column]
            std_data_numeric = [float(x) for x in std_data if is_numeric(x)]
            if std_data_numeric:
                calibration_min = min(std_data_numeric)
                calibration_max = max(std_data_numeric)
                return f"[{format_number(calibration_min)} to {format_number(calibration_max)}]"
        return "[0 to 0]"

    def undo_crm_correction(self, selected_element, app_results, pivot_df, all_pivot_df):
        """Undo CRM correction data logic."""
        if not hasattr(app_results, 'report_change'):
            raise ValueError("No CRM corrections!")

        report_change = app_results.report_change
        crm_mask = (
            (report_change['Element'] == selected_element) & 
            (report_change['Scale'].notna() | report_change['Blank'].notna())
        )

        if not crm_mask.any():
            raise ValueError(f"No CRM corrections for {selected_element}!")

        for _, row in report_change[crm_mask].iterrows():
            sl = row['Solution Label']
            original_val = row['Original Value']

            if hasattr(all_pivot_df) and not all_pivot_df.empty:
                mask = all_pivot_df['Solution Label'] == sl
                if mask.any():
                    all_pivot_df.loc[mask, selected_element] = original_val

            if not pivot_df.empty:
                mask = pivot_df['Solution Label'] == sl
                if mask.any():
                    pivot_df.loc[mask, selected_element] = original_val

        report_change = report_change[~crm_mask].reset_index(drop=True)
        app_results.report_change = report_change

        return pivot_df, all_pivot_df, report_change

    def correct_crm(self, pivot_df, selected_element, excluded_from_correct, scale_range_min, 
                    scale_range_max, scale_above_50_cb, preview_blank, preview_scale):
        """Apply CRM correction to pivot_df."""
        if pivot_df is None or pivot_df.empty:
            raise ValueError("No data available to correct!")

        column_to_correct = selected_element
        if column_to_correct not in pivot_df.columns:
            raise ValueError(f"Column {column_to_correct} not found!")

        corrected_count = 0
        correction_data = []

        for index, row in pivot_df.iterrows():
            solution_label = row['Solution Label']
            current_val = row[column_to_correct]
            if pd.notna(current_val) and is_numeric(current_val):
                val = float(current_val)
                if (solution_label not in excluded_from_correct and
                    (scale_range_min is None or scale_range_max is None or
                    scale_range_min <= val <= scale_range_max) and
                    (not scale_above_50_cb or val > 50)):

                    new_val = (val - preview_blank) * preview_scale
                    pivot_df.at[index, column_to_correct] = new_val
                    corrected_count += 1

                    correction_data.append({
                        'Solution Label': solution_label,
                        'Element': column_to_correct,
                        'Scale': preview_scale,
                        'Blank': preview_blank,
                        'Original Value': val,
                        'New Value': new_val
                    })

        return pivot_df, corrected_count, correction_data

    def update_global_data_crm(self, column, correction_data, all_pivot_df, pivot_df, app_results, original_df, all_original_df):
        """Update global data with CRM corrections."""
        if hasattr(all_pivot_df) and not all_pivot_df.empty:
            for item in correction_data:
                sl = item['Solution Label']
                mask = all_pivot_df['Solution Label'] == sl
                if mask.any():
                    old_val = all_pivot_df.loc[mask, column].iloc[0]
                    new_val = pivot_df.loc[pivot_df['Solution Label'] == sl, column].iloc[0]
                    all_pivot_df.loc[mask, column] = new_val

        if hasattr(app_results):
            results_data = all_pivot_df.copy() if hasattr(all_pivot_df) else pivot_df.copy()
            app_results.last_filtered_data = results_data

        if hasattr(original_df) and original_df is not None:
            conc_col = get_concentration_column(original_df)
            if conc_col:
                for item in correction_data:
                    sl = item['Solution Label']
                    mask = (
                        (original_df['Solution Label'] == sl) & 
                        (original_df['Element'] == column) &
                        (original_df['Type'].isin(['Samp', 'Sample']))
                    )
                    if mask.any():
                        original_df.loc[mask, conc_col] = item['New Value']

                if hasattr(all_original_df):
                    all_original_df.loc[original_df.index, :] = original_df

        return all_pivot_df, pivot_df, original_df, all_original_df

    def save_crm_to_report_change(self, correction_data, element, app_results):
        """Save CRM to report_change."""
        if not hasattr(app_results, 'report_change'):
            app_results.report_change = pd.DataFrame(
                columns=['Solution Label', 'Element', 'Scale', 'Blank', 'Original Value', 'New Value']
            )

        report_change = app_results.report_change

        if correction_data:
            crm_df = pd.DataFrame(correction_data)

            if 'Element' in report_change.columns:
                existing_mask = report_change['Element'] == element
                report_change = report_change[~existing_mask]

            report_change = pd.concat([report_change, crm_df], ignore_index=True)
            app_results.report_change = report_change

        return report_change

    def calculate_dynamic_range(self, crm_val):
        # Implement or assume
        return crm_val * 0.1  # Example