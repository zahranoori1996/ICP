from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QPushButton, QMessageBox, QComboBox, QLabel, 
    QFrame, QLineEdit, QCheckBox, QDialog, QHeaderView, QTableView, QScrollArea, QFileDialog,
    QTabWidget, QAbstractItemView
)
from PyQt6.QtGui import QFont, QPixmap, QColor
from PyQt6.QtCore import Qt
from .pivot_table_model import PivotTableModel
from .pivot_creator import PivotCreator
from .pivot_exporter import PivotExporter
from .oxide_factors import oxide_factors
import pandas as pd
import logging
import numpy as np
from collections import defaultdict
import os
import pyqtgraph as pg
import re
from datetime import datetime

from utils.var_main import LOGO_PNG_PATH
from ..Common.Freeze_column import FreezeTableWidget
from ..Common.column_filter import ColumnFilterDialog
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


class PlotDialog(QDialog):
    """Dialog for displaying a pyqtgraph plot in a new window with white background."""
    def __init__(self, title, parent=None):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.setModal(False)
        layout = QVBoxLayout(self)
        self.plot_widget = pg.PlotWidget()
        self.plot_widget.setBackground('w')
        layout.addWidget(self.plot_widget)
        self.setGeometry(100, 100, 800, 600)

class PlotOptionsDialog(QDialog):
    """Dialog for selecting plot options."""
    def __init__(self, parent):
        super().__init__(parent)
        self.setWindowTitle("Plot Options")
        layout = QVBoxLayout(self)
        
        self.type_combo = QComboBox()
        self.type_combo.addItems(["Plot Row", "Plot Column", "Plot All Rows", "Plot All Columns"])
        layout.addWidget(QLabel("Plot Type:"))
        layout.addWidget(self.type_combo)
        
        self.selector = QComboBox()
        layout.addWidget(QLabel("Select:"))
        layout.addWidget(self.selector)
        
        self.type_combo.currentTextChanged.connect(self.update_selector)
        
        plot_btn = QPushButton("Plot")
        plot_btn.clicked.connect(self.plot_selected)
        layout.addWidget(plot_btn)
        
        self.update_selector(self.type_combo.currentText())

    def update_selector(self, text):
        self.selector.clear()
        parent = self.parent()
        if text == "Plot Row":
            self.selector.addItems(parent.current_view_df['Solution Label'].unique())
            self.selector.setEnabled(True)
        elif text == "Plot Column":
            self.selector.addItems(parent.current_view_df.columns[1:])
            self.selector.setEnabled(True)
        elif text == "Plot All Rows":
            self.selector.addItems(parent.current_view_df.columns[1:])
            self.selector.setEnabled(True)
        elif text == "Plot All Columns":
            self.selector.addItems(parent.current_view_df['Solution Label'].unique())
            self.selector.setEnabled(True)

    def plot_selected(self):
        text = self.type_combo.currentText()
        parent = self.parent()
        selected_item = self.selector.currentText()
        if text == "Plot Row":
            parent.plot_row(selected_item)
        elif text == "Plot Column":
            parent.plot_column(selected_item)
        elif text == "Plot All Rows":
            parent.plot_all_rows(selected_item)
        elif text == "Plot All Columns":
            parent.plot_all_columns(selected_item)
        self.accept()

