"""
Dialog-related helpers extracted from CalibrationPro.
Each function expects `self` to be an instance of CalibrationPro.
"""
from PyQt6.QtWidgets import (
    QDialog, QGridLayout, QVBoxLayout, QHBoxLayout, QLabel, QLineEdit,
    QPushButton, QTreeView
)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QStandardItemModel, QStandardItem
from typing import Any


def open_range_dialog(self):
    dialog = QDialog(self)
    dialog.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.WindowCloseButtonHint)
    dialog.setWindowTitle("تنظیم بازه‌های مجاز")
    dialog.setGeometry(200, 200, 400, 300)
    layout = QGridLayout(dialog)
    layout.setSpacing(5)

    layout.addWidget(QLabel("|x| <10 (±):"), 0, 0)
    self.range_low_edit = QLineEdit(str(self.range_low))
    self.range_low_edit.setFixedWidth(40)
    layout.addWidget(self.range_low_edit, 0, 1)

    layout.addWidget(QLabel("10<=|x|<100 (%):"), 0, 2)
    self.range_mid_edit = QLineEdit(str(self.range_mid))
    self.range_mid_edit.setFixedWidth(40)
    layout.addWidget(self.range_mid_edit, 0, 3)

    layout.addWidget(QLabel("100<=|x|<1000 (%):"), 1, 0)
    self.range_high1_edit = QLineEdit(str(self.range_high1))
    self.range_high1_edit.setFixedWidth(40)
    layout.addWidget(self.range_high1_edit, 1, 1)

    layout.addWidget(QLabel("1000<=|x|<10000 (%):"), 1, 2)
    self.range_high2_edit = QLineEdit(str(self.range_high2))
    self.range_high2_edit.setFixedWidth(40)
    layout.addWidget(self.range_high2_edit, 1, 3)

    layout.addWidget(QLabel("10000<=|x|<100000 (%):"), 2, 0)
    self.range_high3_edit = QLineEdit(str(self.range_high3))
    self.range_high3_edit.setFixedWidth(40)
    layout.addWidget(self.range_high3_edit, 2, 1)

    layout.addWidget(QLabel("|x|>=100000 (%):"), 2, 2)
    self.range_high4_edit = QLineEdit(str(self.range_high4))
    self.range_high4_edit.setFixedWidth(40)
    layout.addWidget(self.range_high4_edit, 2, 3)

    button_layout = QHBoxLayout()
    ok_btn = QPushButton("OK")
    ok_btn.clicked.connect(lambda: self.apply_ranges(dialog))
    button_layout.addWidget(ok_btn)

    cancel_btn = QPushButton("Cancel")
    cancel_btn.clicked.connect(dialog.reject)
    button_layout.addWidget(cancel_btn)

    layout.addLayout(button_layout, 3, 0, 1, 4)
    dialog.exec()


def apply_ranges(self, dialog: QDialog):
    try:
        self.range_low = float(self.range_low_edit.text())
        self.range_mid = float(self.range_mid_edit.text())
        self.range_high1 = float(self.range_high1_edit.text())
        self.range_high2 = float(self.range_high2_edit.text())
        self.range_high3 = float(self.range_high3_edit.text())
        self.range_high4 = float(self.range_high4_edit.text())
        dialog.accept()
        self.update_pivot_plot()
    except ValueError:
        from PyQt6.QtWidgets import QMessageBox
        QMessageBox.warning(self, "Error", "Invalid range values. Please enter numbers.")


def open_exclude_window(self):
    w = QDialog(self)
    w.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.WindowCloseButtonHint)
    w.setWindowTitle("Exclude from Correct")
    w.setGeometry(200, 200, 400, 400)
    layout = QVBoxLayout(w)
    tree_view = QTreeView()
    model = QStandardItemModel()
    model.setHorizontalHeaderLabels(["Solution Label", "Value", "Exclude"])
    tree_view.setModel(model)
    tree_view.setRootIsDecorated(False)
    tree_view.header().resizeSection(0, 160)
    tree_view.header().resizeSection(1, 80)
    tree_view.header().resizeSection(2, 80)
    for label in sorted(self.pivot_df['Solution Label']):
        match = self.pivot_df[self.pivot_df['Solution Label'] == label]
        value = match[self.selected_element].iloc[0] if not match.empty else 'N/A'
        label_item = QStandardItem(label)
        value_item = QStandardItem(str(value))
        check_item = QStandardItem()
        check_item.setCheckable(True)
        check_item.setCheckState(Qt.CheckState.Checked if label in self.excluded_from_correct else Qt.CheckState.Unchecked)
        model.appendRow([label_item, value_item, check_item])
    tree_view.clicked.connect(lambda index: self.toggle_exclude_check(index, model))
    layout.addWidget(tree_view)
    close_btn = QPushButton("Close")
    close_btn.clicked.connect(w.accept)
    layout.addWidget(close_btn)
    w.exec()


