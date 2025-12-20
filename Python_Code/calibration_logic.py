"""
Data/table/navigation logic extracted from CalibrationPro.
Each function expects `self` to be an instance of CalibrationPro.
"""
import pandas as pd
from PyQt6.QtGui import QStandardItemModel, QStandardItem, QColor, QFont
import numpy as np

def filter_by_file(self, index):
    if self.all_pivot_df is None or self.all_rm_df is None:
        self.rm_plot.clear()
        self.rm_plot.setTitle("No data loaded yet — Please run 'Check RM' first.")
        self.detail_table.setModel(QStandardItemModel())
        self.rm_table.setModel(QStandardItemModel())
        self.update_navigation_buttons()
        return

    if index < 0:
        self.pivot_df = self.all_pivot_df.copy()
        self.rm_df = self.all_rm_df.copy()
        self.initial_rm_df = self.all_initial_rm_df.copy()
        self.positions_df = self.all_positions_df.copy()
        self.segments = self._create_segments(self.positions_df)
        self.unique_rm_nums = sorted(self.rm_df['rm_num'].unique())
    else:
        fr = self.file_ranges[index]
        start = fr['start_pivot_row']
        end = fr['end_pivot_row']

        if 'pivot_index' in self.all_pivot_df.columns:
            self.pivot_df = self.all_pivot_df[self.all_pivot_df['pivot_index'].between(start, end)].copy()
            self.rm_df = self.all_rm_df[self.all_rm_df['pivot_index'].between(start, end)].copy()
            self.initial_rm_df = self.all_initial_rm_df[self.all_initial_rm_df['pivot_index'].between(start, end)].copy()
            self.positions_df = self.all_positions_df[self.all_positions_df['pivot_index'].between(start, end)].copy()
        else:
            self.pivot_df = self.all_pivot_df.iloc[start:end+1].copy()
            self.rm_df = self.all_rm_df.iloc[start:end+1].copy()
            self.initial_rm_df = self.all_initial_rm_df.iloc[start:end+1].copy()
            self.positions_df = self.all_positions_df.iloc[start:end+1].copy()

        self.segments = self._create_segments(self.positions_df)
        self.unique_rm_nums = sorted(self.rm_df['rm_num'].unique())

    if self.unique_rm_nums and self.elements:
        self.navigation_list = [(el, num) for el in self.elements for num in self.unique_rm_nums]
        self.current_nav_index = 0
        self.selected_element, self.current_rm_num = self.navigation_list[0]
        self.element_combo.clear()
        self.element_combo.addItems(self.elements)
        self.update_labels()
        self.update_displays()
        self.auto_flat_btn.setEnabled(True)
        self.auto_zero_slope_btn.setEnabled(True)
    else:
        self.current_nav_index = -1
        self.unique_rm_nums = []

    self.selected_row = -1
    self.selected_point_pivot = None
    self.selected_point_y = None
    self.update_navigation_buttons()


def display_rm_table(self):
    rm_data = self.get_valid_rm_data()
    if not rm_data:
        return

    self.current_valid_pivot_indices = rm_data['pivot_indices']
    self.original_rm_values = rm_data['original_values']
    self.display_rm_values = rm_data['display_values'].copy()
    self.rm_types = rm_data['types']
    self.solution_labels_for_group = rm_data['labels']

    model = QStandardItemModel()
    model.setHorizontalHeaderLabels(["RM Label", "File", "Next RM", "Type", "Original Value", "Current Value", "Ratio"])

    if len(self.display_rm_values) == 0:
        model.appendRow([QStandardItem("No Data") for _ in range(7)])
    else:
        effective_empty = rm_data['effective_empty']
        blue_pivot_indices = self.current_valid_pivot_indices[~effective_empty]
        blue_index_to_pos = {idx: i for i, idx in enumerate(blue_pivot_indices)}

        for i in range(len(self.display_rm_values)):
            current_rm_label = f"{self.solution_labels_for_group[i]}-{self.current_valid_pivot_indices[i]}"
            file_name = rm_data['file_names'][i]
            next_rm_label = "N/A"

            if not effective_empty[i]:
                pos = blue_index_to_pos.get(self.current_valid_pivot_indices[i])
                if pos is not None and pos < len(blue_pivot_indices) - 1:
                    next_pivot = blue_pivot_indices[pos + 1]
                    next_label = self.solution_labels_for_group[np.where(self.current_valid_pivot_indices == next_pivot)[0][0]]
                    next_rm_label = f"{next_label}-{next_pivot}"

            orig_val = self.original_rm_values[i]
            curr_val = self.display_rm_values[i]
            ratio = curr_val / orig_val if orig_val != 0 else np.nan

            row_items = [
                QStandardItem(current_rm_label),
                QStandardItem(file_name),
                QStandardItem(next_rm_label),
                QStandardItem(self.rm_types[i]),
                QStandardItem(f"{orig_val:.2f}"),
                QStandardItem(f"{curr_val:.2f}"),
                QStandardItem(f"{ratio:.2f}" if pd.notna(ratio) else "N/A")
            ]

            if effective_empty[i]:
                bg = QColor('red')
                fg = QColor('white')
                for item in row_items:
                    item.setBackground(bg)
                    item.setForeground(fg)
                    item.setEditable(False)
            else:
                for j in [0, 1, 2, 3]:
                    row_items[j].setEditable(False)
                row_items[5].setEditable(True)
                row_items[6].setEditable(True)
                color_map = {'Base': '#2E7D32', 'Check': '#FF6B00', 'Cone': '#7B1FA2'}
                color = QColor(color_map.get(self.rm_types[i], '#000000'))
                row_items[3].setForeground(color)
                row_items[3].setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))

            model.appendRow(row_items)

    self.rm_table.setModel(model)
    try:
        model.itemChanged.disconnect()
    except Exception:
        pass
    model.itemChanged.connect(self.on_rm_value_changed)
    self.update_slope_from_data()
    if 0 <= self.selected_row < len(self.current_valid_pivot_indices):
        self.rm_table.selectRow(self.selected_row)


