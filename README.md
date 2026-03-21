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

### Архитектура

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
                                     POST /session/{id}/prompt_async ──→
                                     ← SSE /event (session.status busy)
                                     [popup: "Агент работает..."]
                                     ← SSE /event (message.part.delta)
                                     ← SSE /event (session.status idle)
                                     GET /session/{id}/message (ответ)
                                     [popup: фрагмент ответа]
```

### Возможности

- **Автоподключение к Voicer** — claim-протокол с автореконнектом
- **Отправка промптов в OpenCode** — транскрипции без тега отправляются автоматически
- **Конкатенация контекста** — `text + context` объединяются перед отправкой
- **Создание новой сессии** — по транскрипции с настраиваемым тегом (по умолчанию `new-session`)
- **Popup-уведомления** — статус агента (работает / готово) с фрагментом ответа
- **Открытие TUI** — пункт «Открыть» запускает `opencode attach` с привязкой к текущей сессии
- **Выбор модели/провайдера/агента** — загружаются из OpenCode API
- **Запуск OpenCode при старте** — опциональный автозапуск сервера `opencode serve`
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

#### Использование

1. Надиктуйте задачу через **F7** (или другой WS-хоткей без тега) — промпт уйдёт в OpenCode
2. Надиктуйте с **F8** (или WS-хоткей с контекстом) — выделенный текст + голос → промпт
3. Нажмите хоткей с тегом `new-session` — создастся новая сессия OpenCode
4. Правый клик по иконке OpenVoicer → «Открыть» — откроется TUI с текущей сессией

### Настройки

| Параметр | По умолчанию | Описание |
|----------|-------------|----------|
| Порт Voicer WS | 5050 | Порт WebSocket-сервера Voicer |
| Порт OpenCode | 4096 | Порт HTTP API OpenCode |
| Запуск OpenCode | Вкл | Автозапуск `opencode serve` |
| Через WSL2 | Выкл | Запускать OpenCode внутри WSL2 |
| WSL дистрибутив | (по умолчанию) | Имя WSL-дистрибутива (если несколько) |
| Провайдер | — | Провайдер AI (из OpenCode API) |
| Модель | — | Модель AI (из OpenCode API) |
| Агент | build | Агент OpenCode (build/plan/general/explore) |
| Уведомления | Вкл | Popup-уведомления |
| Длительность popup | 4 сек | Время отображения (0.5–30 сек) |
| Макс. символов | 100 | Лимит текста в popup (0 = без лимита) |
| Тег новой сессии | new-session | Тег для создания новой сессии |
| Автозапуск | Выкл | Запуск с системой |

Настройки хранятся в `settings.json` рядом с исполняемым файлом OpenVoicer.

### Работа с WSL2

Если OpenCode установлен в WSL2 (например, через `curl -fsSL https://opencode.ai/install | bash` в Ubuntu):

1. Включите **«Запускать через WSL2»** в настройках OpenVoicer
2. При необходимости укажите имя дистрибутива (например, `Ubuntu-18.04`). Если дистрибутив один — оставьте поле пустым
3. OpenVoicer запустит `wsl -e opencode serve` и будет подключаться к API через `localhost:4096` (WSL2 автоматически пробрасывает порты)
4. TUI также откроется внутри WSL2 через `wsl -e opencode attach`

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
