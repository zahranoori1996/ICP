from PyQt6.QtWidgets import QWidget, QVBoxLayout, QHBoxLayout, QFrame, QLabel, QLineEdit, QPushButton, QTableView, QHeaderView, QGroupBox, QMessageBox, QProgressDialog, QCheckBox
from PyQt6.QtCore import Qt, QThread, pyqtSignal, QItemSelectionModel, QItemSelection, QItemSelectionRange
from PyQt6.QtGui import QStandardItemModel, QStandardItem, QColor
import pandas as pd
import re
import time
import logging
from collections import deque
from styles.common import common_styles
# Setup logging
logging.basicConfig(level=logging.DEBUG, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)

class DFCorrectionThread(QThread):
    """Thread for applying DF corrections in the background."""
    progress = pyqtSignal(int)
    finished = pyqtSignal(str, int)
    error = pyqtSignal(str)

    def __init__(self, df, solution_labels, new_df):
        super().__init__()
        self.df = df.copy()
        self.solution_labels = solution_labels
        self.new_df = new_df

    def run(self):
        try:
            corrected_rows = 0
            total_rows = len(self.solution_labels)
            for i, solution_label in enumerate(self.solution_labels):
                mask = (self.df['Solution Label'] == solution_label) & (self.df['Type'] == 'Samp')
                matching_rows = self.df[mask]
                if not matching_rows.empty:
                    self.df.loc[mask, 'DF'] = self.new_df
                    corrected_rows += len(matching_rows)
                if total_rows > 10:
                    self.progress.emit(int((i + 1) / total_rows * 100))
            self.finished.emit(self.df.to_json(), corrected_rows)
        except Exception as e:
            self.error.emit(str(e))

