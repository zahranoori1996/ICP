"""
UI builders for CalibrationPro to keep calibration_pro.py lean.
Each function operates on the CalibrationPro instance (self).
"""
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QCheckBox, QLabel, QLineEdit, QPushButton,
    QGroupBox, QSlider, QComboBox, QTableView, QSplitter, QDoubleSpinBox, QFrame,
    QScrollArea
)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QColor
from PyQt6.QtWidgets import QHeaderView
import pyqtgraph as pg


def setup_calibration_ui(self):
    """Builds the main UI layout (previously inline in CalibrationPro)."""
    main_layout = QVBoxLayout(self)

    # ====================== Shared Settings ======================
    shared_gb = QGroupBox("Shared Settings")
    # shared_gb.setSizePolicy(shared_gb.sizePolicy(), shared_gb.sizePolicy().Fixed)
    shared_l = QHBoxLayout(shared_gb)
    shared_l.setContentsMargins(10, 10, 10, 10)
    shared_l.setSpacing(12)
    shared_gb.setObjectName("sharedSettings")

    fh = QHBoxLayout()
    fh.addWidget(QLabel("File:"))
    self.file_selector = QComboBox()
    self.file_selector.setMinimumWidth(420)
    self.file_selector.setEnabled(False)
    self.file_selector.setToolTip("Click 'Calibration' to load files")
    self.file_selector.addItem("Click 'Calibration' to load files...")
    fh.addWidget(self.file_selector)
    shared_l.addLayout(fh)

    eh = QHBoxLayout()
    eh.addWidget(QLabel("Element:"))
    self.element_combo = QComboBox()
    self.element_combo.setMinimumWidth(180)
    eh.addWidget(self.element_combo)
    eh.addWidget(QLabel("Current RM:"))
    self.current_rm_label = QLabel("None")
    self.current_rm_label.setObjectName("currentRmLabel")
    self.current_rm_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
    eh.addWidget(self.current_rm_label)
    self.prev_rm_btn = QPushButton("Previous RM")
    self.next_rm_btn = QPushButton("Next RM")
    eh.addWidget(self.prev_rm_btn)
    eh.addWidget(self.next_rm_btn)

    eh.addWidget(QLabel("Filter Solution:"))
    self.filter_solution_edit = QLineEdit()
    self.filter_solution_edit.setPlaceholderText("e.g. CRM001, Check, Sample...")
    eh.addWidget(self.filter_solution_edit)
    self.reset_all_btn = QPushButton("Reset All")
    self.reset_all_btn.setStyleSheet("background-color: #D32F2F; color: white; font-weight: bold;")
    eh.addWidget(self.reset_all_btn)
    self.reset_all_btn.clicked.connect(self.reset_all)

    self.plot_calibration_btn = QPushButton("Calibration")
    eh.addWidget(self.plot_calibration_btn)
    shared_l.addLayout(eh)
    main_layout.addWidget(shared_gb)

    # ====================== Main Splitter ======================
    main_splitter = QSplitter(Qt.Orientation.Horizontal)
    main_layout.addWidget(main_splitter)

    # ====================== Controls with ScrollArea ======================
    controls_content = QWidget()
    controls_layout = QVBoxLayout(controls_content)
    controls_layout.setSpacing(12)
    controls_layout.setContentsMargins(10, 10, 10, 10)

    # CRM Verification
    crm_gb = QGroupBox("CRM Verification")
    crm_l = QVBoxLayout(crm_gb)
    show_l = QHBoxLayout()
    self.show_cert_cb = QCheckBox("Certificate"); self.show_cert_cb.setChecked(True); show_l.addWidget(self.show_cert_cb)
    self.show_crm_cb = QCheckBox("CRM"); self.show_crm_cb.setChecked(True); show_l.addWidget(self.show_crm_cb)
    self.show_range_cb = QCheckBox("Acceptable Range"); self.show_range_cb.setChecked(True); show_l.addWidget(self.show_range_cb)
    crm_l.addLayout(show_l)
    self.scale_above_50_cb = QCheckBox("Scale >50% Only")
    self.scale_above_50_cb.toggled.connect(self.update_pivot_plot)
    crm_l.addWidget(self.scale_above_50_cb)
    minmax_l = QHBoxLayout()
    minmax_l.addWidget(QLabel("Min:")); self.crm_min_edit = QLineEdit("0.0"); self.crm_min_edit.setFixedWidth(80); minmax_l.addWidget(self.crm_min_edit);self.crm_min_edit.textChanged.connect(self.update_scale_range)
    minmax_l.addWidget(QLabel("Max:")); self.crm_max_edit = QLineEdit("1000.0"); self.crm_max_edit.setFixedWidth(80); minmax_l.addWidget(self.crm_max_edit);self.crm_max_edit.textChanged.connect(self.update_scale_range)
    minmax_l.addStretch()
    crm_l.addLayout(minmax_l)
    blank_l = QHBoxLayout()
    blank_l.addWidget(QLabel("Blank:")); self.blank_edit = QLineEdit("0.0"); blank_l.addWidget(self.blank_edit)
    reset_bs_btn = QPushButton("Reset B&S"); blank_l.addWidget(reset_bs_btn)
    reset_bs_btn.clicked.connect(self.reset_blank_and_scale)
    crm_l.addLayout(blank_l)
    crm_l.addWidget(QLabel("Scale:"))
    self.scale_slider = QSlider(Qt.Orientation.Horizontal); self.scale_slider.setRange(0, 200); self.scale_slider.setValue(100)
    crm_l.addWidget(self.scale_slider)
    self.scale_label = QLabel("Scale: 1.00")
    crm_l.addWidget(self.scale_label)
    btns1 = QHBoxLayout()
    self.range_btn = QPushButton("Ranges"); btns1.addWidget(self.range_btn)
    self.range_btn.clicked.connect(self.open_range_dialog)
    self.exclude_btn = QPushButton("Exclude"); btns1.addWidget(self.exclude_btn)
    self.exclude_btn.clicked.connect(self.open_exclude_window)
    self.select_crms_btn = QPushButton("Select CRMs"); btns1.addWidget(self.select_crms_btn)
    self.select_crms_btn.clicked.connect(self.open_select_crms_window)
    crm_l.addLayout(btns1)
    btns2 = QHBoxLayout()
    self.undo_crm_btn = QPushButton("Undo CRM"); btns2.addWidget(self.undo_crm_btn)
    self.undo_crm_btn.clicked.connect(self.crm_handler.undo_crm_correction)
    self.correct_crm_btn = QPushButton("Correct CRM"); btns2.addWidget(self.correct_crm_btn)
    self.correct_crm_btn.clicked.connect(self.correct_crm_callback)
    crm_l.addLayout(btns2)
    model_btns = QHBoxLayout()
    self.apply_model_btn = QPushButton("Apply Our Model")
    self.apply_model_btn.clicked.connect(self.apply_model)
    self.report_btn = QPushButton("Report")
    self.report_btn.clicked.connect(self.show_report)
    model_btns.addWidget(self.apply_model_btn)
    model_btns.addWidget(self.report_btn)
    crm_l.addLayout(model_btns)
    controls_layout.addWidget(crm_gb)

    # RM Drift Correction
    rm_gb = QGroupBox("RM Drift Correction")
    rm_l = QVBoxLayout(rm_gb)
    top_h = QHBoxLayout()
    top_h.addWidget(QLabel("Keyword:"))
    self.keyword_entry2 = QLineEdit("RM"); self.keyword_entry2.setFixedWidth(80); top_h.addWidget(self.keyword_entry2)
    self.run_rm_btn = QPushButton("Check RM");self.run_rm_btn.setEnabled(False); top_h.addWidget(self.run_rm_btn)
    self.reset_original_btn = QPushButton("Reset to Original"); top_h.addWidget(self.reset_original_btn)
    self.reset_original_btn.clicked.connect(self.reset_to_original)
    top_h.addStretch()
    rm_l.addLayout(top_h)
    rm_cheks_layout = QHBoxLayout()
    self.per_file_cb = QCheckBox("Per File RM Reference"); self.per_file_cb.setChecked(True); rm_cheks_layout.addWidget(self.per_file_cb)
    self.global_optimize_cb = QCheckBox("Global Optimize (Ignore Checks)"); rm_cheks_layout.addWidget(self.global_optimize_cb)
    rm_l.addLayout(rm_cheks_layout)
    self.stepwise_cb = QCheckBox("Stepwise Changes"); rm_l.addWidget(self.stepwise_cb)
    slope_h = QHBoxLayout()
    slope_h.addWidget(QLabel("Manual Slope:"))
    self.slope_spin = QDoubleSpinBox(); self.slope_spin.setRange(-9999, 9999); self.slope_spin.setDecimals(8); self.slope_spin.setValue(0.0)
    slope_h.addWidget(self.slope_spin)
    self.apply_slope_btn = QPushButton("Apply Slope"); slope_h.addWidget(self.apply_slope_btn)
    rm_l.addLayout(slope_h)
    self.slope_display = QLabel("Current Slope: 0.00000000")
    self.slope_display.setStyleSheet("font-weight: bold; color: #1A3C34; background: #E8F5E9; padding: 8px; border-radius: 6px;")
    rm_l.addWidget(self.slope_display)
    bottom_h = QHBoxLayout()
    self.auto_flat_btn = QPushButton("Auto Flat"); bottom_h.addWidget(self.auto_flat_btn)
    self.auto_zero_slope_btn = QPushButton("Auto Zero Slope"); bottom_h.addWidget(self.auto_zero_slope_btn)
    self.undo_rm_btn = QPushButton("Undo RM"); bottom_h.addWidget(self.undo_rm_btn)
    rm_l.addLayout(bottom_h)
    controls_layout.addWidget(rm_gb)

    # Calibration Frame
    calib_frame = QFrame(); calib_frame.setObjectName("calibFrame")
    calib_l = QHBoxLayout(calib_frame)
    calib_l.addWidget(QLabel("Calibration Range:"))
    self.calib_range_label = QLabel("[Not Set]")
    calib_l.addWidget(self.calib_range_label); calib_l.addStretch()
    controls_layout.addWidget(calib_frame)

    self.blank_display = QLabel("")
    controls_layout.addWidget(self.blank_display)
    controls_layout.addStretch()

    controls_scroll = QScrollArea()
    controls_scroll.setWidget(controls_content)
    controls_scroll.setWidgetResizable(True)
    controls_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
    controls_scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
    controls_scroll.setFrameShape(QFrame.Shape.NoFrame)

    main_splitter.addWidget(controls_scroll)
    main_splitter.setStretchFactor(0, 2)

    # ====================== Tables with Vertical Splitter ======================
    tables_splitter = QSplitter(Qt.Orientation.Vertical)

    upper_table_widget = QWidget()
    upper_table_layout = QVBoxLayout(upper_table_widget)
    upper_table_layout.setContentsMargins(0, 0, 0, 0)
    upper_table_layout.setSpacing(8)

    self.rm_table = QTableView()
    self.rm_table.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
    self.rm_table.setSelectionMode(QTableView.SelectionMode.SingleSelection)
    self.rm_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
    self.rm_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
    self.rm_table.verticalHeader().setVisible(False)
    self.rm_table.customContextMenuRequested.connect(self.show_rm_context_menu)

    rm_table_gb = QGroupBox("RM Points — Current RM Only")
    rm_table_gb.setStyleSheet("QGroupBox { font-weight: bold; color: #1A3C34; }")
    rm_table_l = QVBoxLayout(rm_table_gb)
    rm_table_l.addWidget(self.rm_table)
    upper_table_layout.addWidget(rm_table_gb)

    tables_splitter.addWidget(upper_table_widget)

    lower_table_widget = QWidget()
    lower_table_layout = QVBoxLayout(lower_table_widget)
    lower_table_layout.setContentsMargins(0, 0, 0, 0)
    lower_table_layout.setSpacing(8)

    self.detail_table = QTableView()
    self.detail_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
    self.detail_table.verticalHeader().setVisible(False)

    detail_gb = QGroupBox("All Acquisition Data — Original + New Value")
    detail_gb.setStyleSheet("QGroupBox { font-weight: bold; color: #1A3C34; }")
    detail_l = QVBoxLayout(detail_gb)
    detail_l.addWidget(self.detail_table)
    lower_table_layout.addWidget(detail_gb)

    tables_splitter.addWidget(lower_table_widget)
    tables_splitter.setStretchFactor(0, 4)
    tables_splitter.setStretchFactor(1, 6)
    tables_splitter.setSizes([300, 500])

    main_splitter.addWidget(tables_splitter)
    main_splitter.setStretchFactor(1, 3)

    # ====================== Plots with Horizontal Splitter (Vertical Split) ======================
    plots_splitter = QSplitter(Qt.Orientation.Vertical)

    upper_plot_widget = QWidget()
    upper_layout = QVBoxLayout(upper_plot_widget)
    upper_layout.setContentsMargins(0, 0, 0, 0)

    self.rm_plot = pg.PlotWidget()
    self.rm_plot.scene().sigMouseMoved.connect(lambda evt: None)
    self.rm_plot.setBackground('w')
    self.rm_plot.showGrid(x=True, y=True, alpha=0.7)
    self.rm_plot.setLabel('left', 'Intensity')
    self.rm_plot.setLabel('bottom', 'Acquisition Order')
    self.rm_plot.addLegend()
    self.highlight_point = pg.ScatterPlotItem(size=20, pen=pg.mkPen('yellow', width=4), brush=None, symbol='o')
    self.rm_plot.addItem(self.highlight_point)

    plot_gb = QGroupBox("Full Drift Plot — All Samples + All RMs")
    plot_l = QVBoxLayout(plot_gb)
    plot_l.addWidget(self.rm_plot)
    upper_layout.addWidget(plot_gb)

    plots_splitter.addWidget(upper_plot_widget)

    lower_plot_widget = QWidget()
    lower_layout = QVBoxLayout(lower_plot_widget)
    lower_layout.setContentsMargins(0, 0, 0, 0)

    self.verification_plot = pg.PlotWidget()
    self.verification_plot.setBackground('w')
    self.verification_plot.showGrid(x=True, y=True, alpha=0.7)
    self.verification_plot.setLabel('left', 'Concentration (ppm)')
    self.verification_plot.setLabel('bottom', 'Acquisition Order')
    self.verification_plot.addLegend()

    verification_gb = QGroupBox("Verification Plot — CRM & Check Standards")
    verification_gb_layout = QVBoxLayout(verification_gb)
    verification_gb_layout.addWidget(self.verification_plot)
    lower_layout.addWidget(verification_gb)

    plots_splitter.addWidget(lower_plot_widget)
    plots_splitter.setStretchFactor(0, 6)
    plots_splitter.setStretchFactor(1, 4)
    plots_splitter.setSizes([400, 300])

    main_splitter.addWidget(plots_splitter)
    main_splitter.setStretchFactor(2, 5)


