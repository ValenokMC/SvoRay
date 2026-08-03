# Contributing to SvoRay · Участие в разработке

Thank you for helping make SvoRay simpler and safer. Спасибо за помощь проекту.

## Before you start

- Use [Discussions](https://github.com/ValenokMC/SvoRay/discussions) for usage questions.
- Search existing [issues](https://github.com/ValenokMC/SvoRay/issues) before opening a bug or
  feature request.
- Never post a subscription URL, profile link, UUID, password, server address, or the complete
  `%LOCALAPPDATA%\SvoRay` directory.
- For vulnerabilities, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

Перед началом проверьте существующие issues и Discussions. Никогда не публикуйте ссылку подписки,
профиль, UUID, пароль, адрес сервера или всю папку `%LOCALAPPDATA%\SvoRay`. Об уязвимостях
сообщайте по инструкции [SECURITY.md](SECURITY.md).

## Development setup

SvoRay targets Windows 10/11 and .NET 10. The installer build also requires Inno Setup 6.

```powershell
dotnet restore .\src\ServiceLib.Tests\ServiceLib.Tests.csproj
dotnet test .\src\ServiceLib.Tests\ServiceLib.Tests.csproj -c Release
```

To build the installer, provide a local official v2rayN distribution containing the required core
binaries:

```powershell
.\build\BuildInstaller.ps1 -CoreSource "C:\path\to\v2rayN-windows-64"
```

Do not commit anything from `dist`, local configuration, logs, databases, subscriptions, or
assistant workspaces.

## Project conventions

- Keep code comments and commit messages in English.
- Keep the simple interface and user-facing project documentation available in both Russian and
  English.
- Add every SvoRay UI string to the paired resource files; do not hard-code it in XAML or C#.
- Preserve the complete v2rayN interface unless a change is required for the focused SvoRay flow.
- Treat subscription data and response headers as untrusted input.
- Prefer a small, focused pull request with tests over a broad rewrite.

## Pull requests

1. Explain the user problem and the chosen behavior.
2. Link the related issue when one exists.
3. Add or update tests for behavior changes.
4. Run the Release test suite.
5. Check the diff for credentials, subscription data, generated files, and unrelated changes.
6. Complete the pull request checklist.

By contributing, you agree that your changes are licensed under GPL-3.0, the same license as the
project.

---

## Коротко по-русски

Сначала опишите задачу в issue или Discussions. Делайте небольшие изменения, добавляйте тесты,
сохраняйте парные ресурсы RU/EN и запускайте Release-тесты перед PR. Комментарии в коде и сообщения
коммитов пишутся по-английски. Не добавляйте в Git пользовательские настройки, логи, базы,
подписки, собранные файлы или локальные рабочие каталоги.