class DFCheckFrame(QWidget):
    data_changed = pyqtSignal()  # Signal to notify data changes

    def __init__(self, app, results_frame, parent=None):
        super().__init__(parent)
        self.app = app
        self.results_frame = results_frame  # Reference to ResultsFrame
        self.df_cache = None
        self.bad_dfs = None
        self.original_bad_dfs = None  # Store initial bad DFs (مثل original_bad_weights)
        self.corrected_dfs = {}  # Store old_df, new_df (مثل corrected_weights)
        self.selected_solution_labels = []
        self.included_samples = set()
        self.df_value = 1.0
        self.new_df = 1.0
        self.undo_stack = deque()
        self.is_select_all_processing = False

        # CRITICAL: Connect this instance to app (مثل WeightCheckFrame)
        self.app.df_check = self
        logger.debug("DFCheckFrame initialized and linked to app.df_check")

        self.setup_ui()

    def setup_ui(self):
        """Set up the UI with enhanced controls and a modern layout."""
        start_time = time.time()
        self.setStyleSheet(common_styles)

        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(15, 15, 15, 15)
        main_layout.setSpacing(15)

        # Input group
        input_group = QGroupBox("DF Value Check")
        input_layout = QHBoxLayout(input_group)
        input_layout.setSpacing(10)

        input_layout.addWidget(QLabel("Expected DF Value:"))
        self.df_entry = QLineEdit()
        self.df_entry.setText(str(self.df_value))
        self.df_entry.setFixedWidth(120)
        self.df_entry.setToolTip("Enter the expected DF value (e.g., 1.0)")
        input_layout.addWidget(self.df_entry)

        check_button = QPushButton("Check DF Values")
        check_button.setToolTip("Check DF values against the expected value or Solution Label")
        check_button.clicked.connect(self.check_df_values)
        input_layout.addWidget(check_button)

        undo_button = QPushButton("Undo Last Change")
        undo_button.setToolTip("Undo the last DF correction")
        undo_button.clicked.connect(self.undo_last_change)
        input_layout.addWidget(undo_button)

        input_layout.addStretch()
        main_layout.addWidget(input_group)

        # Correction group
        correction_group = QGroupBox("DF Correction")
        correction_layout = QVBoxLayout(correction_group)
        correction_layout.setSpacing(10)

        self.select_all_checkbox = QCheckBox("Select All Samples")
        self.select_all_checkbox.setToolTip("Select or deselect all samples for correction")
        self.select_all_checkbox.stateChanged.connect(self.toggle_select_all)
        correction_layout.addWidget(self.select_all_checkbox)

        new_df_frame = QFrame()
        new_df_layout = QHBoxLayout(new_df_frame)
        new_df_layout.setSpacing(10)
        new_df_layout.addWidget(QLabel("New DF:"))
        self.new_df_entry = QLineEdit()
        self.new_df_entry.setText(str(self.new_df))
        self.new_df_entry.setFixedWidth(120)
        self.new_df_entry.setToolTip("Enter the new DF value to apply to the selected samples")
        new_df_layout.addWidget(self.new_df_entry)

        correction_button = QPushButton("Apply Correction")
        correction_button.setToolTip("Apply the new DF value to the selected samples")
        correction_button.clicked.connect(self.apply_df_correction)
        new_df_layout.addWidget(correction_button)

        new_df_layout.addStretch()
        correction_layout.addWidget(new_df_frame)

        self.correction_table = QTableView()
        self.correction_table.setSelectionMode(QTableView.SelectionMode.MultiSelection)
        self.correction_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.correction_table.verticalHeader().setVisible(False)
        self.correction_table.setToolTip("Select rows to correct their DF values (must be included)")
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
                if self.original_bad_dfs is not None:  # مثل original_bad_weights
                    df_row = self.original_bad_dfs[self.original_bad_dfs['Solution Label'] == label]
                    if not df_row.empty:
                        try:
                            actual_df = float(df_row['DF'].iloc[0])
                            self.new_df_entry.setText(f"{actual_df:.3f}")
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

    def check_df_values(self):
        """Check samples where DF doesn't match the number after 'D' in Solution Label or expected input."""
        start_time = time.time()
        try:
            self.df_value = float(self.df_entry.text())
            if self.df_value <= 0:
                raise ValueError("Expected DF must be positive")
        except ValueError as e:
            QMessageBox.warning(self, "Warning", f"Invalid DF: {e}")
            return

        if self.df_cache is None:
            data_start = time.time()
            self.df_cache = self.app.get_data()
            logger.debug(f"Data loading took {time.time() - data_start:.3f} seconds")

        df = self.df_cache
        if df is None or df.empty:
            QMessageBox.warning(self, "Warning", "No data loaded!")
            return

        sample_data = df[df['Type'] == 'Samp'].copy()
        if sample_data.empty:
            QMessageBox.warning(self, "Warning", "No sample data found!")
            return

        # Extract expected DF from Solution Label or use input
        def get_expected_df(label):
            match = re.search(r'D(\d+)(?:-|\b|$)', label)
            return int(match.group(1)) if match else self.df_value

        data_filter_start = time.time()
        sample_data['Expected DF'] = sample_data['Solution Label'].apply(get_expected_df)
        sample_data['DF'] = pd.to_numeric(sample_data['DF'], errors='coerce')
        self.bad_dfs = sample_data[
            (sample_data['DF'] != sample_data['Expected DF'])
        ][['Solution Label', 'DF', 'Expected DF']].drop_duplicates(subset=['Solution Label'])
        
        # Always update original_bad_dfs to include new data (مثل original_bad_weights)
        self.original_bad_dfs = self.bad_dfs.copy()
        logger.debug(f"Filtering bad DFs took {time.time() - data_filter_start:.3f} seconds")
        logger.debug(f"Bad DFs Solution Labels: {self.bad_dfs['Solution Label'].tolist() if self.bad_dfs is not None else 'None'}")

        self.update_correction_table()
        if self.bad_dfs.empty:
            QMessageBox.information(self, "Info", "No issues found with DF values.")
        logger.debug(f"Check DF values took {time.time() - start_time:.3f} seconds")

    def update_correction_table(self):
        """Update the correction table with bad DFs and preserve corrected DFs (مثل Weight)."""
        start_time = time.time()
        model = QStandardItemModel()
        model.setHorizontalHeaderLabels(["Include", "Solution Label", "Old DF", "New DF"])

        if self.bad_dfs is not None and not self.bad_dfs.empty:
            for _, row in self.bad_dfs.iterrows():  # Use bad_dfs (مثل bad_weights)
                solution_label = row['Solution Label']
                old_df = float(row['DF'])

                include_item = QStandardItem()
                include_item.setCheckable(True)
                include_item.setCheckState(Qt.CheckState.Checked if solution_label in self.included_samples else Qt.CheckState.Unchecked)

                label_item = QStandardItem(str(solution_label))
                label_item.setEditable(False)

                old_df_item = QStandardItem(f"{old_df:.3f}")
                old_df_item.setEditable(False)
                old_df_item.setBackground(QColor("#FFE0B2"))

                # Use corrected DFs if available, otherwise use original DF (مثل corrected_weights)
                new_df_value = self.corrected_dfs.get(solution_label, {}).get('new_df', old_df)
                new_df_item = QStandardItem(f"{new_df_value:.3f}")
                new_df_item.setEditable(False)
                new_df_item.setBackground(QColor("#C8E6C9"))

                model.appendRow([include_item, label_item, old_df_item, new_df_item])

            model.itemChanged.connect(self.toggle_include)

        self.correction_table.setModel(model)
        # Connect selectionChanged signal after setting the model
        if self.correction_table.selectionModel():
            try:
                self.correction_table.selectionModel().selectionChanged.disconnect()
            except TypeError:
                pass  # No previous connection to disconnect
            self.correction_table.selectionModel().selectionChanged.connect(self.on_selection_changed)

        self.correction_table.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.Fixed)
        self.correction_table.setColumnWidth(0, 50)
        for col in [1, 2, 3]:
            self.correction_table.horizontalHeader().setSectionResizeMode(col, QHeaderView.ResizeMode.ResizeToContents)
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

    def apply_df_correction(self):
        """Apply DF correction to the included samples (منطق مثل Weight)."""
        start_time = time.time()
        if not self.included_samples:
            QMessageBox.warning(self, "Warning", "No samples included! Check 'Include' checkboxes.")
            return

        try:
            self.new_df = float(self.new_df_entry.text())
            if self.new_df <= 0:
                raise ValueError("DF must be positive")
        except ValueError as e:
            QMessageBox.warning(self, "Warning", f"Invalid DF: {e}")
            return

        if self.df_cache is None:
            data_start = time.time()
            self.df_cache = self.app.get_data()
            logger.debug(f"Data loading in apply_df_correction took {time.time() - data_start:.3f} seconds")

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

        # Store corrections like Weight (استفاده از original_bad_dfs)
        if self.original_bad_dfs is not None:
            bad_dfs_dict = self.original_bad_dfs.set_index('Solution Label')[['DF', 'Expected DF']].to_dict('index')
            for solution_label in valid_labels:
                if solution_label in bad_dfs_dict:
                    old_df = float(bad_dfs_dict[solution_label]['DF'])
                    self.corrected_dfs[solution_label] = {
                        'old_df': old_df,  # Store old DF
                        'new_df': self.new_df
                    }
                    logger.debug(f"Stored corrected DF for {solution_label}: Old DF={old_df:.3f}, New DF={self.new_df:.3f}")

        # Ensure app.df_check reference is correct
        self.app.df_check = self

        if len(valid_labels) <= 10:
            try:
                corrected_rows = 0
                for solution_label in valid_labels:
                    mask = (df['Solution Label'] == solution_label) & (df['Type'] == 'Samp')
                    matching_rows = df[mask]
                    if not matching_rows.empty:
                        df.loc[mask, 'DF'] = self.new_df
                        corrected_rows += len(matching_rows)
                self.df_cache = df
                self.app.set_data(self.df_cache)
                self.data_changed.emit()
                self.recalculate_bad_dfs()  # مثل Weight
                self.update_correction_table()
                self.clear_ui_state()
                self.app.notify_data_changed()
                QMessageBox.information(self, "Success", f"Corrected {corrected_rows} rows")
                if self.bad_dfs.empty:
                    QMessageBox.information(self, "Info", "All DF values are now correct!")
                logger.debug(f"Small batch correction took {time.time() - start_time:.3f} seconds")
                return
            except Exception as e:
                QMessageBox.warning(self, "Error", f"Failed: {str(e)}")
                return

        # Threaded correction
        self.thread = DFCorrectionThread(df, valid_labels, self.new_df)
        self.progress_dialog = QProgressDialog("Applying DF correction...", "Cancel", 0, 100, self)
        self.progress_dialog.setWindowModality(Qt.WindowModality.WindowModal)
        self.progress_dialog.setAutoClose(True)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.canceled.connect(self.thread.terminate)
        self.thread.progress.connect(self.progress_dialog.setValue)
        self.thread.finished.connect(self.on_correction_finished)
        self.thread.error.connect(self.on_correction_error)
        self.thread.start()

        logger.debug(f"Starting threaded apply_df_correction took {time.time() - start_time:.3f} seconds")

    def recalculate_bad_dfs(self):
        """Recalculate bad_dfs after correction (مثل Weight)."""
        if self.df_cache is None:
            return
        sample_data = self.df_cache[self.df_cache['Type'] == 'Samp'].copy()
        
        def get_expected_df(label):
            match = re.search(r'D(\d+)(?:-|\b|$)', label)
            return int(match.group(1)) if match else self.df_value
        
        sample_data['Expected DF'] = sample_data['Solution Label'].apply(get_expected_df)
        sample_data['DF'] = pd.to_numeric(sample_data['DF'], errors='coerce')
        self.bad_dfs = sample_data[
            (sample_data['DF'] != sample_data['Expected DF'])
        ][['Solution Label', 'DF', 'Expected DF']].drop_duplicates(subset=['Solution Label'])
        logger.debug(f"Recalculated bad_dfs shape: {self.bad_dfs.shape}")
        logger.debug(f"Recalculated bad_dfs Solution Labels: {self.bad_dfs['Solution Label'].tolist()}")

    def clear_ui_state(self):
        """Clear UI selection state (مثل Weight)."""
        self.included_samples.clear()
        self.correction_table.clearSelection()
        self.selected_solution_labels = []
        self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)

    def undo_last_change(self):
        """Undo the last DF correction and update the table (مثل Weight)."""
        if not self.undo_stack:
            QMessageBox.information(self, "Info", "No changes to undo!")
            return
        
        # Restore previous data from undo stack
        prev_json = self.undo_stack.pop()
        self.df_cache = pd.read_json(prev_json)
        self.app.set_data(self.df_cache)
        self.data_changed.emit()
        self.app.notify_data_changed()  # Notify all tabs of data change
        
        # Recalculate bad_dfs based on restored data (مثل Weight)
        self.recalculate_bad_dfs()

        # Clear corrected_dfs since we're reverting to previous state (مثل Weight)
        self.corrected_dfs.clear()
        
        # Update table to reflect restored bad DFs
        self.update_correction_table()
        self.clear_ui_state()
        
        QMessageBox.information(self, "Success", "Last change undone")
        if self.bad_dfs.empty:
            QMessageBox.information(self, "Info", "No DF issues found after undo.")

    def on_correction_finished(self, df_json, corrected_rows):
        """Handle thread completion (منطق دقیق مثل Weight)."""
        try:
            self.df_cache = pd.read_json(df_json)
            self.app.set_data(self.df_cache)
            self.data_changed.emit()
            self.app.notify_data_changed()  # Notify all tabs of data change
            
            # Recalculate bad_dfs after correction (مثل Weight)
            self.recalculate_bad_dfs()
            
            # Update table and clear UI state
            self.update_correction_table()
            self.clear_ui_state()
            
            # Ensure app.df_check reference is correct
            self.app.df_check = self
            
            # Success messages
            QMessageBox.information(self, "Success", f"Corrected {corrected_rows} rows")
            if self.bad_dfs.empty:
                QMessageBox.information(self, "Info", "All DF values are now within the valid range!")
            
            # Close progress dialog
            if hasattr(self, 'progress_dialog'):
                self.progress_dialog.close()
                
            logger.debug(f"DF correction thread finished. {corrected_rows} rows updated.")
            logger.debug(f"corrected_dfs has {len(self.corrected_dfs)} entries for reporting.")
            
        except Exception as e:
            logger.error(f"Error in on_correction_finished: {e}", exc_info=True)
            QMessageBox.critical(self, "Error", f"Failed to finalize DF correction:\n{str(e)}")
            if hasattr(self, 'progress_dialog'):
                self.progress_dialog.close()

    def on_correction_error(self, error_msg):
        """Handle thread errors."""
        QMessageBox.warning(self, "Error", f"Failed to apply corrections: {error_msg}")
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.close()

    def reset_state(self):
        """Reset all internal state and UI (مثل Weight)."""
        logger.debug("Resetting DFCheckFrame state")
        
        # Reset internal variables
        self.df_cache = None
        self.bad_dfs = None
        self.original_bad_dfs = None
        self.corrected_dfs.clear()  # Clear corrected_dfs (مثل Weight)
        self.selected_solution_labels = []
        self.included_samples.clear()
        self.undo_stack.clear()
        self.is_select_all_processing = False
        self.df_value = 1.0
        self.new_df = 1.0
        
        # Reset UI elements
        if hasattr(self, 'df_entry'):
            self.df_entry.setText(str(self.df_value))
        if hasattr(self, 'new_df_entry'):
            self.new_df_entry.setText(str(self.new_df))
        if hasattr(self, 'correction_table'):
            model = QStandardItemModel()
            self.correction_table.setModel(model)
        if hasattr(self, 'select_all_checkbox'):
            self.select_all_checkbox.setCheckState(Qt.CheckState.Unchecked)
        
        # Ensure app reference is maintained
        self.app.df_check = self
        
        logger.debug("DFCheckFrame state reset")