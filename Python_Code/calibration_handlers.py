"""
Event and action handlers extracted from CalibrationPro.
Each function receives `self` as the CalibrationPro instance.
"""
import numpy as np
import pandas as pd
from PyQt6.QtWidgets import QMessageBox
from PyQt6.QtGui import QColor, QStandardItem,QStandardItemModel


def on_element_changed(self, element):
    self.selected_element = element
    self.current_element_index = self.element_list.index(element) if element in self.element_list else 0
    self.update_rm_list_and_go_first()
    self.update_displays()


def on_file_changed(self, combo_index: int):
    file_index = -1 if combo_index == 0 else combo_index - 1
    self.filter_by_file(file_index)


def apply_solution_filter(self):
    text = self.filter_solution_edit.text().strip()
    if not text:
        self.update_tables_and_plot()
        return
    QMessageBox.information(self, "Filter", f"Filter applied: {text}")


def prev(self):
    if self.current_nav_index > 0:
        self.prompt_apply_changes()
        self.current_nav_index -= 1
        self.selected_element, self.current_rm_num = self.navigation_list[self.current_nav_index]
        self.selected_row = -1
        self.selected_point_pivot = None
        self.selected_point_y = None
        self.update_labels()
        self.update_displays()
        self.update_navigation_buttons()
        self.update_pivot_plot()


def next(self):
    if self.current_nav_index < len(self.navigation_list) - 1:
        self.prompt_apply_changes()
        self.current_nav_index += 1
        self.selected_element, self.current_rm_num = self.navigation_list[self.current_nav_index]
        self.selected_row = -1
        self.selected_point_pivot = None
        self.selected_point_y = None
        self.update_labels()
        self.update_displays()
        self.update_navigation_buttons()
        self.update_pivot_plot()


def prompt_apply_changes(self):
    if self.has_changes():
        reply = QMessageBox.question(
            self,
            'Apply Changes',
            'Do you want to apply the changes to this RM?',
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No
        )
        if reply == QMessageBox.StandardButton.Yes:
            self.apply_to_single_rm()


def has_changes(self):
    if len(self.original_rm_values) == 0 or len(self.display_rm_values) == 0:
        return False
    return not np.allclose(
        self.original_rm_values,
        self.display_rm_values,
        rtol=1e-5,
        atol=1e-8,
        equal_nan=True,
    )


def update_navigation_buttons(self):
    self.prev_rm_btn.setEnabled(self.current_nav_index > 0)
    self.next_rm_btn.setEnabled(self.current_nav_index < len(self.navigation_list) - 1)
    enabled = bool(self.current_rm_num is not None and self.selected_element)
    self.slope_spin.setEnabled(enabled)
    self.auto_flat_btn.setEnabled(enabled)
    self.auto_zero_slope_btn.setEnabled(enabled)


def show_rm_context_menu(self, pos):
    index = self.rm_table.indexAt(pos)
    if not index.isValid():
        return
    row = index.row()
    if row < 0 or row >= len(self.current_valid_pivot_indices):
        return
    pivot = self.current_valid_pivot_indices[row]
    if pivot in self.empty_pivot_set:
        return
    menu = self._create_context_menu(pivot)
    action = menu.exec(self.rm_table.mapToGlobal(pos))
    if action:
        self._toggle_ignore_pivot(pivot)
        self.update_displays()


def _create_context_menu(self, pivot):
    menu = self._context_menu_class(self)
    if pivot in self.ignored_pivots:
        action = menu.addAction("Unignore this point")
    else:
        action = menu.addAction("Ignore this point")
    menu._single_action = action
    return menu


def _toggle_ignore_pivot(self, pivot):
    if pivot in self.ignored_pivots:
        self.ignored_pivots.remove(pivot)
    else:
        self.ignored_pivots.add(pivot)


def update_rm_list_and_go_first(self):
    if not self.analysis_data:
        return
    rm_df = self.analysis_data['rm_df']
    self.rm_numbers_list = sorted(rm_df['rm_num'].dropna().unique().astype(int).tolist())
    self.current_rm_index = 0 if self.rm_numbers_list else -1
    if self.rm_numbers_list:
        self.selected_rm_num = self.rm_numbers_list[0]
        self.current_rm_label.setText(f"RM-{int(self.selected_rm_num)}")
    self.update_all_displays()


