# Лицензии поставки

Собственный код и документация ScriSelf Studio распространяются по MIT (../LICENSE).
MIT ScriSelf не меняет лицензии зависимостей и не применяется автоматически к рукописям пользователей.

- `01_Component_inventory.txt` сопоставляет Release-зависимости и полные уведомления.
- `sources.json` фиксирует первичные источники и SHA256 полученных текстов.
- Шрифт Inter: OFL 1.1; MIT NuGet-пакета Avalonia.Fonts.Inter не заменяет лицензию шрифта.
- ScriSelf Symbols: изменённое подмножество Fluent System Icons под MIT.
- Нативные компоненты Skia/HarfBuzz и .NET включают собственные подборки уведомлений.
- SQLitePCLRaw: Apache-2.0; условия ядра SQLite воспроизведены в его NOTICE.
- Тестовые, сборочные и Debug-инструменты не перечислены как компоненты программы.

Тексты `.txt` встроены в окно справки. Вся папка и LICENSE.txt также копируются
в установочную директорию. При обновлении зависимостей проверьте их версии,
исходные лицензии и внутренние NOTICE нативных пакетов заново.

Обновление: `tools/update-license-notices.ps1`, затем после Release-сборки
`tools/update-license-inventory.ps1`. Скрипты используют локальный кэш NuGet и
закреплённые upstream-ревизии; изменения текстов нужно просмотреть перед поставкой.
Инвентаризация относится к настольной программе, а не к сайту или сервисам облака.

Необязательная локальная грамматика (ADR 033): LanguageTool 6.6 (LGPL 2.1+)
и Eclipse Temurin JRE 21.0.13+9 (GPLv2 с Classpath Exception). Основные условия
в LanguageTool.txt и Temurin_Java.txt. Поставка модуля сохраняет весь исходный
каталог third-party-licenses, уведомления ресурсов, JRE legal/ и NOTICE;
они не заменяются краткой инвентаризацией. Закреплённые архивы/SHA256 и источники —
tools/prepare-grammar-runtime.ps1 и Grammar/packages.json, Grammar/NOTICE.txt.