def toggle_exclude_check(self, index, model: QStandardItemModel):
    if index.column() != 2:
        return
    label = model.item(index.row(), 0).text()
    if model.item(index.row(), 2).checkState() == Qt.CheckState.Checked:
        self.excluded_from_correct.add(label)
    else:
        self.excluded_from_correct.discard(label)
    self.update_pivot_plot()


def open_select_crms_window(self):
    w = QDialog(self)
    w.setWindowFlags(Qt.WindowType.Dialog | Qt.WindowType.WindowCloseButtonHint)
    w.setWindowTitle("Select Verifications to Include")
    w.setGeometry(200, 200, 300, 400)
    w.setModal(True)
    layout = QVBoxLayout(w)
    tree_view = QTreeView()
    model = QStandardItemModel()
    model.setHorizontalHeaderLabels(["Label", "Include"])
    tree_view.setModel(model)
    tree_view.setRootIsDecorated(False)
    tree_view.header().resizeSection(0, 160)
    tree_view.header().resizeSection(1, 80)

    for checkbox_key in sorted(self.app.crm_check.included_crms.keys()):
        display_label = checkbox_key.rsplit('_', 1)[0] if '_' in checkbox_key else checkbox_key
        value_item = QStandardItem(display_label)
        check_item = QStandardItem()
        check_item.setCheckable(True)
        check_item.setCheckState(
            Qt.CheckState.Checked if self.app.crm_check.included_crms[checkbox_key].isChecked()
            else Qt.CheckState.Unchecked
        )
        model.appendRow([value_item, check_item])

    tree_view.clicked.connect(lambda index: self.toggle_crm_check(index, model))
    layout.addWidget(tree_view)
    button_layout = QHBoxLayout()
    select_all_btn = QPushButton("Select All")
    select_all_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
    select_all_btn.clicked.connect(lambda: self.set_all_crms(True, model))
    button_layout.addWidget(select_all_btn)

    deselect_all_btn = QPushButton("Deselect All")
    deselect_all_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
    deselect_all_btn.clicked.connect(lambda: self.set_all_crms(False, model))
    button_layout.addWidget(deselect_all_btn)

    close_btn = QPushButton("Close")
    close_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
    close_btn.clicked.connect(w.accept)
    button_layout.addWidget(close_btn)

    layout.addLayout(button_layout)
    w.exec()


def update_scale_range(self):
    try:
        min_val = self.crm_min_edit.text().strip()
        max_val = self.crm_max_edit.text().strip()
        self.scale_range_min = float(min_val) if min_val else None
        self.scale_range_max = float(max_val) if max_val else None
        if self.scale_range_min is not None and self.scale_range_max is not None and self.scale_range_min > self.scale_range_max:
            self.scale_range_min, self.scale_range_max = self.scale_range_max, self.scale_range_min
            self.scale_range_min.setText(str(self.scale_range_min))
            self.scale_range_max.setText(str(self.scale_range_max))
        self.update_pivot_plot()
    except ValueError:
        self.scale_range_min = None
        self.scale_range_max = None
        self.update_pivot_plot()


def toggle_crm_check(self, index, model: QStandardItemModel):
    if index.column() != 1:
        return
    display_label = model.item(index.row(), 0).text()

    for checkbox_key in self.app.crm_check.included_crms.keys():
        key_label = checkbox_key.rsplit('_', 1)[0] if '_' in checkbox_key else checkbox_key
        if key_label == display_label:
            self.app.crm_check.included_crms[checkbox_key].setChecked(
                not self.app.crm_check.included_crms[checkbox_key].isChecked()
            )
            model.item(index.row(), 1).setCheckState(
                Qt.CheckState.Checked if self.app.crm_check.included_crms[checkbox_key].isChecked()
                else Qt.CheckState.Unchecked
            )
            break

    self.update_pivot_plot()


def set_all_crms(self, value: bool, model: QStandardItemModel):
    for checkbox_key, checkbox in self.app.crm_check.included_crms.items():
        checkbox.setChecked(value)
    model.clear()
    model.setHorizontalHeaderLabels(["Label", "Include"])

    for checkbox_key in sorted(self.app.crm_check.included_crms.keys()):
        display_label = checkbox_key.rsplit('_', 1)[0] if '_' in checkbox_key else checkbox_key
        value_item = QStandardItem(display_label)
        check_item = QStandardItem()
        check_item.setCheckable(True)
        check_item.setCheckState(
            Qt.CheckState.Checked if self.app.crm_check.included_crms[checkbox_key].isChecked()
            else Qt.CheckState.Unchecked
        )
        model.appendRow([value_item, check_item])

    self.update_pivot_plot()

