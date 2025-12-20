import numpy as np
import pandas as pd
import logging

logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class RMDriftDataManager:
    """Handles data logic for RM drift corrections, separate from UI."""

    def __init__(self):
        self.undo_stack = []
        self.manual_corrections = {}  # {original_index: corrected_value}
        self.ignored_pivots = set()   # set of ignored pivot_indices
        self.corrected_drift = {}

    def get_valid_rm_data(self, rm_df, initial_rm_df, selected_element, current_rm_num,
                          empty_rows_from_check, ignored_pivots, positions_df=None):
        """Extract valid RM data for current group, including masks."""
        label_df = rm_df[rm_df['rm_num'] == current_rm_num].sort_values('pivot_index')
        initial_label_df = initial_rm_df[initial_rm_df['rm_num'] == current_rm_num].sort_values('pivot_index')

        # Align lengths
        if len(label_df) != len(initial_label_df):
            common_pivot = np.intersect1d(label_df['pivot_index'], initial_label_df['pivot_index'])
            label_df = label_df[label_df['pivot_index'].isin(common_pivot)].sort_values('pivot_index')
            initial_label_df = initial_label_df[initial_label_df['pivot_index'].isin(common_pivot)].sort_values('pivot_index')

        pivot_indices = label_df['pivot_index'].values
        original_values = pd.to_numeric(initial_label_df[selected_element], errors='coerce').values
        display_values = pd.to_numeric(label_df[selected_element], errors='coerce').values
        valid_mask = ~np.isnan(original_values) & ~np.isnan(display_values)

        current_valid_pivot_indices = pivot_indices[valid_mask]
        original_rm_values = original_values[valid_mask]
        display_rm_values = display_values[valid_mask]
        rm_types = label_df.loc[label_df.index[valid_mask], 'rm_type'].values
        solution_labels = label_df.loc[label_df.index[valid_mask], 'Solution Label'].values

        # Empty and ignored masks
        empty_pivot_set = set(empty_rows_from_check['original_index'].dropna().astype(int).tolist()) \
            if not empty_rows_from_check.empty and 'original_index' in empty_rows_from_check.columns else set()
        is_really_empty = np.array([p in empty_pivot_set for p in current_valid_pivot_indices], dtype=bool)
        is_manually_ignored = np.array([p in ignored_pivots for p in current_valid_pivot_indices], dtype=bool)
        effective_empty = is_really_empty | is_manually_ignored

        file_names = self._get_file_names_for_pivots(current_valid_pivot_indices, file_ranges=None)  # Adjust if file_ranges needed

        return {
            'pivot_indices': current_valid_pivot_indices,
            'original_values': original_rm_values,
            'display_values': display_rm_values,
            'types': rm_types,
            'labels': solution_labels,
            'effective_empty': effective_empty,
            'file_names': file_names
        }

    def _get_file_names_for_pivots(self, pivot_indices, file_ranges):
        """Helper to get file names for pivots."""
        if not file_ranges:
            return ["All"] * len(pivot_indices)
        file_names = []
        for pivot_idx in pivot_indices:
            for idx, fr in enumerate(file_ranges):
                if fr['start_pivot_row'] <= pivot_idx <= fr['end_pivot_row']:
                    file_names.append(fr.get('file_name', f"File {idx+1}"))
                    break
            else:
                file_names.append("Unknown")
        return file_names

    def auto_optimize_to_flat(self, rm_df, all_rm_df, selected_element, current_rm_num,
                              empty_rows_from_check, ignored_pivots, positions_df,
                              global_optimize, per_file, file_ranges, current_file_index):
        """Optimize RM to flat - data logic only."""
        empty_pivot_set = set(empty_rows_from_check['original_index'].dropna().astype(int).tolist()) \
            if not empty_rows_from_check.empty else set()

        if current_file_index <= 0 and per_file:
            if global_optimize:
                return self._auto_optimize_to_flat_per_file_global_style(
                    all_rm_df, selected_element, current_rm_num, empty_pivot_set,
                    ignored_pivots, file_ranges, rm_df
                )
            else:
                return self._auto_optimize_to_flat_per_file(
                    all_rm_df, selected_element, current_rm_num, empty_pivot_set,
                    ignored_pivots, file_ranges, rm_df
                )

        # Standard logic for single file or no per-file
        rm_mask = (rm_df['rm_num'] == current_rm_num)
        if not rm_mask.any():
            return rm_df, all_rm_df

        y = rm_df.loc[rm_mask, selected_element].astype(float).values
        pivot = rm_df.loc[rm_mask, 'pivot_index'].values
        is_empty = np.array([p in empty_pivot_set for p in pivot])
        normal_mask = ~is_empty & ~np.isnan(y)
        if normal_mask.sum() == 0:
            return rm_df, all_rm_df

        seg_dict = dict(zip(positions_df['pivot_index'], positions_df['segment_id']))
        unique_segs = np.unique([seg_dict.get(p, -1) for p in pivot[normal_mask]])

        if global_optimize:
            first_idx = np.where(normal_mask)[0][0]
            first_val = y[first_idx]
            y[normal_mask] = first_val
        else:
            for seg_id in unique_segs:
                if seg_id == -1:
                    continue
                seg_mask = np.array([seg_dict.get(p, -1) == seg_id for p in pivot])
                seg_normal_mask = seg_mask & normal_mask
                if seg_normal_mask.sum() == 0:
                    continue
                first_idx = np.where(seg_normal_mask)[0][0]
                first_val = y[first_idx]
                y[seg_normal_mask] = first_val

        rm_df.loc[rm_mask, selected_element] = y
        all_rm_df = self._sync_rm_to_all(rm_df, all_rm_df, selected_element)
        return rm_df, all_rm_df

    def _auto_optimize_to_flat_per_file_global_style(self, all_rm_df, selected_element, current_rm_num,
                                                      empty_pivot_set, ignored_pivots, file_ranges, rm_df=None):
        """Per file global flat optimization."""
        for fr in file_ranges:
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']
            file_rm_mask = (
                all_rm_df['pivot_index'].between(start, end) &
                (all_rm_df['rm_num'] == current_rm_num)
            )
            if not file_rm_mask.any():
                continue

            rm_rows = all_rm_df[file_rm_mask].sort_values('pivot_index')
            pivots = rm_rows['pivot_index'].values
            y_file = rm_rows[selected_element].astype(float).values.copy()

            valid_mask = (
                ~np.isnan(y_file) &
                ~np.isin(pivots, list(ignored_pivots)) &
                ~np.isin(pivots, list(empty_pivot_set)) &
                (y_file > 1e-6)
            )

            if not valid_mask.any():
                continue

            first_valid_idx = np.where(valid_mask)[0][0]
            first_valid_value = y_file[first_valid_idx]
            y_file[valid_mask] = first_valid_value
            all_rm_df.loc[file_rm_mask, selected_element] = y_file

        # Sync if in All Files mode
        if rm_df is not None:
            current_rm_mask = all_rm_df['rm_num'] == current_rm_num
            target_mask = rm_df['rm_num'] == current_rm_num
            if current_rm_mask.any() and target_mask.any():
                rm_df.loc[target_mask, selected_element] = \
                    all_rm_df.loc[current_rm_mask, selected_element].values

        return rm_df, all_rm_df

    def _auto_optimize_to_flat_per_file(self, all_rm_df, selected_element, current_rm_num,
                                         empty_pivot_set, ignored_pivots, file_ranges, rm_df=None):
        """Per file flat optimization (previous behavior)."""
        for fr in file_ranges:
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']
            file_rm_mask = all_rm_df['pivot_index'].between(start, end) & (all_rm_df['rm_num'] == current_rm_num)
            if not file_rm_mask.any():
                continue
            rm_rows = all_rm_df[file_rm_mask].sort_values('pivot_index').reset_index(drop=True)
            pivots = rm_rows['pivot_index'].values
            y_file = rm_rows[selected_element].astype(float).values.copy()
            valid_mask = ~np.isnan(y_file)
            ignored_or_empty = np.array([p in ignored_pivots or p in empty_pivot_set for p in pivots])
            usable_mask = valid_mask & ~ignored_or_empty & (y_file > 1e-6)
            if not usable_mask.any():
                continue
            first_valid_idx = np.where(usable_mask)[0][0]
            first_valid_value = y_file[first_valid_idx]
            y_file[usable_mask] = first_valid_value
            all_rm_df.loc[file_rm_mask, selected_element] = y_file

        # Sync if in All Files mode
        if rm_df is not None:
            current_rm_mask = all_rm_df['rm_num'] == current_rm_num
            target_mask = rm_df['rm_num'] == current_rm_num
            if current_rm_mask.any() and target_mask.any():
                rm_df.loc[target_mask, selected_element] = all_rm_df.loc[current_rm_mask, selected_element].values

        return rm_df, all_rm_df

    def auto_optimize_slope_to_zero(self, rm_df, all_rm_df, display_rm_values, selected_element,
                                     current_rm_num, empty_rows_from_check, ignored_pivots,
                                     positions_df, global_optimize, per_file, file_ranges,
                                     current_file_index):
        """Optimize slope to zero - data logic."""
        if len(display_rm_values) < 2:
            return rm_df, all_rm_df, display_rm_values

        if current_file_index <= 0 and per_file:
            return self._auto_optimize_slope_to_zero_per_file(
                all_rm_df, selected_element, current_rm_num, ignored_pivots,
                file_ranges, rm_df, display_rm_values
            )

        rm_data = self.get_valid_rm_data(rm_df, rm_df, selected_element, current_rm_num,  # Assuming initial_rm_df == rm_df for simplicity; adjust if needed
                                         empty_rows_from_check, ignored_pivots, positions_df)
        y = display_rm_values.copy()
        pivot_indices = rm_data['pivot_indices']
        normal_mask = ~rm_data['effective_empty']

        if normal_mask.sum() < 2:
            return rm_df, all_rm_df, y

        seg_dict = dict(zip(positions_df['pivot_index'], positions_df['segment_id']))

        if global_optimize:
            x_n = pivot_indices[normal_mask]
            y_n = y[normal_mask].copy()
            slope, _ = np.polyfit(x_n, y_n, 1)
            first_x = x_n[0]
            y_n -= slope * (x_n - first_x)
            y[normal_mask] = y_n
        else:
            unique_segs = np.unique([seg_dict.get(p, -1) for p in pivot_indices[normal_mask]])
            for seg_id in unique_segs:
                if seg_id == -1:
                    continue
                seg_mask = np.isin(pivot_indices, [p for p, s in seg_dict.items() if s == seg_id])
                seg_normal_mask = seg_mask & normal_mask
                if seg_normal_mask.sum() < 2:
                    continue
                x_n = pivot_indices[seg_normal_mask]
                y_n = y[seg_normal_mask].copy()
                slope, _ = np.polyfit(x_n, y_n, 1)
                first_x = x_n[0]
                y_n -= slope * (x_n - first_x)
                y[seg_normal_mask] = y_n

        # Sync y back to dfs
        for i, pivot in enumerate(pivot_indices):
            if rm_data['effective_empty'][i]:
                continue
            new_value = y[i]
            mask_current = (rm_df['rm_num'] == current_rm_num) & (rm_df['pivot_index'] == pivot)
            if mask_current.any():
                rm_df.loc[mask_current, selected_element] = new_value
            mask_all = (all_rm_df['rm_num'] == current_rm_num) & (all_rm_df['pivot_index'] == pivot)
            if mask_all.any():
                all_rm_df.loc[mask_all, selected_element] = new_value

        return rm_df, all_rm_df, y

    def _auto_optimize_slope_to_zero_per_file(self, all_rm_df, selected_element, current_rm_num,
                                               ignored_pivots, file_ranges, rm_df=None, display_rm_values=None):
        """Per file slope to zero."""
        for fr in file_ranges:
            start = fr['start_pivot_row']
            end = fr['end_pivot_row']
            file_rm_mask = all_rm_df['pivot_index'].between(start, end) & (all_rm_df['rm_num'] == current_rm_num)
            if not file_rm_mask.any():
                continue
            y_file = all_rm_df.loc[file_rm_mask, selected_element].astype(float).values.copy()
            pivot_file = all_rm_df.loc[file_rm_mask, 'pivot_index'].values
            valid_mask = ~np.isnan(y_file)
            ignored_mask = np.array([p in ignored_pivots for p in pivot_file])
            final_valid_mask = valid_mask & ~ignored_mask
            if final_valid_mask.sum() < 2:
                continue
            x_valid = pivot_file[final_valid_mask]
            y_valid = y_file[final_valid_mask]
            slope, intercept = np.polyfit(x_valid, y_valid, 1)
            first_x = x_valid[0]
            y_corrected = y_valid - slope * (x_valid - first_x)
            y_file[final_valid_mask] = y_corrected
            all_rm_df.loc[file_rm_mask, selected_element] = y_file

        # Sync if needed
        if rm_df is not None:
            current_rm_mask = all_rm_df['rm_num'] == current_rm_num
            target_mask = rm_df['rm_num'] == current_rm_num
            if current_rm_mask.any() and target_mask.any():
                rm_df.loc[target_mask, selected_element] = all_rm_df.loc[current_rm_mask, selected_element].values

        return rm_df, all_rm_df, display_rm_values  # display_rm_values not changed here; adjust if needed

    def get_data_between_rm(self, selected_row, current_valid_pivot_indices, pivot_df,
                            selected_element, filter_solution_edit_text):
        """Get data between RMs."""
        if selected_row < 0 or selected_row >= len(current_valid_pivot_indices) - 1:
            return pd.DataFrame()
        pivot_prev = current_valid_pivot_indices[selected_row]
        pivot_curr = current_valid_pivot_indices[selected_row + 1]  # Corrected index
        cond = (pivot_df['original_index'] > pivot_prev) & (pivot_df['original_index'] < pivot_curr) & pivot_df[selected_element].notna()
        data = pivot_df[cond].copy().sort_values('original_index')
        filter_text = filter_solution_edit_text.strip().lower()
        if filter_text:
            filter_mask = data['Solution Label'].str.lower().str.contains(filter_text)
            data = data[filter_mask]
        return data

    def calculate_corrected_values_with_ratios(self, original_values, current_ratio, prev_ratio, stepwise):
        """Calculate corrected values with ratios."""
        n = len(original_values)
        if n == 0:
            return np.array([]), np.array([])
        if not stepwise:
            ratios = np.full(n, current_ratio)
            return original_values * ratios, ratios
        z = (current_ratio - prev_ratio) / n
        i = np.arange(1, n + 1)
        ratios = (z * i) + prev_ratio
        yo = ratios * original_values
        return yo, ratios

    def update_rm_data(self, rm_df, all_rm_df, display_rm_values, current_rm_num, selected_element,
                       last_filtered_data, current_valid_pivot_indices):
        """Sync display values to dataframes."""
        if len(display_rm_values) == 0:
            return rm_df, all_rm_df, last_filtered_data

        label_df = rm_df[(rm_df['rm_num'] == current_rm_num) & (rm_df['pivot_index'].isin(current_valid_pivot_indices))].sort_values('pivot_index').reset_index(drop=True)
        if len(label_df) != len(display_rm_values):
            return rm_df, all_rm_df, last_filtered_data

        for i, row in label_df.iterrows():
            rm_df.loc[rm_df['pivot_index'] == row['pivot_index'], selected_element] = display_rm_values[i]
            all_rm_df.loc[all_rm_df['pivot_index'] == row['pivot_index'], selected_element] = display_rm_values[i]
            if 'original_index' not in last_filtered_data.columns:
                if 'pivot_index' in last_filtered_data.columns:
                    last_filtered_data['original_index'] = last_filtered_data['pivot_index']
                else:
                    last_filtered_data['original_index'] = last_filtered_data.index
            cond = (last_filtered_data['original_index'] == row['original_index'])
            if not last_filtered_data[cond].empty:
                last_filtered_data.loc[cond, selected_element] = display_rm_values[i]

        return rm_df, all_rm_df, last_filtered_data

    def _sync_rm_to_all(self, rm_df, all_rm_df, selected_element):
        """Sync rm_df to all_rm_df."""
        for pivot, val in zip(rm_df['pivot_index'], rm_df[selected_element]):
            all_rm_df.loc[all_rm_df['pivot_index'] == pivot, selected_element] = val
        return all_rm_df

    def apply_to_single_rm(self, selected_element, current_rm_num, last_filtered_data, rm_df, all_rm_df,
                           display_rm_values, original_rm_values, current_valid_pivot_indices,
                           empty_rows_from_check, ignored_pivots, stepwise, manual_corrections, corrected_drift):
        """Apply corrections to single RM - data logic."""
        if not selected_element or current_rm_num is None:
            raise ValueError("No element or RM number selected.")

        rm_data = self.get_valid_rm_data(rm_df, rm_df, selected_element, current_rm_num,  # Adjust initial_rm_df
                                         empty_rows_from_check, ignored_pivots)
        pivot_indices = rm_data['pivot_indices']
        original_rm_values = rm_data['original_values']  # Override if needed
        display_values = display_rm_values  # Use passed
        effective_empty = rm_data['effective_empty']

        new_df = last_filtered_data.copy()
        corrected_drift = {k: v for k, v in corrected_drift.items() if k[1] != selected_element}  # Clear for element

        for i in range(len(pivot_indices) - 1):
            if effective_empty[i] or effective_empty[i + 1]:
                continue
            prev_pivot = pivot_indices[i]
            curr_pivot = pivot_indices[i + 1]
            cond = (new_df.index > prev_pivot) & (new_df.index < curr_pivot) & new_df[selected_element].notna()
            seg_data = new_df[cond].copy()
            if seg_data.empty:
                continue
            orig_values = seg_data[selected_element].values.astype(float)
            current_ratio = display_values[i + 1] / original_rm_values[i + 1] if original_rm_values[i + 1] != 0 else 1.0
            prev_ratio = display_values[i] / original_rm_values[i] if original_rm_values[i] != 0 else 1.0
            corrected, ratios = self.calculate_corrected_values_with_ratios(orig_values, current_ratio, prev_ratio, stepwise)
            new_df.loc[cond, selected_element] = corrected
            for j in range(len(seg_data)):
                sl = seg_data.iloc[j]['Solution Label']
                key = (sl, selected_element)
                corrected_drift[key] = ratios[j]

        # Apply manual corrections
        for orig_index, manual_val in manual_corrections.items():
            mask = new_df['original_index'] == orig_index
            if mask.any():
                old_val = new_df.loc[mask, selected_element].iloc[0]
                new_df.loc[mask, selected_element] = manual_val
                sl = new_df.loc[mask, 'Solution Label'].iloc[0]
                key = (sl, selected_element)
                ratio = manual_val / old_val if old_val != 0 else 1.0
                corrected_drift[key] = ratio

        return new_df, corrected_drift

    def save_corrected_drift(self, corrected_drift, report_change):
        """Save corrected drift to report_change."""
        try:
            drift_data = []
            for (sl, element), ratio in corrected_drift.items():
                drift_data.append({
                    'Solution Label': sl, 
                    'Element': element, 
                    'Ratio': ratio
                })
            
            drift_df = pd.DataFrame(drift_data)
            
            # SAFE REMOVAL OF EXISTING ENTRIES
            if 'Element' in report_change.columns:
                existing_elements = drift_df['Element'].unique()
                existing_mask = report_change['Element'].isin(existing_elements)
                report_change = report_change[~existing_mask]
            else:
                report_change = pd.DataFrame(columns=['Solution Label', 'Element', 'Ratio'])
            
            # SAFE CONCATENATION
            report_change = pd.concat([report_change, drift_df], ignore_index=True)
            
            logger.info(f"✅ Saved {len(drift_df)} drift coefficients to report_change")
            
            return report_change
        except Exception as e:
            logger.error(f"❌ Error saving corrected_drift: {str(e)}")
            return report_change

    def sync_corrected_drift_to_report_change(self, report_change):
        """Sync corrected_drift from report_change."""
        try:
            required_cols = ['Solution Label', 'Element', 'Ratio']
            missing_cols = [col for col in required_cols if col not in report_change.columns]
            if missing_cols:
                logger.warning(f"report_change missing columns: {missing_cols}")
                return {}

            corrected_drift = {}
            if not report_change.empty:
                for _, row in report_change.iterrows():
                    sl = row['Solution Label']
                    element = row['Element']
                    ratio = row['Ratio']
                    if (pd.notna(sl) and pd.notna(element) and pd.notna(ratio)):
                        key = (str(sl), str(element))
                        corrected_drift[key] = float(ratio)
            
            logger.info(f"🔄 Synced corrected_drift from report_change: {len(corrected_drift)} entries")
            return corrected_drift
        except Exception as e:
            logger.error(f"❌ Error syncing corrected_drift: {str(e)}")
            return {}

    def undo_changes(self, undo_stack, last_filtered_data, rm_df, corrected_drift, manual_corrections, report_change, all_rm_df):
        """Undo logic - returns restored states."""
        if not undo_stack:
            return last_filtered_data, rm_df, corrected_drift, manual_corrections, report_change, all_rm_df, undo_stack
        
        last_state = undo_stack.pop()
        
        last_filtered_data = last_state[0].copy()
        rm_df = last_state[1].copy()
        corrected_drift = last_state[2].copy()
        manual_corrections = last_state[3].copy()
        
        if len(last_state) > 4:
            report_change = last_state[4].copy()
        
        if len(last_state) > 5:
            all_rm_df = last_state[5].copy()
        
        corrected_drift = self.sync_corrected_drift_to_report_change(report_change)
        
        return last_filtered_data, rm_df, corrected_drift, manual_corrections, report_change, all_rm_df, undo_stack