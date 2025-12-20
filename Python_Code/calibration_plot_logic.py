"""
Plot and table logic extracted from CalibrationPro to keep the main class lean.
Each function expects `self` to be an instance of CalibrationPro.
"""
import numpy as np
import pyqtgraph as pg
from functools import partial
from PyQt6.QtGui import QStandardItemModel, QStandardItem
from PyQt6.QtCore import Qt


def highlight_rm(self):
    rm_data = self.rm_handler.get_valid_rm_data(
        rm_df=self.rm_df,
        initial_rm_df=self.initial_rm_df,
        selected_element=self.selected_element,
        current_rm_num=self.current_rm_num,
        empty_rows_from_check=self.empty_rows_from_check,
        ignored_pivots=self.rm_handler.ignored_pivots,
        positions_df=self.positions_df
    )

    x_rm = rm_data['pivot_indices']
    y_rm = self.display_rm_values
    effective_empty = rm_data['effective_empty']
    normal_mask = ~effective_empty

    if (not hasattr(self, 'selected_row') or
        self.selected_row is None or
        not (0 <= self.selected_row < len(x_rm)) or
        self.selected_row >= len(x_rm) - 1 or
        not normal_mask[self.selected_row] or
        not normal_mask[self.selected_row - 1]):

        self.selected_segment_line.setData([], [])
        self.selected_start_rm_points.setData([], [])
        self.selected_end_rm_points.setData([], [])
        return

    x1, y1 = x_rm[self.selected_row - 1], y_rm[self.selected_row - 1]
    x2, y2 = x_rm[self.selected_row], y_rm[self.selected_row]

    self.selected_segment_line.setData(
        [x1, x2], [y1, y2],
        pen=pg.mkPen('#FFD700', width=8)
    )

    self.selected_start_rm_points.setData(
        [x1], [y1],
        symbol='s', size=20,
        brush='#1976D2', pen=pg.mkPen('white', width=4)
    )

    self.selected_end_rm_points.setData(
        [x2], [y2],
        symbol='o', size=20,
        brush='#D32F2F', pen=pg.mkPen('white', width=4)
    )


