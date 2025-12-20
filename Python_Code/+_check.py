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

class VolumeCorrectionThread(QThread):
    """Thread for applying volume corrections in the background."""
    progress = pyqtSignal(int)
    finished = pyqtSignal(str, int)
    error = pyqtSignal(str)

    def __init__(self, df, solution_labels, new_volume):
        super().__init__()
        self.df = df.copy()
        self.solution_labels = solution_labels
        self.new_volume = new_volume

    def run(self):
        try:
            corrected_rows = 0
            total_rows = len(self.solution_labels)
            for i, solution_label in enumerate(self.solution_labels):
                mask = (self.df['Solution Label'] == solution_label) & (self.df['Type'] == 'Samp')
                matching_rows = self.df[mask]
                if not matching_rows.empty:
                    for idx in matching_rows.index:
                        current_volume = float(self.df.loc[idx, 'Act Vol'])
                        current_corr_con = float(self.df.loc[idx, 'Corr Con'])
                        corrected_corr_con = (self.new_volume / current_volume) * current_corr_con if current_volume != 0 else current_corr_con
                        self.df.loc[idx, 'Corr Con'] = corrected_corr_con
                        self.df.loc[idx, 'Act Vol'] = self.new_volume
                    corrected_rows += len(matching_rows)
                if total_rows > 10:
                    self.progress.emit(int((i + 1) / total_rows * 100))
            self.finished.emit(self.df.to_json(), corrected_rows)
        except Exception as e:
            self.error.emit(str(e))

