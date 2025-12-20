from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QFrame, QLabel, QLineEdit, QPushButton, QTableView, QHeaderView, QGroupBox, QMessageBox, QProgressDialog, QCheckBox
from PyQt6.QtCore import Qt, QThread, pyqtSignal, QItemSelectionModel, QItemSelection, QItemSelectionRange
from PyQt6.QtGui import QStandardItemModel, QStandardItem, QColor
import pandas as pd
import numpy as np
import time
import logging
from collections import deque
from styles.common import common_styles
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class WeightCorrectionThread(QThread):
    """Thread for applying weight corrections in the background."""
    progress = pyqtSignal(int)
    finished = pyqtSignal(str, int)
    error = pyqtSignal(str)

    def __init__(self, df, solution_labels, new_weight):
        super().__init__()
        self.df = df.copy()
        self.solution_labels = solution_labels
        self.new_weight = new_weight

    def run(self):
        try:
            corrected_rows = 0
            total_rows = len(self.solution_labels)
            for i, solution_label in enumerate(self.solution_labels):
                mask = (self.df['Solution Label'] == solution_label) & (self.df['Type'] == 'Samp')
                matching_rows = self.df[mask]
                if not matching_rows.empty:
                    for idx in matching_rows.index:
                        current_weight = float(self.df.loc[idx, 'Act Wgt'])
                        current_corr_con = float(self.df.loc[idx, 'Corr Con'])
                        corrected_corr_con = (self.new_weight / current_weight) * current_corr_con if current_weight != 0 else current_corr_con
                        self.df.loc[idx, 'Corr Con'] = corrected_corr_con
                        self.df.loc[idx, 'Act Wgt'] = self.new_weight
                    corrected_rows += len(matching_rows)
                if total_rows > 10:
                    self.progress.emit(int((i + 1) / total_rows * 100))
            self.finished.emit(self.df.to_json(), corrected_rows)
        except Exception as e:
            self.error.emit(str(e))