def update_rm_plot(self):
    self.rm_plot.clear()
    self.setup_plot_items()

    rm_data = self.rm_handler.get_valid_rm_data(
        rm_df=self.rm_df,
        initial_rm_df=self.initial_rm_df,
        selected_element=self.selected_element,
        current_rm_num=self.current_rm_num,
        empty_rows_from_check=self.empty_rows_from_check,
        ignored_pivots=self.rm_handler.ignored_pivots,
        positions_df=self.positions_df
    )

    display_values = rm_data['display_values']
    if len(display_values) == 0:
        self.rm_plot.setTitle("No RM Data")
        self.rm_plot.autoRange()
        return

    x_rm = rm_data['pivot_indices']
    y_rm = self.display_rm_values
    types_rm = rm_data['types']
    labels_rm = rm_data['labels']
    effective_empty = rm_data['effective_empty']
    normal_mask = ~effective_empty

    brush_colors = []
    empty_pivot_set = self.empty_pivot_set if hasattr(self, 'empty_pivot_set') else set()
    ignored_pivots = self.rm_handler.ignored_pivots

    for i in range(len(x_rm)):
        if x_rm[i] in ignored_pivots:
            brush_colors.append('#FF9800')
        elif x_rm[i] in empty_pivot_set:
            brush_colors.append('#B0BEC5')
        else:
            brush_colors.append('#2E7D32')

    symbol_map = {'Base': 'o', 'Check': 't', 'Cone': 's', 'Unknown': 'o'}
    symbols = [symbol_map.get(t, 'o') for t in types_rm]

    rm_scatter = pg.ScatterPlotItem(
        x=x_rm, y=y_rm, symbol=symbols, size=12,
        brush=brush_colors, pen=pg.mkPen('white', width=1.5),
        hoverable=True
    )
    rm_scatter.sigClicked.connect(partial(self.handle_point_click, 'rm'))
    self.rm_plot.addItem(rm_scatter)

    filter_text = self.filter_solution_edit.text().strip().lower()
    original_values = rm_data['original_values']

    for i in range(len(x_rm) - 1):
        if not normal_mask[i] or not normal_mask[i + 1]:
            continue
        prev_pivot = x_rm[i]
        curr_pivot = x_rm[i + 1]

        if 'original_index' in self.pivot_df.columns:
            cond = (self.pivot_df['original_index'] > prev_pivot) & \
                (self.pivot_df['original_index'] < curr_pivot) & \
                self.pivot_df[self.selected_element].notna()
        else:
            cond = (self.pivot_df.index > prev_pivot) & \
                (self.pivot_df.index < curr_pivot) & \
                self.pivot_df[self.selected_element].notna()

        seg_data = self.pivot_df[cond].copy()
        if filter_text:
            seg_data = seg_data[seg_data['Solution Label'].str.lower().str.contains(filter_text, na=False)]
        if seg_data.empty:
            continue

        if 'original_index' in seg_data.columns:
            x_seg = seg_data['original_index'].values.astype(float)
        else:
            x_seg = seg_data.index.values.astype(float)

        y_orig = seg_data[self.selected_element].values.astype(float)
        labels = seg_data['Solution Label'].values

        orig_scatter = pg.ScatterPlotItem(
            x=x_seg, y=y_orig, symbol='x', size=9, brush='#F44336', pen='#D32F2F',
            hoverable=True, data=labels
        )
        orig_scatter.sigClicked.connect(partial(self.handle_point_click, 'detail'))
        self.rm_plot.addItem(orig_scatter)

        current_ratio = y_rm[i + 1] / original_values[i + 1] if original_values[i + 1] != 0 else 1.0
        prev_ratio = y_rm[i] / original_values[i] if original_values[i] != 0 else 1.0

        adjusted = y_orig - self.preview_blank
        scaled = adjusted * self.preview_scale
        y_corr, _ = self.rm_handler.calculate_corrected_values_with_ratios(
            scaled, current_ratio, prev_ratio, self.stepwise_cb.isChecked()
        )

        corr_scatter = pg.ScatterPlotItem(
            x=x_seg, y=y_corr, symbol='o', size=7, brush='#2196F3', pen='#1976D2',
            hoverable=True, data=labels
        )
        corr_scatter.sigClicked.connect(partial(self.handle_point_click, 'detail'))
        self.rm_plot.addItem(corr_scatter)

    colors = ['#43A047', '#FF6B00', '#7B1FA2', '#1A3C34', '#1976D2']
    for seg_idx, seg in enumerate(self.segments):
        seg_pivots = seg['positions']['pivot_index'].values
        mask = np.isin(x_rm, seg_pivots) & normal_mask
        if not np.any(mask):
            continue
        x_seg = x_rm[mask]
        y_seg = y_rm[mask]
        color = colors[seg_idx % len(colors)]
        pen = pg.mkPen(color, width=3)
        self.rm_plot.plot(x_seg, y_seg, pen=pen, name=f"Segment {seg_idx+1}")

        if len(x_seg) >= 2:
            slope, intercept = np.polyfit(x_seg, y_seg, 1)
            line_y = slope * x_seg + intercept
            self.rm_plot.plot(x_seg, line_y, pen=pg.mkPen(color, width=2, style=Qt.PenStyle.DashLine),
                            name=f"Segment Trend (slope: {slope:.7f})")

    rm_slope = 0.0
    if np.sum(normal_mask) >= 2:
        x_trend = x_rm[normal_mask]
        y_trend = y_rm[normal_mask]
        rm_slope, intercept = np.polyfit(x_trend, y_trend, 1)
        line_y = rm_slope * x_trend + intercept
        self.rm_plot.plot(x_trend, line_y, pen=pg.mkPen('black', width=2.5, style=Qt.PenStyle.DashLine),
                        name=f"RM Trend (slope: {rm_slope:.7f})")

    if self.selected_point_pivot is not None and self.selected_point_y is not None:
        self.highlight_point.setData([self.selected_point_pivot], [self.selected_point_y])

    corrected_slope = 0.0
    if len(x_rm) > 1:
        all_x_corr = []
        all_y_corr = []
        for i in range(len(x_rm) - 1):
            if not (normal_mask[i] and normal_mask[i + 1]):
                continue
            if 'original_index' in self.pivot_df.columns:
                cond = (self.pivot_df['original_index'] > x_rm[i]) & \
                    (self.pivot_df['original_index'] < x_rm[i + 1]) & \
                    self.pivot_df[self.selected_element].notna()
            else:
                cond = (self.pivot_df.index > x_rm[i]) & \
                    (self.pivot_df.index < x_rm[i + 1]) & \
                    self.pivot_df[self.selected_element].notna()

            seg_data = self.pivot_df[cond].copy()
            if filter_text:
                seg_data = seg_data[seg_data['Solution Label'].str.lower().str.contains(filter_text, na=False)]
            if seg_data.empty:
                continue

            y_orig = seg_data[self.selected_element].values.astype(float)
            current_ratio = y_rm[i + 1] / original_values[i + 1] if original_values[i + 1] != 0 else 1.0
            prev_ratio = y_rm[i] / original_values[i] if original_values[i] != 0 else 1.0
            adjusted = y_orig - self.preview_blank
            scaled = adjusted * self.preview_scale
            y_corr, _ = self.rm_handler.calculate_corrected_values_with_ratios(
                scaled, current_ratio, prev_ratio, self.stepwise_cb.isChecked()
            )

            if 'original_index' in seg_data.columns:
                all_x_corr.extend(seg_data['original_index'].values.astype(float))
            else:
                all_x_corr.extend(seg_data.index.values.astype(float))
            all_y_corr.extend(y_corr)

        if len(all_x_corr) >= 2:
            x_arr = np.array(all_x_corr)
            y_arr = np.array(all_y_corr)
            corrected_slope, intercept = np.polyfit(x_arr, y_arr, 1)
            line_y = corrected_slope * x_arr + intercept
            self.rm_plot.plot(x_arr, line_y, pen=pg.mkPen('#E91E63', width=3.5),
                            name=f"Corrected Trend (slope: {corrected_slope:.7f})")

    if self.current_file_index <= 0 and len(self.file_ranges) > 1:
        for fr in self.file_ranges:
            self.rm_plot.addItem(pg.InfiniteLine(pos=fr['start_pivot_row'], angle=90,
                                                pen=pg.mkPen('#9E9E9E', width=1, style=Qt.PenStyle.DashLine)))
            self.rm_plot.addItem(pg.InfiniteLine(pos=fr['end_pivot_row'], angle=90,
                                                pen=pg.mkPen('#9E9E9E', width=1, style=Qt.PenStyle.DashLine)))

    self.rm_plot.setTitle(f"Drift Plot — {self.selected_element} — RM {self.current_rm_num}")
    self.rm_plot.addLegend(offset=(10, 10))
    self.rm_plot.autoRange()


