from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QFrame, QLabel, QLineEdit, QPushButton, QTableView, QHeaderView, QGroupBox, QMessageBox, QCheckBox, QDialog, QScrollArea, QTabWidget
from PyQt6.QtCore import Qt,pyqtSignal
from PyQt6.QtGui import QStandardItemModel, QStandardItem
import pandas as pd
import numpy as np
import time
import logging
from ..Common.column_filter import ColumnFilterDialog
from styles.common import common_styles
# Setup logging with minimal output
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class ElementSelectionDialog(QDialog):
    """Dialog for selecting main elements with checkboxes."""
    def __init__(self, parent, columns):
        super().__init__(parent)
        self.setWindowTitle("Select Main Elements")
        self.parent = parent
        self.columns = [col for col in columns if col != 'Solution Label']
        self.checkboxes = {}
        self.setMinimumSize(300, 400)

        layout = QVBoxLayout(self)

        # Search bar
        self.search_edit = QLineEdit()
        self.search_edit.setPlaceholderText("Search elements...")
        self.search_edit.textChanged.connect(self.filter_checkboxes)
        layout.addWidget(self.search_edit)

        # Scroll area for checkboxes
        scroll = QScrollArea()
        scroll_widget = QWidget()
        scroll_layout = QVBoxLayout(scroll_widget)
        scroll.setWidget(scroll_widget)
        scroll.setWidgetResizable(True)
        layout.addWidget(scroll)

        # Extract unique elements from column names (e.g., 'Na' from 'Na 326.068')
        unique_elements = sorted(set(col.split()[0] for col in self.columns if ' ' in col))

        # Pre-select main elements
        default_elements = {'Na', 'Ca', 'Al', 'Mg', 'K'}
        selected_elements = self.parent.main_elements if hasattr(self.parent, 'main_elements') else default_elements

        for elem in unique_elements:
            cb = QCheckBox(elem)
            cb.setChecked(elem in selected_elements)
            self.checkboxes[elem] = cb
            scroll_layout.addWidget(cb)

        # Select/Deselect all buttons
        buttons = QHBoxLayout()
        select_all_btn = QPushButton("Select All")
        select_all_btn.clicked.connect(lambda: self.toggle_all(True))
        buttons.addWidget(select_all_btn)

        deselect_all_btn = QPushButton("Deselect All")
        deselect_all_btn.clicked.connect(lambda: self.toggle_all(False))
        buttons.addWidget(deselect_all_btn)
        layout.addLayout(buttons)

        # OK and Cancel buttons
        action_buttons = QHBoxLayout()
        ok_btn = QPushButton("OK")
        ok_btn.clicked.connect(self.apply_selection)
        action_buttons.addWidget(ok_btn)

        cancel_btn = QPushButton("Cancel")
        cancel_btn.clicked.connect(self.reject)
        action_buttons.addWidget(cancel_btn)
        layout.addLayout(action_buttons)

    def filter_checkboxes(self, text):
        """Filter checkboxes based on search text."""
        text = text.lower()
        for elem, cb in self.checkboxes.items():
            cb.setVisible(text == '' or text in elem.lower())
            if not cb.isVisible():
                cb.setChecked(False)

    def toggle_all(self, checked):
        """Select or deselect all visible checkboxes."""
        for cb in self.checkboxes.values():
            if cb.isVisible():
                cb.setChecked(checked)

    def apply_selection(self):
        """Save selected elements and close dialog."""
        self.parent.main_elements = {elem for elem, cb in self.checkboxes.items() if cb.isChecked()}
        if not self.parent.main_elements:
            QMessageBox.warning(self, "Warning", "At least one element must be selected!")
            return
        logger.debug(f"Selected main elements: {self.parent.main_elements}")
        self.accept()

