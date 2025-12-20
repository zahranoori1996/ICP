# screens/Common/column_filter.py
from PyQt6.QtWidgets import *
from PyQt6.QtCore import Qt
import pandas as pd
import numpy as np
import logging

logger = logging.getLogger(__name__)

class ColumnFilterDialog(QDialog):
    def __init__(self, parent, col_name, data_source, column_filters, on_apply_callback):
        super().__init__(parent)
        self.setWindowTitle(f"Filter: {col_name}")
        self.col_name = col_name
        self.data_source = data_source
        self.column_filters = column_filters
        self.on_apply_callback = on_apply_callback

        if data_source is None or data_source.empty or col_name not in data_source.columns:
            QMessageBox.warning(self, "Error", "No data or invalid column.")
            self.reject()
            return

        numeric = pd.to_numeric(data_source[col_name], errors='coerce')
        self.is_numeric = numeric.notna().any() and col_name != 'Solution Label'

        values = data_source[col_name].replace({np.nan: 'NaN'}).astype(str)
        unique = sorted(values.unique(), key=lambda x: (x != 'NaN', x))

        self.checkboxes = {}
        self.min_edit = self.max_edit = None
        self.setup_ui(unique)

    def setup_ui(self, unique):
        layout = QVBoxLayout(self)
        self.setMinimumSize(420, 500)

        tabs = QTabWidget()
        list_tab = QWidget()
        tabs.addTab(list_tab, "List")
        if self.is_numeric:
            num_tab = QWidget()
            tabs.addTab(num_tab, "Number")
            self.setup_number_tab(num_tab)
        layout.addWidget(tabs)
        self.setup_list_tab(list_tab, unique)

        btns = QHBoxLayout()
        ok = QPushButton("Apply")
        ok.clicked.connect(self.apply_filters)
        cancel = QPushButton("Cancel")
        cancel.clicked.connect(self.reject)
        btns.addWidget(ok)
        btns.addWidget(cancel)
        layout.addLayout(btns)

    def setup_list_tab(self, w, vals):
        l = QVBoxLayout(w)
        search = QLineEdit()
        search.setPlaceholderText("Search...")
        search.textChanged.connect(self.filter_checkboxes)
        l.addWidget(search)

        scroll = QScrollArea()
        container = QWidget()
        sl = QVBoxLayout(container)
        scroll.setWidget(container)
        scroll.setWidgetResizable(True)
        l.addWidget(scroll)

        curr = self.column_filters.get(self.col_name, {})
        sel = curr.get('selected_values', set(vals))

        for v in vals:
            cb = QCheckBox(v)
            cb.setChecked(v in sel)
            cb.stateChanged.connect(lambda s, vv=v: self.checkboxes[vv].setChecked(s == Qt.CheckState.Checked.value))
            self.checkboxes[v] = cb
            sl.addWidget(cb)

        # دکمه‌های Select All / Deselect All
        btns = QHBoxLayout()
        select_all_btn = QPushButton("Select All")
        select_all_btn.clicked.connect(lambda: self.toggle_all(True))
        btns.addWidget(select_all_btn)

        deselect_all_btn = QPushButton("Deselect All")
        deselect_all_btn.clicked.connect(lambda: self.toggle_all(False))
        btns.addWidget(deselect_all_btn)

        l.addLayout(btns)

    def setup_number_tab(self, w):
        layout = QVBoxLayout(w)

        self.min_edit = QLineEdit()
        self.max_edit = QLineEdit()
        self.min_edit.setFixedWidth(100)
        self.max_edit.setFixedWidth(100)

        min_layout = QHBoxLayout()
        min_layout.addWidget(QLabel("Minimum:"))
        min_layout.addWidget(self.min_edit)
        min_layout.addStretch()
        layout.addLayout(min_layout)

        max_layout = QHBoxLayout()
        max_layout.addWidget(QLabel("Maximum:"))
        max_layout.addWidget(self.max_edit)
        max_layout.addStretch()
        layout.addLayout(max_layout)

        try:
            nums = pd.to_numeric(self.data_source[self.col_name], errors='coerce').dropna()
            if not nums.empty:
                range_lbl = QLabel(f"Data Range: {nums.min():.3f} – {nums.max():.3f}")
                range_lbl.setStyleSheet("color:#1976D2;font-size:11px;")
                layout.addWidget(range_lbl)
        except Exception as e:
            logger.debug(f"Range error: {e}")

        curr = self.column_filters.get(self.col_name, {})
        if curr.get('min_val') is not None:
            self.min_edit.setText(str(curr['min_val']))
        if curr.get('max_val') is not None:
            self.max_edit.setText(str(curr['max_val']))

    def filter_checkboxes(self, t):
        t = t.lower()
        for v, cb in self.checkboxes.items():
            visible = t == '' or t in v.lower()
            cb.setVisible(visible)
            if not visible:
                cb.setChecked(False)

    def toggle_all(self, c):
        for cb in self.checkboxes.values():
            if cb.isVisible():
                cb.setChecked(c)

    def apply_filters(self):
        sel = {v for v, cb in self.checkboxes.items() if cb.isChecked()}
        if not sel and not self.is_numeric:
            QMessageBox.warning(self, "Warning", "Select at least one value.")
            return

        f = {'selected_values': {np.nan if v=='NaN' else float(v) if self.is_numeric and v!='NaN' else v for v in sel}}

        if self.is_numeric:
            try:
                min_v = float(self.min_edit.text()) if self.min_edit.text().strip() else None
                max_v = float(self.max_edit.text()) if self.max_edit.text().strip() else None
                if min_v and max_v and min_v > max_v:
                    QMessageBox.warning(self, "Error", "Min > Max")
                    return
                if min_v: f['min_val'] = min_v
                if max_v: f['max_val'] = max_v
            except:
                QMessageBox.warning(self, "Error", "Invalid number")
                return

        self.column_filters[self.col_name] = f
        if self.on_apply_callback:
            self.on_apply_callback()
        self.accept()