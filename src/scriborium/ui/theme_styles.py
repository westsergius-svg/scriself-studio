from __future__ import annotations


THEME_STANDARD = "standard"
THEME_CLASSIC = "classic"


def normalize_theme(raw: str | None) -> str:
    value = (raw or "").strip().lower()
    if value in {THEME_STANDARD, THEME_CLASSIC}:
        return value
    if value in {"light", "dark", "system", "steampunk"}:
        return THEME_STANDARD
    return THEME_STANDARD


def _classic_contrast_boost() -> str:
    """Extra rules appended to the Classic stylesheet to raise contrast while
    keeping the classic (cream/brown) look instead of switching to the standard
    scheme."""
    return """
            QWidget { color: #171008; }
            QWidget#launcherWindow, QWidget#launcherRoot { background: #f1e7d0; }
            QFrame#classicSidebar { background: #e0caa4; border-right: 2px solid #8f6f3a; }
            QLabel#classicSidebarTitle { color: #201503; }
            QLabel#classicBrand { color: #41280a; }
            QLabel#classicTitle { color: #55360c; }
            QLabel#classicCaption { color: #201606; font-weight: 700; }
            QLabel, QCheckBox, QRadioButton { color: #171008; }
            QPushButton { color: #171008; background: #fffaf0; border: 2px solid #7a5a26; }
            QPushButton:hover { background: #f0e2c2; }
            QPushButton#classicBrowseButton, QPushButton#classicSecondaryButton {
                color: #171008; background: #fffaf0; border: 2px solid #7a5a26;
            }
            QLineEdit#classicInput, QComboBox#classicInput {
                background: #ffffff; color: #0f0a04; border: 3px solid #4a3414;
            }
            QLineEdit#classicInput:focus, QComboBox#classicInput:focus { border: 3px solid #0e4c8a; }
            QListWidget#classicRecentList {
                background: #ffffff; color: #0f0a04; border: 2px solid #5e4319;
            }
            QListWidget#classicRecentList::item:selected { background: #1f5f9e; color: #ffffff; }
            QLineEdit, QPlainTextEdit, QTextEdit, QComboBox, QSpinBox, QListWidget, QTreeWidget {
                background: #ffffff; color: #101010;
                border: 2px solid #4a3414; selection-background-color: #1f5f9e; selection-color: #ffffff;
            }
            QStatusBar, QMenuBar { background: #d9c298; color: #171008; }
        """