class PivotTab(QWidget):
    """PivotTab with inline duplicate rows, difference coloring, plot visualization, and editable cells."""
    def __init__(self, app, parent_frame):
        super().__init__(parent_frame)
        self.logger = logging.getLogger(__name__)
        self.app = app
        self.parent_frame = parent_frame
        self.pivot_data = None
        self.solution_label_order = None
        self.element_order = None
        self.row_filter_values = {}
        self.column_filter_values = {}
        self.filters = {}
        self.column_widths = {}
        self.cached_formatted = {}
        self.current_view_df = None
        self._inline_duplicates = {}
        self._inline_duplicates_display = {}
        self.current_plot_dialog = None
        self.search_var = QLineEdit()
        self.row_filter_field = QComboBox()
        self.column_filter_field = QComboBox()
        self.decimal_places = QComboBox()
        self.use_int_var = QCheckBox("Use Int")
        self.use_oxide_var = QCheckBox("Use Oxide")
        self.duplicate_threshold = 10.0
        self.duplicate_threshold_edit = QLineEdit("10")
        self.pivot_creator = PivotCreator(self)
        self.pivot_exporter = PivotExporter(self)
        self.setup_ui()

    def setup_ui(self):
        self.logger.debug("Setting up PivotTab UI")
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        subtab_bar = QWidget()
        subtab_bar.setFixedHeight(50)
        subtab_bar.setStyleSheet("background-color: #f0e6ff;")
        subtab_layout = QHBoxLayout()
        subtab_layout.setContentsMargins(8, 6, 8, 6)
        subtab_layout.setSpacing(8)
        subtab_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
        subtab_bar.setLayout(subtab_layout)

        subtab_layout.addWidget(QLabel("Decimal Places:"))
        self.decimal_places.addItems(["0", "1", "2", "3"])
        self.decimal_places.setCurrentText("1")
        self.decimal_places.setFixedWidth(40)
        self.decimal_places.currentTextChanged.connect(self.update_pivot_display)
        subtab_layout.addWidget(self.decimal_places)
        
        self.use_int_var.toggled.connect(self.pivot_creator.create_pivot)
        subtab_layout.addWidget(self.use_int_var)
        
        self.use_oxide_var.toggled.connect(self.pivot_creator.create_pivot)
        subtab_layout.addWidget(self.use_oxide_var)
        
        subtab_layout.addWidget(QLabel("Duplicate Range (%):"))
        self.duplicate_threshold_edit.setFixedWidth(30)
        self.duplicate_threshold_edit.textChanged.connect(self.update_duplicate_threshold)
        subtab_layout.addWidget(self.duplicate_threshold_edit)
        
        plot_data_btn = QPushButton("Plot Data")
        plot_data_btn.setFixedSize(60, 30)
        plot_data_btn.clicked.connect(self.show_plot_options)
        subtab_layout.addWidget(plot_data_btn)
        
        self.search_var.setPlaceholderText("Search...")
        self.search_var.setFixedWidth(100)
        self.search_var.textChanged.connect(self.update_pivot_display)
        subtab_layout.addWidget(self.search_var)
        
        row_filter_btn = QPushButton("Row Filter")
        row_filter_btn.setFixedSize(70, 30)
        row_filter_btn.clicked.connect(self.open_row_filter_window)
        subtab_layout.addWidget(row_filter_btn)
        
        col_filter_btn = QPushButton("Column Filter")
        col_filter_btn.setFixedSize(80, 30)
        col_filter_btn.clicked.connect(self.open_column_filter_window)
        subtab_layout.addWidget(col_filter_btn)
        
        detect_duplicates_btn = QPushButton("Detect Dup")
        detect_duplicates_btn.setFixedSize(70, 30)
        detect_duplicates_btn.clicked.connect(self.detect_duplicates)
        subtab_layout.addWidget(detect_duplicates_btn)
        
        clear_duplicates_btn = QPushButton("Clear Dup")
        clear_duplicates_btn.setFixedSize(70, 30)
        clear_duplicates_btn.clicked.connect(self.clear_inline_duplicates)
        subtab_layout.addWidget(clear_duplicates_btn)
        
        clear_filter_btn = QPushButton("Clear Filters")
        clear_filter_btn.setFixedSize(80, 30)
        clear_filter_btn.clicked.connect(self.clear_all_filters)
        subtab_layout.addWidget(clear_filter_btn)
        
        export_btn = QPushButton("Export")
        export_btn.setFixedSize(60, 30)
        export_btn.clicked.connect(self.pivot_exporter.export_pivot)
        subtab_layout.addWidget(export_btn)
        
        logo_path = LOGO_PNG_PATH
        if os.path.exists(logo_path):
            logo_label = QLabel()
            logo_label.setPixmap(QPixmap(logo_path).scaled(100, 40, Qt.AspectRatioMode.KeepAspectRatio))
            logo_label.setAlignment(Qt.AlignmentFlag.AlignRight)
            subtab_layout.addStretch()
            subtab_layout.addWidget(logo_label)
        else:
            self.logger.warning(f"Logo file {logo_path} not found")

        indicator = QWidget()
        indicator.setFixedHeight(3)
        indicator.setStyleSheet("background-color: #7b68ee;")

        content_area = QWidget()
        content_layout = QVBoxLayout()
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(0)
        content_area.setLayout(content_layout)

        self.table_view = FreezeTableWidget(PivotTableModel(self), frozen_columns=1, parent=self)
        self.table_view.set_header_click_callback(self.on_header_clicked)
        self.table_view.setAlternatingRowColors(True)
        self.table_view.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.table_view.setSortingEnabled(True)
        self.table_view.setEditTriggers(
            QTableView.EditTrigger.DoubleClicked |
            QTableView.EditTrigger.SelectedClicked |
            QTableView.EditTrigger.EditKeyPressed |
            QTableView.EditTrigger.AnyKeyPressed
        )
        self.table_view.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        self.table_view.horizontalHeader().sectionClicked.connect(self.on_header_clicked)
        self.table_view.doubleClicked.connect(self.on_cell_double_click)
        self.table_view.keyPressEvent = self.handle_key_press

        # Legend بالای جدول
        # === Legend با اسکرول افقی (برای فایل‌های زیاد) ===
        self.legend_widget = QWidget()
        self.legend_widget.setFixedHeight(50)
        self.legend_widget.setStyleSheet("""
            QWidget {
                background-color: #f8f9fa;
                border-bottom: 1px solid #dee2e6;
            }
        """)

        # Scroll Area برای لیجند
        self.legend_scroll = QScrollArea()
        self.legend_scroll.setWidgetResizable(True)
        self.legend_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        self.legend_scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.legend_scroll.setFixedHeight(50)
        self.legend_scroll.setFrameShape(QFrame.Shape.NoFrame)

        # Widget داخل اسکرول
        self.legend_container = QWidget()
        self.legend_layout = QHBoxLayout(self.legend_container)
        self.legend_layout.setContentsMargins(15, 8, 15, 8)
        self.legend_layout.setSpacing(20)
        self.legend_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)

        self.legend_scroll.setWidget(self.legend_container)
        
        # اضافه کردن به layout
        content_layout.insertWidget(0, self.legend_scroll)
        self.legend_widget.hide()  # تا وقتی داده نباشه مخفی باشه

        content_layout.addWidget(self.table_view)
        
        self.status_label = QLabel("Pivot table will be displayed here.")
        self.status_label.setFont(QFont("Segoe UI", 14))
        self.status_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        content_layout.addWidget(self.status_label)

        layout.addWidget(subtab_bar)
        layout.addWidget(indicator)
        layout.addWidget(content_area, 1)

    def on_header_clicked(self, section, col_name=None):
        if self.pivot_data is None:
            return

        if col_name is None:
            model = self.table_view.model()
            if model:
                col_name = model.headerData(section, Qt.Orientation.Horizontal, Qt.ItemDataRole.DisplayRole)
                col_name = str(col_name)

        dialog = ColumnFilterDialog(
            parent=self,
            col_name=col_name,
            data_source=self.pivot_data,
            column_filters=self.filters,
            on_apply_callback=self.update_pivot_display
        )
        dialog.exec()

    def handle_key_press(self, event):
        self.logger.debug(f"Key pressed: {event.key()}")
        if event.key() in (Qt.Key.Key_Return, Qt.Key.Key_Enter):
            if self.table_view.state() == QTableView.State.EditingState:
                self.logger.debug("Committing edit on Enter key")
                self.table_view.commitData(self.table_view.currentIndex())
                self.table_view.closeEditor(
                    self.table_view.indexWidget(self.table_view.currentIndex()),
                    QTableView.EditTrigger.NoEditTriggers
                )
                self.table_view.clearSelection()
                self.table_view.setFocus()
        super(QTableView, self.table_view).keyPressEvent(event)

    def format_value(self, x):
        try:
            d = int(self.decimal_places.currentText())
            return f"{float(x):.{d}f}"
        except (ValueError, TypeError):
            return "" if pd.isna(x) or x is None else str(x)

    def update_duplicate_threshold(self):
        try:
            self.duplicate_threshold = float(self.duplicate_threshold_edit.text())
            self.update_pivot_display()
        except ValueError:
            pass

    def detect_duplicates(self):
        if self.pivot_data is None or self.pivot_data.empty:
            self.logger.warning("No data to detect duplicates")
            return

        self._inline_duplicates = {}
        self._inline_duplicates_display = {}

        duplicate_patterns = r'(?i)\b(TEK|ret|RET)\b'
        number_pattern = r'(\d+[-]\d+|\d+)'
        label_to_base = {}
        base_to_duplicates = defaultdict(list)

        all_labels = self.pivot_data['Solution Label'].unique()

        for label in all_labels:
            if re.search(duplicate_patterns, str(label)):
                match = re.search(number_pattern, str(label))
                if match:
                    base_label = match.group(1).strip()
                    base_to_duplicates[base_label].append(label)
            else:
                clean_label = str(label).strip()
                label_to_base[label] = clean_label

        for base, dups in base_to_duplicates.items():
            for main_label, clean in label_to_base.items():
                if re.search(re.escape(base), clean):
                    main_row = self.pivot_data[self.pivot_data['Solution Label'] == main_label].iloc[0]
                    if main_label not in self._inline_duplicates_display:
                        self._inline_duplicates_display[main_label] = []
                    for dup in dups:
                        if dup != main_label:
                            dup_row = self.pivot_data[self.pivot_data['Solution Label'] == dup].iloc[0]
                            diff_row = pd.Series(['Diff for ' + dup] + ['' for _ in range(len(self.pivot_data.columns) - 1)], index=self.pivot_data.columns)
                            tags_diff = {}
                            for col in self.pivot_data.columns:
                                if col != 'Solution Label':
                                    if self.is_numeric(main_row[col]) and self.is_numeric(dup_row[col]):
                                        main_val = float(main_row[col])
                                        dup_val = float(dup_row[col])
                                        if main_val != 0:
                                            diff_percent = abs((dup_val - main_val) / main_val) * 100
                                            diff_row[col] = diff_percent
                                            tags_diff[col] = 'out_range' if diff_percent > self.duplicate_threshold else 'in_range'
                                        else:
                                            diff_row[col] = 0
                                            tags_diff[col] = 'in_range'
                                    else:
                                        diff_row[col] = ''
                                        tags_diff[col] = ''
                            self._inline_duplicates_display[main_label].append((dup_row.tolist(), 'duplicate'))
                            self._inline_duplicates_display[main_label].append((diff_row.tolist(), tags_diff))
                    break

        self.update_pivot_display()

    def clear_inline_duplicates(self):
        self.logger.debug("Clearing inline duplicates data")
        self._inline_duplicates.clear()
        self._inline_duplicates_display.clear()
        self.update_pivot_display()

    def clear_all_filters(self):
        self.logger.debug("Clearing all column filters")
        self.filters.clear()
        self.update_pivot_display()
        QMessageBox.information(self, "Filters Cleared", "All column filters have been cleared.")

    def update_pivot_display(self):
        self.logger.debug("Starting update_pivot_display")
        if self.pivot_data is None or self.pivot_data.empty:
            self.logger.warning("No data loaded for pivot display")
            self.status_label.setText("No data loaded")
            self.table_view.setModel(None)
            self.table_view.frozenTableView.setModel(None)
            return

        # Convert potentially numeric columns to numeric type
        df = self.pivot_data.copy()
        for col in df.columns:
            if col != 'Solution Label':
                try:
                    df[col] = pd.to_numeric(df[col], errors='coerce')
                    # self.logger.debug(f"Converted column {col} to numeric")
                except Exception as e:
                    self.logger.debug(f"Column {col} not converted to numeric: {str(e)}")

        self.logger.debug(f"Pivot data shape before filtering: {df.shape}")

        # Apply filters
        mask = pd.Series(True, index=df.index)  # Initialize mask with all True
        for col, filt in self.filters.items():
            if col in df.columns:
                col_data = pd.to_numeric(df[col], errors='coerce') if col != 'Solution Label' else df[col]
                if 'min_val' in filt and filt['min_val'] is not None:
                    try:
                        min_mask = (col_data >= filt['min_val']) | col_data.isna()
                        mask = mask & min_mask
                        self.logger.debug(f"Applied min filter {filt['min_val']} on column {col}, rows left: {mask.sum()}")
                    except Exception as e:
                        self.logger.error(f"Error applying min filter on {col}: {str(e)}")
                if 'max_val' in filt and filt['max_val'] is not None:
                    try:
                        max_mask = (col_data <= filt['max_val']) | col_data.isna()
                        mask = mask & max_mask
                        self.logger.debug(f"Applied max filter {filt['max_val']} on column {col}, rows left: {mask.sum()}")
                    except Exception as e:
                        self.logger.error(f"Error applying max filter on {col}: {str(e)}")
                if 'selected_values' in filt and filt['selected_values']:
                    try:
                        selected_values = filt['selected_values']  # No conversion for non-numeric
                        selected_mask = col_data.isin(selected_values)
                        mask = mask & selected_mask
                        self.logger.debug(f"Applied selected values filter on column {col}: {selected_values}, rows left: {mask.sum()}")
                    except Exception as e:
                        self.logger.error(f"Error applying selected values filter on {col}: {str(e)}")

        # Apply combined mask
        try:
            df = df[mask]
            self.logger.debug(f"Data shape after all filters: {df.shape}")
        except Exception as e:
            self.logger.error(f"Error applying combined filter mask: {str(e)}")
            df = self.pivot_data.copy()  # Fallback to original if error

        s = self.search_var.text().strip().lower()
        if s:
            search_mask = df.apply(lambda r: r.astype(str).str.lower().str.contains(s, na=False).any(), axis=1)
            df = df[search_mask]
            self.logger.debug(f"Applied search filter '{s}', rows left: {len(df)}")

        for field, values in self.row_filter_values.items():
            if field in df.columns:
                selected = [k for k, v in values.items() if v]
                if selected:
                    df = df[df[field].isin(selected)]
                    self.logger.debug(f"Applied row filter on {field}: {selected}, rows left: {len(df)}")

        selected_cols = ['Solution Label']
        if self.use_oxide_var.isChecked():
            for field, values in self.column_filter_values.items():
                if field == 'Element':
                    selected_cols.extend([
                        oxide_factors[el][0] for el, v in values.items()
                        if v and el in oxide_factors and oxide_factors[el][0] in df.columns
                    ])
        else:
            for field, values in self.column_filter_values.items():
                if field == 'Element':
                    selected_cols.extend([k for k, v in values.items() if k in df.columns])

        if len(selected_cols) > 1:
            df = df[selected_cols]
 
        df = df.reset_index(drop=True)

        self.current_view_df = df
        self.logger.debug(f"Current view data shape: {df.shape}")

        if df.empty:
            self.logger.warning("No data remains after applying filters")
            self.status_label.setText("No data matches the current filters")
        else:
            self.status_label.setText("Data loaded successfully")

        combined_rows = []
        for sol_label in df['Solution Label']:
            if sol_label in self._inline_duplicates_display:
                combined_rows.append((sol_label, self._inline_duplicates_display[sol_label]))

        # === رنگ‌بندی ردیف‌ها بر اساس فایل + ساخت لیجند ===
        # === رنگ‌بندی فقط برای Row Header (شماره سطر سمت چپ) ===
        self.row_header_colors = ['#FFFFFF'] * len(df)  # پیش‌فرض سفید
        self.file_colors = {}  # برای لیجند

        color_palette = [
            '#FF6B6B', '#4ECDC4', '#45B7D1', '#96CEB4', '#FECA57',
            '#A29BFE', '#FD79A8', '#55E6C1', '#FFD93D', '#6C5CE7'
        ]

        for i, file_info in enumerate(self.app.file_ranges):
            clean_name = file_info['clean_name']
            start = file_info['start_pivot_row']
            end = file_info['end_pivot_row']
            color = color_palette[i % len(color_palette)]
            self.file_colors[clean_name] = color

            # فقط ردیف‌هایی که الان در نمای فیلتر شده وجود دارن
            for idx in range(len(df)):
                if start <= idx <= end:
                    self.row_header_colors[idx] = color



        model = PivotTableModel(self, df, combined_rows)
        self.table_view.setModel(model)
        self.table_view.frozenTableView.setModel(model)
        self.table_view.update_frozen_columns()
        self.table_view.model().layoutChanged.emit()
        self.table_view.frozenTableView.model().layoutChanged.emit()
        self.table_view.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        for col, width in self.column_widths.items():
            if col < len(df.columns):
                self.table_view.horizontalHeader().resizeSection(col, width)
        self.table_view.viewport().update()
        self.logger.debug("Completed update_pivot_display")
        
        # === آپدیت لیجند با اسکرول افقی ===
        # پاک کردن محتوای قبلی
        for i in reversed(range(self.legend_layout.count())):
            item = self.legend_layout.itemAt(i)
            if item.widget():
                item.widget().setParent(None)

        if hasattr(self, 'file_colors') and self.file_colors:
            # عنوان
            title = QLabel("File Groups:")
            title.setStyleSheet("font-weight: bold; color: #495057; margin-right: 10px;")
            self.legend_layout.addWidget(title)

            # اضافه کردن هر فایل با مربع رنگی
            for file_name, color in self.file_colors.items():
                color_box = QLabel("■■")
                color_box.setStyleSheet(f"""
                    color: {color};
                    font-size: 20px;
                    background: transparent;
                    margin-right: 6px;
                """)
                name_label = QLabel(file_name)
                name_label.setStyleSheet("""
                    font-size: 11px;
                    color: #212529;
                    white-space: nowrap;
                """)

                item_widget = QWidget()
                item_layout = QHBoxLayout(item_widget)
                item_layout.setContentsMargins(0, 0, 0, 0)
                item_layout.setSpacing(6)
                item_layout.addWidget(color_box)
                item_layout.addWidget(name_label)
                item_layout.addStretch()

                self.legend_layout.addWidget(item_widget)

            self.legend_layout.addStretch()
            self.legend_scroll.show()
        else:
            self.legend_scroll.hide()

    def calculate_dynamic_range(self, value):
        try:
            value = float(value)
            abs_value = abs(value)
            if abs_value < 10:
                return 2
            elif 10 <= abs_value < 100:
                return abs_value * 0.2
            else:
                return abs_value * 0.05
        except (ValueError, TypeError):
            return 0

    def on_cell_double_click(self, index):
        self.logger.debug(f"Cell double-clicked at row {index.row()}, col {index.column()}")
        if not index.isValid() or self.current_view_df is None:
            return
        row = index.row()
        col = index.column()
        col_name = self.current_view_df.columns[col]
        if col_name == "Solution Label":
            return

        try:
            pivot_row = row
            current_row = 0
            for sol_label, combined_data in self._inline_duplicates_display.items():
                pivot_idx = self.current_view_df.index[self.current_view_df['Solution Label'] == sol_label].tolist()
                if not pivot_idx:
                    continue
                pivot_idx = pivot_idx[0]
                if current_row <= row < current_row + len(combined_data) + 1:
                    if row == current_row + 1 or row == current_row + 2:
                        return
                    row = pivot_idx
                    break
                current_row += 1 + len(combined_data)

            solution_label = self.current_view_df.iloc[row]['Solution Label']
            element = col_name
            if self.use_oxide_var.isChecked():
                for el, (oxide_formula, _) in oxide_factors.items():
                    if oxide_formula == col_name:
                        element = el
                        break
            cond = (self.original_df['Solution Label'] == solution_label) & (self.original_df['Element'].str.startswith(element))
            cond &= (self.original_df['Type'] == 'Samp')
            match = self.original_df[cond]
            if match.empty:
                return

            r = match.iloc[0]
            value_column = 'Int' if self.use_int_var.isChecked() else 'Corr Con'
            value = float(r.get(value_column, 0)) / 10000
            info = [
                f"Solution: {solution_label}",
                f"Element: {col_name}",
                f"Act Wgt: {self.format_value(r.get('Act Wgt', 'N/A'))}",
                f"Act Vol: {self.format_value(r.get('Act Vol', 'N/A'))}",
                f"DF: {self.format_value(r.get('DF', 'N/A'))}",
                f"Concentration: {self.format_value(value)}"
            ]
            if element.split()[0] in oxide_factors and self.use_oxide_var.isChecked():
                formula, factor = oxide_factors[element.split()[0]]
                try:
                    oxide_value = float(value) * factor
                    info.extend([f"Oxide Formula: {formula}", f"Oxide %: {self.format_value(oxide_value)}"])
                except (ValueError, TypeError):
                    info.extend([f"Oxide Formula: {formula}", "Oxide %: N/A"])

            w = QDialog(self)
            w.setWindowTitle("Cell Information")
            w.setGeometry(200, 200, 300, 200)
            layout = QVBoxLayout(w)
            for line in info:
                layout.addWidget(QLabel(line))
            close_btn = QPushButton("Close")
            close_btn.clicked.connect(w.accept)
            layout.addWidget(close_btn)
            w.exec()

        except Exception as e:
            self.logger.error(f"Failed to display cell info: {str(e)}")
            QMessageBox.warning(self, "Error", f"Failed to display cell info: {str(e)}")

    def open_row_filter_window(self):
        if self.pivot_data is None:
            QMessageBox.warning(self, "Warning", "No data to filter!")
            return

        dialog = ColumnFilterDialog(
            parent=self,
            col_name='Solution Label',
            data_source=self.pivot_data,
            column_filters=self.filters,
            on_apply_callback=self.update_pivot_display
        )
        dialog.exec()

    def open_column_filter_window(self):
        if self.pivot_data is None:
            QMessageBox.warning(self, "Warning", "No data to filter!")
            return

        # برای فیلتر ستون‌ها، از داده اصلی استفاده کن
        dialog = ColumnFilterDialog(
            parent=self,
            col_name='Element',
            data_source=self.original_df if hasattr(self, 'original_df') else self.pivot_data,
            column_filters=self.column_filter_values,  # یا self.filters
            on_apply_callback=self.update_pivot_display
        )
        dialog.exec()

    def reset_cache(self):
        self.logger.debug("Resetting PivotTab cache")
        self.pivot_data = None
        self.solution_label_order = None
        self.element_order = None
        self.column_widths.clear()
        self.cached_formatted.clear()
        self._inline_duplicates.clear()
        self._inline_duplicates_display.clear()
        self.filters.clear()
        if self.current_plot_dialog:
            self.logger.debug("Closing existing plot dialog")
            self.current_plot_dialog.close()
            self.current_plot_dialog = None
        self.update_pivot_display()

    def show_plot_options(self):
        if self.current_view_df is None or self.current_view_df.empty:
            QMessageBox.warning(self, "Warning", "No data to plot!")
            return
        dialog = PlotOptionsDialog(self)
        dialog.exec()

    def plot_row(self, solution_label):
        row_data = self.current_view_df[self.current_view_df['Solution Label'] == solution_label]
        if row_data.empty:
            return
        row_data = row_data.iloc[0, 1:]
        y = pd.to_numeric(row_data, errors='coerce').fillna(0).values
        dialog = PlotDialog(f"Row Plot: {solution_label}", self)
        plot_item = dialog.plot_widget.getPlotItem()
        plot_item.plot(range(len(row_data)), y, pen=None, symbol='o', symbolPen='b', symbolBrush='b')
        ticks = [(i, name) for i, name in enumerate(row_data.index)]
        ax = plot_item.getAxis('bottom')
        ax.setTicks([ticks])
        plot_item.setLabel('bottom', 'Element')
        plot_item.setLabel('left', 'Values')
        dialog.show()

    def plot_column(self, col):
        col_data = self.current_view_df[col]
        y = pd.to_numeric(col_data, errors='coerce').fillna(0).values
        dialog = PlotDialog(f"Column Plot: {col}", self)
        plot_item = dialog.plot_widget.getPlotItem()
        plot_item.plot(range(len(col_data)), y, pen=None, symbol='o', symbolPen='g', symbolBrush='g')
        ticks = [(i, name) for i, name in enumerate(self.current_view_df['Solution Label'])]
        ax = plot_item.getAxis('bottom')
        ax.setTicks([ticks])
        plot_item.setLabel('bottom', 'Solution Label')
        plot_item.setLabel('left', 'Values')
        dialog.show()

    def plot_all_rows(self, selected_column):
        dialog = PlotDialog(f"All Rows Plot: {selected_column}", self)
        plot_item = dialog.plot_widget.getPlotItem()
        colors = ['r', 'g', 'b', 'y', 'c', 'm', 'k']
        y = pd.to_numeric(self.current_view_df[selected_column], errors='coerce').fillna(0).values
        for idx, label in enumerate(self.current_view_df['Solution Label']):
            color = colors[idx % len(colors)]
            plot_item.plot([idx], [y[idx]], pen=None, symbol='o', symbolPen=color, symbolBrush=color, name=str(label))
        ticks = [(i, name) for i, name in enumerate(self.current_view_df['Solution Label'])]
        ax = plot_item.getAxis('bottom')
        ax.setTicks([ticks])
        plot_item.setLabel('bottom', 'Solution Label')
        plot_item.setLabel('left', 'Values')
        plot_item.addLegend()
        dialog.show()

    def plot_all_columns(self, selected_row):
        row_data = self.current_view_df[self.current_view_df['Solution Label'] == selected_row]
        if row_data.empty:
            return
        dialog = PlotDialog(f"All Columns Plot: {selected_row}", self)
        plot_item = dialog.plot_widget.getPlotItem()
        colors = ['r', 'g', 'b', 'y', 'c', 'm', 'k']
        row_data = row_data.iloc[0, 1:]
        y = pd.to_numeric(row_data, errors='coerce').fillna(0).values
        for idx, col in enumerate(row_data.index):
            color = colors[idx % len(colors)]
            plot_item.plot([idx], [y[idx]], pen=None, symbol='o', symbolPen=color, symbolBrush=color, name=col)
        ticks = [(i, name) for i, name in enumerate(row_data.index)]
        ax = plot_item.getAxis('bottom')
        ax.setTicks([ticks])
        plot_item.setLabel('bottom', 'Element')
        plot_item.setLabel('left', 'Values')
        plot_item.addLegend()
        dialog.show()