def update_slope_from_data(self):
    trend_x = []
    trend_y = []
    rm_data = self.get_valid_rm_data()
    x_rm = rm_data['pivot_indices']
    y_rm_raw = self.display_rm_values
    y_rm = (y_rm_raw - self.preview_blank) * self.preview_scale
    for px, py in zip(x_rm, y_rm):
        if px not in self.empty_pivot_set and px not in self.ignored_pivots:
            trend_x.append(px)
            trend_y.append(py)

    for orig_index, manual_val in self.manual_corrections.items():
        row = self.pivot_df[self.pivot_df['original_index'] == orig_index]
        if row.empty:
            continue
        pivot_val = row['pivot_index'].iloc[0]
        final_val = (manual_val - self.preview_blank) * self.preview_scale
        trend_x.append(pivot_val)
        trend_y.append(final_val)

    if len(trend_x) >= 2:
        tx = np.array(trend_x)
        ty = np.array(trend_y)
        order = np.argsort(tx)
        tx = tx[order]
        ty = ty[order]
        self.current_slope = np.polyfit(tx, ty, 1)[0]
    else:
        self.current_slope = 0.0

    self.slope_spin.blockSignals(True)
    self.slope_spin.setValue(self.current_slope)
    self.slope_spin.blockSignals(False)
    color = "#2E7D32" if self.current_slope >= 0 else "#D32F2F"
    sign = "+" if self.current_slope >= 0 else ""
    self.slope_display.setText(f"Current Slope: <span style='color:{color};font-weight:bold'>{sign}{self.current_slope:.7f}</span>")


