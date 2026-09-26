# Scriborium

Desktop-приложение для Windows по ТЗ из `TZ.txt`.

## Правило №1
Все файлы проекта должны быть в кодировке UTF-8 без BOM.

## Быстрый старт
```powershell
cd D:\2\project\Programm\PROG
python -m venv .venv
.venv\Scripts\Activate.ps1
pip install -e .
python -m scriborium.main
```

## Проверка кодировки
```powershell
python tools/check_encoding.py
```
