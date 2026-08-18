# 🤖 CapyLib.DiscordBot

> **Многосерверный Discord-бот для управления, мониторинга и синхронизации игровых серверов SCP: Secret Laboratory (EXILED) через CapyLib Bridge с криптографической аутентификацией по SSH RSA ключам.**

```
===============================================================================
        (\_/)
       ( •_•)    ██████╗ ██╗███████╗ ██████╗ ██████╗ ██████╗ ██████╗  ██████╗ ████████╗
      / >🤖     ██╔══██╗██║██╔════╝██╔════╝██╔═══██╗██╔══██╗██╔══██╗██╔═══██╗╚══██╔══╝
     /     \    ██║  ██║██║███████╗██║     ██║   ██║██████╔╝██████╔╝██║   ██║   ██║   
    (_______)   ██║  ██║██║╚════██║██║     ██║   ██║██╔══██╗██╔══██╗██║   ██║   ██║   
                ██████╔╝██║███████║╚██████╗╚██████╔╝██║  ██║██████╔╝╚██████╔╝   ██║   
                ╚═════╝ ╚═╝╚══════╝ ╚═════╝ ╚═════╝ ╚═╝  ╚═╝╚═════╝  ╚═════╝    ╚═╝   

               [ CapyLib Multi-Server Discord Bot v1.2.0 for SCP:SL ]
===============================================================================
```

---

## 🌟 Ключевые возможности

* 🛡️ **Криптографическая безопасность (SSH RSA-SHA256)**:
  Каждый исходящий запрос к игровым серверам подписывается закрытым ключом (`aspect_bridge.key`) с защитой от Replay-атак по Unix-таймштампу и хешу тела.
* 🌐 **Мультисерверность (NR, MRP и др.)**:
  Одновременная работа с неограниченным числом игровых серверов из одного процесса бота с раздельными правами, каналами логов и статусов.
* 📊 **Авто-обновляемый Live-статус**:
  Динамический статус серверов в Discord (онлайн, карта, фракции SCP/людей, статус боеголовки, TPS) с авто-ротацией активности бота.
* 🎮 **RemoteAdmin команды из Discord**:
  Удобное выполнение консольных и RA-команд через Slash-команду `/command` с многоуровневым контролем доступа (`ra_role_ids`, `creator_key_role_ids`).
* 🔗 **Привязка аккаунтов (`/steamsl`) и синхронизация ролей**:
  Игроки генерируют код привязки в Discord и активируют его в игре командой `.linkdiscord <code>`. Бот автоматически синхронизирует роли Discord с группами прав EXILED в реальном времени.
* 📜 **Раздельное логирование 5 категорий событий**:
  1. **Наказания (Punishments)**: баны, кики, муты интеркома/голосового чата, разбаны.
  2. **Игровой процесс (Rounds)**: старты/концы раундов, волны возрождения МОГ/Хаоса/Длани Змеи, детонация боеголовки.
  3. **События сервера (Server)**: подключения/отключения игроков, смерти, перезагрузки конфигов.
  4. **Команды (Commands)**: аудит выполнения команд в игре и из Discord.
  5. **Жалобы (Reports)**: жалобы на читеров и вызовы администрации через меню игры.

---

## 🚀 Быстрый старт

### 1. Требования
* [.NET 8.0 SDK / Runtime](https://dotnet.microsoft.com/download) или новее.
* Игровой сервер SCP:SL с установленной библиотекой [**CapyLib**](https://github.com/CapybaraPR/CapyLib) (`Capy.API.DiscordBridge`).

### 2. Генерация пары SSH-ключей
Сгенерируйте пару ключей встроенной командой:
```bash
dotnet CapyLib.DiscordBot.dll --generate-keys
```
* `aspect_bridge.key` — приватный ключ (остается в папке с ботом).
* `aspect_bridge.pub` — публичный ключ (скопируйте на сервер SCP:SL в папку `EXILED/Configs/CapyLib/aspect_bridge.pub`).

### 3. Настройка конфигурации (`bot-config.json`)
Скопируйте `bot-config.example.json` в `bot-config.json` и укажите данные вашего Discord-приложения:

```json
{
  "bot_token": "ВАШ_DISCORD_BOT_TOKEN",
  "guild_id": 123456789012345678,
  "status_refresh_seconds": 15,
  "servers": [
    {
      "id": "nr",
      "display_name": "NR Server",
      "api_base_url": "http://127.0.0.1:8123/",
      "ssh_private_key_path": "aspect_bridge.key",
      "public_address": "nr.yourserver.ru:7777",
      "status_channel_id": 111111111111111111,
      "status_message_id": 0,
      "audit_channel_id": 222222222222222222,
      "required_server_role_ids": [],
      "ra_role_ids": [333333333333333333],
      "creator_key_role_ids": [444444444444444444],
      "log_channels": {
        "punishments": [{ "id": 555555555555555555, "mode": "embed" }],
        "rounds": [{ "id": 555555555555555555, "mode": "embed" }],
        "server": [{ "id": 555555555555555555, "mode": "embed" }],
        "commands": [{ "id": 555555555555555555, "mode": "embed" }],
        "reports": [{ "id": 555555555555555555, "mode": "embed" }]
      }
    }
  ]
}
```

### 4. Проверка конфигурации
```bash
dotnet CapyLib.DiscordBot.dll --validate-config
```

### 5. Запуск бота
```bash
dotnet CapyLib.DiscordBot.dll
```

---

## 🛠️ Slash-команды Discord

| Команда | Аргументы | Описание |
| :--- | :--- | :--- |
| `/server` | `server` (опц.) | Подробная информация о статусе сервера, игроках, SCP и раунде. |
| `/players` | `server` (опц.) | Список онлайн игроков, их роли, пинг и группы. |
| `/command` | `server`, `command` | Выполнение RemoteAdmin команды на выбранном сервере. |
| `/steamsl` | — | Получение 6-значного кода для привязки Steam/Discord в игре. |
| `/rolesync` | `user` (опц.) | Принудительная синхронизация ролей для пользователя. |
| `/groups` | `server` (опц.) | Список всех зарегистрированных групп прав EXILED на сервере. |

---

## 🐧 Автозапуск на Linux (systemd)

1. Скопируйте шаблон службы:
```bash
sudo cp deploy/capy-discord-bot.service /etc/systemd/system/capy-discord-bot.service
```
2. Отредактируйте пути к директории бота (`WorkingDirectory` и `ExecStart`):
```bash
sudo nano /etc/systemd/system/capy-discord-bot.service
```
3. Запустите сервис:
```bash
sudo systemctl daemon-reload
sudo systemctl enable capy-discord-bot
sudo systemctl start capy-discord-bot
```
4. Логи в реальном времени:
```bash
journalctl -u capy-discord-bot -f
```

---

## 📄 Лицензия и безопасность

Продукт разработан в рамках экосистемы **CapybaraPR** (2026) и защищен [PROPRIETARY LICENSE](./LICENSE).
Подробнее о безопасности в [SECURITY.md](./SECURITY.md).