class VolumeCheckFrame(QWidget):
    data_changed = pyqtSignal()  # Signal to notify data changes

    def __init__(self, app, results_frame, parent=None):
        super().__init__(parent)
        self.app = app
        self.results_frame = results_frame  # Reference to ResultsFrame
        self.df_cache = None
        self.bad_volumes = None
        self.initial_bad_volumes = None  # Store initial bad volumes
        self.original_bad_volumes = None  # Working copy of bad volumes
        self.corrected_volumes = {}  # Store corrected New Volume and New Corr Con
        self.correction_volume = {}
        self.selected_solution_labels = []
        self.included_samples = set()
        self.volume_value = 50.0
        self.new_volume = 50.0
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

        # Input group
        input_group = QGroupBox("Volume Check")
        input_layout = QHBoxLayout(input_group)
        input_layout.setSpacing(10)

        input_layout.addWidget(QLabel("Expected Volume:"))
        self.volume_entry = QLineEdit()
        self.volume_entry.setText(str(self.volume_value))
        self.volume_entry.setFixedWidth(120)
        self.volume_entry.setToolTip("Enter the expected volume (e.g., 50.0)")
        input_layout.addWidget(self.volume_entry)

        check_button = QPushButton("Check Volumes")
        check_button.setToolTip("Check volumes against the expected value")
        check_button.clicked.connect(self.check_volumes)
        input_layout.addWidget(check_button)

        undo_button = QPushButton("Undo Last Change")
        undo_button.setToolTip("Undo the last volume correction")
        undo_button.clicked.connect(self.undo_last_change)
        input_layout.addWidget(undo_button)

        input_layout.addStretch()
        main_layout.addWidget(input_group)

        # Correction group
        correction_group = QGroupBox("Volume Correction")
        correction_layout = QVBoxLayout(correction_group)
        correction_layout.setSpacing(10)

        self.select_all_checkbox = QCheckBox("Select All Samples")
        self.select_all_checkbox.setToolTip("Select or deselect all samples for correction")
        self.select_all_checkbox.stateChanged.connect(self.toggle_select_all)
        correction_layout.addWidget(self.select_all_checkbox)

        new_volume_frame = QFrame()
        new_volume_layout = QHBoxLayout(new_volume_frame)
        new_volume_layout.setSpacing(10)
        new_volume_layout.addWidget(QLabel("New Volume:"))
        self.new_volume_entry = QLineEdit()
        self.new_volume_entry.setText(str(self.new_volume))
        self.new_volume_entry.setFixedWidth(120)
        self.new_volume_entry.setToolTip("Enter the new volume to apply to the selected samples")
        new_volume_layout.addWidget(self.new_volume_entry)

        correction_button = QPushButton("Apply Correction")
        correction_button.setToolTip("Apply the new volume to the selected samples")
        correction_button.clicked.connect(self.apply_volume_correction)
        new_volume_layout.addWidget(correction_button)

        new_volume_layout.addStretch()
        correction_layout.addWidget(new_volume_frame)

        self.correction_table = QTableView()
        self.correction_table.setSelectionMode(QTableView.SelectionMode.MultiSelection)
        self.correction_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.correction_table.verticalHeader().setVisible(False)
        self.correction_table.setToolTip("Select rows to correct their volumes (must be included)")
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
                if self.original_bad_volumes is not None:
                    volume_row = self.original_bad_volumes[self.original_bad_volumes['Solution Label'] == label]
                    if not volume_row.empty:
                        try:
                            actual_volume = float(volume_row['Act Vol'].iloc[0])
                            self.new_volume_entry.setText(f"{actual_volume:.3f}")
                        except (ValueError, TypeError):
                            pass
            logger.debug(f"Included samples: {self.included_samples}")

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

    def check_volumes(self):
        """Check volumes and display bad volumes in the table."""
        start_time = time.time()
        try:
            self.volume_value = float(self.volume_entry.text())
            if self.volume_value <= 0:
                raise ValueError("Expected volume must be positive")
        except ValueError as e:
            QMessageBox.warning(self, "Warning", f"Invalid volume: {e}")
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
        self.bad_volumes = sample_data[
            (sample_data['Act Vol'] != self.volume_value)
        ][['Solution Label', 'Act Vol', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
        
        # Always update initial_bad_volumes and original_bad_volumes to include new data
        self.initial_bad_volumes = self.bad_volumes.copy()
        self.original_bad_volumes = self.bad_volumes.copy()
        logger.debug(f"Filtering bad volumes took {time.time() - data_filter_start:.3f} seconds")
        logger.debug(f"Bad volumes Solution Labels: {self.bad_volumes['Solution Label'].tolist() if self.bad_volumes is not None else 'None'}")

        self.update_correction_table()
        if self.bad_volumes.empty:
            QMessageBox.information(self, "Info", "No issues found with volumes.")
        logger.debug(f"Check volumes took {time.time() - start_time:.3f} seconds")

    def update_correction_table(self):
        """Update the correction table with bad volumes and preserve corrected volumes."""
        start_time = time.time()
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["Include", "Solution Label", "Old Volume", "Old Corr Con", "New Volume", "New Corr Con"])

        if self.bad_volumes is not None and not self.bad_volumes.empty:
            self.correction_volume = {}
            for _, row in self.bad_volumes.iterrows():
                solution_label = row['Solution Label']
                old_volume = float(row['Act Vol'])
                old_corr_con = float(row['Corr Con'])

                include_item = QStandardItem()
                include_item.setCheckable(True)
                include_item.setCheckState(Qt.CheckState.Checked if solution_label in self.included_samples else Qt.CheckState.Unchecked)

                label_item = QStandardItem(str(solution_label))
                label_item.setEditable(False)

                old_volume_item = QStandardItem(f"{old_volume:.3f}")
                old_volume_item.setEditable(False)
                old_volume_item.setBackground(QColor("#FFE0B2"))

                old_corr_con_item = QStandardItem(f"{old_corr_con:.3f}")
                old_corr_con_item.setEditable(False)
                old_corr_con_item.setBackground(QColor("#BBDEFB"))

                new_volume = self.corrected_volumes.get(solution_label, {}).get('new_volume', old_volume)
                new_corr_con = self.corrected_volumes.get(solution_label, {}).get('new_corr_con', old_corr_con)
                new_volume_item = QStandardItem(f"{new_volume:.3f}")
                new_volume_item.setEditable(False)
                new_volume_item.setBackground(QColor("#C8E6C9"))

                new_corr_con_item = QStandardItem(f"{new_corr_con:.3f}")
                new_corr_con_item.setEditable(False)
                new_corr_con_item.setBackground(QColor("#A5D6A7"))

                model.appendRow([include_item, label_item, old_volume_item, old_corr_con_item, new_volume_item, new_corr_con_item])
                self.correction_volume[solution_label] = old_volume

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

            logger.debug(f"Toggled include for {solution_label} to {'Checked' if new_state == Qt.CheckState.Checked else 'Unchecked'}, selected labels: {self.selected_solution_labels}")

    def apply_volume_correction(self):
        """Apply volume correction to the included samples and update table."""
        start_time = time.time()
        if not self.included_samples:
            QMessageBox.warning(self, "Warning", "No samples included! Check 'Include' checkboxes.")
            return

        try:
            self.new_volume = float(self.new_volume_entry.text())
            if self.new_volume <= 0:
                raise ValueError("Volume must be positive")
        except ValueError as e:
            QMessageBox.warning(self, "Warning", f"Invalid volume: {e}")
            return

        if self.df_cache is None:
            data_start = time.time()
            self.df_cache = self.app.get_data()
            logger.debug(f"Data loading in apply_volume_correction took {time.time() - data_start:.3f} seconds")

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

        # Update corrected volumes dictionary
        if self.bad_volumes is not None:
            bad_volumes_dict = self.bad_volumes.set_index('Solution Label')[['Act Vol', 'Corr Con']].to_dict('index')
            for solution_label in valid_labels:
                if solution_label in bad_volumes_dict:
                    old_volume = float(bad_volumes_dict[solution_label]['Act Vol'])
                    old_corr_con = float(bad_volumes_dict[solution_label]['Corr Con'])
                    new_corr_con = (self.new_volume / old_volume) * old_corr_con if old_volume != 0 else old_corr_con
                    self.corrected_volumes[solution_label] = {
                        'old_volume': old_volume,
                        'new_volume': self.new_volume,
                        'new_corr_con': new_corr_con
                    }
                    logger.debug(f"Stored corrected volumes for {solution_label}: Old Volume={old_volume:.3f}, New Volume={self.new_volume:.3f}, New Corr Con={new_corr_con:.3f}")

        if len(valid_labels) <= 10:
            try:
                corrected_rows = 0
                for solution_label in valid_labels:
                    mask = (df['Solution Label'] == solution_label) & (df['Type'] == 'Samp')
                    matching_rows = df[mask]
                    if not matching_rows.empty:
                        for idx in matching_rows.index:
                            current_volume = float(df.loc[idx, 'Act Vol'])
                            current_corr_con = float(df.loc[idx, 'Corr Con'])
                            corrected_corr_con = (self.new_volume / current_volume) * current_corr_con if current_volume != 0 else current_corr_con
                            df.loc[idx, 'Corr Con'] = corrected_corr_con
                            df.loc[idx, 'Act Vol'] = self.new_volume
                        corrected_rows += len(matching_rows)
                self.df_cache = df
                self.app.set_data(self.df_cache)
                self.data_changed.emit()
                self.app.notify_data_changed()
                sample_data = self.df_cache[self.df_cache['Type'] == 'Samp']
                self.bad_volumes = sample_data[
                    (sample_data['Act Vol'] != self.volume_value)
                ][['Solution Label', 'Act Vol', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
                self.update_correction_table()
                self.correction_table.clearSelection()
                self.selected_solution_labels = []
                self.included_samples.clear()
                self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
                QMessageBox.information(self, "Success", f"Corrected {corrected_rows} rows")
                if self.bad_volumes.empty:
                    QMessageBox.information(self, "Info", "All volumes are now within the valid range!")
                logger.debug(f"Apply volume correction took {time.time() - start_time:.3f} seconds")
            except Exception as e:
                QMessageBox.warning(self, "Error", f"Failed: {str(e)}")
                logger.error(f"Error in apply_volume_correction: {str(e)}")
            return

        self.thread = VolumeCorrectionThread(df, valid_labels, self.new_volume)
        self.progress_dialog = QProgressDialog("Applying volume correction...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.setAutoClose(True)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.canceled.connect(self.thread.terminate)
        self.thread.progress.connect(self.progress_dialog.setValue)
        self.thread.finished.connect(self.on_correction_finished)
        self.thread.error.connect(self.on_correction_error)
        self.thread.start()

        logger.debug(f"Starting apply_volume_correction took {time.time() - start_time:.3f} seconds")

    def undo_last_change(self):
        """Undo the last volume correction."""
        if not self.undo_stack:
            QMessageBox.information(self, "Info", "No changes to undo!")
            return
        prev_json = self.undo_stack.pop()
        self.df_cache = pd.read_json(prev_json)
        self.app.set_data(self.df_cache)
        self.data_changed.emit()  # Emit signal to notify ResultsFrame
        self.app.notify_data_changed()
        sample_data = self.df_cache[self.df_cache['Type'] == 'Samp']
        self.bad_volumes = sample_data[
            (sample_data['Act Vol'] != self.volume_value)
        ][['Solution Label', 'Act Vol', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
        self.corrected_volumes.clear()
        self.included_samples.clear()
        self.correction_table.clearSelection()
        self.selected_solution_labels = []
        self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        self.update_correction_table()
        QMessageBox.information(self, "Success", "Last change undone")
        if self.bad_volumes.empty:
            QMessageBox.information(self, "Info", "No issues found with volumes after undo.")

    def on_correction_finished(self, df_json, corrected_rows):
        """Handle thread completion."""
        self.df_cache = pd.read_json(df_json)
        self.app.set_data(self.df_cache)
        self.data_changed.emit()  # Emit signal to notify ResultsFrame
        self.app.notify_data_changed()
        sample_data = self.df_cache[self.df_cache['Type'] == 'Samp']
        self.bad_volumes = sample_data[
            (sample_data['Act Vol'] != self.volume_value)
        ][['Solution Label', 'Act Vol', 'Corr Con']].drop_duplicates(subset=['Solution Label'])
        self.update_correction_table()
        self.correction_table.clearSelection()
        self.selected_solution_labels = []
        self.included_samples.clear()
        self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        QMessageBox.information(self, "Success", f"Corrected volumes and Corr Con values for {corrected_rows} rows")
        if self.bad_volumes.empty:
            QMessageBox.information(self, "Info", "All volumes are now within the valid range!")
        self.progress_dialog.close()

    def on_correction_error(self, error_msg):
        """Handle thread errors."""
        QMessageBox.warning(self, "Error", f"Failed to apply corrections: {error_msg}")
        self.progress_dialog.close()

    def reset_state(self):
        """Reset all internal state and UI."""
        logger.debug("Resetting VolumeCheckFrame state")
        
        # Reset internal variables
        self.df_cache = None
        self.bad_volumes = None
        self.initial_bad_volumes = None
        self.original_bad_volumes = None
        self.corrected_volumes = {}
        self.correction_volume = {}
        self.selected_solution_labels = []
        self.included_samples = set()
        self.volume_value = 50.0
        self.new_volume = 50.0
        self.undo_stack = deque()
        self.is_select_all_processing = False
        
        # Reset UI elements
        if hasattr(self, 'volume_entry'):
            self.volume_entry.setText(str(self.volume_value))
        if hasattr(self, 'new_volume_entry'):
            self.new_volume_entry.setText(str(self.new_volume))
        if hasattr(self, 'correction_table'):
            model = QStandardItemModel()
            self.correction_table.setModel(model)
        if hasattr(self, 'select_all_checkbox'):
            self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        
        logger.debug("VolumeCheckFrame state reset")