def update_displays(self):
    if self.current_rm_num is not None and self.selected_element:
        self.display_rm_table()
        self.update_rm_plot()
        self.update_detail_table()


def update_labels(self):
    self.current_rm_label.setText(f"Current RM: {self.current_rm_num if self.current_rm_num is not None else 'None'}")
    if self.element_combo.count() > 0:
        self.element_combo.blockSignals(True)
        self.element_combo.setCurrentText(self.selected_element or '')
        self.element_combo.blockSignals(False)


def update_tables_and_plot(self):
    if not self.analysis_data or not self.selected_element:
        return
    rm_df = self.analysis_data['rm_df']
    full_df = self.analysis_data['full_df']
    element = self.selected_element
    if element not in full_df.columns:
        return

    model_rm = QStandardItemModel()
    model_rm.setHorizontalHeaderLabels(["Label", "Original", "Current", "Ratio"])
    if self.selected_rm_num is not None:
        selected_rm_data = rm_df[rm_df['rm_num'] == self.selected_rm_num]
        for _, row in selected_rm_data.iterrows():
            val = row[element]
            if pd.notna(val):
                model_rm.appendRow([
                    QStandardItem(row['Solution Label']),
                    QStandardItem(f"{val:.5f}"),
                    QStandardItem(f"{val:.5f}"),
                    QStandardItem("1.000")
                ])
    self.rm_table.setModel(model_rm)

    model_all = QStandardItemModel()
    model_all.setHorizontalHeaderLabels(["Solution Label", "Original Value", "New Value"])
    for _, row in full_df.sort_values('pivot_index').iterrows():
        val = row.get(element)
        if pd.notna(val):
            orig_item = QStandardItem(f"{val:.5f}")
            new_item = QStandardItem(f"{val:.5f}")
            new_item.setEditable(True)
            new_item.setForeground(QColor("#1976D2"))
            model_all.appendRow([
                QStandardItem(row['Solution Label']),
                orig_item,
                new_item
            ])
    self.detail_table.setModel(model_all)

    self.rm_plot.clear()
    self.rm_plot.addLegend()
    sample_mask = ~full_df['Solution Label'].str.contains("RM", case=False, na=False)
    samples = full_df[sample_mask & pd.notna(full_df[element])]
    if not samples.empty:
        self.rm_plot.addItem(pg.ScatterPlotItem(
            x=samples['pivot_index'].values,
            y=pd.to_numeric(samples[element], errors='coerce').values,
            size=7, brush='#B0BEC5', pen=None, name="Samples"
        ))

    rm_valid = rm_df[pd.notna(rm_df[element]) & rm_df['rm_num'].notna()].sort_values('pivot_index')
    if not rm_valid.empty:
        x_rm = rm_valid['pivot_index'].values
        y_rm = pd.to_numeric(rm_valid[element], errors='coerce').values
        symbol_map = {'Base': 'o', 'Check': 't', 'Cone': 's'}
        color_map = {'Base': '#2E7D32', 'Check': '#FF6B00', 'Cone': '#7B1FA2'}
        scatter = pg.ScatterPlotItem(
            x=x_rm, y=y_rm,
            symbol=[symbol_map.get(t, 'o') for t in rm_valid['rm_type']],
            size=18,
            brush=[color_map.get(t, '#2E7D32') for t in rm_valid['rm_type']],
            pen=pg.mkPen('white', width=2),
            name="All RMs"
        )
        def on_rm_click(plot_item, points):
            if not points:
                return
            point = points[0]
            idx = point.index()
            clicked_rm_num = rm_valid.iloc[idx]['rm_num']
            if clicked_rm_num != self.selected_rm_num:
                self.selected_rm_num = clicked_rm_num
                self.current_rm_index = self.rm_numbers_list.index(int(clicked_rm_num))
                self.current_rm_label.setText(f"RM-{int(clicked_rm_num)}")
                self.update_tables_and_plot()
        scatter.sigClicked.connect(on_rm_click)
        self.rm_plot.addItem(scatter)
        self.rm_plot.plot(x_rm, y_rm, pen=pg.mkPen('#43A047', width=3), name="RM Trend Line")

    self.rm_plot.setLabel('left', f'{element} Intensity')
    self.rm_plot.setTitle(f"Drift Plot — {element} — Click on RM to select")
    self.rm_plot.autoRange()


def on_filter_changed(self):
    self.update_rm_plot()
    self.crm_handler.update_pivot_plot()

