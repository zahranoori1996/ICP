# screens/statistics_tab/Statistics_Tab.py
import sys
from pathlib import Path
import pandas as pd
import pyqtgraph as pg
import numpy as np
from PyQt6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QGridLayout, QFrame, QLabel,
    QComboBox, QPushButton, QTableWidget, QTableWidgetItem, QHeaderView,
    QProgressBar, QMessageBox, QFileDialog, QTabWidget,QDialog
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal
from PyQt6.QtGui import QFont,QColor
from utils.date import get_week_in_month
from utils.var_main import CRM_IDS
# ایمپورت‌های مشترک از qc.py
from screens.qc_tab.crm_visulation.qc import (
    DataLoaderThread, get_db_connection
)

from utils.var_main import CRM_DATA_PATH
from utils.date import jalali_to_number
from utils.utils import normalize_crm_id
class StatisticsTab(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.parent = parent
        self.crm_df = pd.DataFrame()
        self.blank_df = pd.DataFrame()
        self.verification_cache = {}
        self.boxplot_legend = None

        self.setup_ui()
        self.load_data()

    def setup_ui(self):
        layout = QVBoxLayout(self)

        # === Toolbar ===
        toolbar = QWidget()
        toolbar.setFixedHeight(70)
        toolbar.setStyleSheet("background: qlineargradient(x1:0,y1:0,x2:0,y2:1,stop:0 #6366f1,stop:1 #4f46e5);")
        tb = QHBoxLayout(toolbar)
        tb.setContentsMargins(25, 0, 25, 0)

        title = QLabel("آمار عملکرد عناصر")
        title.setStyleSheet("color: white; font-size: 24px; font-weight: bold;")
        tb.addWidget(title)
        tb.addStretch()

        refresh_btn = QPushButton("به‌روزرسانی")
        export_btn = QPushButton("خروجی اکسل")
        for btn in (refresh_btn, export_btn):
            btn.setStyleSheet("""
                QPushButton {
                    background: rgba(255,255,255,0.2); color: white; 
                    border: 1px solid rgba(255,255,255,0.3); padding: 12px 28px; 
                    border-radius: 14px; font-weight: bold; font-size: 14px;
                }
                QPushButton:hover { background: rgba(255,255,255,0.35); }
            """)
            btn.setCursor(Qt.CursorShape.PointingHandCursor)

        tb.addWidget(refresh_btn)
        tb.addWidget(export_btn)
        layout.addWidget(toolbar)

        # === فیلتر دوره ===
        card = QFrame()
        card.setStyleSheet("background: white; border-radius: 16px; border: 1px solid #e2e8f0;")
        fl = QVBoxLayout(card)
        fl.setContentsMargins(25, 20, 25, 20)

        row = QHBoxLayout()
        row.addWidget(QLabel("دوره زمانی:"))
        self.period_combo = QComboBox()
        self.period_combo.addItems(["روزانه", "هفتگی", "ماهانه"])
        self.period_combo.setCurrentText("ماهانه")
        self.period_combo.setStyleSheet("padding: 10px; font-size: 14px; border-radius: 10px;")
        row.addWidget(self.period_combo)
        row.addStretch()
        fl.addLayout(row)
        layout.addWidget(card)

        # === Progress Bar ===
        self.progress = QProgressBar()
        self.progress.setVisible(False)
        self.progress.setStyleSheet("QProgressBar { border-radius: 8px; } QProgressBar::chunk { background: #6366f1; }")
        layout.addWidget(self.progress)

        # === Tab Widget برای تفکیک نمایش عناصر و روزها ===
        self.tab_widget = QTabWidget()
        self.tab_widget.setStyleSheet("""
            QTabWidget::pane { border: 1px solid #e2e8f0; border-radius: 8px; background: white; }
            QTabBar::tab { padding: 12px 24px; font-size: 14px; font-weight: bold; }
            QTabBar::tab:selected { background: #6366f1; color: white; }
            QTabBar::tab:!selected { background: #f1f5f9; color: #64748b; }
        """)

        # === تب عناصر ===
        elements_tab = QWidget()
        elements_layout = QGridLayout(elements_tab)
        elements_layout.setSpacing(20)

        self.best_plot = pg.PlotWidget(title="بهترین عناصر (کمترین انحراف)")
        self.best_plot.setBackground('w')
        self.best_plot.showGrid(x=True, y=True, alpha=0.3)
        self.best_plot.setLabel('left', 'میانگین انحراف (%)')
        self.best_plot.setLabel('bottom', 'دوره زمانی')

        self.worst_plot = pg.PlotWidget(title="بدترین عناصر (بیشترین انحراف)")
        self.worst_plot.setBackground('w')
        self.worst_plot.showGrid(x=True, y=True, alpha=0.3)
        self.worst_plot.setLabel('left', 'میانگین انحراف (%)')
        self.worst_plot.setLabel('bottom', 'دوره زمانی')

        self.table = QTableWidget()
        self.table.cellClicked.connect(self.show_element_period_details)
        self.table.setColumnCount(6)
        self.table.setHorizontalHeaderLabels([
            "دوره", "عنصر", "میانگین انحراف %", "تعداد", "وضعیت", "CRM ID"
        ])
        header = self.table.horizontalHeader()
        header.setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.table.setStyleSheet("font-size: 13px;")

        elements_layout.addWidget(self.best_plot, 0, 0)
        elements_layout.addWidget(self.worst_plot, 0, 1)
        elements_layout.addWidget(self.table, 1, 0, 1, 2)

        # === تب روزها ===
        days_tab = QWidget()
        days_layout = QGridLayout(days_tab)
        days_layout.setSpacing(20)

        self.best_days_plot = pg.PlotWidget(title="بهترین روزها (کمترین میانگین انحراف)")
        self.best_days_plot.setBackground('w')
        self.best_days_plot.showGrid(x=True, y=True, alpha=0.3)
        self.best_days_plot.setLabel('left', 'میانگین انحراف کل روز (%)')
        self.best_days_plot.setLabel('bottom', 'تاریخ')

        self.worst_days_plot = pg.PlotWidget(title="بدترین روزها (بیشترین میانگین انحراف)")
        self.worst_days_plot.setBackground('w')
        self.worst_days_plot.showGrid(x=True, y=True, alpha=0.3)
        self.worst_days_plot.setLabel('left', 'میانگین انحراف کل روز (%)')
        self.worst_days_plot.setLabel('bottom', 'تاریخ')

        self.days_table = QTableWidget()
        self.days_table.cellClicked.connect(self.show_day_details)
        self.days_table.setColumnCount(5)
        self.days_table.setHorizontalHeaderLabels([
            "تاریخ", "میانگین انحراف %", "تعداد نمونه", "تعداد عناصر", "وضعیت"
        ])
        days_header = self.days_table.horizontalHeader()
        days_header.setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.days_table.setStyleSheet("font-size: 13px;")

        days_layout.addWidget(self.best_days_plot, 0, 0)
        days_layout.addWidget(self.worst_days_plot, 0, 1)
        days_layout.addWidget(self.days_table, 1, 0, 1, 2)

        # اضافه کردن تب‌ها
        self.tab_widget.addTab(elements_tab, "آمار عناصر")
        self.tab_widget.addTab(days_tab, "آمار روزها")
        
        layout.addWidget(self.tab_widget, stretch=1)

        # اتصالات
        self.period_combo.currentTextChanged.connect(self.update_stats)
        refresh_btn.clicked.connect(self.load_data)
        export_btn.clicked.connect(self.export_to_excel)

    def load_data(self):
        self.progress.setVisible(True)
        self.progress.setValue(10)

        db_path = CRM_DATA_PATH  # یا از parent بگیر
        if self.parent and hasattr(self.parent, "ver_db_path"):
            db_path = self.parent.ver_db_path

        self.thread = DataLoaderThread(db_path)
        self.thread.data_loaded.connect(self.on_data_loaded)
        self.thread.progress_updated.connect(self.progress.setValue)
        self.thread.error_occurred.connect(lambda msg: QMessageBox.critical(self, "خطا", msg))
        self.thread.finished.connect(lambda: self.progress.setVisible(False))
        self.thread.start()

    def on_data_loaded(self, crm_df, blank_df):
        self.crm_df = crm_df.copy()
        self.blank_df = blank_df.copy()

        # نرمال‌سازی CRM ID
        self.crm_df['norm_crm_id'] = self.crm_df['crm_id'].apply(normalize_crm_id)
        self.crm_df = self.crm_df[self.crm_df['norm_crm_id'].isin(CRM_IDS)].copy()

        self.crm_df['date_str'] = self.crm_df['date'].astype(str)

        print(f"داده لود شد → {len(self.crm_df)} رکورد")

        # مهم: حالا که داده هست، تب Boxplot رو اضافه کن
        if not hasattr(self, 'boxplot_tab_added'):
            self.add_boxplot_tab()
            self.boxplot_tab_added = True  # فقط یک بار اضافه بشه

        self.update_stats()

    def get_verification_value(self, crm_id, element_base):
        key = f"{crm_id}_{element_base}"
        if key in self.verification_cache:
            return self.verification_cache[key]

        try:
            conn = get_db_connection()
            cursor = conn.cursor()
            table = "oreas_hs j" if "oreas" in crm_id.lower() else "pivot_crm"
            cursor.execute(f'SELECT "{element_base}" FROM "{table}" WHERE "CRM ID" LIKE ?', (f"%{crm_id}%",))
            row = cursor.fetchone()
            if row and row[0] not in (None, '', 'N/A'):
                val = float(row[0])
                self.verification_cache[key] = val
                return val
        except Exception as e:
            print(f"خطا در دریافت مقدار مرجع: {e}")
        self.verification_cache[key] = None
        return None

    def update_stats(self):
        if self.crm_df.empty:
            return

        df = self.crm_df.copy()
        df['element_base'] = df['element'].apply(lambda x: x.split()[0] if isinstance(x, str) and ' ' in x else x)

        # دوره زمانی (جلالی)
        period = self.period_combo.currentText()
        if period == "روزانه":
            df['period'] = df['date_str']
        elif period == "هفتگی":

            df['period'] = df['date_str'].apply(get_week_in_month)
        else:  # ماهانه
            df['period'] = df['date_str'].apply(lambda x: x[:7] if '/' in x else x[:7].replace('-', '/'))

        self.crm_df['period'] = df['period']
        # === آمار عناصر (نیاز به مقدار مرجع داره) ===
        df_with_ref = df.copy()
        df_with_ref['ref_value'] = df_with_ref.apply(
            lambda row: self.get_verification_value(row['norm_crm_id'], row['element_base']), axis=1
        )
        df_with_ref = df_with_ref.dropna(subset=['ref_value']).copy()
        df_with_ref['deviation'] = (abs(df_with_ref['value'] - df_with_ref['ref_value']) / df_with_ref['ref_value']) * 100

        stats = df_with_ref.groupby(['period', 'element_base', 'norm_crm_id']).agg(
            avg_dev=('deviation', 'mean'),
            count=('id', 'count')
        ).reset_index()

        if not stats.empty:
            best = stats.loc[stats.groupby('period')['avg_dev'].idxmin()].copy()
            worst = stats.loc[stats.groupby('period')['avg_dev'].idxmax()].copy()

            # جدول و نمودار عناصر (همون قبلی)
            self.table.setRowCount(0)
            result = pd.concat([best, worst]).sort_values('period').reset_index(drop=True)

            for idx, row in result.iterrows():
                self.table.insertRow(self.table.rowCount())
                self.table.setItem(self.table.rowCount()-1, 0, QTableWidgetItem(row['period']))
                self.table.setItem(self.table.rowCount()-1, 1, QTableWidgetItem(row['element_base']))
                self.table.setItem(self.table.rowCount()-1, 2, QTableWidgetItem(f"{row['avg_dev']:.2f}%"))
                self.table.setItem(self.table.rowCount()-1, 3, QTableWidgetItem(str(row['count'])))

                # وضعیت درست — مقایسه با دوره و مقدار انحراف
                current_period = row['period']
                is_best = (
                    (best['period'] == current_period) &
                    (abs(best['avg_dev'] - row['avg_dev']) < 1e-6)
                ).any()

                status_text = "بهترین" if is_best else "بدترین"
                status_item = QTableWidgetItem(status_text)
                
                # رنگ‌بندی وضعیت
                if is_best:
                    status_item.setBackground(QColor("#d1fae5"))  # سبز روشن
                    status_item.setForeground(QColor("#065f46"))
                else:
                    status_item.setBackground(QColor("#fee2e2"))  # قرمز روشن
                    status_item.setForeground(QColor("#991b1b"))

                self.table.setItem(self.table.rowCount()-1, 4, status_item)
                self.table.setItem(self.table.rowCount()-1, 5, QTableWidgetItem(row['norm_crm_id']))

                # نمودارهای عناصر
                self.best_plot.clear()
                self.worst_plot.clear()
                all_periods = sorted(set(best['period'].tolist() + worst['period'].tolist()), key=jalali_to_number)
                p_to_x = lambda p: all_periods.index(p)

            if not best.empty:
                x = [p_to_x(p) for p in best['period']]
                h = best['avg_dev'].tolist()
                bars = pg.BarGraphItem(x=x, height=h, width=0.6, brush='#22c55e', pen='#16a34a')
                self.best_plot.addItem(bars)
                for xb, val, row in zip(x, h, best.itertuples()):
                    text = pg.TextItem(f"{row.element_base}\n{row.period}\n{val:.1f}%", color='black', anchor=(0.5, 1))
                    text.setFont(QFont("Tahoma", 9, weight=75))
                    text.setPos(xb, val + (max(h)*0.07 if h else 0))
                    self.best_plot.addItem(text)

            if not worst.empty:
                x = [p_to_x(p) for p in worst['period']]
                h = worst['avg_dev'].tolist()
                bars = pg.BarGraphItem(x=x, height=h, width=0.6, brush='#ef4444', pen='#dc2626')
                self.worst_plot.addItem(bars)
                for xb, val, row in zip(x, h, worst.itertuples()):
                    text = pg.TextItem(f"{row.element_base}\n{row.period}\n{val:.1f}%", color='white', anchor=(0.5, 1))
                    text.setFont(QFont("Tahoma", 9, weight=75))
                    text.setPos(xb, val + (max(h)*0.07 if h else 0))
                    self.worst_plot.addItem(text)

            ticks = {i: p for i, p in enumerate(all_periods)}
            self.best_plot.getAxis('bottom').setTicks([ticks.items()])
            self.worst_plot.getAxis('bottom').setTicks([ticks.items()])

        # === آمار روزهای کاری (حتی بدون ref_value) ===
        self.update_days_stats(df)  # df اصلی بدون حذف رکورد

    def update_days_stats(self, df):
        """محاسبه آمار روزها — بدون نیاز به ref_value"""
        # فقط از value استفاده می‌کنیم، نه ref_value
        # میانگین انحراف نسبی از میانگین کل عناصر در اون روز
        days_stats = df.groupby('date_str').agg(
            avg_value=('value', 'mean'),
            sample_count=('id', 'count'),
            element_count=('element_base', 'nunique')
        ).reset_index()

        # محاسبه انحراف نسبی از میانگین کل (برای رتبه‌بندی روزها)
        overall_mean = df['value'].mean()
        if overall_mean == 0:
            overall_mean = 1  # جلوگیری از تقسیم بر صفر

        days_stats['dev_from_mean'] = abs(days_stats['avg_value'] - overall_mean) / overall_mean * 100

        if days_stats.empty:
            return

        # مرتب‌سازی بر اساس تاریخ
        days_stats = days_stats.sort_values('date_str', key=lambda x: x.map(jalali_to_number))

        # ۱۰ روز برتر و بدترین
        top_n = 10
        best_days = days_stats.nsmallest(top_n, 'dev_from_mean')
        worst_days = days_stats.nlargest(top_n, 'dev_from_mean')

        # جدول روزها
        self.days_table.setRowCount(0)
        for _, row in pd.concat([best_days, worst_days]).sort_values('dev_from_mean').iterrows():
            self.days_table.insertRow(self.days_table.rowCount())
            self.days_table.setItem(self.days_table.rowCount()-1, 0, QTableWidgetItem(row['date_str']))
            self.days_table.setItem(self.days_table.rowCount()-1, 1, QTableWidgetItem(f"{row['dev_from_mean']:.2f}%"))
            self.days_table.setItem(self.days_table.rowCount()-1, 2, QTableWidgetItem(str(row['sample_count'])))
            self.days_table.setItem(self.days_table.rowCount()-1, 3, QTableWidgetItem(str(row['element_count'])))
            self.days_table.setItem(self.days_table.rowCount()-1, 4, QTableWidgetItem("بهترین روز" if row['dev_from_mean'] <= days_stats['dev_from_mean'].median() else "بدترین روز"))

        # نمودارها
        self.best_days_plot.clear()
        self.worst_days_plot.clear()

        if not best_days.empty:
            x = list(range(len(best_days)))
            h = best_days['dev_from_mean'].tolist()
            bars = pg.BarGraphItem(x=x, height=h, width=0.6, brush='#22c55e')
            self.best_days_plot.addItem(bars)
            for i, row in best_days.iterrows():
                text = pg.TextItem(f"{row['date_str']}\n{row['dev_from_mean']:.1f}%", color='black', anchor=(0.5, 1))
                text.setFont(QFont("Tahoma", 9, weight=75))
                text.setPos(i, row['dev_from_mean'] + max(h)*0.05)
                self.best_days_plot.addItem(text)
            self.best_days_plot.getAxis('bottom').setTicks([{i: d for i, d in enumerate(best_days['date_str'])}.items()])

        if not worst_days.empty:
            x = list(range(len(worst_days)))
            h = worst_days['dev_from_mean'].tolist()
            bars = pg.BarGraphItem(x=x, height=h, width=0.6, brush='#ef4444')
            self.worst_days_plot.addItem(bars)
            for i, row in worst_days.iterrows():
                text = pg.TextItem(f"{row['date_str']}\n{row['dev_from_mean']:.1f}%", color='white', anchor=(0.5, 1))
                text.setFont(QFont("Tahoma", 9, weight=75))
                text.setPos(i, row['dev_from_mean'] + max(h)*0.05)
                self.worst_days_plot.addItem(text)
            self.worst_days_plot.getAxis('bottom').setTicks([{i: d for i, d in enumerate(worst_days['date_str'])}.items()])

        self.best_days_plot.enableAutoRange()
        self.worst_days_plot.enableAutoRange()

    def export_to_excel(self):
        current_tab = self.tab_widget.currentIndex()
        
        if current_tab == 0:  # تب عناصر
            if self.table.rowCount() == 0:
                return QMessageBox.warning(self, "هشدار", "داده‌ای برای خروجی وجود ندارد")

            path, _ = QFileDialog.getSaveFileName(
                self, "ذخیره گزارش آماری عناصر", "گزارش_آماری_عملکرد_عناصر.xlsx", "Excel Files (*.xlsx)"
            )
            if path:
                data = []
                for r in range(self.table.rowCount()):
                    row = [self.table.item(r, c).text() for c in range(6)]
                    data.append(row)
                pd.DataFrame(data, columns=["دوره", "عنصر", "میانگین انحراف %", "تعداد", "وضعیت", "CRM ID"]) \
                  .to_excel(path, index=False)
                QMessageBox.information(self, "موفقیت", f"گزارش با موفقیت ذخیره شد:\n{path}")
        
        else:  # تب روزها
            if self.days_table.rowCount() == 0:
                return QMessageBox.warning(self, "هشدار", "داده‌ای برای خروجی وجود ندارد")

            path, _ = QFileDialog.getSaveFileName(
                self, "ذخیره گزارش آماری روزها", "گزارش_آماری_روزها.xlsx", "Excel Files (*.xlsx)"
            )
            if path:
                data = []
                for r in range(self.days_table.rowCount()):
                    row = [self.days_table.item(r, c).text() for c in range(5)]
                    data.append(row)
                pd.DataFrame(data, columns=["تاریخ", "میانگین انحراف %", "تعداد نمونه", "تعداد عناصر", "وضعیت"]) \
                  .to_excel(path, index=False)
                QMessageBox.information(self, "موفقیت", f"گزارش با موفقیت ذخیره شد:\n{path}")

    def show_day_details(self, row, column):
        """وقتی روی یک سطر از جدول روزها کلیک شد"""
        if column < 0 or row < 0:
            return

        date_item = self.days_table.item(row, 0)
        if not date_item:
            return
        selected_date = date_item.text()

        # فیلتر کردن تمام رکوردهای آن روز
        day_data = self.crm_df[self.crm_df['date_str'] == selected_date].copy()

        if day_data.empty:
            QMessageBox.information(self, "اطلاعات", f"هیچ داده‌ای برای تاریخ {selected_date} یافت نشد")
            return

        # محاسبه انحراف برای هر عنصر
        day_data['element_base'] = day_data['element'].apply(lambda x: x.split()[0] if isinstance(x, str) and ' ' in x else x)
        day_data['ref_value'] = day_data.apply(
            lambda r: self.get_verification_value(r['norm_crm_id'], r['element_base']), axis=1
        )

        # حذف عناصری که مقدار مرجع ندارن
        day_data = day_data.dropna(subset=['ref_value']).copy()
        if day_data.empty:
            QMessageBox.information(self, "هشدار", f"هیچ عنصری با مقدار مرجع معتبر برای تاریخ {selected_date} وجود ندارد")
            return

        day_data['deviation'] = abs(day_data['value'] - day_data['ref_value']) / day_data['ref_value'] * 100

        # مرتب‌سازی از بدترین به بهترین
        day_data = day_data.sort_values('deviation', ascending=False).reset_index(drop=True)

        # ایجاد پنجره جدید
        dialog = QDialog(self)
        dialog.setWindowTitle(f"جزئیات عناصر - {selected_date}")
        dialog.resize(1000, 600)
        dialog.setStyleSheet("background: white; font-family: Tahoma;")

        layout = QVBoxLayout(dialog)

        title = QLabel(f"عملکرد عناصر در تاریخ: {selected_date}")
        title.setStyleSheet("font-size: 18px; font-weight: bold; color: #1e293b; padding: 10px;")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(title)

        table = QTableWidget()
        table.setColumnCount(7)
        table.setHorizontalHeaderLabels([
            "ردیف", "عنصر", "مقدار اندازه‌گیری", "مقدار مرجع", "انحراف %", "CRM ID", "وضعیت"
        ])
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        table.setStyleSheet("""
            QTableWidget { font-size: 13px; }
            QHeaderView::section { background: #6366f1; color: white; font-weight: bold; }
        """)

        # پر کردن جدول
        for i, r in day_data.iterrows():
            table.insertRow(i)
            table.setItem(i, 0, QTableWidgetItem(str(i+1)))
            table.setItem(i, 1, QTableWidgetItem(r['element_base']))
            table.setItem(i, 2, QTableWidgetItem(f"{r['value']:.6f}"))
            table.setItem(i, 3, QTableWidgetItem(f"{r['ref_value']:.6f}"))
            table.setItem(i, 4, QTableWidgetItem(f"{r['deviation']:.2f}%"))

            crm_item = QTableWidgetItem(r['norm_crm_id'])
            table.setItem(i, 5, crm_item)

            # وضعیت با رنگ
            status_item = QTableWidgetItem()
            if r['deviation'] < 5:
                status_item.setText("عالی")
                status_item.setBackground(QColor("#d1fae5"))
                status_item.setForeground(QColor("#065f46"))
            elif r['deviation'] < 15:
                status_item.setText("خوب")
                status_item.setBackground(QColor("#fef3c7"))
                status_item.setForeground(QColor("#92400e"))
            else:
                status_item.setText("نیاز به بررسی")
                status_item.setBackground(QColor("#fee2e2"))
                status_item.setForeground(QColor("#991b1b"))

            table.setItem(i, 6, status_item)

        layout.addWidget(table)

        # دکمه بستن
        close_btn = QPushButton("بستن")
        close_btn.setStyleSheet("""
            QPushButton { padding: 10px 30px; background: #ef4444; color: white; border-radius: 8px; font-weight: bold; }
            QPushButton:hover { background: #dc2626; }
        """)
        close_btn.clicked.connect(dialog.reject)
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        btn_layout.addWidget(close_btn)
        btn_layout.addStretch()
        layout.addLayout(btn_layout)

        dialog.exec()

    def show_element_period_details(self, row, column):
        """نمایش جزئیات یک عنصر در یک دوره خاص"""
        if row < 0:
            return

        period_item = self.table.item(row, 0)
        element_item = self.table.item(row, 1)
        if not period_item or not element_item:
            return

        period = period_item.text()
        element_base = element_item.text()

        # استفاده از ستون period که همیشه وجود داره
        filtered = self.crm_df[
            (self.crm_df['period'] == period) &
            (self.crm_df['element'].str.startswith(element_base + ' ', na=False) |
             (self.crm_df['element'] == element_base))
        ].copy()

        if filtered.empty:
            QMessageBox.information(self, "جزئیات", f"هیچ داده‌ای برای این عنصر در این دوره یافت نشد")
            return

        # محاسبه مقدار مرجع
        filtered['ref_value'] = filtered.apply(
            lambda r: self.get_verification_value(r['norm_crm_id'], element_base), axis=1
        )
        filtered = filtered.dropna(subset=['ref_value']).copy()
        if filtered.empty:
            QMessageBox.warning(self, "هشدار", f"مقدار مرجع معتبری برای عنصر {element_base} وجود ندارد")
            return

        filtered['deviation'] = abs(filtered['value'] - filtered['ref_value']) / filtered['ref_value'] * 100
        filtered = filtered.sort_values('deviation', ascending=False).reset_index(drop=True)

        # پنجره جزئیات
        dialog = QDialog(self)
        dialog.setWindowTitle(f"جزئیات {element_base} — {period}")
        dialog.resize(1050, 620)

        layout = QVBoxLayout(dialog)

        title = QLabel(f"تمام اندازه‌گیری‌های <b>{element_base}</b> در دوره <b>{period}</b>")
        title.setStyleSheet("font-size: 19px; font-weight: bold; padding: 15px; background: #f8fafc; border-bottom: 2px solid #6366f1;")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(title)

        table = QTableWidget()
        table.setRowCount(len(filtered))
        table.setColumnCount(8)
        table.setHorizontalHeaderLabels([
            "ردیف", "تاریخ", "CRM ID", "عنصر کامل", "مقدار", "مرجع", "انحراف %", "وضعیت"
        ])
        table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)

        for i, r in filtered.iterrows():
            table.setItem(i, 0, QTableWidgetItem(str(i+1)))
            table.setItem(i, 1, QTableWidgetItem(r['date_str']))
            table.setItem(i, 2, QTableWidgetItem(r['norm_crm_id']))
            table.setItem(i, 3, QTableWidgetItem(r['element']))
            table.setItem(i, 4, QTableWidgetItem(f"{r['value']:.6f}"))
            table.setItem(i, 5, QTableWidgetItem(f"{r['ref_value']:.6f}"))
            table.setItem(i, 6, QTableWidgetItem(f"{r['deviation']:.2f}%"))

            status = QTableWidgetItem()
            if r['deviation'] < 5:
                status.setText("عالی")
                status.setBackground(QColor("#d1fae5"))
            elif r['deviation'] < 15:
                status.setText("خوب")
                status.setBackground(QColor("#fef3c7"))
            else:
                status.setText("نیاز به بررسی")
                status.setBackground(QColor("#fee2e2"))
            table.setItem(i, 7, status)

        layout.addWidget(table)

        close = QPushButton("بستن")
        close.clicked.connect(dialog.close)
        close.setStyleSheet("padding: 10px 40px; background: #ef4444; color: white; border-radius: 8px; font-weight: bold;")
        btn_box = QHBoxLayout()
        btn_box.addStretch()
        btn_box.addWidget(close)
        btn_box.addStretch()
        layout.addLayout(btn_box)

        dialog.exec()

    def add_boxplot_tab(self):
        """اضافه کردن تب Boxplot مقایسه عناصر در یک CRM"""
        boxplot_tab = QWidget()
        layout = QVBoxLayout(boxplot_tab)

        # هدر
        header = QHBoxLayout()
        header.addWidget(QLabel("مقایسه توزیع عناصر در یک CRM:"))
        
        self.crm_combo_boxplot = QComboBox()
        self.crm_combo_boxplot.addItems(["همه CRMها"] + sorted(self.crm_df['norm_crm_id'].unique().tolist()))
        self.crm_combo_boxplot.setCurrentText("258")  # پیش‌فرض
        self.crm_combo_boxplot.currentTextChanged.connect(self.update_boxplot)
        header.addWidget(self.crm_combo_boxplot)
        
        header.addStretch()
        layout.addLayout(header)

        # نمودار
        self.boxplot_widget = pg.PlotWidget(title="Boxplot مقایسه انحراف عناصر از مقدار مرجع")
        self.boxplot_widget.setBackground('w')
        self.boxplot_widget.showGrid(x=True, y=True, alpha=0.3)
        self.boxplot_widget.setLabel('left', 'انحراف از مقدار مرجع (%)')
        self.boxplot_widget.setLabel('bottom', 'عنصر')
        layout.addWidget(self.boxplot_widget)

        # اضافه کردن تب
        self.tab_widget.addTab(boxplot_tab, "Boxplot عناصر")

    def update_boxplot(self):
        """Boxplot توزیع مقدار اندازه‌گیری عناصر — با خط Ref Value متفاوت برای هر عنصر"""
        self.boxplot_widget.clear()

        # پاک کردن Legend قبلی به صورت امن
        if hasattr(self, 'boxplot_legend') and self.boxplot_legend is not None:
            try:
                self.boxplot_widget.removeItem(self.boxplot_legend)
            except:
                pass

        selected_crm = self.crm_combo_boxplot.currentText()
        if selected_crm == "همه CRMها":
            df = self.crm_df.copy()
        else:
            df = self.crm_df[self.crm_df['norm_crm_id'] == selected_crm].copy()

        if df.empty:
            self.boxplot_widget.setTitle("داده‌ای برای نمایش وجود ندارد")
            return

        df['element_base'] = df['element'].apply(lambda x: x.split()[0] if isinstance(x, str) and ' ' in x else x)
        df['ref'] = df.apply(lambda r: self.get_verification_value(r['norm_crm_id'], r['element_base']), axis=1)
        df = df.dropna(subset=['ref']).copy()
        if df.empty:
            self.boxplot_widget.setTitle("هیچ عنصری با مقدار مرجع معتبر یافت نشد")
            return

        # گروه‌بندی بر اساس عنصر (برای مقدار اندازه‌گیری شده)
        grouped = df.groupby('element_base')['value'].apply(list)
        valid = grouped[grouped.apply(len) >= 3]
        if valid.empty:
            self.boxplot_widget.setTitle("داده کافی برای رسم Boxplot نیست (حداقل ۳ تکرار نیاز است)")
            return

        # مرتب‌سازی بر اساس میانه
        elements = sorted(valid.index, key=lambda x: np.median(valid[x]))
        x_pos = np.arange(len(elements))

        # رسم دستی Boxplot
        for i, element in enumerate(elements):
            values = sorted(valid[element])
            q1 = np.percentile(values, 25)
            q3 = np.percentile(values, 75)
            median = np.median(values)
            iqr = q3 - q1
            lower = max(values[0], q1 - 1.5 * iqr)
            upper = min(values[-1], q3 + 1.5 * iqr)
            x = x_pos[i]

            # جعبه
            box = pg.QtWidgets.QGraphicsRectItem(x - 0.4, q1, 0.8, q3 - q1)
            box.setPen(pg.mkPen('#374151', width=2))
            box.setBrush(pg.mkBrush('#e2e8f0'))
            self.boxplot_widget.addItem(box)

            # خط میانه
            median_line = pg.QtWidgets.QGraphicsLineItem(x - 0.4, median, x + 0.4, median)
            median_line.setPen(pg.mkPen('#1f2937', width=4))
            self.boxplot_widget.addItem(median_line)

            # خطوط رابط
            line = pg.QtWidgets.QGraphicsLineItem(x, lower, x, upper)
            line.setPen(pg.mkPen('#374151', width=2))
            self.boxplot_widget.addItem(line)

            # کپ‌ها
            cap_low = pg.QtWidgets.QGraphicsLineItem(x - 0.3, lower, x + 0.3, lower)
            cap_high = pg.QtWidgets.QGraphicsLineItem(x - 0.3, upper, x + 0.3, upper)
            for cap in (cap_low, cap_high):
                cap.setPen(pg.mkPen('#374151', width=3))
                self.boxplot_widget.addItem(cap)

            # نقاط پرت
            outliers = [v for v in values if v < lower or v > upper]
            if outliers:
                # ساخت ScatterPlotItem جداگانه برای داشتن کلیک
                scatter = pg.ScatterPlotItem(
                    x=[x] * len(outliers),
                    y=outliers,
                    size=12,
                    brush='#ef4444',
                    pen=pg.mkPen('white', width=2),
                    hoverable=True,
                    hoverBrush='#dc2626',
                    hoverSize=15
                )
                self.boxplot_widget.addItem(scatter)

                # اتصال کلیک به نمایش جزئیات
                scatter.sigClicked.connect(lambda _, pts: self.show_outlier_detail(pts, element, selected_crm))

            # خط Ref Value متفاوت برای هر عنصر (قرمز و افقی)
            ref_value = df[df['element_base'] == element]['ref'].iloc[0]  # مقدار مرجع هر عنصر
            ref_line = pg.QtWidgets.QGraphicsLineItem(x - 0.5, ref_value, x + 0.5, ref_value)
            ref_line.setPen(pg.mkPen('#ef4444', width=3, style=Qt.PenStyle.DashLine))
            self.boxplot_widget.addItem(ref_line)

            # متن برای Ref Value
            ref_text = pg.TextItem(f"Ref: {ref_value:.2f}", color='#991b1b', anchor=(0, 0))
            ref_text.setPos(x + 0.2, ref_value + 0.05)
            ref_text.setFont(QFont("Tahoma", 9))
            self.boxplot_widget.addItem(ref_text)

        # Legend ساده و ثابت — فقط توضیحات
        legend = pg.LegendItem(offset=(10, 10))
        legend.setParentItem(self.boxplot_widget.getViewBox())
        legend.setBrush(pg.mkBrush(255, 255, 255, 240))
        legend.setPen(pg.mkPen('#cbd5e1', width=1))

        legend.addItem(pg.ScatterPlotItem(pen=None, brush='#e2e8f0', size=15, symbol='s'), "جعبه: محدوده ۵۰٪ میانی داده‌ها")
        legend.addItem(pg.PlotDataItem(pen=pg.mkPen('#1f2937', width=5)), "خط وسط: مقدار میانه")
        legend.addItem(pg.PlotDataItem(pen=pg.mkPen('#374151', width=3)), "خطوط رابط: محدوده طبیعی داده‌ها")
        legend.addItem(pg.ScatterPlotItem(pen=None, brush='#ef4444', size=10, symbol='o'), "نقطه قرمز: داده غیرعادی")
        legend.addItem(pg.PlotDataItem(pen=pg.mkPen('#ef4444', width=2, style=Qt.PenStyle.DashLine)), "خط قرمز: مقدار مرجع (Ref)")

        self.boxplot_legend = legend

        # محور X — اسم عناصر
        self.boxplot_widget.getAxis('bottom').setTicks([[(i, el) for i, el in enumerate(elements)]])
        self.boxplot_widget.getAxis('bottom').setStyle(tickFont=QFont("Tahoma", 11, weight=75))

        # عنوان
        crm_text = "همه CRMها" if selected_crm == "همه CRMها" else f"CRM {selected_crm}"
        self.boxplot_widget.setTitle(f"توزیع مقدار اندازه‌گیری عناصر — {crm_text}", size='16pt', color='#1e293b')

        # تنظیمات نهایی
        self.boxplot_widget.setLabel('left', 'مقدار اندازه‌گیری شده')
        self.boxplot_widget.setLabel('bottom', 'عنصر')
        self.boxplot_widget.showGrid(x=True, y=True, alpha=0.3)
        self.boxplot_widget.setBackground('w')
        self.boxplot_widget.enableAutoRange()

    def show_outlier_detail(self, points, element, crm_id):
        """نمایش جزئیات کامل وقتی روی نقطه پرت کلیک شد"""
        if not points:
            return

        point = points[0]  # فقط اولین نقطه
        y_value = point.pos().y()

        # پیدا کردن دقیق آن رکورد در دیتافریم اصلی
        filtered = self.crm_df[
            (self.crm_df['element'].str.startswith(element + ' ', na=False) |
             (self.crm_df['element'] == element)) &
            (self.crm_df['norm_crm_id'] == crm_id)
        ].copy()

        # محاسبه انحراف دقیق
        filtered['ref'] = filtered.apply(lambda r: self.get_verification_value(r['norm_crm_id'], element), axis=1)
        filtered['dev'] = abs(filtered['value'] - filtered['ref']) / filtered['ref'] * 100

        # پیدا کردن رکوردی که دقیقاً با این مقدار انحراف
        record = filtered[abs(filtered['dev'] - y_value) < 0.01]
        if record.empty:
            record = filtered.iloc[0:1]  # اگر دقیق پیدا نشد، اولین رو نشون بده

        r = record.iloc[0]

        # پنجره جزئیات
        msg = QMessageBox(self)
        msg.setWindowTitle("جزئیات داده غیرعادی")
        msg.setIcon(QMessageBox.Icon.Warning)
        msg.setStyleSheet("""
            QMessageBox { background: #fefce8; font-family: Tahoma; }
            QLabel { color: #92400e; font-size: 14px; }
        """)

        details = f"""
        <b style='color:#dc2626'>داده غیرعادی شناسایی شد!</b><br><br>
        <b>عنصر:</b> {element}<br>
        <b>CRM ID:</b> {r['norm_crm_id']}<br>
        <b>تاریخ:</b> {r['date_str']}<br>
        <b>مقدار اندازه‌گیری شده:</b> {r['value']:.6f}<br>
        <b>مقدار مرجع:</b> {r['ref']:.6f}<br>
        <b>انحراف:</b> <span style='color:#dc2626; font-weight:bold'>{r['dev']:.2f}%</span><br><br>
        <i>این داده خارج از محدوده طبیعی است و نیاز به بررسی دارد.</i>
        """

        msg.setText(details)
        msg.exec()