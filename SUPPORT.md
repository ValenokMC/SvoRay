# Support · Поддержка

## Прежде чем писать

Проверьте [CHANGELOG.md](CHANGELOG.md) — возможно, проблема уже исправлена в более новой версии,
и заметки к релизу описывают, что именно изменилось.

## Куда писать

| Что у вас | Куда |
| --- | --- |
| Не работает, падает, ведёт себя не так | [Открыть issue → Ошибка](https://github.com/ValenokMC/SvoRay/issues/new?template=bug_report.yml) |
| Вопрос «как сделать» или «почему так» | [Обсуждения → Q&A](https://github.com/ValenokMC/SvoRay/discussions) |
| Идея или пожелание | [Открыть issue → Предложение](https://github.com/ValenokMC/SvoRay/issues/new?template=feature_request.yml) |
| Уязвимость | Не в публичный issue — см. [Безопасность](#безопасность) |

Проект развивает один человек в свободное время. Ответ может занять несколько дней, и не каждое
предложение будет реализовано. Issue при этом не пропадает: он остаётся в списке.

## Что приложить к сообщению об ошибке

Без этого разобраться почти невозможно:

- версия SvoRay (заголовок окна) и версия Windows;
- режим подключения — «Прокси» или TUN;
- что вы делали, что ожидали увидеть и что увидели;
- относящиеся к делу строки из `%LOCALAPPDATA%\SvoRay\guiLogs`.

## Чего НЕ прикладывать

**Никогда не отправляйте папку `%LOCALAPPDATA%\SvoRay` целиком и не вставляйте ссылку на подписку
или профиль.** Там лежат ваши учётные данные в открытом виде: ссылка подписки, UUID, пароли
профилей. Из логов перед отправкой уберите адреса серверов.

Скриншот простого экрана безопасен: он намеренно не показывает адрес сервера.

## Безопасность

Об уязвимостях не сообщайте публичным issue. Используйте приватный канал GitHub:
[Security → Report a vulnerability](https://github.com/ValenokMC/SvoRay/security/advisories/new).

Что уже проверялось перед публикацией и какие риски остались — в
[docs/SECURITY_AUDIT.md](docs/SECURITY_AUDIT.md).

## Что поддержкой не является

SvoRay не даёт серверов и не продаёт подписки. Вопросы вида «дайте рабочий ключ» останутся без
ответа. Если не работает конкретная подписка — это вопрос к тому, кто её выдал; SvoRay может лишь
показать, куда идёт трафик и что отвечает сервер.

---

## In English

Check [CHANGELOG.md](CHANGELOG.md) first — the problem may already be fixed in a newer version.

- **Something is broken** → [open a bug report](https://github.com/ValenokMC/SvoRay/issues/new?template=bug_report.yml)
- **A question** → [Discussions](https://github.com/ValenokMC/SvoRay/discussions)
- **An idea** → [open a feature request](https://github.com/ValenokMC/SvoRay/issues/new?template=feature_request.yml)
- **A vulnerability** → [report it privately](https://github.com/ValenokMC/SvoRay/security/advisories/new), never in a public issue

Include the SvoRay version, your Windows version, the connection mode, what you expected, what
happened, and the relevant lines from `%LOCALAPPDATA%\SvoRay\guiLogs`.

**Never attach the `%LOCALAPPDATA%\SvoRay` folder and never paste a subscription or profile link.**
That folder holds your credentials in clear text. Strip server addresses from logs before posting.

This is a one-person project maintained in spare time. Replies can take a few days.
