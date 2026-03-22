# Voicer

Кроссплатформенное приложение для голосового ввода текста. Работает в системном трее, распознаёт русскую речь офлайн с помощью модели GigaAM v3 (e2e) и отдаёт результат через WebSocket или вставляет текст в активное окно.

## Обзор экосистемы

Репозиторий содержит два приложения:

| Приложение | Назначение |
|------------|-----------|
| **Voicer** | Офлайн-распознавание речи → вставка в курсор или трансляция по WebSocket |
| **OpenVoicer** | Tray-мост между Voicer и [OpenCode](https://opencode.ai) — голосовое управление AI-агентом |

```
┌─────────────┐   WebSocket    ┌──────────────┐   HTTP/SSE    ┌──────────────┐
│   Voicer    │ ──────────────→│  OpenVoicer  │ ────────────→│   OpenCode   │
│  (STT, трей)│   :5050        │  (трей-мост) │   :4096      │  (AI-агент)  │
└─────────────┘                └──────────────┘              └──────────────┘
      │                              │                        Windows или WSL2
      │ F6: вставка в курсор         │ Popup-уведомления
      │ F7/F8: WS-трансляция         │ Открытие TUI (opencode attach)
```

**Voicer** работает самостоятельно. **OpenVoicer** — опциональная надстройка для пользователей OpenCode.

---

## Voicer

### Возможности

- **Офлайн-распознавание речи** — модель GigaAM v3 e2e (CTC + пунктуация + капитализация), работает без интернета
- **Push-to-talk** — удерживайте горячую клавишу для записи, отпустите для распознавания
- **Комбинации клавиш** — хоткеи поддерживают модификаторы (Ctrl+F6, Alt+Shift+F7 и т.д.)
- **Три режима работы:**
  - **Вставка в курсор** (F6) — вставляет текст в активное приложение через Ctrl+V / Cmd+V
  - **WebSocket** (F7) — транслирует распознанный текст подключённым клиентам
  - **WS + выделенный текст** (F8) — захватывает выделенный текст, добавляет его к надиктованному и отправляет по WebSocket
- **Расширяемые WS-хоткеи** — произвольное количество горячих клавиш с настраиваемыми тегами
- **Всплывающие уведомления** — показ распознанного текста в углу экрана (отключаемо)
- **Настройки** — выбор микрофона, горячих клавиш, порта WebSocket, количества потоков
- **Автозапуск** — опциональный запуск вместе с системой
- **Кроссплатформенность** — Windows, macOS, Linux (Avalonia UI)

### Требования

- .NET 9.0 (включён в установщик при self-contained публикации)
- **Windows**: Windows 10/11 x64
- **macOS**: macOS 12+ (x64/arm64), требуется разрешение Accessibility для хоткеев
- **Linux**: X11, PulseAudio (Wayland: хоткеи требуют XWayland — запускайте с `GDK_BACKEND=x11`)

### Быстрый старт

#### 1. Скачать модель

```powershell
# Windows (~215 MB)
./scripts/download-model.ps1
```

```bash
# macOS / Linux
./scripts/download-model.sh
```

#### 2. Собрать и запустить

```powershell
dotnet build src/Voicer.Desktop/Voicer.Desktop.csproj
dotnet run --project src/Voicer.Desktop/Voicer.Desktop.csproj
```

#### 3. Использование

1. Приложение запускается в системном трее
2. Удерживайте **F6** для записи (результат вставляется в активное окно)
3. Удерживайте **F7** для записи (результат отправляется по WebSocket)
4. Удерживайте **F8** для записи с захватом выделенного текста (выделенный текст + надиктованное отправляется по WebSocket)
5. Правый клик по иконке в трее — настройки и выход

### Настройки

| Параметр | По умолчанию | Описание |
|----------|-------------|----------|
| Микрофон | Системный | Устройство захвата |
| Потоки | 4 | Потоки для inference |
| Вставка (горячая клавиша) | F6 | Режим вставки в курсор |
| Порт WebSocket | 5050 | Порт WebSocket-сервера |
| Горячие клавиши WS | F7, F8 (+контекст) | Настраиваемый список с тегами |
| Уведомления | Вкл | Всплывающее уведомление |
| Автозапуск | Выкл | Запуск с системой |

Настройки хранятся в `settings.json` рядом с исполняемым файлом.

### WebSocket API

Сервер слушает на `ws://localhost:5050` (порт настраивается). Поддерживается claim-протокол — только один клиент одновременно получает транскрипции, остальные получают статусы.

Подробная документация: [docs/websocket-protocol.md](docs/websocket-protocol.md)

Тестовый клиент: [client/index.html](client/index.html)

---

## OpenVoicer

Tray-утилита, связывающая Voicer с [OpenCode](https://opencode.ai). Принимает транскрипции от Voicer по WebSocket и отправляет их как промпты в OpenCode API.

### Зачем нужен OpenVoicer

OpenCode — AI-агент для написания кода (TUI-приложение). OpenVoicer позволяет управлять им голосом:

1. Надиктуйте задачу в Voicer (F7) → OpenVoicer отправит её как промпт в OpenCode
2. OpenCode обработает промпт выбранным AI-агентом (build, plan, general, explore)
3. OpenVoicer покажет popup-уведомление с результатом
4. Для просмотра полного ответа откройте TUI через меню трея

### Архитектура и поток данных

```
Voicer (WS :5050)                    OpenVoicer (трей)                   OpenCode (:4096)
─────────────────                    ─────────────────                   ────────────────
                   claim при старте
             ←─────────────────────
                   claimed: true
             ─────────────────────→

[Пользователь диктует F7]
                   transcription
             ─────────────────────→
                                     popup: "Обработка..."
                                     POST /session/{id}/prompt_async ──→
                                     ← SSE: session.status busy
                                     ← SSE: message.part.delta (поток ответа)
                                     ← SSE: session.status idle
                                     popup: "Готово" + текст ответа

[Пользователь нажимает "Отменить" в popup]
                                     POST /session/{id}/abort ──→
                                     popup: "Отменено"
```

#### Обработка тегов

Voicer передаёт поле `tag` в транскрипции. OpenVoicer обрабатывает теги в следующем приоритете:

```
Транскрипция от Voicer (text, context, tag)
     │
     ├─ tag == NewSessionTag ("new-session")
     │   → Создать новую сессию OpenCode
     │   → Если есть text/context → отправить как первый промпт
     │
     ├─ tag == ContextTag ("context")
     │   → Добавить в коллекцию контекстных сообщений
     │   → Показать persistent popup "Контекст" (можно удалить)
     │   → НЕ отправлять в OpenCode
     │
     └─ Любой другой (включая пустой тег)
         → Если есть накопленный контекст → prepend к промпту
         → Отправить в OpenCode
         → Показать popup "Обработка..." с кнопкой "Отменить"
         → Очистить коллекцию контекстных сообщений
```

#### Сбор контекста из нескольких сообщений

OpenVoicer поддерживает накопление контекста из нескольких голосовых сообщений:

1. Настройте в Voicer WS-хоткей с тегом `context` (или другим, заданным в настройках OpenVoicer)
2. Нажмите этот хоткей, надиктуйте фрагмент контекста → появится фиолетовый popup "Контекст"
3. Повторите для добавления дополнительных фрагментов → попапы стекаются
4. Каждый popup можно удалить кнопкой "Удалить"
5. Когда готовы — надиктуйте основной промпт через обычный хоткей (F7)
6. Все контекстные сообщения будут добавлены перед промптом и отправлены в OpenCode вместе
7. Контекстные попапы закроются автоматически

#### Popup-уведомления

| Popup | Цвет | Поведение |
|-------|------|-----------|
| **Обработка...** | синий | Persistent, кнопка "Отменить". Появляется сразу при отправке промпта |
| **Контекст** | фиолетовый | Persistent, кнопка "Удалить". Виден пока не отправлен основной промпт |
| **Готово** | зелёный | Кнопка "OK", раскрытие текста ▼. Текст можно выделять и копировать |
| **Отменено** | синий | Автозакрытие. Появляется после отмены запроса |
| **Агент работает** | синий | Автозакрытие. Показывается если нет processing-popup |
| **Ошибка** | красный | Кнопка "Закрыть" |

При отправке нескольких сообщений подряд каждое получает свой popup "Обработка...". Когда ответ приходит, все промежуточные попапы закрываются, последний переходит в "Готово".

#### Иконка трея OpenVoicer

Иконка динамически отображает состояние подключений:

| Состояние | Иконка |
|-----------|--------|
| Voicer + OpenCode подключены | OV с зелёным бейджем |
| Одно подключение отсутствует | OV с жёлтым бейджем |
| Оба отключены | Серая иконка OV |

### Возможности

- **Автоподключение к Voicer** — claim-протокол с автореконнектом (backoff 1–10 сек)
- **Отправка промптов в OpenCode** — транскрипции без специальных тегов отправляются автоматически
- **Конкатенация контекста** — `context + text` объединяются перед отправкой
- **Сбор контекста** — накопление фрагментов из нескольких сообщений через настраиваемый тег
- **Создание новой сессии** — по транскрипции с настраиваемым тегом (по умолчанию `new-session`)
- **Отмена запроса** — кнопка "Отменить" в popup → `POST /session/{id}/abort`
- **Popup-уведомления** — persistent popup "Обработка..." с кнопкой отмены, раскрываемый ответ с форматированием markdown
- **Открытие TUI** — пункт «Открыть» запускает `opencode attach` с привязкой к текущей сессии
- **Выбор модели/провайдера/агента** — загружаются из OpenCode API
- **Рабочий каталог** — задаётся отдельно для Windows и WSL-режимов
- **Запуск OpenCode при старте** — опциональный автозапуск `opencode serve`
- **Поддержка WSL2** — OpenCode может запускаться внутри WSL2 (для Linux-only зависимостей)
- **Автозапуск с системой** — через реестр Windows

### Быстрый старт

#### Предварительные требования

1. Установленный и работающий **Voicer**
2. Установленный **OpenCode** (`opencode` в PATH или в WSL2)
3. Настроенный провайдер в OpenCode (`~/.config/opencode/opencode.jsonc`)

#### Запуск

```powershell
dotnet build src/OpenVoicer/OpenVoicer.csproj
dotnet run --project src/OpenVoicer/OpenVoicer.csproj
```

#### Порядок запуска

Порядок запуска Voicer и OpenVoicer не важен — OpenVoicer автоматически подключится к Voicer при его появлении (реконнект с backoff 1–10 сек).

Рекомендуемый порядок:
1. Запустите **OpenCode** (`opencode serve` или включите автозапуск в настройках OpenVoicer)
2. Запустите **Voicer**
3. Запустите **OpenVoicer**

#### Настройка горячих клавиш в Voicer

Для полноценной работы с OpenVoicer рекомендуется настроить в Voicer следующие WS-хоткеи:

| Хоткей | Действие | Тег | Назначение |
|--------|----------|-----|-----------|
| F7 | TranscribeAndSend | _(пусто)_ | Голосовой промпт → OpenCode |
| F8 | TranscribeWithContext | _(пусто)_ | Голос + выделенный текст → OpenCode |
| F9 | TranscribeAndSend | `context` | Добавить голосовой фрагмент в контекст |
| F10 | TranscribeWithContext | `context` | Добавить голос + выделение в контекст |
| F11 | SendTag | `new-session` | Создать новую сессию OpenCode |

Теги `context` и `new-session` настраиваются в OpenVoicer (Настройки → Уведомления).

#### Использование

1. **Простой промпт:** надиктуйте через **F7** → промпт уйдёт в OpenCode
2. **Промпт с контекстом:** выделите код, надиктуйте через **F8** → выделенный текст + голос → промпт
3. **Сбор контекста из нескольких частей:**
   - Нажмите **F9**, надиктуйте первый фрагмент → popup "Контекст"
   - Выделите код, нажмите **F10** → ещё один фрагмент + выделение
   - Нажмите **F7**, надиктуйте основной запрос → всё отправится в OpenCode вместе
4. **Новая сессия:** нажмите **F11** → создастся новая сессия OpenCode
5. **Отмена:** нажмите "Отменить" в popup → запрос прервётся
6. **Просмотр ответа:** кликните по popup "Готово" для раскрытия, Ctrl+C для копирования
7. **TUI:** правый клик по иконке OpenVoicer → «Открыть»

### Настройки

| Параметр | По умолчанию | Описание |
|----------|-------------|----------|
| Порт Voicer WS | 5050 | Порт WebSocket-сервера Voicer |
| Порт OpenCode | 4096 | Порт HTTP API OpenCode |
| Рабочий каталог | _(пусто)_ | Каталог проекта для OpenCode (Windows) |
| Запуск OpenCode | Вкл | Автозапуск `opencode serve` |
| Через WSL2 | Выкл | Запускать OpenCode внутри WSL2 |
| WSL дистрибутив | _(по умолчанию)_ | Имя WSL-дистрибутива (если несколько) |
| Рабочий каталог WSL | ~ | Каталог проекта для OpenCode в WSL |
| Провайдер | — | Провайдер AI (из OpenCode API) |
| Модель | — | Модель AI (из OpenCode API) |
| Агент | build | Агент OpenCode (build/plan/general/explore) |
| Уведомления | Вкл | Popup-уведомления |
| Длительность popup | 4 сек | Время автозакрытия (0.5–30 сек) |
| Макс. символов | 100 | Лимит текста в preview (0 = без лимита) |
| Тег новой сессии | new-session | Тег для создания новой сессии |
| Тег контекста | context | Тег для накопления контекстных сообщений |
| Автозапуск | Выкл | Запуск с системой |

Настройки хранятся в `settings.json` рядом с исполняемым файлом OpenVoicer.

### Работа с WSL2

Если OpenCode установлен в WSL2 (например, через `curl -fsSL https://opencode.ai/install | bash` в Ubuntu):

1. Включите **«Запускать через WSL2»** в настройках OpenVoicer
2. При необходимости укажите имя дистрибутива (например, `Ubuntu-18.04`). Если дистрибутив один — оставьте поле пустым
3. Укажите **рабочий каталог WSL** — путь к проекту внутри WSL (например, `~/projects/myapp`)
4. OpenVoicer запустит `wsl -e opencode serve` и будет подключаться к API через `localhost:4096` (WSL2 автоматически пробрасывает порты)
5. TUI также откроется внутри WSL2 через `wsl -e opencode attach`

---

## Сборка дистрибутивов

Все скрипты сборки находятся в папке `scripts/`. Результат — папка `output/`.

### Voicer

```powershell
# Windows — установщик (Inno Setup 6)
./scripts/build-windows.ps1
```

```bash
# macOS — .app + .dmg (x64 + arm64)
./scripts/build-macos.sh

# Linux — .deb + .tar.gz
./scripts/build-linux.sh [Release] [linux-x64|linux-arm64]
```

### OpenVoicer

```powershell
# Windows — установщик (Inno Setup 6)
./scripts/build-openvoicer-windows.ps1
```

```bash
# macOS — .app + .dmg (x64 + arm64)
./scripts/build-openvoicer-macos.sh

# Linux — .deb + .tar.gz
./scripts/build-openvoicer-linux.sh [Release] [linux-x64|linux-arm64]
```

## Структура проекта

```
Voicer/
├── src/
│   ├── Voicer.Core/                    # Кроссплатформенное ядро
│   │   ├── Interfaces/                 # IAudioCaptureService, IHotkeyService, ...
│   │   ├── Models/
│   │   │   └── AppSettings.cs          # Настройки (JSON)
│   │   └── Services/
│   │       ├── SpeechRecognitionService.cs  # Распознавание (sherpa-onnx)
│   │       └── VoicerWebSocketServer.cs     # WebSocket-сервер + claim
│   │
│   ├── Voicer.Desktop/                 # Avalonia UI приложение (трей)
│   │   ├── Program.cs                  # Точка входа, DI
│   │   ├── App.axaml.cs               # TrayIcon, NativeMenu
│   │   ├── AppOrchestrator.cs          # Стейт-машина (запись → распознавание → отправка)
│   │   ├── Views/
│   │   │   ├── SettingsWindow.axaml    # Окно настроек
│   │   │   └── TranscriptionPopup.axaml # Всплывающее уведомление
│   │   └── Services/
│   │       └── SkiaTrayIconGenerator.cs # Генерация иконок трея (SkiaSharp)
│   │
│   ├── OpenVoicer/                     # Tray-мост Voicer → OpenCode
│   │   ├── Program.cs                  # Точка входа, DI
│   │   ├── App.axaml.cs               # TrayIcon, popup, запуск TUI
│   │   ├── Models/
│   │   │   └── OpenVoicerSettings.cs   # Настройки
│   │   ├── Services/
│   │   │   ├── VoicerWsClient.cs       # WS-клиент к Voicer (claim, реконнект)
│   │   │   ├── OpenCodeClient.cs       # HTTP-клиент к OpenCode API
│   │   │   ├── OpenCodeEventService.cs # SSE-клиент (статусы агента)
│   │   │   ├── OpenCodeProcessManager.cs # Запуск/остановка opencode serve
│   │   │   └── AutoStartService.cs     # Автозапуск (реестр Windows)
│   │   └── Views/
│   │       ├── SettingsWindow.axaml    # Окно настроек
│   │       └── NotificationPopup.axaml # Popup-уведомления
│   │
│   ├── Voicer.Platform.Windows/        # Windows: NAudio, P/Invoke, Registry
│   ├── Voicer.Platform.macOS/          # macOS: CoreAudio, CGEvent, LaunchAgent
│   └── Voicer.Platform.Linux/          # Linux: PulseAudio, X11, .desktop
│
├── client/index.html                   # Тестовый WebSocket-клиент
├── docs/websocket-protocol.md          # Документация WS-протокола
├── installer/
│   ├── icons/                         # Иконки (voicer.ico, openvoicer.ico)
│   ├── windows/
│   │   ├── voicer.iss                 # Inno Setup: Voicer
│   │   └── openvoicer.iss            # Inno Setup: OpenVoicer
│   ├── macos/
│   │   ├── Info.plist                 # Voicer bundle metadata
│   │   ├── OpenVoicer-Info.plist      # OpenVoicer bundle metadata
│   │   └── entitlements.plist         # macOS entitlements
│   └── linux/
│       ├── voicer.desktop             # Voicer desktop entry
│       ├── openvoicer.desktop         # OpenVoicer desktop entry
│       ├── openvoicer-install.sh      # OpenVoicer tarball installer
│       ├── openvoicer-uninstall.sh    # OpenVoicer uninstaller
│       ├── install.sh                 # Voicer tarball installer
│       ├── uninstall.sh               # Voicer uninstaller
│       └── DEBIAN/                    # .deb package files
├── models/                             # ML-модели (не в git)
└── scripts/
    ├── download-model.ps1             # Скачивание модели (PowerShell)
    ├── download-model.sh              # Скачивание модели (bash)
    ├── build-windows.ps1              # Сборка Voicer (Windows)
    ├── build-macos.sh                 # Сборка Voicer (macOS)
    ├── build-linux.sh                 # Сборка Voicer (Linux)
    ├── build-openvoicer-windows.ps1   # Сборка OpenVoicer (Windows)
    ├── build-openvoicer-macos.sh      # Сборка OpenVoicer (macOS)
    ├── build-openvoicer-linux.sh      # Сборка OpenVoicer (Linux)
    └── generate-icon.py               # Генерация иконок (Pillow)
```

## Используемые технологии

- **.NET 9.0 / Avalonia UI** — кроссплатформенный UI и системный трей
- **GigaAM v3 e2e** — офлайн-распознавание русской речи с пунктуацией (CTC, ONNX, BPE)
- **sherpa-onnx** — обёртка для запуска ONNX-моделей распознавания
- **NAudio / WASAPI** — захват аудио (Windows)
- **PulseAudio** — захват аудио (Linux)
- **CoreAudio** — захват аудио (macOS)
- **Fleck** — WebSocket-сервер
- **SkiaSharp** — генерация иконок трея
- **Inno Setup** — установщик для Windows

## Лицензии

Информация о лицензиях используемых компонентов: [THIRD_PARTY_LICENSES](THIRD_PARTY_LICENSES)