def stylesheet_for_theme(theme: str, high_contrast: bool = False) -> str:
    t = normalize_theme(theme)
    if t == THEME_CLASSIC:
        css = """
            QMainWindow#launcherWindow {
                background: #f7f0df;
            }
            QWidget#launcherRoot {
                background: #f7f0df;
            }
            QTabWidget::pane {
                border: none;
                background: #f7f0df;
                top: -1px;
            }
            QTabBar {
                qproperty-drawBase: 0;
            }
            QTabBar::tab {
                background: transparent;
                color: #5f4a28;
                min-width: 180px;
                padding: 14px 18px 12px 18px;
                margin: 0 8px 0 0;
                border: none;
                font-family: 'Segoe UI';
                font-size: 11pt;
                font-weight: 600;
                text-transform: uppercase;
            }
            QTabBar::tab:selected {
                color: #73511f;
                border-bottom: 4px solid #244a32;
            }
            QTabBar::tab:hover:!selected {
                color: #735a31;
            }
            QWidget#classicShell {
                background: #f6edd9;
                border-top: 1px solid #cdb78e;
            }
            QFrame#classicSidebar {
                background: #ead9b8;
                border-right: 1px solid #bea075;
            }
            QLabel#classicSidebarTitle {
                color: #39270f;
                font-family: 'Georgia';
                font-size: 17pt;
                font-weight: 600;
            }
            QPushButton#classicSidebarItem {
                text-align: left;
                color: #2e2113;
                background: rgba(255,255,255,0.45);
                border: none;
                border-radius: 0;
                padding: 16px 18px;
                font-family: 'Georgia';
                font-size: 12pt;
            }
            QPushButton#classicSidebarItem:hover {
                background: rgba(255,255,255,0.72);
            }
            QPushButton#classicSidebarItem[active="true"] {
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #fff8ea, stop:1 #eddab4);
                border-left: 5px solid #1f452e;
                padding-left: 14px;
            }
            QFrame#classicCanvas {
                background: qradialgradient(cx:0.52, cy:0.42, radius:1.04, stop:0 #fffef9, stop:0.66 #fbf6ea, stop:1 #ecddc2);
                border: none;
            }
            QLabel#classicBrand {
                color: #624520;
                font-family: 'Georgia';
                font-size: 19pt;
                letter-spacing: 1px;
            }
            QLabel#classicTitle {
                color: #825c2a;
                font-family: 'Georgia';
                font-size: 28pt;
                font-weight: 500;
            }
            QLabel#classicCaption {
                color: #4a3d2b;
                font-family: 'Segoe UI';
                font-size: 11pt;
                font-weight: 600;
            }
            QLabel#classicDecorFeather, QLabel#classicDecorBook {
                color: rgba(158, 122, 75, 0.76);
                font-family: 'Georgia';
                font-size: 88pt;
            }
            QLabel#classicDecorFeather {
                color: rgba(163, 123, 76, 0.82);
            }
            QLabel#classicDecorBook {
                color: rgba(140, 126, 101, 0.78);
            }
            QLineEdit#classicInput, QComboBox#classicInput {
                background: rgba(255, 252, 246, 0.97);
                color: #352716;
                border: 2px solid #ab8753;
                border-radius: 7px;
                padding: 10px 14px;
                font-family: 'Segoe UI';
                font-size: 12pt;
                min-height: 24px;
            }
            QLineEdit#classicInput:focus, QComboBox#classicInput:focus {
                border: 2px solid #6f5227;
            }
            QComboBox#classicInput {
                background: #fff8ec;
                color: #332615;
                padding-right: 26px;
            }
            QComboBox#classicInput QAbstractItemView {
                background: #fff9ef;
                color: #2d2214;
                border: 2px solid #ab8753;
                selection-background-color: #d9c093;
                selection-color: #1f180f;
            }
            QPushButton#classicBrowseButton {
                background: #fbf1de;
                color: #6d5127;
                border: 2px solid #ab8753;
                border-radius: 7px;
                padding: 9px 14px;
                min-width: 52px;
                font-size: 12pt;
                font-weight: 600;
            }
            QPushButton#classicBrowseButton:hover {
                background: #f0dfbc;
            }
            QPushButton#classicPrimaryButton {
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #113321, stop:1 #234b33);
                color: #fff8eb;
                border: 2px solid #32583f;
                border-radius: 12px;
                padding: 14px 24px;
                font-family: 'Georgia';
                font-size: 18pt;
                min-height: 30px;
            }
            QPushButton#classicPrimaryButton:hover {
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #18422b, stop:1 #2a573c);
            }
            QPushButton#primaryActionButton {
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #143722, stop:1 #2c5a3d);
                color: #fff8eb;
                border: 2px solid #32583f;
                border-radius: 10px;
                padding: 10px 18px;
                font-family: 'Georgia';
                font-size: 13pt;
                font-weight: 700;
            }
            QPushButton#primaryActionButton:hover {
                background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #1a452c, stop:1 #336645);
            }
            QPushButton#classicSecondaryButton {
                background: rgba(255, 249, 239, 0.92);
                color: #3f301c;
                border: 2px solid #b99967;
                border-radius: 10px;
                padding: 10px 16px;
                font-family: 'Segoe UI';
                font-size: 11pt;
                font-weight: 600;
            }
            QPushButton#classicSecondaryButton:hover {
                background: #f1e2c4;
            }
            QLabel#classicFooter {
                color: #4f412d;
                font-family: 'Segoe UI';
                font-size: 10.5pt;
                font-weight: 600;
            }
            QListWidget#classicRecentList {
                background: rgba(255, 251, 245, 0.96);
                color: #352818;
                border: 2px solid #b89c6f;
                border-radius: 10px;
                padding: 10px;
                font-family: 'Georgia';
                font-size: 12pt;
            }
            QListWidget#classicRecentList::item {
                padding: 10px 8px;
                border-bottom: 1px solid rgba(120, 90, 45, 0.22);
            }
            QListWidget#classicRecentList::item:selected {
                background: rgba(34, 83, 53, 0.26);
                color: #20170d;
            }
            QDialog#exportDialog {
                background: #f6edd9;
            }
            QTextBrowser#exportPreview {
                background: rgba(255, 252, 246, 0.96);
                color: #2f2418;
                border: 2px solid #c7ae81;
                border-radius: 12px;
                padding: 12px;
            }
            QWidget#classicSettingsPanel {
                background: rgba(255, 252, 246, 0.88);
                border: 2px solid #c7ae81;
                border-radius: 12px;
            }
            QWidget#timelineRoot {
                background: rgba(255, 252, 246, 0.78);
                border: 2px solid #c9b085;
                border-radius: 12px;
            }
            QWidget#timelineCanvas {
                background: transparent;
            }
            QLabel#timelineTitle {
                color: #5c4120;
                font-family: 'Georgia';
                font-size: 15pt;
                font-weight: 700;
            }
            QLabel#timelineZoomLabel, QLabel#timelineHint {
                color: #5a4a31;
                font-family: 'Segoe UI';
                font-size: 10.5pt;
            }
            QLabel, QCheckBox, QRadioButton {
                color: #322517;
                font-family: 'Segoe UI';
                font-size: 11pt;
            }
            QLineEdit, QPlainTextEdit, QTextEdit {
                qproperty-layoutDirection: LeftToRight;
            }
            QCheckBox::indicator, QRadioButton::indicator {
                width: 16px;
                height: 16px;
            }
            QScrollArea {
                background: transparent;
                border: none;
            }
            QScrollBar:vertical {
                background: rgba(222, 209, 181, 0.55);
                width: 12px;
                margin: 6px 2px 6px 2px;
                border-radius: 6px;
            }
            QScrollBar::handle:vertical {
                background: #8f7346;
                min-height: 28px;
                border-radius: 6px;
            }
            QPushButton {
                background: rgba(255, 249, 239, 0.92);
                color: #3f301c;
                border: 2px solid #b99967;
                border-radius: 8px;
                padding: 8px 14px;
            }
            QPushButton:hover {
                background: #f1e2c4;
            }
            QStatusBar, QMenuBar {
                background: #eadcc1;
                color: #382714;
            }
        """
        if high_contrast:
            css += _classic_contrast_boost()
        return css
    if high_contrast:
        return """
            QMainWindow, QWidget { background: #dde3e9; color: #14212e; font-family: 'Segoe UI'; font-size: 10pt; }
            QLineEdit, QPlainTextEdit, QComboBox, QSpinBox, QListWidget, QTreeWidget {
                background: #ffffff; border: 2px solid #7d8b9a; border-radius: 4px; padding: 5px;
                selection-background-color: #2c5f94; selection-color: #ffffff;
            }
            QLineEdit, QPlainTextEdit, QTextEdit { qproperty-layoutDirection: LeftToRight; }
            QPushButton { background: #d9e4ef; border: 2px solid #71859b; border-radius: 4px; padding: 6px 10px; font-weight: 600; }
            QPushButton:hover { background: #cbd9e8; }
            QPushButton:focus, QLineEdit:focus, QPlainTextEdit:focus, QComboBox:focus, QSpinBox:focus {
                border: 2px solid #235f9e;
            }
            QMenuBar { background: #bcc8d4; color: #14212e; }
            QStatusBar { background: #c7d2de; color: #14212e; }
            QTreeWidget::item:selected, QListWidget::item:selected { background: #2c5f94; color: #ffffff; }
        """
    return """
        QMainWindow, QWidget { background: #e7ebef; color: #1f2933; font-family: 'Segoe UI'; font-size: 10pt; }
        QLineEdit, QPlainTextEdit, QComboBox, QSpinBox, QListWidget, QTreeWidget {
            background: #f6f8fa; border: 1px solid #c7ced6; border-radius: 4px; padding: 4px;
        }
        QLineEdit, QPlainTextEdit, QTextEdit { qproperty-layoutDirection: LeftToRight; }
        QPushButton {
            background: #eef2f6; border: 1px solid #b8c2cc; border-radius: 4px; padding: 6px 10px;
        }
        QPushButton:hover { background: #e5ebf1; }
        QPushButton#primaryActionButton {
            background: #205a39;
            color: #ffffff;
            border: 1px solid #18472d;
            border-radius: 6px;
            padding: 7px 14px;
            font-weight: 700;
        }
        QPushButton#primaryActionButton:hover { background: #2a7349; }
        QPushButton:focus, QLineEdit:focus, QPlainTextEdit:focus, QComboBox:focus, QSpinBox:focus {
            border: 1px solid #7b9bbc;
        }
        QDialog#exportDialog, QWidget#timelineRoot {
            background: #eef2f4;
        }
        QTextBrowser#exportPreview {
            background: #ffffff;
            border: 1px solid #c7ced6;
            border-radius: 8px;
            padding: 8px;
        }
        QLabel#timelineTitle { font-weight: 700; font-size: 12pt; }
        QLabel#timelineZoomLabel, QLabel#timelineHint { color: #52606d; }
        QMenuBar { background: #c9d1d8; }
        QStatusBar { background: #d5dce2; }
    """
