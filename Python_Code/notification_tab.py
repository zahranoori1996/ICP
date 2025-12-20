# screens/notifications/notification_tab.py
import sqlite3
import pandas as pd
import logging
from PyQt6.QtWidgets import (
    QVBoxLayout, QWidget, QLabel, QPushButton,
    QTableView, QHeaderView, QHBoxLayout, QMessageBox
)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QStandardItemModel, QStandardItem, QColor

from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QLabel, QTabWidget, QMessageBox,
    QProgressDialog, QSystemTrayIcon, QMenu
)
from PyQt6.QtCore import QRunnable,QThreadPool,QObject,pyqtSignal,QTimer
from PyQt6.QtGui import QIcon,QFont
import time
import datetime
import threading
from db.db import get_db_connection,resource_path
logger = logging.getLogger(__name__)

class NotificationTab(QWidget):
    def __init__(self, app):
        super().__init__()
        self.app = app
        self.user_id = app.user_id
        self.setup_ui()
        self.load_notifications()

    def setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(10)

        header = QHBoxLayout()
        refresh_btn = QPushButton("Refresh")
        refresh_btn.clicked.connect(self.load_notifications)
        header.addWidget(refresh_btn)
        self.notif_count_label = QLabel("")
        self.notif_count_label.setStyleSheet("color: #dc2626; font-weight: bold;")
        header.addWidget(self.notif_count_label)
        header.addStretch()
        layout.addLayout(header)

        self.notif_table = QTableView()
        self.notif_table.setSelectionBehavior(QTableView.SelectionBehavior.SelectRows)
        self.notif_table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        layout.addWidget(self.notif_table)

        mark_read_btn = QPushButton("Mark as Read")
        mark_read_btn.clicked.connect(self.mark_selected_as_read)
        layout.addWidget(mark_read_btn)

    def load_notifications(self):
            """بارگذاری نوتیفیکیشن‌ها از دیتابیس"""
            try:
                conn =get_db_connection()
                query = """
                    SELECT 
                        n.id,
                        n.message,
                        n.type,
                        n.created_at,
                        n.is_read,
                        -- فرستنده
                        COALESCE(
                            u_from.full_name,
                            u_from.username,
                            'System'
                        ) AS sender,
                        -- گیرنده
                        COALESCE(
                            u_to.full_name,
                            u_to.username,
                            'Unknown'
                        ) AS recipient
                    FROM notifications n
                    LEFT JOIN changes_log cl ON n.related_entity_id = cl.id AND n.type IN ('approval_needed', 'change_approved', 'change_rejected')
                    LEFT JOIN approvals a ON cl.id = a.change_id
                    LEFT JOIN users u_from ON 
                        (n.type = 'approval_needed' AND cl.user_id = u_from.id) OR
                        (n.type IN ('change_approved', 'change_rejected') AND a.approved_by = u_from.id)
                    LEFT JOIN users u_to ON n.user_id = u_to.id
                    WHERE n.user_id = ?
                    ORDER BY n.created_at DESC
                """
                df = pd.read_sql_query(query, conn, params=(self.user_id,))
                

                model = QStandardItemModel()
                model.setHorizontalHeaderLabels([
                    "ID", "Message", "Type", "Time", "Read", "From", "To"
                ])

                unread_count = 0
                for _, row in df.iterrows():
                    read_status = "Yes" if row['is_read'] else "No"
                    if not row['is_read']:
                        unread_count += 1

                    items = [
                        QStandardItem(str(row['id'])),
                        QStandardItem(row['message']),
                        QStandardItem(row['type'].replace('_', ' ').title()),
                        QStandardItem(row['created_at'][:16].replace('T', ' ')),
                        QStandardItem(read_status),
                        QStandardItem(row['sender']),
                        QStandardItem(row['recipient'])
                    ]

                    # رنگ قرمز + بولد برای خوانده‌نشده
                    if not row['is_read']:
                        for item in items:
                            item.setForeground(QColor("#dc2626"))
                            item.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))

                    model.appendRow(items)

                self.notif_table.setModel(model)
                self.notif_table.resizeColumnsToContents()
                self.notif_count_label.setText(f"{unread_count} Unread")

            except Exception as e:
                logger.error(f"Notifications load error: {e}")
                QMessageBox.critical(self, "Error", f"Failed to load notifications:\n{e}")

    def mark_selected_as_read(self):
        """علامت‌گذاری نوتیفیکیشن انتخاب‌شده به عنوان خوانده‌شده"""
        index = self.notif_table.currentIndex()
        if not index.isValid():
            QMessageBox.warning(self, "Warning", "Please select a notification to mark as read.")
            return

        notif_id = self.notif_table.model().index(index.row(), 0).data()
        try:
            conn =get_db_connection()
            cur = conn.cursor()
            cur.execute("UPDATE notifications SET is_read = 1 WHERE id = ?", (notif_id,))
            conn.commit()
            

            self.load_notifications()
            QMessageBox.information(self, "Success", "Notification marked as read.")

            # رفرش تب مدیریت (اگر باز باشد)
            if hasattr(self.app, 'management_tab') and self.app.management_tab:
                self.app.management_tab.load_notifications()

        except Exception as e:
            logger.error(f"Mark as read failed: {e}")
            QMessageBox.critical(self, "Error", f"Failed to update:\n{e}")


    def start_notification_checker(self):
        self.last_notif_check = 0
        self.notification_thread = threading.Thread(target=self.check_notifications_loop, daemon=True)
        self.notification_thread.start()

    def check_notifications_loop(self):
        while True:
            if self.app.user_role == 'lab_manager':
                self.check_new_notifications()
            time.sleep(10)

    def check_new_notifications(self):
        try:
            conn = get_db_connection()
            cur = conn.cursor()
            cur.execute("""
                SELECT id, message FROM notifications 
                WHERE user_id = ? AND is_read = 0 AND created_at > ?
                ORDER BY created_at DESC LIMIT 1
            """, (self.user_id, datetime.fromtimestamp(self.last_notif_check).isoformat()))
            row = cur.fetchone()
            

            if row:
                notif_id, message = row
                self.last_notif_check = time.time()
                self.tray_icon.showMessage("RASF - New Change", message, QSystemTrayIcon.MessageIcon.Information, 10000)
                self.show_notification_popup(message, notif_id)
        except Exception as e:
            logger.error(f"Notification check error: {e}")

    def setup_system_tray(self):
        self.tray_icon = QSystemTrayIcon(self)
        self.tray_icon.setIcon(QIcon(resource_path("icons/app_icon.png")))
        tray_menu = QMenu()
        open_action = tray_menu.addAction("Open RASF")
        open_action.triggered.connect(lambda: self.showNormal())
        exit_action = tray_menu.addAction("Exit")
        exit_action.triggered.connect(QApplication.quit)
        self.tray_icon.setContextMenu(tray_menu)
        self.tray_icon.activated.connect(self.on_tray_activated)
        self.tray_icon.show()

    def on_tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self.showNormal()
            self.activateWindow()
            
    # ────────────────────────────────────────
    # Notifications
    # ────────────────────────────────────────
    def show_notification_popup(self, message, notif_id):
        msg = QMessageBox(self)
        msg.setIcon(QMessageBox.Icon.Information)
        msg.setWindowTitle("New Change Pending Approval")
        msg.setText(f"<b>{message}</b>")
        msg.setStandardButtons(QMessageBox.StandardButton.Ok)
        msg.buttonClicked.connect(lambda: self.mark_notification_read(notif_id))
        msg.exec()

    def mark_notification_read(self, notif_id):
        try:
            conn = get_db_connection()
            cur = conn.cursor()
            cur.execute("UPDATE notifications SET is_read = 1 WHERE id = ?", (notif_id,))
            conn.commit()
            

            if hasattr(self, 'management_tab') and self.management_tab:
                self.management_tab.load_notifications()
            if hasattr(self, 'notification_tab') and self.notification_tab:
                self.notification_tab.load_notifications()
        except Exception as e:
            logger.error(f"Mark read error: {e}")