class EmptyCheckFrame(QWidget):
    empty_rows_found = pyqtSignal(pd.DataFrame)  # ارسال DataFrame با ایندکس اصلی
    def __init__(self, app, parent=None):
        super().__init__(parent)
        self.app = app
        self.df_cache = None
        self.empty_rows = None
        self.mean_percentage_threshold = 70  # Threshold for mean comparison
        self.filters = {}
        self.main_elements = {'Na', 'Ca', 'Al', 'Mg', 'K'}  # Default main elements
        self.setup_ui()

    def setup_ui(self):
        """Set up the UI with enhanced controls."""
        self.setStyleSheet(common_styles)

        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(15, 15, 15, 15)
        main_layout.setSpacing(15)

        input_group = QGroupBox("Empty Rows Check")
        input_layout = QHBoxLayout(input_group)
        input_layout.setSpacing(10)

        # Button to select main elements
        self.select_elements_btn = QPushButton("Select Main Elements")
        self.select_elements_btn.clicked.connect(self.open_element_selection)
        self.select_elements_btn.setToolTip("Select the main elements to consider for empty row detection")
        input_layout.addWidget(self.select_elements_btn)

        # Mean percentage threshold input
        input_layout.addWidget(QLabel("Mean % Threshold:"))
        self.mean_percentage_entry = QLineEdit()
        self.mean_percentage_entry.setText(str(self.mean_percentage_threshold))
        self.mean_percentage_entry.setFixedWidth(120)
        self.mean_percentage_entry.setToolTip("Enter the percentage below column mean (e.g., 70, range: 0 to 100)")
        input_layout.addWidget(self.mean_percentage_entry)

        self.check_button = QPushButton("Check Empty Rows")
        self.update_button_tooltip()
        self.check_button.clicked.connect(self.check_empty_rows)
        input_layout.addWidget(self.check_button)

        self.clear_filters_button = QPushButton("Clear Filters")
        self.clear_filters_button.clicked.connect(self.clear_filters)
        input_layout.addWidget(self.clear_filters_button)
        input_layout.addStretch()

        main_layout.addWidget(input_group)

        main_container = QFrame()
        main_layout.addWidget(main_container, stretch=1)
        container_layout = QHBoxLayout(main_container)
        container_layout.setSpacing(15)

        empty_group = QGroupBox("Empty Rows")
        empty_layout = QVBoxLayout(empty_group)
        empty_layout.setSpacing(10)

        self.empty_table = QTableView()
        self.empty_table.setSelectionMode(QTableView.SelectionMode.SingleSelection)
        self.empty_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.empty_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        self.empty_table.verticalHeader().setVisible(False)
        self.empty_table.setToolTip("List of empty rows")
        self.empty_table.horizontalHeader().sectionClicked.connect(self.on_header_clicked)
        empty_layout.addWidget(self.empty_table)

        container_layout.addWidget(empty_group, stretch=1)

    def update_button_tooltip(self):
        """Update the tooltip of the Check Empty Rows button."""
        self.check_button.setToolTip(
            f"Check for rows where all main elements "
            f"are {self.mean_percentage_threshold}% below column mean"
        )

    def open_element_selection(self):
        """Open dialog to select main elements."""
        df = self.app.results.last_filtered_data
        if df is None or df.empty:
            QMessageBox.warning(self, "Warning", "No data available to select elements!")
            return
        dialog = ElementSelectionDialog(self, df.columns)
        dialog.exec()

    def check_empty_rows(self):
        """Check for rows where all main elements are significantly below column means."""
        try:
            mean_percentage = float(self.mean_percentage_entry.text())
            if not 0 <= mean_percentage <= 100:
                raise ValueError("Mean percentage must be between 0 and 100")
            self.mean_percentage_threshold = mean_percentage
        except ValueError as e:
            QMessageBox.warning(self, "Warning", f"Invalid mean percentage: {e}")
            return

        self.update_button_tooltip()

        df = self.app.results.last_filtered_data
        if df is None or df.empty:
            QMessageBox.warning(self, "Warning", "No pivoted data available! Please check the Results tab.")
            logger.warning("No pivoted data available")
            return

        # --- اضافه کردن original_index ---
        if 'original_index' not in df.columns:
            df = df.reset_index(drop=True)
            df['original_index'] = df.index  # ایجاد ایندکس اصلی
        else:
            df = df.copy()  # جلوگیری از warning

        # Get valid elements
        valid_elements = [col for col in df.columns if col != 'Solution Label' and col.split()[0] in self.main_elements]
        if not valid_elements:
            QMessageBox.warning(self, "Warning", "No valid main elements selected or available!")
            logger.warning("No valid main elements selected")
            return

        # Convert to numeric
        df_numeric = df[valid_elements].apply(pd.to_numeric, errors='coerce')

        # Calculate column means and thresholds
        column_means = df_numeric.mean()
        threshold_values = column_means * (1 - self.mean_percentage_threshold / 100)

        # Mask: all main elements below threshold
        below_threshold = df_numeric < threshold_values
        empty_rows_mask = below_threshold.all(axis=1)

        # --- استخراج ردیف‌های خالی با original_index ---
        self.empty_rows = df[empty_rows_mask][['Solution Label'] + valid_elements].drop_duplicates(subset=['Solution Label'])

        if not self.empty_rows.empty:
            empty_with_index = df[empty_rows_mask][['Solution Label', 'original_index'] + valid_elements].drop_duplicates(subset=['Solution Label'])
            self.empty_rows_found.emit(empty_with_index)
        else:
            self.empty_rows_found.emit(pd.DataFrame())

        self.update_empty_table()
        QMessageBox.information(
            self, "Info",
            f"Found {len(self.empty_rows)} empty rows." if not self.empty_rows.empty else "No empty rows found."
        )

    def on_header_clicked(self, section):
        if self.empty_rows is None or self.empty_rows.empty:
            QMessageBox.warning(self, "Warning", "No data to filter!")
            return

        model = self.empty_table.model()
        if model is None:
            return

        col_name = model.headerData(section, Qt.Orientation.Horizontal, Qt.ItemDataRole.DisplayRole)
        if col_name is None:
            return

        logger.debug(f"Opening filter dialog for column: {col_name}")

        dialog = ColumnFilterDialog(
            parent=self,
            col_name=col_name,
            data_source=self.empty_rows,  # داده اصلی
            column_filters=self.filters,  # فیلترهای فعلی
            on_apply_callback=self.update_empty_table  # بعد از اعمال → آپدیت جدول
        )
        dialog.exec()

    def clear_filters(self):
        self.filters.clear()
        self.update_empty_table()
        QMessageBox.information(self, "Filters Cleared", "All column filters have been cleared.")

    def update_empty_table(self):
        """Update the empty rows table, applying any column filters."""
        model = QStandardItemModel()
        headers = ["Solution Label"] + (list(self.empty_rows.columns[1:]) if self.empty_rows is not None else [])
        model.setHorizontalHeaderLabels(headers)

        if self.empty_rows is not None and not self.empty_rows.empty:
            df = self.empty_rows.copy()
            # Convert all non-Solution Label columns to numeric
            for col in df.columns:
                if col != 'Solution Label':
                    df[col] = pd.to_numeric(df[col], errors='coerce')

            # Apply filters
            for col, filt in self.filters.items():
                if col not in df.columns:
                    logger.warning(f"Column {col} not found in DataFrame")
                    continue

                col_data = df[col] if col == 'Solution Label' else pd.to_numeric(df[col], errors='coerce')
                mask = pd.Series(True, index=df.index)

                # Apply list filter
                if 'selected_values' in filt and filt['selected_values']:
                    list_mask = pd.Series(False, index=df.index)
                    if np.nan in filt['selected_values']:
                        list_mask |= col_data.isna()
                    non_nan_values = {x for x in filt['selected_values'] if not pd.isna(x)}
                    if non_nan_values:
                        if col != 'Solution Label':
                            try:
                                non_nan_values = {float(x) for x in non_nan_values}
                            except ValueError as e:
                                logger.error(f"Error converting selected values to float for {col}: {str(e)}")
                                continue
                        list_mask |= col_data.isin(non_nan_values)
                    mask &= list_mask
                    logger.debug(f"Applied list filter on {col}: {filt['selected_values']}, rows left: {len(df[mask])}")

                # Apply numeric filter for non-Solution Label columns
                if col != 'Solution Label' and ('min_val' in filt or 'max_val' in filt):
                    try:
                        num_mask = pd.Series(True, index=df.index)
                        if 'min_val' in filt and filt['min_val'] is not None:
                            num_mask &= (col_data >= filt['min_val']) | col_data.isna()
                        if 'max_val' in filt and filt['max_val'] is not None:
                            num_mask &= (col_data <= filt['max_val']) | col_data.isna()
                        mask &= num_mask
                        logger.debug(f"Applied numeric filter on {col}: min={filt.get('min_val', None)}, max={filt.get('max_val', None)}, rows left: {len(df[mask])}")
                    except Exception as e:
                        logger.error(f"Error applying numeric filter on {col}: {str(e)}")
                        continue

                df = df[mask]
                if df.empty:
                    logger.debug(f"No rows remain after filtering column {col}")
                    break

            if df.empty:
                logger.debug("No rows remain after applying all filters")
            else:
                for _, row in df.iterrows():
                    solution_label = row['Solution Label']
                    label_item = QStandardItem(str(solution_label))
                    label_item.setTextAlignment(Qt.AlignmentFlag.AlignLeft)

                    row_items = [label_item]
                    for col in df.columns[1:]:
                        value = row[col]
                        item = QStandardItem(f"{value:.3f}" if pd.notna(value) else "")
                        item.setTextAlignment(Qt.AlignmentFlag.AlignRight)
                        row_items.append(item)

                    model.appendRow(row_items)

        self.empty_table.setModel(model)
        self.empty_table.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.Interactive)
        self.empty_table.resizeColumnToContents(0)
        for col in range(1, len(headers)):
            self.empty_table.horizontalHeader().setSectionResizeMode(col, QHeaderView.ResizeMode.Fixed)
            self.empty_table.setColumnWidth(col, 100)

    def data_changed(self):
        """Handle data change notifications."""
        self.df_cache = None
        self.empty_rows = None
        self.filters.clear()
        self.empty_table.setModel(QStandardItemModel())

    def reset_state(self):
        """Reset all internal state and UI."""
        self.df_cache = None
        self.empty_rows = None
        self.mean_percentage_threshold = 70
        self.filters.clear()
        self.main_elements = {'Na', 'Ca', 'Al', 'Mg', 'K'}
        
        if hasattr(self, 'mean_percentage_entry'):
            self.mean_percentage_entry.setText(str(self.mean_percentage_threshold))
        if hasattr(self, 'empty_table'):
            self.empty_table.setModel(QStandardItemModel())
        if hasattr(self, 'check_button'):
            self.update_button_tooltip()