class WeightCheckFrame(QWidget):
    data_changed = pyqtSignal()
    def __init__(self, app, parent=None):
        super().__init__(parent)
        self.app = app
        self.df_cache = None
        self.bad_weights = None
        self.original_bad_weights = None  # Store initial bad weights
        self.correction_weight = {}
        self.corrected_weights = {}  # Store corrected New Weight and New Corr Con
        self.selected_solution_labels = []
        self.included_samples = set()
        self.weight_min = 0.190
        self.weight_max = 0.210
        self.new_weight = 0.2
        self.undo_stack = deque()
        self.is_select_all_processing = False
        self.setup_ui()

    def setup_ui(self):
        """Set up the UI with enhanced controls and a modern layout."""
        start_time = time.time()
        self.setStyleSheet(common_styles)

        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(15, 15, 15, 15)
        main_layout.setSpacing(15)

        input_group = QGroupBox("Weight Range Check")
        input_layout = QHBoxLayout(input_group)
        input_layout.setSpacing(10)

        input_layout.addWidget(QLabel("Min Weight:"))
        self.min_weight_entry = QLineEdit()
        self.min_weight_entry.setText(str(self.weight_min))
        self.min_weight_entry.setFixedWidth(120)
        self.min_weight_entry.setToolTip("Enter the minimum acceptable weight (e.g., 0.190)")
        input_layout.addWidget(self.min_weight_entry)

        input_layout.addWidget(QLabel("Max Weight:"))
        self.max_weight_entry = QLineEdit()
        self.max_weight_entry.setText(str(self.weight_max))
        self.max_weight_entry.setFixedWidth(120)
        self.min_weight_entry.setToolTip("Enter the maximum acceptable weight (e.g., 0.210)")
        input_layout.addWidget(self.max_weight_entry)

        check_button = QPushButton("Check Weights")
        check_button.setToolTip("Check weights against the specified range")
        check_button.clicked.connect(self.check_weights)
        input_layout.addWidget(check_button)

        undo_button = QPushButton("Undo Last Change")
        undo_button.setToolTip("Undo the last weight correction")
        undo_button.clicked.connect(self.undo_last_change)
        input_layout.addWidget(undo_button)

        input_layout.addStretch()
        main_layout.addWidget(input_group)

        correction_group = QGroupBox("Weight Correction")
        correction_layout = QVBoxLayout(correction_group)
        correction_layout.setSpacing(10)

        self.select_all_checkbox = QCheckBox("Select All Samples")
        self.select_all_checkbox.setToolTip("Select or deselect all samples for correction")
        self.select_all_checkbox.stateChanged.connect(self.toggle_select_all)
        correction_layout.addWidget(self.select_all_checkbox)

        new_weight_frame = QFrame()
        new_weight_layout = QHBoxLayout(new_weight_frame)
        new_weight_layout.setSpacing(10)
        new_weight_layout.addWidget(QLabel("New Weight:"))
        self.new_weight_entry = QLineEdit()
        self.new_weight_entry.setText(str(self.new_weight))
        self.new_weight_entry.setFixedWidth(120)
        self.new_weight_entry.setToolTip("Enter the new weight to apply to the selected samples")
        new_weight_layout.addWidget(self.new_weight_entry)

        correction_button = QPushButton("Apply Correction")
        correction_button.setToolTip("Apply the new weight to the selected samples")
        correction_button.clicked.connect(self.apply_weight_correction)
        new_weight_layout.addWidget(correction_button)

        new_weight_layout.addStretch()
        correction_layout.addWidget(new_weight_frame)

        self.correction_table = QTableView()
        self.correction_table.setSelectionMode(QTableView.SelectionMode.MultiSelection)
        self.correction_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.correction_table.verticalHeader().setVisible(False)
        self.correction_table.setToolTip("Select rows to correct their weights (must be included)")
        correction_layout.addWidget(self.correction_table)

        main_container = QFrame()
        main_layout.addWidget(main_container, stretch=1)
        container_layout = QHBoxLayout(main_container)
        container_layout.setSpacing(15)
        container_layout.addWidget(correction_group, stretch=1)

        logger.debug(f"UI setup took {time.time() - start_time:.3f} seconds")

    def on_selection_changed(self, selected, deselected):
        """Update selected labels on selection change."""
        model = self.correction_table.model()
        if model:
            self.selected_solution_labels = [model.data(model.index(index.row(), 1)) for index in self.correction_table.selectionModel().selectedRows()]
            logger.debug(f"Selected labels updated: {self.selected_solution_labels}")
            if len(self.selected_solution_labels) == 1:
                label = self.selected_solution_labels[0]
                if self.original_bad_weights is not None:
                    weight_row = self.original_bad_weights[self.original_bad_weights['Solution Label'] == label]
                    if not weight_row.empty:
                        try:
                            actual_weight = float(weight_row['Act Wgt'].iloc[0])
                            self.new_weight_entry.setText(f"{actual_weight:.3f}")
                        except (ValueError, TypeError):
                            pass
            # logger.debug(f"Included samples: {self.included_samples}")

    def toggle_select_all(self, state):
        """Toggle selection of all samples and their include checkboxes."""
        if self.is_select_all_processing:
            logger.debug("Select All already processing, skipping")
            return

        self.is_select_all_processing = True
        logger.debug(f"toggle_select_all called with state: {state}")

        model = self.correction_table.model()
        if not model:
            logger.debug("No model available for Select All")
            self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
            QMessageBox.warning(self, "Warning", "No data available to select!")
            self.is_select_all_processing = False
            return

        selection_model = self.correction_table.selectionModel()
        selection = QItemSelection()
        deselection = QItemSelection()

        if state == 2:  # Qt.CheckState.Checked
            logger.debug(f"Processing Select All: Checking all rows")
            for row in range(model.rowCount()):
                index = model.index(row, 0)
                selection.append(QItemSelectionRange(index, model.index(row, model.columnCount() - 1)))
                include_item = model.item(row, 0)
                if include_item:
                    include_item.setCheckState(Qt.CheckState.Checked)
                    solution_label = model.data(model.index(row, 1))
                    self.included_samples.add(solution_label)
            self.selected_solution_labels = [model.data(model.index(row, 1)) for row in range(model.rowCount())]
            logger.debug(f"Select All: Selected {len(self.selected_solution_labels)} rows, included: {self.included_samples}")
        else:  # Qt.CheckState.Unchecked
            logger.debug(f"Processing Select All: Unchecking all rows")
            for row in range(model.rowCount()):
                index = model.index(row, 0)
                deselection.append(QItemSelectionRange(index, model.index(row, model.columnCount() - 1)))
                include_item = model.item(row, 0)
                if include_item:
                    include_item.setCheckState(Qt.CheckState.Unchecked)
                    solution_label = model.data(model.index(row, 1))
                    self.included_samples.discard(solution_label)
            self.selected_solution_labels = []
            logger.debug("Select All: Cleared selections and checkboxes")

        selection_model.clearSelection()
        if state == 2:
            selection_model.select(selection, QItemSelectionModel.SelectionFlag.Select | QItemSelectionModel.SelectionFlag.Rows)
        selection_model.selectionChanged.emit(selection, deselection)
        self.is_select_all_processing = False

    def check_weights(self):
        """Check weights and display bad weights in the table."""
        start_time = time.time()
        try:
            self.weight_min = float(self.min_weight_entry.text())
            self.weight_max = float(self.max_weight_entry.text())
            if self.weight_min >= self.weight_max:
                raise ValueError("Minimum weight must be less than maximum weight")
        except ValueError as e:
            QMessageBox.warning(self, "Warning", f"Invalid weight range: {e}")
            return

        if self.df_cache is None:
            data_start = time.time()
            self.df_cache = self.app.get_data()
            logger.debug(f"Data loading took {time.time() - data_start:.3f} seconds")

        df = self.df_cache
        if df is None or df.empty:
            QMessageBox.warning(self, "Warning", "No data loaded!")
            return

        df['Corr Con'] = pd.to_numeric(df['Corr Con'], errors='coerce')
        self.df_cache = df[df['Corr Con'].notna()].copy()
        df = self.df_cache

        data_filter_start = time.time()
        sample_data = df[df['Type'] == 'Samp']
        self.bad_weights = sample_data[
            (sample_data['Act Wgt'] < self.weight_min) | (sample_data['Act Wgt'] > self.weight_max)
        ][['Solution Label', 'Act Wgt', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
        
        # Always reset original_bad_weights to include new data
        self.original_bad_weights = self.bad_weights.copy()  # Update original_bad_weights
        logger.debug(f"Filtering bad weights took {time.time() - data_filter_start:.3f} seconds")

        self.update_correction_table()
        if self.bad_weights.empty:
            QMessageBox.information(self, "Info", "No issues found with weights.")
        logger.debug(f"Check weights took {time.time() - start_time:.3f} seconds")

    def update_correction_table(self):
        """Update the correction table with bad weights and preserve corrected weights."""
        start_time = time.time()
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["Include", "Solution Label", "Old Weight", "Old Corr Con", "New Weight", "New Corr Con"])

        if self.bad_weights is not None and not self.bad_weights.empty:
            self.correction_weight = {}
            for _, row in self.bad_weights.iterrows():  # Use bad_weights instead of original_bad_weights
                solution_label = row['Solution Label']
                old_weight = float(row['Act Wgt'])
                old_corr_con = float(row['Corr Con'])

                include_item = QStandardItem()
                include_item.setCheckable(True)
                include_item.setCheckState(Qt.CheckState.Checked if solution_label in self.included_samples else Qt.CheckState.Unchecked)

                label_item = QStandardItem(str(solution_label))
                label_item.setEditable(False)

                old_weight_item = QStandardItem(f"{old_weight:.3f}")
                old_weight_item.setEditable(False)
                old_weight_item.setBackground(QColor("#FFE0B2"))

                old_corr_con_item = QStandardItem(f"{old_corr_con:.3f}")
                old_corr_con_item.setEditable(False)
                old_corr_con_item.setBackground(QColor("#BBDEFB"))

                # Use corrected weights if available, otherwise use original weights
                new_weight = self.corrected_weights.get(solution_label, {}).get('new_weight', old_weight)
                new_corr_con = self.corrected_weights.get(solution_label, {}).get('new_corr_con', old_corr_con)
                new_weight_item = QStandardItem(f"{new_weight:.3f}")
                new_weight_item.setEditable(False)
                new_weight_item.setBackground(QColor("#C8E6C9"))

                new_corr_con_item = QStandardItem(f"{new_corr_con:.3f}")
                new_corr_con_item.setEditable(False)
                new_corr_con_item.setBackground(QColor("#A5D6A7"))

                model.appendRow([include_item, label_item, old_weight_item, old_corr_con_item, new_weight_item, new_corr_con_item])
                self.correction_weight[solution_label] = old_weight

                model.itemChanged.connect(self.toggle_include)

        self.correction_table.setModel(model)
        self.correction_table.selectionModel().selectionChanged.connect(self.on_selection_changed)

        header = self.correction_table.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.Fixed)
        self.correction_table.setColumnWidth(0, 50)
        for col in [1, 2, 3, 4, 5]:
            header.setSectionResizeMode(col, QHeaderView.ResizeMode.ResizeToContents)
        self.correction_table.resizeColumnsToContents()
        self.correction_table.resizeRowsToContents()

        logger.debug(f"Updating correction table took {time.time() - start_time:.3f} seconds")

    def toggle_include(self, item):
        """Toggle inclusion of a sample and select/deselect the row."""
        if item.column() == 0:
            model = self.correction_table.model()
            solution_label = model.data(model.index(item.row(), 1))
            new_state = item.checkState()
            selection_model = self.correction_table.selectionModel()
            row_index = model.index(item.row(), 0)

            if new_state == Qt.CheckState.Checked:
                self.included_samples.add(solution_label)
                selection = QItemSelection(row_index, model.index(item.row(), model.columnCount() - 1))
                selection_model.select(selection, QItemSelectionModel.SelectionFlag.Select | QItemSelectionModel.SelectionFlag.Rows)
                self.selected_solution_labels.append(solution_label)
            else:
                self.included_samples.discard(solution_label)
                deselection = QItemSelection(row_index, model.index(item.row(), model.columnCount() - 1))
                selection_model.select(deselection, QItemSelectionModel.SelectionFlag.Deselect | QItemSelectionModel.SelectionFlag.Rows)
                if solution_label in self.selected_solution_labels:
                    self.selected_solution_labels.remove(solution_label)

            # logger.debug(f"Toggled include for {solution_label} to {'Checked' if new_state == Qt.CheckState.Checked else 'Unchecked'}, selected labels: {self.selected_solution_labels}")

    def apply_weight_correction(self):
            start_time = time.time()
            if not self.included_samples:
                QMessageBox.warning(self, "Warning", "No samples included! Check 'Include' checkboxes.")
                return

            try:
                new_weight = float(self.new_weight_entry.text())
                if new_weight <= 0:
                    raise ValueError("Weight must be positive")
            except ValueError as e:
                QMessageBox.warning(self, "Warning", f"Invalid weight: {e}")
                return

            if self.df_cache is None:
                self.df_cache = self.app.get_data()

            df = self.df_cache
            if df is None or df.empty:
                QMessageBox.warning(self, "Warning", "No data loaded!")
                return

            valid_labels = list(self.included_samples)
            logger.debug(f"Valid labels for correction: {valid_labels}")

            if not valid_labels:
                QMessageBox.warning(self, "Warning", "No samples included! Check 'Include' checkboxes.")
                return

            self.undo_stack.append(df.copy().to_json())

            if self.original_bad_weights is not None:
                bad_weights_dict = self.original_bad_weights.set_index('Solution Label')[['Act Wgt', 'Corr Con']].to_dict('index')
                for solution_label in valid_labels:
                    if solution_label in bad_weights_dict:
                        old_weight = float(bad_weights_dict[solution_label]['Act Wgt'])
                        old_corr_con = float(bad_weights_dict[solution_label]['Corr Con'])
                        new_corr_con = (new_weight / old_weight) * old_corr_con if old_weight != 0 else old_corr_con
                        self.corrected_weights[solution_label] = {
                            'old_weight': old_weight,  # Store old weight
                            'new_weight': new_weight,
                            'new_corr_con': new_corr_con
                        }
                        logger.debug(f"Stored corrected weights for {solution_label}: Old Weight={old_weight:.3f}, New Weight={new_weight:.3f}, New Corr Con={new_corr_con:.3f}")

            if len(valid_labels) <= 10:
                try:
                    corrected_rows = 0
                    for solution_label in valid_labels:
                        mask = (df['Solution Label'] == solution_label) & (df['Type'] == 'Samp')
                        matching_rows = df[mask]
                        if not matching_rows.empty:
                            for idx in matching_rows.index:
                                current_weight = float(df.loc[idx, 'Act Wgt'])
                                current_corr_con = float(df.loc[idx, 'Corr Con'])
                                corrected_corr_con = (new_weight / current_weight) * current_corr_con if current_weight != 0 else current_corr_con
                                df.loc[idx, 'Corr Con'] = corrected_corr_con
                                df.loc[idx, 'Act Wgt'] = new_weight
                            corrected_rows += len(matching_rows)
                    self.df_cache = df
                    self.app.set_data(self.df_cache)
                    self.data_changed.emit()
                    sample_data = self.df_cache[self.df_cache['Type'] == 'Samp']
                    self.bad_weights = sample_data[
                        (sample_data['Act Wgt'] < self.weight_min) | (sample_data['Act Wgt'] > self.weight_max)
                    ][['Solution Label', 'Act Wgt', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
                    logger.debug(f"Updated bad_weights shape: {self.bad_weights.shape}")
                    logger.debug(f"Updated bad_weights Solution Labels: {self.bad_weights['Solution Label'].tolist()}")
                    self.update_correction_table()
                    self.correction_table.clearSelection()
                    self.selected_solution_labels = []
                    self.included_samples.clear()
                    self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
                    self.app.notify_data_changed()  # Notify all tabs of data change
                    QMessageBox.information(self, "Success", f"Corrected {corrected_rows} rows")
                    if self.bad_weights.empty:
                        QMessageBox.information(self, "Info", "All weights are now within the valid range!")
                    logger.debug(f"Updating correction table took {time.time() - start_time:.3f} seconds")
                    return
                except Exception as e:
                    QMessageBox.warning(self, "Error", f"Failed: {str(e)}")
                    return

            self.thread = WeightCorrectionThread(df, valid_labels, new_weight)
            self.progress_dialog = QProgressDialog("Applying...", "Cancel", 0, 100, self)
            self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
            self.progress_dialog.setAutoClose(True)
            self.progress_dialog.setMinimumDuration(0)
            self.progress_dialog.canceled.connect(self.thread.terminate)
            self.thread.progress.connect(self.progress_dialog.setValue)
            self.thread.finished.connect(self.on_correction_finished)
            self.thread.error.connect(self.on_correction_error)
            self.thread.start()

    def undo_last_change(self):
        """Undo the last weight correction and update the table."""
        if not self.undo_stack:
            QMessageBox.information(self, "Info", "No changes to undo!")
            return
        
        # Restore previous data from undo stack
        prev_json = self.undo_stack.pop()
        self.df_cache = pd.read_json(prev_json)
        self.app.set_data(self.df_cache)
        self.data_changed.emit()
        self.app.notify_data_changed()  # Notify all tabs of data change
        
        # Recalculate bad_weights based on restored data
        sample_data = self.df_cache[self.df_cache['Type'] == 'Samp']
        self.bad_weights = sample_data[
            (sample_data['Act Wgt'] < self.weight_min) | (sample_data['Act Wgt'] > self.weight_max)
        ][['Solution Label', 'Act Wgt', 'Corr Con']].drop_duplicates(subset=['Solution Label'])

        # Clear corrected weights since we're reverting to previous state
        self.corrected_weights.clear()
        
        # Update table to reflect restored bad weights
        self.update_correction_table()
        self.correction_table.clearSelection()
        self.selected_solution_labels = []
        self.included_samples.clear()
        self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        
        QMessageBox.information(self, "Success", "Last change undone")
        if self.bad_weights.empty:
            QMessageBox.information(self, "Info", "No issues found with weights after undo.")

    def on_correction_finished(self, df_json, corrected_rows):
        self.df_cache = pd.read_json(df_json)
        self.app.set_data(self.df_cache)
        self.data_changed.emit()
        sample_data = self.df_cache[self.df_cache['Type'] == 'Samp']
        self.bad_weights = sample_data[
            (sample_data['Act Wgt'] < self.weight_min) | (sample_data['Act Wgt'] > self.weight_max)
        ][['Solution Label', 'Act Wgt', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
        logger.debug(f"Updated bad_weights shape: {self.bad_weights.shape}")
        logger.debug(f"Updated bad_weights Solution Labels: {self.bad_weights['Solution Label'].tolist()}")
        self.update_correction_table()
        self.correction_table.clearSelection()
        self.selected_solution_labels = []
        self.included_samples.clear()
        self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        self.app.notify_data_changed()  # Notify all tabs of data change
        QMessageBox.information(self, "Success", f"Corrected {corrected_rows} rows")
        if self.bad_weights.empty:
            QMessageBox.information(self, "Info", "All weights are now within the valid range!")
        self.progress_dialog.close()

    def on_correction_error(self, error_msg):
        """Handle thread errors."""
        QMessageBox.warning(self, "Error", f"Failed: {error_msg}")
        self.progress_dialog.close()

    def reset_state(self):
        """Reset all internal state and UI."""
        self.df_cache = None
        self.bad_weights = None
        self.original_bad_weights = None
        self.correction_weight = {}
        self.corrected_weights = {}
        self.selected_solution_labels = []
        self.included_samples = set()
        self.weight_min = 0.190
        self.weight_max = 0.210
        self.new_weight = 0.2
        self.undo_stack = deque()
        self.is_select_all_processing = False
        
        # Reset UI elements
        if hasattr(self, 'min_weight_entry'):
            self.min_weight_entry.setText(str(self.weight_min))
        if hasattr(self, 'max_weight_entry'):
            self.max_weight_entry.setText(str(self.weight_max))
        if hasattr(self, 'new_weight_entry'):
            self.new_weight_entry.setText(str(self.new_weight))
        if hasattr(self, 'correction_table'):
            model = QStandardItemModel()
            self.correction_table.setModel(model)
        if hasattr(self, 'select_all_checkbox'):
            self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        logger.debug("WeightCheckFrame state reset")