def apply_slope_from_spin(self):
    new_slope = self.slope_spin.value()
    rm_data = self.get_valid_rm_data()
    if len(rm_data['display_values']) < 2:
        return

    x = rm_data['pivot_indices']
    y = self.display_rm_values.copy()
    normal_mask = ~rm_data['effective_empty']

    if normal_mask.sum() < 2:
        return

    x_n = x[normal_mask]
    y_n = y[normal_mask].copy()

    current_slope, intercept = np.polyfit(x_n, y_n, 1)

    first_x = x_n[0]
    delta_slope = current_slope - new_slope
    y_n -= delta_slope * (x_n - first_x)

    y[normal_mask] = y_n
    self.display_rm_values = y

    valid_pivot_indices = rm_data['pivot_indices']
    for i, pivot in enumerate(valid_pivot_indices):
        if not rm_data['effective_empty'][i]:
            new_val = y[i]
            mask_current = (self.rm_df['rm_num'] == self.current_rm_num) & (self.rm_df['pivot_index'] == pivot)
            if mask_current.any():
                self.rm_df.loc[mask_current, self.selected_element] = new_val

            mask_all = (self.all_rm_df['rm_num'] == self.current_rm_num) & (self.all_rm_df['pivot_index'] == pivot)
            if mask_all.any():
                self.all_rm_df.loc[mask_all, self.selected_element] = new_val

    self.update_displays()
    self.update_slope_from_data()

    QMessageBox.information(self, "Applied", f"Target slope applied: {new_slope:.7f}")


def reset_to_original(self):
    if len(self.display_rm_values) == 0:
        return
    
    self.display_rm_values[:] = self.original_rm_values
    
    try:
        model = self.rm_table.model()
        if model:
            model.blockSignals(True)
            n = min(model.rowCount(), len(self.display_rm_values))
            for i in range(n):
                item5 = model.item(i, 5)
                item6 = model.item(i, 6)
                if item5:
                    item5.setText(f"{self.display_rm_values[i]:.2f}")
                if item6:
                    item6.setText("1.000")
    finally:
        if model:
            model.blockSignals(False)
    
    self.update_rm_plot()


def update_rm_table_values_only(self):
    model = self.rm_table.model()
    if model is None or len(self.display_rm_values) == 0:
        return
    
    n_rows = min(model.rowCount(), len(self.display_rm_values))
    
    for i in range(n_rows):
        if model.item(i, 5):
            model.item(i, 5).setText(f"{self.display_rm_values[i]:.2f}")
        
        if model.item(i, 6) and i < len(self.original_rm_values):
            orig_val = self.original_rm_values[i]
            ratio = self.display_rm_values[i] / orig_val if orig_val != 0 else 1.0
            model.item(i, 6).setText(f"{ratio:.2f}")


def on_rm_value_changed(self, item):
    row = item.row()
    model = self.rm_table.model()
    try:
        if item.column() == 5:
            val = float(item.text())
            self.display_rm_values[row] = val
        elif item.column() == 6:
            ratio = float(item.text())
            val = self.original_rm_values[row] * ratio
            self.display_rm_values[row] = val
            model.item(row, 5).setText(f"{val:.2f}")

        pivot = self.current_valid_pivot_indices[row]
        mask_all = (self.all_rm_df['rm_num'] == self.current_rm_num) & \
                (self.all_rm_df['pivot_index'] == pivot)
        if mask_all.any():
            self.all_rm_df.loc[mask_all, self.selected_element] = val

        new_ratio = val / self.original_rm_values[row] if self.original_rm_values[row] != 0 else np.nan
        model.item(row, 6).setText(f"{new_ratio:.2f}" if pd.notna(new_ratio) else "N/A")

        self.update_rm_plot()
        self.update_slope_from_data()
        self.update_detail_table()
    except ValueError:
        QMessageBox.warning(self, "Invalid Value", "Please enter a valid number.")
        item.setText(f"{self.display_rm_values[row]:.2f}")

