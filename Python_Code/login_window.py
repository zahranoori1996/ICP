# screens/login_window.py
import os
import sys
import sqlite3
from PyQt6.QtWidgets import *
from PyQt6.QtGui import QPixmap, QFont, QIcon, QColor
from PyQt6.QtCore import Qt, pyqtSignal, QTimer
from db.db import get_auth_db,resource_path_user,resource_path
from db.auth_db import init_auth_db
from styles.login_styles import login_style
from utils.var_main import AUTH_DB_PATH,LOGO_PNG_PATH
class LoginWindow(QWidget):
    login_successful = pyqtSignal(str, str, str, str)  # username, full_name, position, user_id

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("RASF - Login")
        self.setFixedSize(1000, 620)
        self.setWindowIcon(QIcon(resource_path_user("icon.png")))
        self.db_user = resource_path_user(AUTH_DB_PATH)
        self.remembered = False

        # فقط یکبار دیتابیس را راه‌اندازی کن
        # from db.db_initializer import init_db_schema
        # init_auth_db()

        # # #ایجاد کاربران پیش‌فرض
        self.create_default_users()

        # اگر کاربر قبلاً لاگین کرده بود
        if self.check_remembered_user():
            return

        self.init_ui()
        self.apply_light_style()

    def create_default_users(self):
        """ایجاد 6 کاربر پیش‌فرض با username و password از 1 تا 6"""
        default_users = [
            ("admin", "1", "Admin", "Admin", "admin"),
            ("lab1", "2", "Lab Manager 1", "Lab Manager", "lab_manager"),
            ("report1", "3", "Report Officer", "Report Officer", "report_manager"),
            ("operator1", "4", "Device Operator", "Device Operator", "device_operator"),
            ("viewer1", "5", "Viewer User", "Viewer", "viewer"),
            ("guest", "6", "Guest User", "Guest", "viewer"),
            ("qc1", "7", "QC Officer", "QC", "qc"),
        ]

        try:
            conn =get_auth_db()
            cur = conn.cursor()

            for username, password, full_name, position, role in default_users:
                cur.execute("""
                    INSERT OR IGNORE INTO users 
                    (username, password, full_name, position, role, remember_me, is_active)
                    VALUES (?, ?, ?, ?, ?, 0, 1)
                """, (username, password, full_name, position, role))

            conn.commit()
            
        except Exception as e:
            print(f"Failed to create default users: {e}")

    def check_remembered_user(self):
        try:
            conn =get_auth_db()
            cur = conn.cursor()
            cur.execute("""
                SELECT id, username, full_name, position, role 
                FROM users 
                WHERE remember_me = 1 AND is_active = 1 
                LIMIT 1
            """)
            user = cur.fetchone()
            

            if user:
                user_id, username, name, pos, role = user
                name = name or username.capitalize()
                pos = pos or "User"
                print(f"Auto-login: {name} ({username}) - Role: {role}")

                self.remembered = True
                QTimer.singleShot(0, lambda: self.login_successful.emit(username, name, pos, str(user_id)))
                return True
            return False
        except Exception as e:
            print(f"Auto-login failed: {e}")
            return False

    def init_ui(self):
        main_layout = QHBoxLayout(self)
        main_layout.setContentsMargins(0, 0, 0, 0)
        main_layout.setSpacing(0)

        # LEFT: Logo
        left_panel = QFrame()
        left_panel.setFixedWidth(420)
        left_layout = QVBoxLayout(left_panel)
        left_layout.setAlignment(Qt.AlignmentFlag.AlignCenter)

        self.logo = QLabel()
        logo_path = resource_path(LOGO_PNG_PATH)
        print(logo_path)
        if os.path.exists(logo_path):
            pixmap = QPixmap(logo_path).scaled(300, 300, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            self.logo.setPixmap(pixmap)
        else:
            self.logo.setText("RASF")
            self.logo.setFont(QFont("Segoe UI", 90, QFont.Weight.Bold))
            self.logo.setStyleSheet("color: #2c3e50;")
        self.logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
        left_layout.addStretch()
        left_layout.addWidget(self.logo)
        left_layout.addStretch()

        # RIGHT: Form (فقط لاگین)
        right_panel = QFrame()
        right_layout = QVBoxLayout(right_panel)
        right_layout.setContentsMargins(70, 70, 70, 70)
        right_layout.setSpacing(22)

        # فقط صفحه لاگین
        self.login_page = self.create_login_page()
        right_layout.addWidget(self.login_page)
        right_layout.addStretch()

        main_layout.addWidget(left_panel)
        main_layout.addWidget(right_panel, 1)

    def create_login_page(self):
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setSpacing(18)

        title = QLabel("Welcome Back")
        title.setFont(QFont("Segoe UI", 32, QFont.Weight.Bold))
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title.setStyleSheet("color: #2c3e50;")

        self.username = QLineEdit()
        self.username.setPlaceholderText("Username")
        self.username.setMinimumHeight(56)

        self.password = QLineEdit()
        self.password.setPlaceholderText("Password")
        self.password.setEchoMode(QLineEdit.EchoMode.Password)
        self.password.setMinimumHeight(56)

        login_btn = QPushButton("Sign In")
        login_btn.setMinimumHeight(60)
        login_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        login_btn.clicked.connect(self.handle_login)
        self.add_shadow(login_btn)

        guest_btn = QPushButton("Continue as Guest")
        guest_btn.setMinimumHeight(52)
        guest_btn.setObjectName("guestBtn")
        guest_btn.clicked.connect(self.guest_login)

        self.remember_me = QCheckBox("Remember Me")
        self.remember_me.setFont(QFont("Segoe UI", 11))
        self.remember_me.setStyleSheet("color: #475569;")
        self.remember_me.setChecked(True)

        layout.addWidget(title)
        layout.addSpacing(30)
        layout.addWidget(self.username)
        layout.addWidget(self.password)
        layout.addWidget(login_btn)
        layout.addSpacing(15)
        layout.addWidget(guest_btn)
        layout.addSpacing(10)
        layout.addWidget(self.remember_me, alignment=Qt.AlignmentFlag.AlignCenter)
        layout.addStretch()
        return page

    def add_shadow(self, widget):
        shadow = QGraphicsDropShadowEffect()
        shadow.setBlurRadius(25)
        shadow.setXOffset(0)
        shadow.setYOffset(8)
        shadow.setColor(QColor(0, 0, 0, 60))
        widget.setGraphicsEffect(shadow)

    def handle_login(self):
        username = self.username.text().strip()
        password = self.password.text().strip()
        remember = 1 if self.remember_me.isChecked() else 0

        if not username or not password:
            QMessageBox.warning(self, "Error", "Username and password are required.")
            return

        try:
            conn =get_auth_db()
            cur = conn.cursor()
            cur.execute("""
                SELECT id, full_name, position, role 
                FROM users 
                WHERE username=? AND password=? AND is_active=1
            """, (username, password))
            user = cur.fetchone()

            if user:
                user_id, name, pos, role = user
                name = name or username.capitalize()
                pos = pos or "User"

                cur.execute("UPDATE users SET remember_me = ? WHERE username = ?", (remember, username))
                conn.commit()
                

                QMessageBox.information(self, "Success", f"Welcome back, {name}!")
                self.login_successful.emit(username, name, pos, str(user_id))
                self.close()
            else:
                
                QMessageBox.critical(self, "Error", "Invalid username or password.")
        except Exception as e:
            QMessageBox.critical(self, "Error", f"Database error:\n{e}")

    def guest_login(self):
        self.login_successful.emit("guest", "Guest", "Guest", "0")
        self.close()

    def apply_light_style(self):
        self.setStyleSheet(login_style)

        for btn in self.findChildren(QPushButton):
            if btn.text() == "Continue as Guest":
                btn.setObjectName("guestBtn")