def setup_plot_items(self):
    """Create plot items for RM plot (extracted for clarity)."""
    self.corrected_scatter = pg.ScatterPlotItem(
        x=[], y=[],
        symbol='o', size=9,
        brush=pg.mkBrush(33, 150, 243, 230),
        pen=pg.mkPen('#1976D2', width=1.5),
        hoverable=True,
        name="Corrected Values"
    )
    self.rm_plot.addItem(self.corrected_scatter)

    self.original_scatter = pg.ScatterPlotItem(
        x=[], y=[],
        symbol='x', size=8,
        pen=pg.mkPen('#D32F2F', width=2),
        name="Original Values"
    )
    self.rm_plot.addItem(self.original_scatter)

    self.trend_line = pg.PlotDataItem(pen=pg.mkPen(width=3, style=Qt.PenStyle.DashLine))
    self.rm_plot.addItem(self.trend_line)

    self.rm_line = pg.PlotDataItem(pen=pg.mkPen(color=(100, 180, 100, 80), width=4, style=Qt.PenStyle.DotLine))
    self.rm_plot.addItem(self.rm_line)

    self.rm_scatter = pg.ScatterPlotItem(
        x=[], y=[],
        symbol='o', size=12,
        brush=pg.mkBrush(100, 180, 100, 180),
        pen=pg.mkPen('darkgreen', width=2),
        name="RM Points"
    )
    self.rm_plot.addItem(self.rm_scatter)

    self.selected_segment_line = pg.PlotDataItem(pen=pg.mkPen('#FFD700', width=11))
    self.rm_plot.addItem(self.selected_segment_line)
    self.selected_start_rm_points = pg.ScatterPlotItem(
        symbol='s', size=22,
        brush=pg.mkBrush('#1976D2'),
        pen=pg.mkPen('white', width=4)
    )
    self.rm_plot.addItem(self.selected_start_rm_points)

    self.selected_end_rm_points = pg.ScatterPlotItem(
        symbol='o', size=22,
        brush=pg.mkBrush('#FFD700'),
        pen=pg.mkPen('white', width=4)
    )
    self.rm_plot.addItem(self.selected_end_rm_points)

    self.detail_highlight_point = pg.ScatterPlotItem(
        symbol='o', size=18, brush=pg.mkBrush('#FFEB3B'), pen=pg.mkPen('black', width=3),
        name="Selected Detail Point")
    self.rm_plot.addItem(self.detail_highlight_point)

