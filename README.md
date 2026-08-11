# SoundSwitch Quick — Desktop Widget

Hover-виджет для быстрого переключения системного аудиовыхода Windows 11.

## Как это работает

- После запуска на рабочем столе появляется маленькая капсула с текущим аудиовыходом.
- Наведи мышь — виджет раскрывается и показывает активные устройства воспроизведения.
- Нажми на TV / наушники / колонки — системный выход переключится и виджет сам свернётся.
- Итого для обычного сценария нужен **один клик**.
- Виджет можно перетащить за верхнюю карточку.
- Позиция сохраняется между запусками.
- Правый клик по капсуле: закрепить поверх окон / обновить / выйти.
- Иконка в трее оставлена как резервный способ открыть переключатель.

## Дизайн

Свернутое состояние: компактная тёмная полупрозрачная капсула с иконкой и названием текущего выхода.
Раскрытое состояние: карточки устройств, мягкая подсветка текущего выхода, hover-состояния и короткая анимация появления.

## Запуск из Visual Studio

1. Установи Visual Studio 2022 с workload **.NET desktop development** или .NET 8 SDK.
2. Открой `SoundSwitchQuick.csproj`.
3. Выполни Restore NuGet packages.
4. Запусти проект.

## Сборка одного EXE

Открой PowerShell в папке проекта и выполни:

```powershell
.\\build.ps1
```

или напрямую:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Готовый файл будет здесь:

`bin\\Release\\net8.0-windows\\win-x64\\publish\\SoundSwitchQuick.exe`

## Техническая заметка

Список устройств и определение текущего default endpoint выполняются через Windows Core Audio/MMDevice API (NAudio).
Для назначения нового системного default endpoint используется Windows PolicyConfig COM-интерфейс. Он широко применяется desktop-утилитами, но не документирован Microsoft как публичный API.

## Автоматическая сборка через GitHub Actions

В проект добавлен `.github/workflows/build-windows.yml`. После push в `main` или ручного запуска workflow GitHub собирает self-contained `SoundSwitchQuick.exe` для Windows x64 и публикует его в Artifacts.