def update_detail_table(self):
    model = QStandardItemModel()
    model.setHorizontalHeaderLabels(["Solution Label", "Original Value", "Corrected Value"])
    
    if (self.selected_row < 0 or 
        self.selected_row >= len(self.display_rm_values) - 1):
        self.detail_table.setModel(model)
        return

    data = self.rm_handler.get_data_between_rm(
        selected_row=self.selected_row + 1,
        current_valid_pivot_indices=self.current_valid_pivot_indices,
        pivot_df=self.pivot_df,
        selected_element=self.selected_element,
        filter_solution_edit_text=self.filter_solution_edit.text()
    )

    if data.empty:
        self.detail_table.setModel(model)
        return

    orig = data[self.selected_element].values.astype(float)

    current_ratio = (self.display_rm_values[self.selected_row + 1] /
                     self.original_rm_values[self.selected_row + 1]
                     if self.original_rm_values[self.selected_row + 1] != 0 else 1.0)
    
    prev_ratio = (self.display_rm_values[self.selected_row] /
                  self.original_rm_values[self.selected_row]
                  if self.original_rm_values[self.selected_row] != 0 else 1.0)

    adjusted = orig - self.preview_blank
    scaled = adjusted * self.preview_scale

    base_corr, _ = self.rm_handler.calculate_corrected_values_with_ratios(
        original_values=scaled,
        current_ratio=current_ratio,
        prev_ratio=prev_ratio,
        stepwise=self.stepwise_cb.isChecked()
    )

    for i in range(len(data)):
        sl_item = QStandardItem(data.iloc[i]['Solution Label'])
        sl_item.setEditable(False)
        
        o_item = QStandardItem(f"{orig[i]:.2f}")
        o_item.setEditable(False)

        if 'original_index' in data.columns:
            orig_index = int(data.iloc[i]['original_index'])
        else:
            orig_index = data.index[i]

        manual_val = self.rm_handler.manual_corrections.get(orig_index)
        display_val = manual_val if manual_val is not None else base_corr[i]

        c_item = QStandardItem(f"{display_val:.2f}")
        c_item.setEditable(True)
        c_item.setData(orig_index, Qt.ItemDataRole.UserRole)

        model.appendRow([sl_item, o_item, c_item])

    self.detail_table.setModel(model)

    try:
        model.itemChanged.disconnect()
    except TypeError:
        pass

    model.itemChanged.connect(self.on_detail_value_changed)


def on_detail_table_clicked(self, index):
    row = index.row()
    if row < 0 or self.selected_row < 0:
        return

    data = self.rm_handler.get_data_between_rm(
        selected_row=self.selected_row + 1,
        current_valid_pivot_indices=self.current_valid_pivot_indices,
        pivot_df=self.pivot_df,
        selected_element=self.selected_element,
        filter_solution_edit_text=self.filter_solution_edit.text()
    )

    if data.empty or row >= len(data):
        return

    selected_row = data.iloc[row]

    if 'original_index' in selected_row.index:
        original_index = int(selected_row['original_index'])
    else:
        original_index = selected_row.name

    pivot_index = selected_row.get('pivot_index', original_index)

    orig_y = float(selected_row[self.selected_element])

    i = self.selected_row
    current_ratio = (self.display_rm_values[i + 1] /
                     self.original_rm_values[i + 1]
                     if self.original_rm_values[i + 1] != 0 else 1.0)
    
    prev_ratio = (self.display_rm_values[i] /
                  self.original_rm_values[i]
                  if self.original_rm_values[i] != 0 else 1.0)

    adjusted = orig_y - self.preview_blank
    scaled = adjusted * self.preview_scale

    corrected_y_array, _ = self.rm_handler.calculate_corrected_values_with_ratios(
        original_values=np.array([scaled]),
        current_ratio=current_ratio,
        prev_ratio=prev_ratio,
        stepwise=self.stepwise_cb.isChecked()
    )
    corrected_y = float(corrected_y_array[0])

    self.selected_point_pivot = pivot_index
    self.selected_point_y = corrected_y

    if hasattr(self, 'highlight_point'):
        self.highlight_point.setData([pivot_index], [orig_y])

    if hasattr(self, 'detail_highlight_point'):
        self.detail_highlight_point.setData(
            [pivot_index],
            [corrected_y],
            symbol='o',
            size=20,
            brush=pg.mkBrush('#FFD700'),
            pen=pg.mkPen('black', width=4)
        )

    self.highlight_rm()

